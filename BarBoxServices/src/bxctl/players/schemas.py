from datetime import datetime
from typing import Annotated, Any
from uuid import UUID

from pydantic import BaseModel, PlainSerializer

from bxctl.schemas import Identifiable, Tagged


class PlayerCreate(Identifiable, Tagged):
    origin_id: UUID
    pin: str  # Plaintext PIN (will be hashed before storage)
    phone_number: str  # E.164 format phone number (e.g., +15551234567)


class PlayerDetail(Identifiable, Tagged):
    origin_id: UUID
    phone_number: str


class PlayerLoginRequest(BaseModel):
    """Player authentication request."""

    phone_number: str
    pin: str
    box_id: UUID


class PlayerLoginResponse(BaseModel):
    """Player authentication response with JWT access token (arcade-optimized)."""

    access_token: str
    player_id: UUID
    username: str  # Player's display name (tag)
    expires_at: Annotated[
        datetime,
        PlainSerializer(lambda v: v.isoformat().replace("+00:00", "Z")),
    ]


class UsernameAvailabilityResponse(BaseModel):
    username: str
    is_available: bool


class PlayerCreditsResponse(BaseModel):
    player_id: UUID
    location_id: str
    credits: int


class ValidationErrorDetail(BaseModel):
    """Validation-specific error details"""

    field: str
    message: str
    value: Any | None = None


class ValidationResult(BaseModel):
    """Result of validation check"""

    valid: bool
    errors: list[ValidationErrorDetail] = []
