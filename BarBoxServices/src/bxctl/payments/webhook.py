"""Stripe webhook event handling.

Stripe API References:
- Webhook Events: https://docs.stripe.com/api/events
- Signature Verification: https://docs.stripe.com/webhooks/signatures
- checkout.session.completed: https://docs.stripe.com/api/events/types#event_types-checkout.session.completed

CRITICAL SAFETY PATTERNS:
1. Signature verification (with explicit 5-minute tolerance)
2. Atomic idempotency (INSERT ON CONFLICT - prevents race conditions)
3. Transaction wrapping (all-or-nothing - prevents partial failures)
4. Payment-first architecture (StripePaymentIntent is source of truth)
5. Global credits with venue tracking (box_id for revenue attribution)
"""

import asyncio
from collections.abc import Awaitable, Callable
from datetime import datetime
from typing import Any, Final
from uuid import UUID, uuid4

import stripe
from fastapi import HTTPException, status
from sqlalchemy import update
from sqlalchemy.dialects.sqlite import insert as sqlite_insert
from stripe import APIConnectionError as StripeAPIConnectionError
from stripe import StripeError
from structlog import get_logger

from bxctl import env
from bxctl.db import defs
from bxctl.db.service import CRUD
from bxctl.payments import schemas, service

logger = get_logger()

# Reject Stripe webhook events signed longer ago than this (replay protection)
STRIPE_WEBHOOK_TOLERANCE_SECONDS: Final = 300


class CheckoutProcessingAbort(Exception):  # noqa: N818  # control-flow signal carrying a 200 response, not an error
    """Processing halted after the idempotency claim.

    Stripe must receive a 200 (we own the event now; retries would be
    duplicates), so aborts carry the exact response payload to return plus
    the processing_error string to record on the StripeWebhookEvent row for
    later investigation/admin retry.
    """

    def __init__(self, response: dict, processing_error: str) -> None:
        super().__init__(processing_error)
        self.response = response
        self.processing_error = processing_error


def construct_verified_event(
    payload: bytes,
    signature: str | None,
    client_host: str,
) -> stripe.Event:
    """Verify the webhook signature and parse the event.

    Runs OUTSIDE any transaction - no DB locks held during verification.
    """
    if not signature:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing Stripe-Signature header",
        )

    settings = env.acquire()

    try:
        return stripe.Webhook.construct_event(
            payload=payload,
            sig_header=signature,
            secret=settings.stripe_webhook_secret,
            tolerance=STRIPE_WEBHOOK_TOLERANCE_SECONDS,
        )
    except stripe.error.SignatureVerificationError as e:
        logger.exception(
            "webhook_signature_invalid",
            error=str(e),
            client_ip=client_host,
        )
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid signature",
        ) from e
    except ValueError as e:
        logger.exception("webhook_payload_invalid", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid payload",
        ) from e


async def _record_processing_error(
    db_service: CRUD,
    webhook_event_id: UUID,
    message: str,
) -> None:
    await db_service.session.execute(
        update(defs.StripeWebhookEvent)
        .where(defs.StripeWebhookEvent.id == webhook_event_id)
        .values(processing_error=message)
    )
    await db_service.session.commit()


async def claim_event(
    event: stripe.Event,
    db_service: CRUD,
    now: datetime,
) -> UUID | None:
    """Claim idempotency EARLY (before the Stripe API call).

    This prevents retry storms: if we timeout later, we return 200 because
    we've already claimed this event. The event stays marked as
    processed=False and can be retried via admin endpoint.

    Returns the StripeWebhookEvent id, or None if already claimed.
    """
    webhook_event_id = uuid4()
    stmt = (
        sqlite_insert(defs.StripeWebhookEvent)
        .values(
            id=webhook_event_id,
            created_at=now,
            stripe_event_id=event.id,
            stripe_event_type=event.type,
            processed=False,
            event_data=event.to_dict(),
        )
        .on_conflict_do_nothing(index_elements=["stripe_event_id"])
    )

    result = await db_service.session.execute(stmt)
    await db_service.session.commit()  # Commit idempotency claim immediately

    # rowcount is present on DML results; ty's Result stubs don't know that
    if result.rowcount == 0:  # ty: ignore[possibly-missing-attribute]
        return None
    return webhook_event_id


