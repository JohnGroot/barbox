"""Business logic for Stripe payment integration.

Payment-first architecture: StripePaymentIntent is source of truth.

SDK Patterns (Modern):
- Uses StripeClient class instead of module-level stripe.api_key
- Wraps sync Stripe calls with asyncio.to_thread() for non-blocking async
- Specific error handling for different Stripe error types

Stripe API References:
- Checkout Sessions: https://docs.stripe.com/api/checkout/sessions
- Webhooks: https://docs.stripe.com/webhooks
- Error Handling: https://docs.stripe.com/error-handling
"""

import threading
from datetime import datetime, timedelta
from enum import StrEnum
from typing import Final
from uuid import UUID, uuid4

from sqlalchemy import insert, select, update
from stripe import StripeClient
from structlog import get_logger

from bxctl import env
from bxctl.db import defs
from bxctl.db.defs import PaymentStatus, SessionType
from bxctl.db.service import CRUD
from bxctl.payments import packs, schemas
from bxctl.registry import CoreEvent

logger = get_logger()


class CreditSource(StrEnum):
    """Values recorded in credit/earn payload "source" fields."""

    STRIPE_PAYMENT = "stripe_payment"
    STRIPE_PAYMENT_RETRY = "stripe_payment_retry"


# StripePaymentIntent.payment_method value for Checkout Session purchases
PAYMENT_METHOD_CHECKOUT_SESSION: Final = "checkout_session"

# game_tag sentinel for ephemeral payment sessions. game_tag normally holds a
# GAMES key; this is its own vocabulary, deliberately not tied to SessionType
# so the two persisted columns can evolve independently.
PAYMENT_GAME_TAG: Final = "payment"

# Cap on rows returned per reconciliation query
RECONCILIATION_QUERY_LIMIT: Final = 100

# Timeout for Stripe API calls to prevent indefinite hangs during outages
STRIPE_API_TIMEOUT_SECONDS: Final = 30

# Thread-local storage for StripeClient instances
# Each thread gets its own client to avoid race conditions with requests.Session
_stripe_client_local = threading.local()


def get_stripe_client() -> StripeClient:
    """Get thread-local StripeClient instance.

    Uses thread-local storage instead of singleton because Stripe SDK uses
    requests.Session internally, which is NOT thread-safe. With asyncio.to_thread(),
    multiple threads can access the client concurrently, risking connection
    pool corruption or request mixing.

    Raises ValueError if STRIPE_SECRET_KEY not configured.
    """
    if not hasattr(_stripe_client_local, "client"):
        settings = env.acquire()
        if not settings.stripe_secret_key:
            msg = "STRIPE_SECRET_KEY not configured"
            raise ValueError(msg)
        _stripe_client_local.client = StripeClient(api_key=settings.stripe_secret_key)
    return _stripe_client_local.client


# Session staleness threshold (24 hours)
STALE_SESSION_HOURS = 24


