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


class PlayerDetail(Identifiable):
    pass


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


class BoxSession(Identifiable):
    box_id: UUID
    player_id: UUID
    start_time: Annotated[datetime, Field(default_factory=datetime.now)]
    events: Annotated[
        list[SessionEventBase],
        Field(default_factory=lambda: [BeginPlay()]),
    ]


# ============= RACING GAME STRUCTURES =============

class CheckpointData(BaseModel):
    index: int
    time: float
    gap: float


class RacingLapCompletePayload(BaseModel):
    lap_num: int
    lap_time: float
    checkpoints: list[CheckpointData]


class RacingRaceFinishPayload(BaseModel):
    track_id: str
    total_time: float
    total_laps: int
    lap_times: list[float]
    checkpoints: list[CheckpointData] | None = None


class RacingLeaderboardEntry(BaseModel):
    player_id: UUID
    username: str
    metric_value: float  # lap time or race time
    entry_date: datetime
    lap_times: list[float] | None = None


class RacingLeaderboardResponse(BaseModel):
    track_id: str
    metric: str  # "best_lap" or "best_race"
    leaderboard: list[RacingLeaderboardEntry]


# ============= MINING GAME STRUCTURES =============

class MiningExtractCompletePayload(BaseModel):
    gem_type: str  # "ruby", "sapphire", etc.
    quantity: int
    location_id: str


class MiningUpgradePurchasePayload(BaseModel):
    upgrade_type: str  # "capacity", "mining_speed", "mining_amount"
    level: int
    cost: dict[str, int]  # {gem_type: quantity}


class MiningInventoryResponse(BaseModel):
    player_id: UUID
    gems: dict[str, int]  # {gem_type: quantity}
    last_updated: datetime


# ============= CARROM GAME STRUCTURES =============

class CarromRoundFinishPayload(BaseModel):
    mode: Literal["practice", "competitive"]
    winner: str  # player_id
    scores: dict[str, int]  # {player_id: score}


class CarromLeaderboardEntry(BaseModel):
    player_id: UUID
    username: str
    total_score: int
    total_wins: int | None = None


class CarromLeaderboardResponse(BaseModel):
    metric: str  # "total_score" or "total_wins"
    leaderboard: list[CarromLeaderboardEntry]


# ============= CORE PLAYER STRUCTURES =============

class UsernameAvailabilityResponse(BaseModel):
    username: str
    is_available: bool


class PlayerCreditsResponse(BaseModel):
    player_id: UUID
    location_id: str
    credits: int
