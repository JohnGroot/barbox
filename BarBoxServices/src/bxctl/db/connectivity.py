from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from bxctl import env

# SQLite connection timeout: 15 seconds to handle concurrent writes
# Without this, multiple simultaneous webhook deliveries could cause SQLITE_BUSY errors
engine = create_async_engine(
	env.acquire().db_url,
	echo=False,
	paramstyle="named",
	connect_args={"timeout": 15},
)

Session = async_sessionmaker(engine)


@asynccontextmanager
async def db_session() -> AsyncIterator[AsyncSession]:
    """Provide a session that automatically closes and commits."""
    async with Session() as session:
        yield session
        await session.commit()