# explicit payment fields beat an opaque params object
async def issue_credits_for_payment(  # noqa: PLR0913
    *,
    event_id: str,
    session_id: str,
    payment_intent_id: str,
    player_id: UUID,
    box_id: UUID,
    credits: int,
    bonus_credits: int,
    amount_cents: int,
    pack_id: str,
    price_id: str | None,
    payment_method_type: str | None,
    db_service: CRUD,
    now: datetime,
    webhook_event_id: UUID | None = None,
) -> dict:
    """Issue credits for a completed payment.

    Pure function with primitive parameters - no Stripe SDK types.
    Used by both the real webhook handler and the test endpoint.

    This function handles:
    - Get/create credit session (prefer lobby, else ephemeral)
    - Create StripePaymentIntent record (source of truth)
    - Create credit/earn event
    - Mark webhook event as processed (if webhook_event_id provided)

    Args:
            event_id: Stripe event ID or test event ID
            session_id: Checkout session ID
            payment_intent_id: Stripe payment intent ID
            player_id: Player receiving credits
            box_id: Box where purchase was made
            credits: Base credits to issue
            bonus_credits: Bonus credits to issue
            amount_cents: Payment amount in cents
            pack_id: Credit pack identifier
            price_id: Stripe Price ID (None for test)
            payment_method_type: Payment method type (card, etc.)
            db_service: Database service
            now: Current timestamp
            webhook_event_id: StripeWebhookEvent ID to mark as processed (optional)

    Returns:
            dict with status and credits_added
    """
    internal_payment_id = uuid4()
    credit_event_id = uuid4()

    try:
        # Get/create session for credit event (prefer lobby, else ephemeral)
        credit_session_id = await get_or_create_credit_session(
            player_id=player_id,
            box_id=box_id,
            db_service=db_service,
            now=now,
        )

        # Create payment record FIRST (source of truth)
        # id comes from Base's PkMixin; ty doesn't see mixin fields through
        # SQLAlchemy's MappedAsDataclass synthesized __init__.
        db_service.session.add(
            defs.StripePaymentIntent(
                id=internal_payment_id,  # ty: ignore[unknown-argument]  # PkMixin field
                created_at=now,
                stripe_session_id=session_id,
                stripe_payment_intent_id=payment_intent_id,
                player_id=player_id,
                box_id=box_id,
                amount_cents=amount_cents,
                credits_purchased=credits,
                bonus_credits=bonus_credits,
                selected_price_id=price_id or "",
                payment_method=PAYMENT_METHOD_CHECKOUT_SESSION,
                payment_method_type=payment_method_type,
                status=PaymentStatus.SUCCEEDED,
                completed_at=now,
                # Reconciliation links
                credit_event_id=credit_event_id,
                credited_to_session_id=credit_session_id,
                credited_at=now,
                payment_metadata={"pack_id": pack_id},
            )
        )

        # Emit credit/earn event (global credits, references payment)
        # Use Core INSERT to bypass MappedAsDataclass FK/relationship sync issues
        await db_service.session.execute(
            insert(defs.BoxSessionEvent).values(
                id=credit_event_id,
                session_id=credit_session_id,
                type=CoreEvent.CREDIT_EARN,
                timestamp=now,
                payload={
                    "amount": credits + bonus_credits,
                    "source": CreditSource.STRIPE_PAYMENT,
                    "global": True,  # Spendable at any location
                    "stripe_payment_intent_id": str(internal_payment_id),
                    "box_id": str(box_id),  # Where purchased (for bookkeeping)
                },
            )
        )

        # Mark webhook event as processed (if provided)
        if webhook_event_id:
            await db_service.session.execute(
                update(defs.StripeWebhookEvent)
                .where(defs.StripeWebhookEvent.id == webhook_event_id)
                .values(
                    processed=True,
                    processed_at=now,
                    payment_intent_id=internal_payment_id,
                )
            )

        # Commit all changes
        await db_service.session.commit()

        total_credits = credits + bonus_credits
        logger.info(
            "payment_completed",
            event_id=event_id,
            player_id=str(player_id),
            session_id=str(credit_session_id),
            credits=total_credits,
            amount_cents=amount_cents,
            payment_intent_id=str(internal_payment_id),
        )

    except Exception as e:
        logger.exception(
            "credit_issuance_failed",
            event_id=event_id,
            error=str(e),
            error_type=type(e).__name__,
        )
        await db_service.session.rollback()

        # Mark webhook event as failed (if provided)
        if webhook_event_id:
            try:
                await db_service.session.execute(
                    update(defs.StripeWebhookEvent)
                    .where(defs.StripeWebhookEvent.id == webhook_event_id)
                    .values(processing_error=str(e))
                )
                await db_service.session.commit()
            except Exception as recording_error:
                logger.exception(
                    "webhook_error_recording_failed",
                    event_id=event_id,
                    original_error=str(e),
                    recording_error=str(recording_error),
                )

        raise
    else:
        return {"status": "success", "credits_added": total_credits}


async def get_or_create_credit_session(
    player_id: UUID,
    box_id: UUID,
    db_service: CRUD,
    now: datetime,
) -> UUID:
    """Get session for credit event attribution.

    Strategy:
    1. Prefer existing active lobby session (player logged in, not stale)
    2. If no active lobby, create ephemeral "payment" session

    Returns session_id only (not the full session object).
    """
    stale_threshold = now - timedelta(hours=STALE_SESSION_HOURS)

    # Check for existing active, non-stale lobby session
    # Use ORDER BY + LIMIT 1 to handle multiple active sessions gracefully
    result = await db_service.session.execute(
        select(defs.BoxSession.id)
        .where(
            defs.BoxSession.host_player_id == player_id,
            defs.BoxSession.box_id == box_id,
            defs.BoxSession.session_type == SessionType.LOBBY,
            defs.BoxSession.end_time.is_(None),  # Still active
            defs.BoxSession.start_time > stale_threshold,  # Not stale
        )
        .order_by(defs.BoxSession.start_time.desc())  # Most recent first
        .limit(1)
    )
    existing_session_id = result.scalar_one_or_none()

    if existing_session_id:
        logger.info("credit_session_using_lobby", session_id=str(existing_session_id))
        return existing_session_id

    # Create ephemeral "payment" session (player logged out or stale session)
    # id comes from Base's PkMixin; ty doesn't see mixin fields through
    # SQLAlchemy's MappedAsDataclass synthesized __init__.
    session_id = uuid4()
    db_service.session.add(
        defs.BoxSession(
            id=session_id,  # ty: ignore[unknown-argument]  # PkMixin field
            box_id=box_id,
            host_player_id=player_id,
            player_ids=[str(player_id)],
            game_tag=PAYMENT_GAME_TAG,
            session_type=SessionType.PAYMENT,  # Ephemeral, just for credit event
            start_time=now,
            end_time=now,  # Immediately closed
        )
    )
    logger.info("credit_session_created_ephemeral", session_id=str(session_id))

    return session_id


