"""Box and Player authentication utilities.

This module provides authentication functions for:
- Box API key derivation and verification (deterministic HMAC-based)
- Player JWT token creation and validation (jose)
- Phone number validation and normalization (libphonenumber)
"""

import hashlib
import hmac
from datetime import UTC, datetime, timedelta
from uuid import UUID, uuid4

import bcrypt
import phonenumbers
from jose import JWTError, jwt
from structlog import get_logger

from bxctl import env

logger = get_logger()

# Load JWT and bcrypt configuration from environment
_settings = env.acquire()
JWT_SECRET_KEY = _settings.jwt_secret_key
JWT_ALGORITHM = _settings.jwt_algorithm
JWT_ACCESS_TOKEN_HOURS = _settings.jwt_access_token_hours
BCRYPT_ROUNDS = _settings.bcrypt_rounds


# Box API Key Functions (Deterministic)


def derive_box_api_key(box_id: UUID) -> str:
    """Derive deterministic API key from box_id using HMAC-SHA256.

    The API key is derived from the box_id using the server's JWT secret.
    This means:
    - The same box_id always produces the same API key
    - Keys can be regenerated on demand (no "lost key" scenario)
    - Only the server can derive keys (requires JWT_SECRET_KEY)

    Args:
            box_id: The box's unique identifier

    Returns:
            64-character hex string (256 bits of derived key material)
    """
    return hmac.new(
        JWT_SECRET_KEY.encode(), str(box_id).encode(), hashlib.sha256
    ).hexdigest()


def verify_box_api_key(provided_key: str, box_id: UUID) -> bool:
    """Verify API key by deriving expected key from box_id.

    Uses constant-time comparison to prevent timing attacks.

    Args:
            provided_key: API key from request header
            box_id: Box ID from request path

    Returns:
            True if provided key matches derived key, False otherwise
    """
    expected_key = derive_box_api_key(box_id)
    return hmac.compare_digest(provided_key, expected_key)


# Player PIN Functions


def hash_player_pin(pin: str) -> str:
    """Hash player PIN for storage using bcrypt.

    Args:
            pin: Plaintext PIN (numeric string)

    Returns:
            Bcrypt hash suitable for database storage
    """
    return bcrypt.hashpw(pin.encode(), bcrypt.gensalt(rounds=BCRYPT_ROUNDS)).decode()


def verify_player_pin(pin: str, stored_hash: str) -> bool:
    """Verify PIN against stored hash.

    Args:
            pin: Plaintext PIN from login request
            stored_hash: Bcrypt hash from database

    Returns:
            True if PIN matches hash, False otherwise
    """
    try:
        return bcrypt.checkpw(pin.encode(), stored_hash.encode())
    except Exception as e:
        logger.warning("pin_verification_failed", error=str(e))
        return False


# Player JWT Functions


def create_player_token(player_id: UUID, box_id: UUID) -> tuple[str, datetime]:
    """Generate JWT access token for authenticated player.

    Arcade-optimized token strategy:
    - Single access token (2 hours) covers typical arcade sessions
    - No refresh token needed (sessions are short-lived)
    - Idle timeout (10 minutes) handles session cleanup
    - Players actively playing stay logged in indefinitely

    Args:
            player_id: Player's unique identifier
            box_id: Box that authenticated the player

    Returns:
            Tuple of (access_token, expiration_datetime)
    """
    now = datetime.now(UTC)
    access_exp = now + timedelta(hours=JWT_ACCESS_TOKEN_HOURS)

    # Access token claims
    access_claims = {
        "player_id": str(player_id),
        "issued_by_box": str(box_id),
        "type": "access",
        "iat": int(now.timestamp()),
        "exp": int(access_exp.timestamp()),
        "jti": str(uuid4()),  # Unique token ID for revocation
    }

    access_token = jwt.encode(access_claims, JWT_SECRET_KEY, algorithm=JWT_ALGORITHM)

    logger.info(
        "jwt_token_created",
        player_id=str(player_id),
        box_id=str(box_id),
        expires_at=access_exp.isoformat(),
    )

    return access_token, access_exp


def decode_player_jwt(token: str) -> dict:
    """Decode and validate JWT token.

    Args:
            token: JWT token string

    Returns:
            Decoded claims dictionary

    Raises:
            JWTError: If token is invalid, expired, or malformed
    """
    try:
        payload = jwt.decode(token, JWT_SECRET_KEY, algorithms=[JWT_ALGORITHM])
        return payload
    except JWTError as e:
        logger.warning("jwt_decode_failed", error=str(e), error_type=type(e).__name__)
        raise


# Phone Number Validation Functions


def validate_and_normalize_phone(phone_number: str, default_region: str = "US") -> str:
    """Validate and normalize phone number to E.164 format.

    Uses libphonenumber for validation and normalization.
    E.164 is the international standard format: +[country_code][number]

    Args:
            phone_number: Phone number to validate (can be in various formats)
            default_region: Default country code if not specified (ISO 3166-1 alpha-2)

    Returns:
            Normalized phone number in E.164 format (e.g., "+15551234567")

    Raises:
            ValueError: If phone number is invalid or cannot be parsed
    """
    try:
        # Parse the phone number with the default region
        parsed = phonenumbers.parse(phone_number, default_region)

        # Validate that it's a possible number
        if not phonenumbers.is_possible_number(parsed):
            raise ValueError(
                f"Phone number '{phone_number}' is not a possible phone number"
            )

        # Validate that it's a valid number
        if not phonenumbers.is_valid_number(parsed):
            raise ValueError(
                f"Phone number '{phone_number}' is not a valid phone number"
            )

        # Format in E.164 format (+15551234567)
        normalized = phonenumbers.format_number(
            parsed, phonenumbers.PhoneNumberFormat.E164
        )

        logger.debug("phone_normalized", original=phone_number, normalized=normalized)

        return normalized

    except phonenumbers.NumberParseException as e:
        logger.warning("phone_validation_failed", phone=phone_number, error=str(e))
        raise ValueError(f"Invalid phone number format: {str(e)}") from e


def is_valid_phone_number(phone_number: str, default_region: str = "US") -> bool:
    """Check if a phone number is valid without raising exceptions.

    Args:
            phone_number: Phone number to check
            default_region: Default country code if not specified

    Returns:
            True if phone number is valid, False otherwise
    """
    try:
        validate_and_normalize_phone(phone_number, default_region)
        return True
    except ValueError:
        return False
