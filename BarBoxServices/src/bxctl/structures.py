from datetime import UTC, datetime
from typing import Annotated, Any, Literal
from uuid import UUID

from pydantic import BaseModel, Field, PlainSerializer

# Import game modules for registry
from bxctl.games import carrom, mining, racing

# Single source of truth: All games registered here
GAMES = {
    "carrom": {"schemas": carrom.schemas, "router": carrom.router},
    "racing": {"schemas": racing.schemas, "router": racing.router},
    "mining": {"schemas": mining.schemas, "router": mining.router},
}


class Named(BaseModel):
    name: str


class Tagged(BaseModel):
    tag: str


class Identifiable(BaseModel):
    id: UUID


class BoxCreate(Identifiable, Named, Tagged):
    pass


class BoxDetail(Identifiable):
    pass


class BoxDetailWithAPIKey(Identifiable, Named, Tagged):
    """Box details with API key - only returned on box creation."""
    api_key: str
    warning: str = "Save this API key securely - it will not be shown again"


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
        PlainSerializer(lambda v: v.isoformat().replace('+00:00', 'Z')),
    ]


# Core event types (non-game-specific)
CoreEventType = Literal[
    # Generic session events
    "play/begin",
    "play/score",
    "play/finish",
    "quit",
    # User events
    "user/login",
    "user/logout",
    # Credit events
    "credit/spend",
    "credit/earn",
    # Machine credit events (per box+game credit pools)
    "machine_credit/deposit",
    "machine_credit/consume",
]

# Compose SessionEventType from core and game-specific types
# Single source of truth: game event types defined in games/{game}/schemas.py
type SessionEventType = (
    CoreEventType
    | carrom.schemas.CarromEventType
    | racing.schemas.RacingEventType
    | mining.schemas.MiningEventType
)


class SessionEventBase(BaseModel):
    type: SessionEventType
    timestamp: Annotated[
        datetime,
        Field(default_factory=lambda: datetime.now(UTC)),
        PlainSerializer(lambda v: v.isoformat()),
    ]
    payload: Any


type GameTag = Literal["carrom"]


class BeginPlay(SessionEventBase):
    type: Literal["play/begin"]


class EndPlay(SessionEventBase):
    type: Literal["play/finish"]


class Score(SessionEventBase):
    type: Literal["play/score"]


class BoxSessionDetail(Identifiable):
    """Minimal session response for creation endpoints"""
    game_tag: str


class BoxSession(Identifiable):
    box_id: UUID
    host_player_id: UUID
    player_ids: list[str]  # JSON array of player UUID strings
    game_tag: str  # Game type identifier (e.g., "carrom", "racing", "mining")
    start_time: Annotated[datetime, Field(default_factory=datetime.now)]
    end_time: datetime | None = None
    events: Annotated[
        list[SessionEventBase],
        Field(default_factory=lambda: [BeginPlay()]),
    ]

# ============= CORE PLAYER STRUCTURES =============

class UsernameAvailabilityResponse(BaseModel):
    username: str
    is_available: bool


class PlayerCreditsResponse(BaseModel):
    player_id: UUID
    location_id: str
    credits: int


class MachinePlayerContribution(BaseModel):
    """Individual player contribution to machine credit pot"""
    player_id: UUID
    amount: int


class MachineCreditsResponse(BaseModel):
    """Machine credit pot balance and player contributions"""
    box_id: UUID
    game_tag: str
    balance: int
    contributions: list[MachinePlayerContribution]


class MachineCreditsDepositRequest(BaseModel):
    """Request body for depositing credits to machine pot"""
    box_id: UUID
    player_id: UUID
    amount: Annotated[int, Field(gt=0, description="Credits to deposit (must be positive)")]
    lobby_session_id: UUID


class MachineCreditsConsumeRequest(BaseModel):
    """Request body for consuming credits from machine pot"""
    box_id: UUID
    amount: Annotated[int, Field(gt=0, description="Credits to consume (must be positive)")]
    game_session_id: UUID


# ============= ERROR HANDLING STRUCTURES =============

class ErrorCode(str):
    """Standard error codes for structured error responses"""
    # Validation errors
    VALIDATION_ERROR = "VALIDATION_ERROR"
    INVALID_INPUT = "INVALID_INPUT"

    # Resource errors
    RESOURCE_NOT_FOUND = "RESOURCE_NOT_FOUND"
    DUPLICATE_RESOURCE = "DUPLICATE_RESOURCE"

    # Constraint errors
    FK_VIOLATION = "FK_VIOLATION"
    UNIQUE_CONSTRAINT = "UNIQUE_CONSTRAINT"

    # Operation errors
    OPERATION_FAILED = "OPERATION_FAILED"
    INTERNAL_ERROR = "INTERNAL_ERROR"


class ErrorDetail(BaseModel):
    """Structured error response model"""
    code: str  # ErrorCode value
    message: str  # Human-readable error message
    details: dict[str, Any] | None = None  # Additional context
    request_id: str | None = None  # Request tracking ID


class ValidationErrorDetail(BaseModel):
    """Validation-specific error details"""
    field: str
    message: str
    value: Any | None = None


class ValidationResult(BaseModel):
    """Result of validation check"""
    valid: bool
    errors: list[ValidationErrorDetail] = []
