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
    - DROP_DB_ON_STARTUP: Drop and recreate database on startup (defaults to False)
    - JWT_SECRET_KEY: Secret key for JWT signing (REQUIRED in production)
    - JWT_ALGORITHM: JWT signing algorithm, defaults to "HS256"
    - JWT_EXPIRATION_HOURS: JWT token expiration in hours, defaults to 24
    - BCRYPT_ROUNDS: Bcrypt hashing cost factor, defaults to 12
    """
    env: Environment = "local"
    sqlite_path: str = "app.db"
    redis_url: str = ""
    drop_db_on_startup: bool = False

    # JWT Configuration
    jwt_secret_key: str = "dev-secret-UNSAFE-change-in-production"
    jwt_algorithm: str = "HS256"
    jwt_access_token_hours: int = 2  # Arcade session token (covers continuous play)

    # Bcrypt Configuration
    bcrypt_rounds: int = 12

    # Account Lockout Configuration
    max_failed_login_attempts: int = 5
    lockout_duration_minutes: int = 15
    failed_attempt_window_minutes: int = 30  # Time window to count failed attempts

    # CORS Configuration
    cors_origins: str = "*"  # Comma-separated list of allowed origins, or "*" for all
    cors_allow_credentials: bool = True
    cors_allow_methods: str = "*"  # Comma-separated list, or "*"
    cors_allow_headers: str = "*"  # Comma-separated list, or "*"

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

    def should_drop_database(self) -> bool:
        """Check if database should be dropped on startup (only in dev mode with explicit flag)"""
        return self.is_dev_mode() and self.drop_db_on_startup

    def model_post_init(self, __context) -> None:
        """Validate critical settings after initialization"""
        if self.is_production() and self.jwt_secret_key.startswith("dev-"):
            raise ValueError(
                "Production environment requires secure JWT_SECRET_KEY environment variable. "
                "Generate one with: openssl rand -base64 64"
            )


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
