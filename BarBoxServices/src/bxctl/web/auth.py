"""Box and Player authentication utilities.

This module provides authentication functions for:
- Box API key generation and verification (bcrypt + SHA256 lookup optimization)
- Player JWT token creation and validation (jose)
- Phone number validation and normalization (libphonenumber)
"""

import hashlib
import secrets
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


# Box API Key Functions

def generate_box_api_key() -> str:
	"""Generate cryptographically secure API key for box.

	Returns:
		URL-safe base64 encoded string with 256 bits of entropy
	"""
	return secrets.token_urlsafe(32)


def hash_api_key(api_key: str) -> str:
	"""Hash API key for storage using bcrypt.

	Args:
		api_key: Plaintext API key

	Returns:
		Bcrypt hash suitable for database storage
	"""
	return bcrypt.hashpw(api_key.encode(), bcrypt.gensalt(rounds=BCRYPT_ROUNDS)).decode()


def hash_api_key_lookup(api_key: str) -> str:
	"""Create fast SHA256 lookup hash for API key.

	This is NOT for security - it's for performance optimization.
	The bcrypt hash is still used for actual verification.
	This allows us to query for a specific box by API key without
	checking bcrypt hashes for all boxes.

	Args:
		api_key: Plaintext API key

	Returns:
		SHA256 hash as hex string for database indexing
	"""
	return hashlib.sha256(api_key.encode()).hexdigest()


def _verify_bcrypt_hash(value: str, stored_hash: str, context: str) -> bool:
	"""Internal function to verify a value against a bcrypt hash.

	Shared implementation for API key and PIN verification to ensure
	consistent error handling and logging.

	Args:
		value: Plaintext value to verify
		stored_hash: Bcrypt hash from database
		context: Context for logging (e.g., "api_key", "pin")

	Returns:
		True if value matches hash, False otherwise
	"""
	try:
		return bcrypt.checkpw(value.encode(), stored_hash.encode())
	except Exception as e:
		logger.warning(f"{context}_verification_failed", error=str(e))
		return False


def verify_api_key(api_key: str, stored_hash: str) -> bool:
	"""Verify API key against stored hash.

	Args:
		api_key: Plaintext API key from request
		stored_hash: Bcrypt hash from database

	Returns:
		True if key matches hash, False otherwise
	"""
	return _verify_bcrypt_hash(api_key, stored_hash, "api_key")


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
	return _verify_bcrypt_hash(pin, stored_hash, "pin")


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
			raise ValueError(f"Phone number '{phone_number}' is not a possible phone number")

		# Validate that it's a valid number
		if not phonenumbers.is_valid_number(parsed):
			raise ValueError(f"Phone number '{phone_number}' is not a valid phone number")

		# Format in E.164 format (+15551234567)
		normalized = phonenumbers.format_number(parsed, phonenumbers.PhoneNumberFormat.E164)

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