async def retrieve_session_with_line_items(
    session_id: str,
    event_id: str,
    webhook_event_id: UUID,
) -> stripe.checkout.Session:
    """Retrieve the checkout session with expanded line items.

    line_items are NOT included in the webhook payload - explicit retrieval
    is required (https://docs.stripe.com/api/checkout/sessions/line_items).
    Network I/O runs in a thread with a timeout so a Stripe outage can't
    hang the event loop; failures after the idempotency claim raise
    CheckoutProcessingAbort so the caller returns 200 to Stripe.
    """
    try:
        stripe_client = service.get_stripe_client()
        checkout_session = await asyncio.wait_for(
            asyncio.to_thread(
                stripe_client.checkout.sessions.retrieve,
                session_id,
                params={"expand": ["line_items.data.price"]},
            ),
            timeout=service.STRIPE_API_TIMEOUT_SECONDS,
        )
        logger.info("webhook_session_retrieved", session_id=session_id)
    except TimeoutError as e:
        logger.exception(
            "stripe_api_timeout",
            operation="webhook_session_retrieve",
            session_id=session_id,
            webhook_id=str(webhook_event_id),
        )
        raise CheckoutProcessingAbort(
            response={
                "status": "timeout_claimed",
                "event_id": event_id,
                "webhook_id": str(webhook_event_id),
            },
            processing_error="Stripe API timeout during session retrieval",
        ) from e
    except StripeAPIConnectionError as e:
        logger.exception(
            "webhook_session_retrieval_connection_failed",
            session_id=session_id,
            error=str(e),
        )
        raise CheckoutProcessingAbort(
            response={
                "status": "connection_error_claimed",
                "event_id": event_id,
                "webhook_id": str(webhook_event_id),
            },
            processing_error=f"Stripe connection error: {e}",
        ) from e
    except StripeError as e:
        logger.exception(
            "webhook_session_retrieval_failed", session_id=session_id, error=str(e)
        )
        raise CheckoutProcessingAbort(
            response={
                "status": "stripe_error_claimed",
                "event_id": event_id,
                "webhook_id": str(webhook_event_id),
            },
            processing_error=f"Stripe error: {e}",
        ) from e

    if not checkout_session.line_items or not checkout_session.line_items.data:
        logger.error("webhook_no_line_items", session_id=session_id)
        raise CheckoutProcessingAbort(
            response={"status": "no_line_items", "event_id": event_id},
            processing_error="No line items in session",
        )

    return checkout_session


def extract_purchase_metadata(
    session_data: Any,  # noqa: ANN401  # stripe event object
    event_id: str,
) -> tuple[UUID, UUID, str | None]:
    """Extract player_id, box_id, and pack_id from checkout session metadata."""
    try:
        player_id = UUID(session_data.metadata["player_id"])
        box_id = UUID(session_data.metadata["box_id"])
        pack_id = session_data.metadata.get("pack_id")
    except (KeyError, ValueError) as e:
        logger.exception(
            "webhook_invalid_metadata", session_id=session_data.id, error=str(e)
        )
        raise CheckoutProcessingAbort(
            response={"status": "invalid_metadata", "event_id": event_id},
            processing_error=f"Invalid metadata: {e}",
        ) from e
    return player_id, box_id, pack_id


def extract_price_metadata(
    line_item: Any,  # noqa: ANN401  # stripe line-item object
    event_id: str,
) -> schemas.StripePriceMetadata:
    """Validate the line item's Price metadata (credits/bonus_credits)."""
    price = line_item.price
    try:
        return schemas.StripePriceMetadata.from_stripe_metadata(price.metadata)
    except (ValueError, TypeError) as e:
        logger.exception(
            "webhook_price_invalid_metadata", price_id=price.id, error=str(e)
        )
        raise CheckoutProcessingAbort(
            response={"status": "invalid_price_metadata", "event_id": event_id},
            processing_error=f"Price metadata error: {e}",
        ) from e


async def handle_checkout_session_completed(
    event: stripe.Event,
    db_service: CRUD,
    now: datetime,
) -> dict:
    """Process a checkout.session.completed event into issued credits."""
    session_data = event.data.object

    webhook_event_id = await claim_event(event, db_service, now)
    if webhook_event_id is None:
        logger.info("webhook_already_processed", event_id=event.id)
        return {"status": "already_processed"}

    logger.info("webhook_claimed", event_id=event.id, webhook_id=str(webhook_event_id))

    try:
        checkout_session = await retrieve_session_with_line_items(
            session_data.id, event.id, webhook_event_id
        )
        player_id, box_id, pack_id = extract_purchase_metadata(session_data, event.id)
        # line_items is non-None here - retrieve_session_with_line_items
        # already aborted on missing/empty line items
        line_item = checkout_session.line_items.data[0]  # ty: ignore[possibly-missing-attribute]
        price_metadata = extract_price_metadata(line_item, event.id)
    except CheckoutProcessingAbort as abort:
        await _record_processing_error(
            db_service, webhook_event_id, abort.processing_error
        )
        return abort.response

    credit_amount = price_metadata.credits
    bonus = price_metadata.bonus_credits
    logger.info("webhook_credits_extracted", credits=credit_amount, bonus=bonus)

    try:
        return await service.issue_credits_for_payment(
            event_id=event.id,
            session_id=session_data.id,
            payment_intent_id=session_data.payment_intent or "",
            player_id=player_id,
            box_id=box_id,
            credits=credit_amount,
            bonus_credits=bonus,
            amount_cents=session_data.amount_total,
            pack_id=pack_id or "",
            price_id=line_item.price.id,  # ty: ignore[possibly-missing-attribute]  # validated in extract_price_metadata
            payment_method_type=session_data.payment_method_types[0]
            if session_data.payment_method_types
            else None,
            db_service=db_service,
            now=now,
            webhook_event_id=webhook_event_id,
        )
    except Exception as e:
        logger.exception(
            "webhook_processing_failed",
            event_id=event.id,
            error=str(e),
            error_type=type(e).__name__,
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="Processing failed",
        ) from e


EventHandler = Callable[[stripe.Event, CRUD, datetime], Awaitable[dict]]

EVENT_HANDLERS: dict[str, EventHandler] = {
    "checkout.session.completed": handle_checkout_session_completed,
}
