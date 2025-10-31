from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from uuid import uuid4

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from structlog import get_logger

from bxctl import env
from bxctl.db.connectivity import engine
from bxctl.db.defs import Base

from . import box, carrom, game, mining, player, racing, test

logger = get_logger()


@asynccontextmanager
async def lifespan(_app: FastAPI) -> AsyncIterator[None]:
    settings = env.acquire()

    async with engine.begin() as conn:
        # Optional: Drop database for clean testing
        if settings.should_drop_database():
            logger.info("Development mode: Dropping and recreating database")
            await conn.run_sync(Base.metadata.drop_all)

        # Always create missing tables
        await conn.run_sync(Base.metadata.create_all)
        logger.info(f"Database ready in {settings.env} mode (drop_on_startup={settings.drop_db_on_startup})")

        yield
        # No cleanup on shutdown - preserve data


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
		path=request.path,
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
    """Simple health check endpoint for service availability."""
    return {"status": "alive"}


routers = (
    player.router,
    box.router,
    game.router,
    racing.router,
    mining.router,
    carrom.router,
    test.router,  # Test endpoints (only available in dev/test modes)
)
for router in routers:
    app.include_router(router)