async def build_reconciliation_report(
    db_service: CRUD,
    now: datetime,
) -> schemas.ReconciliationReport:
    """Find payment/credit mismatches for investigation.

    Returns:
    - Payments that succeeded but have no credit event
    - Credit events that reference non-existent payments
    """
    # Find payments without credit events
    missing_credits_sql = """
	SELECT
		id, stripe_session_id, player_id, box_id,
		amount_cents, credits_purchased, status, credit_event_id, created_at
	FROM stripe_payment_intent
	WHERE status = :succeeded
	AND credit_event_id IS NULL
	ORDER BY created_at DESC
	LIMIT :limit
	"""
    result = await db_service.get_many_raw(
        missing_credits_sql,
        {
            "succeeded": PaymentStatus.SUCCEEDED.value,
            "limit": RECONCILIATION_QUERY_LIMIT,
        },
    )
    payments_without_credits = [
        schemas.PaymentMismatch(
            payment_id=UUID(str(row[0])),
            stripe_session_id=str(row[1]),
            player_id=UUID(str(row[2])),
            box_id=UUID(str(row[3])),
            amount_cents=int(row[4]),
            credits_purchased=int(row[5]),
            status=str(row[6]),
            credit_event_id=UUID(str(row[7])) if row[7] else None,
            created_at=row[8],
        )
        for row in result.tuples()
    ]

    # Calculate total missing credits
    total_missing = sum(
        p.credits_purchased
        + (
            pack.bonus
            if (pack := packs.CREDIT_PACKS.get(f"pack_{p.amount_cents // 100}"))
            else 0
        )
        for p in payments_without_credits
    )

    # Find orphan credit events (credit/earn from stripe with no payment record)
    orphan_sql = """
	SELECT bse.id, bse.session_id, bse.timestamp, bse.payload
	FROM box_session_event bse
	WHERE bse.type = :earn_type
	AND json_extract(bse.payload, '$.source') = :stripe_source
	AND NOT EXISTS (
		SELECT 1 FROM stripe_payment_intent spi
		WHERE spi.credit_event_id = bse.id
	)
	ORDER BY bse.timestamp DESC
	LIMIT :limit
	"""
    orphan_result = await db_service.get_many_raw(
        orphan_sql,
        {
            "earn_type": CoreEvent.CREDIT_EARN.value,
            "stripe_source": CreditSource.STRIPE_PAYMENT.value,
            "limit": RECONCILIATION_QUERY_LIMIT,
        },
    )
    orphan_credit_events = [
        {
            "event_id": str(row[0]),
            "session_id": str(row[1]),
            "timestamp": row[2].isoformat() if row[2] else None,
            "payload": row[3],
        }
        for row in orphan_result.tuples()
    ]

    return schemas.ReconciliationReport(
        payments_without_credits=payments_without_credits,
        orphan_credit_events=orphan_credit_events,
        total_missing_credits=total_missing,
        report_generated_at=now,
    )


async def retry_credit_issuance(
    payment_id: UUID,
    db_service: CRUD,
    now: datetime,
) -> schemas.RetryResult:
    """Manually issue credits for a payment that failed to credit.

    Use this when a payment succeeded but the credit/earn event was not created.
    """
    # Find the payment
    result = await db_service.session.execute(
        select(defs.StripePaymentIntent).where(
            defs.StripePaymentIntent.id == payment_id
        )
    )
    payment = result.scalar_one_or_none()

    if not payment:
        return schemas.RetryResult(
            success=False,
            credits_issued=0,
            payment_intent_id=payment_id,
            error=f"Payment {payment_id} not found",
        )

    if payment.credit_event_id:
        return schemas.RetryResult(
            success=False,
            credits_issued=0,
            payment_intent_id=payment_id,
            error=(
                f"Payment {payment_id} already has credit event "
                f"{payment.credit_event_id}"
            ),
        )

    if payment.status != PaymentStatus.SUCCEEDED:
        return schemas.RetryResult(
            success=False,
            credits_issued=0,
            payment_intent_id=payment_id,
            error=(
                f"Payment {payment_id} has status '{payment.status}', not 'succeeded'"
            ),
        )

    # Create credit session
    credit_session_id = await get_or_create_credit_session(
        player_id=payment.player_id,
        box_id=payment.box_id,
        db_service=db_service,
        now=now,
    )

    # Create credit event
    # Use Core INSERT to bypass MappedAsDataclass FK/relationship sync issues
    credit_event_id = uuid4()
    total_credits = payment.credits_purchased + payment.bonus_credits

    await db_service.session.execute(
        insert(defs.BoxSessionEvent).values(
            id=credit_event_id,
            session_id=credit_session_id,
            type=CoreEvent.CREDIT_EARN,
            timestamp=now,
            payload={
                "amount": total_credits,
                "source": CreditSource.STRIPE_PAYMENT_RETRY,
                "global": True,
                "stripe_payment_intent_id": str(payment_id),
                "box_id": str(payment.box_id),
                "retry": True,
            },
        )
    )

    # Update payment with credit event link
    payment.credit_event_id = credit_event_id
    payment.credited_to_session_id = credit_session_id
    payment.credited_at = now

    await db_service.session.commit()

    logger.info(
        "credit_retry_success",
        payment_id=str(payment_id),
        credit_event_id=str(credit_event_id),
        credits=total_credits,
    )

    return schemas.RetryResult(
        success=True,
        credits_issued=total_credits,
        payment_intent_id=payment_id,
    )
