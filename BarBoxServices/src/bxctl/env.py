from functools import cache
from typing import Literal

from pydantic_settings import BaseSettings, SettingsConfigDict

# Environment types
Environment = Literal["local", "test", "prod"]


class _Settings(BaseSettings):
    """Application settings loaded from environment variables and .env file.

    Settings are loaded in order of precedence (highest first):
    1. Environment variables
    2. .env file in current working directory

    Environment variables:
    - ENV: Environment mode (local/test/prod), defaults to "local"
    - SQLITE_PATH: Path to SQLite database file, defaults to "app.db"
    - REDIS_URL: Redis connection URL (for future use)
    - DROP_DB_ON_STARTUP: Drop and recreate database on startup (defaults to False)
    - JWT_SECRET_KEY: Secret key for JWT signing (REQUIRED in production)
    - JWT_ALGORITHM: JWT signing algorithm, defaults to "HS256"
    - JWT_EXPIRATION_HOURS: JWT token expiration in hours, defaults to 24
    - BCRYPT_ROUNDS: Bcrypt hashing cost factor, defaults to 12
    - STRIPE_SECRET_KEY: Stripe API secret key (REQUIRED in production)
    - STRIPE_WEBHOOK_SECRET: Stripe webhook signing secret (REQUIRED in production)
    - STRIPE_SUCCESS_URL: Post-payment redirect URL
    - STRIPE_CANCEL_URL: Payment cancellation redirect URL
    - STRIPE_PRICE_*_CREDITS: Stripe Price IDs for credit packs
    """

    model_config = SettingsConfigDict(
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

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

    # CORS Configuration
    cors_origins: str = "*"  # Comma-separated list of allowed origins, or "*" for all
    cors_allow_credentials: bool = True
    cors_allow_methods: str = "*"  # Comma-separated list, or "*"
    cors_allow_headers: str = "*"  # Comma-separated list, or "*"

    # Stripe Configuration
    stripe_secret_key: str = ""  # Stripe API secret key (REQUIRED in production)
    stripe_webhook_secret: str = ""  # Stripe webhook signing secret (REQUIRED in production)
    stripe_success_url: str = "https://barbox.app/payment/success"  # Post-payment redirect
    stripe_cancel_url: str = "https://barbox.app/payment/cancel"  # Payment cancellation redirect

    # Stripe Price IDs (created via Stripe Dashboard or CLI)
    stripe_price_5_credits: str = ""  # $5 pack: 5,000 credits
    stripe_price_10_credits: str = ""  # $10 pack: 10,000 credits
    stripe_price_25_credits: str = ""  # $25 pack: 28,000 credits (3k bonus)
    stripe_price_50_credits: str = ""  # $50 pack: 60,000 credits (10k bonus)
    stripe_price_100_credits: str = ""  # $100 pack: 125,000 credits (25k bonus)

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
        if self.is_production() and not self.stripe_secret_key:
            raise ValueError(
                "Production environment requires STRIPE_SECRET_KEY environment variable."
            )
        if self.is_production() and not self.stripe_webhook_secret:
            raise ValueError(
                "Production environment requires STRIPE_WEBHOOK_SECRET environment variable."
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
