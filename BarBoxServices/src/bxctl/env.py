from functools import cache
from typing import Literal

from pydantic_settings import BaseSettings

# Environment types
Environment = Literal["local", "test", "prod"]


class _Settings(BaseSettings):
    """Application settings loaded from environment variables.

    Environment variables:
    - ENV: Environment mode (local/test/prod), defaults to "local"
    - SQLITE_PATH: Path to SQLite database file, defaults to "app.db"
    - REDIS_URL: Redis connection URL (for future use)
    """
    env: Environment = "local"
    sqlite_path: str = "app.db"
    redis_url: str = ""

    @property
    def db_url(self) -> str:
        """SQLite database URL for SQLAlchemy"""
        return f"sqlite+aiosqlite:///{self.sqlite_path}"

    def is_production(self) -> bool:
        """Check if running in production environment"""
        return self.env == "prod"

    def is_test_mode(self) -> bool:
        """Check if running in test environment"""
        return self.env == "test"

    def is_dev_mode(self) -> bool:
        """Check if running in local development environment"""
        return self.env == "local"


@cache
def acquire() -> _Settings:
    """Get cached settings instance (reads from environment variables once)"""
    return _Settings()


# Convenience functions for quick environment checks
def is_production() -> bool:
    """Check if running in production environment"""
    return acquire().is_production()


def is_test_mode() -> bool:
    """Check if running in test environment"""
    return acquire().is_test_mode()


def is_dev_mode() -> bool:
    """Check if running in local development environment"""
    return acquire().is_dev_mode()
