from datetime import datetime
from typing import Annotated, Any
from uuid import UUID, uuid4

from sqlalchemy import ForeignKey, Index, String
from sqlalchemy.dialects import sqlite
from sqlalchemy.orm import (
    DeclarativeBase,
    Mapped,
    MappedAsDataclass,
    MappedColumn,
    declared_attr,
    mapped_column,
    relationship,
)

type JsonObject = dict[str, Any]
type JsonArray = list[JsonObject]
type PlayerIdArray = list[str]  # JSON array of player UUID strings


class PkMixin:
    # defined as a mixin to avoid kerfuffles with Base's generated __init__ signature
    # (kerfuffle example: id seems 'required', but in reality has a default)
    id: Mapped[Annotated[UUID, mapped_column(primary_key=True, default_factory=uuid4)]]


class Base(PkMixin, MappedAsDataclass, DeclarativeBase):
    @declared_attr.directive
    def __tablename__(cls) -> str:  # noqa: N805
        # Convert PascalCase to snake_case
        return "".join(
            f"_{c.lower()}" if c.isupper() and i > 0 else c.lower()
            for i, c in enumerate(cls.__name__)
        )

    type_annotation_map = {  # noqa: RUF012
        JsonObject: sqlite.JSON,
        JsonArray: sqlite.JSON,
        PlayerIdArray: sqlite.JSON,
    }


def fk_to(model: type[Base]) -> MappedColumn[Any]:
    """Helper to create foreign key columns with correct target table name."""
    name = f"{model.__tablename__}.id"
    return mapped_column(ForeignKey(name))


class Box(Base):
    name: Mapped[str]
    tag: Mapped[str]
    # API key is now deterministically derived from box_id using HMAC(server_secret, box_id)
    # No storage needed - key can be regenerated on demand
    created_at: Mapped[datetime]
    last_seen: Mapped[datetime | None]


type BoxFk = Annotated[UUID, fk_to(Box)]


class Player(Base):
    tag: Mapped[str]
    origin_id: Mapped[BoxFk]
    pin_hash: Mapped[str]  # bcrypt hash of player PIN
    phone_number: Mapped[str]  # E.164 format phone number (e.g., +15551234567)


class BoxSession(Base):
    box_id: Mapped[BoxFk]
    host_player_id: Mapped[
        Annotated[UUID, fk_to(Player)] | None
    ]  # Primary player who created session (nullable for practice mode)
    player_ids: Mapped[
        PlayerIdArray
    ]  # All players (single-player: ["uuid"], multi-player: ["uuid1", "uuid2", ...])
    game_tag: Mapped[
        str
    ]  # Game type identifier (e.g., "carrom", "racing", "mining", "lobby")
    session_type: Mapped[str]  # Session type: "lobby" | "game" | "practice"
    start_time: Mapped[datetime]
    end_time: Mapped[datetime | None]
    events: Mapped[list["BoxSessionEvent"]] = relationship(
        back_populates="session", init=False, default_factory=list
    )


class BoxSessionEvent(Base):
    session_id: Mapped[Annotated[UUID, fk_to(BoxSession)]]
    type: Mapped[str]
    timestamp: Mapped[datetime]
    payload: Mapped[JsonObject]
    session: Mapped["BoxSession"] = relationship(
        back_populates="events", init=False, default=None
    )


class MiningLocation(Base):
    """Registered mining locations with balanced gem type assignment."""

    venue_name: Mapped[str] = mapped_column(unique=True, index=True)
    gem_type: Mapped[str]  # "ruby", "sapphire", "emerald", "diamond", "amethyst"
    display_name: Mapped[str]
    created_at: Mapped[datetime]


class StripePaymentIntent(Base):
    """Payment record - SOURCE OF TRUTH for Stripe payments.

    This table is the authoritative record of all Stripe payments.
    Credits are derived from confirmed payments via credit/earn events.
    """

    created_at: Mapped[datetime]

    # Stripe identifiers (unique, indexed for lookups)
    stripe_session_id: Mapped[str] = mapped_column(String, unique=True, index=True)
    stripe_payment_intent_id: Mapped[str] = mapped_column(
        String, unique=True, index=True
    )

    # BarBox identifiers (NO session FK - payments are independent of session lifecycle)
    player_id: Mapped[Annotated[UUID, fk_to(Player)]]
    box_id: Mapped[BoxFk]  # Where payment initiated (for venue revenue attribution)

    # Payment details
    amount_cents: Mapped[int]
    credits_purchased: Mapped[int]  # Base credits from pack
    bonus_credits: Mapped[int]  # Bonus credits (default 0 in most packs)
    selected_price_id: Mapped[str | None]  # Which Stripe Price was selected

    # Payment method tracking
    payment_method: Mapped[str]  # 'checkout_session'
    payment_method_type: Mapped[
        str | None
    ]  # 'card', 'apple_pay', 'google_pay' (analytics)

    # Status tracking
    status: Mapped[str]  # 'pending', 'processing', 'succeeded', 'failed', 'refunded'
    completed_at: Mapped[datetime | None]

    # Reconciliation links (nullable - set after credit event issued)
    credit_event_id: Mapped[
        UUID | None
    ]  # References BoxSessionEvent for reconciliation
    credited_to_session_id: Mapped[UUID | None]  # Audit trail only (not FK)
    credited_at: Mapped[datetime | None]  # When credits were issued

    # Additional payment metadata (JSON)
    payment_metadata: Mapped[JsonObject]

    # Indexes for common queries
    __table_args__ = (
        Index("ix_stripe_payment_intent_player_id", "player_id"),
        Index("ix_stripe_payment_intent_box_id", "box_id"),
        Index("ix_stripe_payment_intent_status", "status"),
    )


class StripeWebhookEvent(Base):
    """Idempotency tracking for Stripe webhook processing.

    Prevents duplicate credit issuance from webhook retries.
    Uses INSERT ON CONFLICT pattern for atomic idempotency claims.
    """

    created_at: Mapped[datetime]

    # Stripe webhook identifiers
    stripe_event_id: Mapped[str] = mapped_column(String, unique=True, index=True)
    stripe_event_type: Mapped[str]

    # Processing status
    processed: Mapped[bool]  # True after successful processing
    processed_at: Mapped[datetime | None]
    processing_error: Mapped[str | None]

    # Payment intent reference (nullable - set if event creates payment)
    payment_intent_id: Mapped[Annotated[UUID, fk_to(StripePaymentIntent)] | None]

    # Raw event data (for debugging/replay)
    event_data: Mapped[JsonObject]
