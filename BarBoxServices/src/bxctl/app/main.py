import asyncio
import pathlib
import time
from collections.abc import AsyncIterator, Awaitable, Callable
from contextlib import asynccontextmanager
from datetime import UTC, datetime
from uuid import uuid4

import structlog
from alembic.config import Config as AlembicConfig
from fastapi import FastAPI, HTTPException, Request, Response
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from slowapi import Limiter, _rate_limit_exceeded_handler
from slowapi.errors import RateLimitExceeded
from slowapi.util import get_remote_address
from sqlalchemy import inspect as sa_inspect
from structlog import get_logger

from alembic import command as alembic_command
from bxctl import db, env
from bxctl.boxes import router as boxes_router
from bxctl.db.connectivity import engine
from bxctl.db.defs import Base
from bxctl.players import router as players_router
from bxctl.registry import GAMES, game_module
from bxctl.web import machine_credits, test
from bxctl.web.payments import router as payments_router
from bxctl.web.test import _seed_test_box_and_players

logger = get_logger()

# Resolved relative to cwd, not __file__: once installed (Docker's `uv pip
# install .`), bxctl lives under site-packages and no longer sits next to
# alembic.ini. Every entry point (dev.sh, start_backend.sh, the Docker CMD)
# already runs with cwd set to the project root, so this matches all of them.
_ALEMBIC_INI = pathlib.Path("alembic.ini")

# Rate limiter for authentication endpoints
# Disable in dev/test environments to avoid interfering with integration tests
settings = env.acquire()
limiter = Limiter(key_func=get_remote_address, enabled=settings.is_production())

# Readiness signaling - set after full initialization
_ready_event = asyncio.Event()
READY_FILE = pathlib.Path("app.db.ready")


@asynccontextmanager
async def lifespan(_app: FastAPI) -> AsyncIterator[None]:
    settings = env.acquire()

    # Remove stale ready file from previous run
    READY_FILE.unlink(missing_ok=True)

    if settings.is_production():
        # Schema is Alembic-owned in production; migrations run against the
        # persistent volume instead of create_all so schema changes are
        # tracked and reviewable rather than inferred from the models. The
        # existing prod volume predates Alembic and already has every table
        # from the baseline revision with no alembic_version row, so the
        # first boot on this revision must stamp rather than upgrade (an
        # upgrade would try to CREATE TABLE on tables that already exist).
        alembic_cfg = AlembicConfig(str(_ALEMBIC_INI))

        async with engine.begin() as conn:
            already_versioned = await conn.run_sync(
                lambda c: sa_inspect(c).has_table("alembic_version")
            )
            has_baseline_tables = await conn.run_sync(
                lambda c: sa_inspect(c).has_table("box")
            )

        if not already_versioned and has_baseline_tables:
            await asyncio.to_thread(alembic_command.stamp, alembic_cfg, "head")
            logger.info("Existing pre-Alembic database stamped at head")
        else:
            await asyncio.to_thread(alembic_command.upgrade, alembic_cfg, "head")
            logger.info("Database migrated to head via Alembic")
    else:
        # Dev/test: create_all keeps iteration fast (no migration authoring
        # needed for local schema changes).
        async with engine.begin() as conn:
            if settings.should_drop_database():
                logger.info("Development mode: Dropping and recreating database")
                await conn.run_sync(Base.metadata.drop_all)

            await conn.run_sync(Base.metadata.create_all)
        logger.info(
            "database_ready",
            env=settings.env,
            drop_on_startup=settings.drop_db_on_startup,
        )

    # Auto-seed test data in development mode for consistent editor/test experience
    if settings.is_dev_mode():
        async with db.connectivity.db_session() as session:
            db_service = db.service.CRUD(session)
            now = datetime.now(tz=UTC)

            try:
                result = await _seed_test_box_and_players(db_service, now)
                logger.info(
                    "dev_mode_auto_seed_completed",
                    status=result["status"],
                    message="Test data auto-seeded on startup",
                )
            except Exception as e:  # noqa: BLE001  # best-effort; log and continue
                logger.warning(
                    "dev_mode_auto_seed_failed",
                    error=str(e),
                    message=(
                        "Failed to auto-seed test data - use POST /test/seed if needed"
                    ),
                )

    # Signal that application is fully initialized and ready to serve traffic
    _ready_event.set()
    READY_FILE.write_text("ready")
    logger.info("Application ready to serve traffic - readiness signaled")

    yield

    # Cleanup ready file on shutdown
    READY_FILE.unlink(missing_ok=True)
    logger.info("Application shutting down - ready file removed")


tags_metadata = [
    {
        "name": "Core: Boxes & Sessions",
        "description": "Physical box registration and gameplay session management",
    },
    {
        "name": "Core: Players",
        "description": "Player accounts and registration",
    },
    {
        "name": "Core: Machine Credits",
        "description": "Shared credit pools per box+game (machine pot system)",
    },
    {
        "name": "Core: Payments",
        "description": "Stripe payment integration for credit purchases",
    },
    {
        "name": "Admin: Payments",
        "description": "Payment reconciliation and admin operations (localhost only)",
    },
    {
        "name": "Auth",
        "description": "Player authentication, token management, and session control",
    },
    {
        "name": "Game: Carrom",
        "description": "Carrom leaderboards and statistics",
    },
    {
        "name": "Game: Racing",
        "description": "Racing leaderboards and lap times",
    },
    {
        "name": "Game: Mining",
        "description": "Mining inventory and progression tracking",
    },
    {
        "name": "Game: Nines",
        "description": "Nines card game with time-based jackpot system",
    },
    {
        "name": "Testing",
        "description": (
            "Development and testing utilities "
            "(not available in production environments)"
        ),
    },
]

