from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from fastapi import FastAPI

from bxctl.db.connectivity import engine
from bxctl.db.defs import Base

from . import box, carrom, game, mining, player, racing


@asynccontextmanager
async def lifespan(_app: FastAPI) -> AsyncIterator[None]:
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
        yield
        # Database persists across restarts - no drop_all


app = FastAPI(title="BXCTL API", version="0.1.0", lifespan=lifespan)


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
)
for router in routers:
    app.include_router(router)
