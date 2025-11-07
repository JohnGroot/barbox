from datetime import UTC, datetime
from typing import Annotated, Any, Literal
from uuid import UUID

from pydantic import BaseModel, Field, PlainSerializer


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


class PlayerCreate(Identifiable, Tagged):
    origin_id: UUID


class PlayerDetail(Identifiable, Tagged):
    origin_id: UUID


class GameCreate(Named, Tagged):
    pass


class GameDetail(Identifiable):
    pass


SessionEventType = Literal[
    # Generic session events
    "play/begin",
    "play/score",
    "play/finish",
    "quit",
    # Racing game events
    "racing/lap_complete",
    "racing/checkpoint",
    "racing/race_finish",
    # Mining game events
    "mining/extract_start",
    "mining/extract_complete",
    "mining/upgrade_purchase",
    "mining/credit_deposit",
    "mining/tick_update",
    # Carrom game events
    "carrom/round_start",
    "carrom/piece_pocketed",
    "carrom/round_finish",
    # User events
    "user/login",
    "user/logout",
    "credit/spend",
    "credit/earn",
]


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


# ============= GAME STRUCTURES =============
# Note: Game-specific structures have been moved to their respective game modules:
# - Racing: bxctl.games.racing.schemas
# - Mining: bxctl.games.mining.schemas
# - Carrom: bxctl.games.carrom.schemas


# ============= CORE PLAYER STRUCTURES =============

class UsernameAvailabilityResponse(BaseModel):
    username: str
    is_available: bool


class PlayerCreditsResponse(BaseModel):
    player_id: UUID
    location_id: str
    credits: int


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
