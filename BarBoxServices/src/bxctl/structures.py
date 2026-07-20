from datetime import datetime
from typing import Annotated, Any, Literal
from uuid import UUID

from pydantic import BaseModel, Field, PlainSerializer


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
    amount: Annotated[
        int, Field(gt=0, description="Credits to deposit (must be positive)")
    ]
    lobby_session_id: UUID


class MachineCreditsConsumeRequest(BaseModel):
    """Request body for consuming credits from machine pot"""

    box_id: UUID
    amount: Annotated[
        int, Field(gt=0, description="Credits to consume (must be positive)")
    ]
    game_session_id: UUID


class CheckoutSessionRequest(BaseModel):
    """Request to create a Stripe Checkout Session for credit purchase.

    Stripe API: https://docs.stripe.com/api/checkout/sessions/create
    """

    pack_id: str  # Credit pack identifier: pack_5, pack_10, pack_25, pack_50, pack_100


class CheckoutSessionResponse(BaseModel):
    """Response with Stripe Checkout Session details for QR code generation.

    Stripe API: https://docs.stripe.com/api/checkout/sessions/object
    """

    session_id: str  # Stripe Checkout Session ID
    session_url: str  # URL to redirect/display as QR code


class CheckoutStatusResponse(BaseModel):
    """Response for checking payment completion status.

    Used by clients to poll for payment completion instead of
    polling credit balance (which is expensive and brittle).
    """

    session_id: str  # Stripe Checkout Session ID
    status: Literal["pending", "completed", "failed"]
    credits_granted: int | None = None  # Total credits (base + bonus) if completed
    completed_at: datetime | None = None  # When payment was processed


class StripePriceMetadata(BaseModel):
    """Type-safe validation for Stripe Price metadata.

    Stripe Prices store credits and bonus_credits in metadata as strings.
    This model validates and parses them safely.

    Stripe API: https://docs.stripe.com/api/prices/object#price_object-metadata
    """

    credits: int = Field(gt=0, description="Base credits (must be positive)")
    bonus_credits: int = Field(ge=0, default=0, description="Bonus credits (default 0)")

    @classmethod
    def from_stripe_metadata(
        cls, metadata: dict[str, str] | None
    ) -> "StripePriceMetadata":
        """Parse Stripe metadata dict into validated model.

        Args:
            metadata: Stripe Price.metadata dict (string values)

        Raises:
            ValueError: If credits is missing or not a positive integer
        """
        if not metadata or "credits" not in metadata:
            msg = "Price metadata missing required 'credits' field"
            raise ValueError(msg)

        return cls(
            credits=int(metadata["credits"]),
            bonus_credits=int(metadata.get("bonus_credits", "0")),
        )


class PaymentMismatch(BaseModel):
    """A payment record that may have issues."""

    payment_id: UUID
    stripe_session_id: str
    player_id: UUID
    box_id: UUID
    amount_cents: int
    credits_purchased: int
    status: str
    credit_event_id: UUID | None
    created_at: Annotated[
        datetime,
        PlainSerializer(lambda v: v.isoformat()),
    ]


class ReconciliationReport(BaseModel):
    """Report of payment/credit mismatches for admin review."""

    payments_without_credits: list[
        PaymentMismatch
    ]  # Payment succeeded but no credit event
    orphan_credit_events: list[dict[str, Any]]  # Credit events without payment record
    total_missing_credits: int  # Sum of credits that should have been issued
    report_generated_at: Annotated[
        datetime,
        PlainSerializer(lambda v: v.isoformat()),
    ]


class RetryResult(BaseModel):
    """Result of manually retrying credit issuance for a payment."""

    success: bool
    credits_issued: int
    payment_intent_id: UUID
    error: str | None = None
