from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from uuid import uuid4

from fastapi import FastAPI, Request
from structlog import get_logger

from bxctl.db.connectivity import engine
from bxctl.db.defs import Base

from . import box, carrom, game, mining, player, racing, test

logger = get_logger()


@asynccontextmanager
async def lifespan(_app: FastAPI) -> AsyncIterator[None]:
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        yield
        # Database persists across restarts - no drop_all


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