app = FastAPI(
    title="BarBox API",
    version="0.1.0",
    lifespan=lifespan,
    openapi_tags=tags_metadata,
)

# Configure rate limiter (only in production)
if settings.is_production():
    app.state.limiter = limiter
    app.add_exception_handler(RateLimitExceeded, _rate_limit_exceeded_handler)
    logger.info("Rate limiting enabled for production environment")
else:
    logger.info("rate_limiting_disabled", env=settings.env)

# Configure CORS middleware
origins = settings.cors_origins.split(",") if settings.cors_origins != "*" else ["*"]

app.add_middleware(
    CORSMiddleware,
    allow_origins=origins,
    allow_credentials=settings.cors_allow_credentials,
    allow_methods=settings.cors_allow_methods.split(",")
    if settings.cors_allow_methods != "*"
    else ["*"],
    allow_headers=settings.cors_allow_headers.split(",")
    if settings.cors_allow_headers != "*"
    else ["*"],
)


@app.middleware("http")
async def add_request_id_middleware(
    request: Request, call_next: Callable[[Request], Awaitable[Response]]
) -> Response:
    """Add request ID to all requests for tracking and logging.

    The request ID is:
    - Generated as a UUID for each request
    - Added to request.state for access in endpoints
    - Included in the X-Request-Id response header
    - Logged with all log messages for this request
    """
    request_id = str(uuid4())
    request.state.request_id = request_id

    # Bind request_id to logger context for all logs in this request.
    # contextvars.bind_contextvars (unlike logger.bind) propagates automatically
    # into every structlog call on this task, so it must be cleared in a
    # finally block - otherwise a leaked value could attach the wrong
    # request_id to a later, unrelated request on the same worker.
    structlog.contextvars.bind_contextvars(request_id=request_id)
    try:
        # Process the request
        response = await call_next(request)

        # Add request ID to response headers
        response.headers["X-Request-Id"] = request_id

        return response
    finally:
        structlog.contextvars.clear_contextvars()


# Requests slower than this are logged as a warning. 500ms is generous for a
# SQLite-backed API serving touchscreen terminals (most endpoints are simple
# CRUD/lookup queries) - it flags genuine outliers without being noisy on
# every request that happens to cross an arbitrary tight bound.
SLOW_REQUEST_THRESHOLD_S = 0.5


@app.middleware("http")
async def add_process_time_middleware(
    request: Request, call_next: Callable[[Request], Awaitable[Response]]
) -> Response:
    """Time request handling and record it for logging and monitoring.

    Process time is:
    - Measured around call_next() for the full request/response cycle
    - Included in the X-Process-Time response header
    - Logged as a warning when it exceeds SLOW_REQUEST_THRESHOLD_S
    """
    start_time = time.perf_counter()

    response = await call_next(request)

    process_time = time.perf_counter() - start_time
    response.headers["X-Process-Time"] = str(process_time)

    if process_time > SLOW_REQUEST_THRESHOLD_S:
        logger.warning(
            "slow_request",
            request_id=getattr(request.state, "request_id", "unknown"),
            path=request.url.path,
            method=request.method,
            process_time=process_time,
        )

    return response


@app.exception_handler(Exception)
async def global_exception_handler(request: Request, exc: Exception) -> JSONResponse:
    """
    Global exception handler to log all unhandled errors.
    Prevents crashes from propagating as timeouts/empty responses.
    """
    request_id = getattr(request.state, "request_id", "unknown")
    logger.exception(
        "unhandled_exception",
        request_id=request_id,
        path=request.url.path,
        method=request.method,
        error=str(exc),
        error_type=type(exc).__name__,
    )

    return JSONResponse(
        status_code=500,
        content={
            "code": "INTERNAL_ERROR",
            "message": "An internal error occurred",
            "request_id": request_id,
        },
    )


@app.get("/alive")
async def health_check() -> dict:
    """
    Health check endpoint - only returns OK when fully initialized and ready.

    Returns 503 Service Unavailable when:
    - Application is still starting up
    - Database initialization in progress

    Returns 200 OK when:
    - Database schema has been created
    - All initialization is complete
    - Application can safely handle traffic
    """
    if not _ready_event.is_set():
        raise HTTPException(
            status_code=503,
            detail="Service unavailable - application is still initializing",
        )

    return {"status": "alive"}


routers = (
    players_router.router,
    boxes_router.router,
    machine_credits.router,  # Machine credit pot management
    payments_router.router,  # Stripe checkout sessions for credit purchases
    payments_router.admin_router,  # Payment reconciliation (localhost only)
    test.router,  # Test endpoints (only available in dev/test modes)
)
for router in routers:
    app.include_router(router)

# Game routers - auto-registered from GAMES registry
for game_data in GAMES.values():
    app.include_router(game_module(game_data, "router").router)
