import asyncio
import pathlib
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from uuid import uuid4

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
from structlog import get_logger

from bxctl import env
from bxctl.db.connectivity import engine
from bxctl.db.defs import Base

from bxctl.games.carrom import router as carrom_router
from bxctl.games.mining import router as mining_router
from bxctl.games.racing import router as racing_router

from . import box, game, machine_credits, player, test

logger = get_logger()

# Readiness signaling - set after full initialization
_ready_event = asyncio.Event()
READY_FILE = pathlib.Path("app.db.ready")


@asynccontextmanager
async def lifespan(_app: FastAPI) -> AsyncIterator[None]:
    settings = env.acquire()

    # Remove stale ready file from previous run
    READY_FILE.unlink(missing_ok=True)

    async with engine.begin() as conn:
        # Optional: Drop database for clean testing
        if settings.should_drop_database():
            logger.info("Development mode: Dropping and recreating database")
            await conn.run_sync(Base.metadata.drop_all)

        # Always create missing tables
        await conn.run_sync(Base.metadata.create_all)
        logger.info(f"Database ready in {settings.env} mode (drop_on_startup={settings.drop_db_on_startup})")

    # Signal that application is fully initialized and ready to serve traffic
    _ready_event.set()
    READY_FILE.write_text("ready")
    logger.info("Application ready to serve traffic - readiness signaled")

    yield

    # Cleanup ready file on shutdown
    READY_FILE.unlink(missing_ok=True)
    logger.info("Application shutting down - ready file removed")


app = FastAPI(title="BXCTL API", version="0.1.0", lifespan=lifespan)


@app.middleware("http")
async def add_request_id_middleware(request: Request, call_next):
    """Add request ID to all requests for tracking and logging.

    The request ID is:
    - Generated as a UUID for each request
    - Added to request.state for access in endpoints
    - Included in the X-Request-Id response header
    - Logged with all log messages for this request
    """
    request_id = str(uuid4())
    request.state.request_id = request_id

    # Bind request_id to logger context for all logs in this request
    logger.bind(request_id=request_id)

    # Process the request
    response = await call_next(request)

    # Add request ID to response headers
    response.headers["X-Request-Id"] = request_id

    return response


@app.exception_handler(Exception)
async def global_exception_handler(request: Request, exc: Exception):
	"""
	Global exception handler to log all unhandled errors.
	Prevents crashes from propagating as timeouts/empty responses.
	"""
	request_id = getattr(request.state, "request_id", "unknown")
	logger.error(
		"unhandled_exception",
		request_id=request_id,
		path=request.url.path,
		method=request.method,
		error=str(exc),
		error_type=type(exc).__name__,
		exc_info=True,  # Include full traceback
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
            detail="Service unavailable - application is still initializing"
        )

    return {"status": "alive"}


routers = (
    player.router,
    box.router,
    game.router,
    machine_credits.router,  # Machine credit pot management
    test.router,  # Test endpoints (only available in dev/test modes)
)
for router in routers:
    app.include_router(router)

# Game routers from embedded game modules
game_routers = (
    carrom_router.router,
    racing_router.router,
    mining_router.router,
)
for router in game_routers:
    app.include_router(router)
