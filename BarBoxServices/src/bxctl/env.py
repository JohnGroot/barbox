from functools import cache
from typing import Literal

from pydantic_settings import BaseSettings


class _Settings(BaseSettings):
    env: Literal["local", "test", "prod"] = "local"
    sqlite_path: str = "app.db"
    redis_url: str = ""

    @property
    def db_url(self) -> str:
        return f"sqlite+aiosqlite:///{self.sqlite_path}"


@cache
def acquire() -> _Settings:
    return _Settings()
