"""Stripe payment integration endpoints.

Implements Checkout Sessions for credit purchases with QR code mobile payments.
See service.py for the business logic (credit issuance, session attribution,
reconciliation) - this module is the thin FastAPI/Stripe-SDK glue layer.
"""

import asyncio
from time import perf_counter
from uuid import UUID, uuid4

import stripe
from fastapi import APIRouter, HTTPException, Request, status
from sqlalchemy import select, update
from sqlalchemy.dialects.sqlite import insert as sqlite_insert
from stripe import (
    APIConnectionError as StripeAPIConnectionError,
)
from stripe import (
    AuthenticationError as StripeAuthenticationError,
)
from stripe import (
    InvalidRequestError as StripeInvalidRequestError,
)
from stripe import (
    RateLimitError as StripeRateLimitError,
)
from stripe import (
    StripeError,
)
from structlog import get_logger

from bxctl import env, structures
from bxctl.db import defs
from bxctl.web import dependencies

from . import service

# Timeout for Stripe API calls to prevent indefinite hangs during outages
STRIPE_API_TIMEOUT_SECONDS = 30

logger = get_logger()
router = APIRouter(prefix="/payments", tags=["Core: Payments"])


@router.post("/checkout/create", status_code=201)
async def create_checkout_session(
    request: structures.CheckoutSessionRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    authenticated_player: dependencies.AuthenticatedPlayer,
) -> structures.CheckoutSessionResponse:
    """Create Stripe Checkout Session for a specific credit pack.

    Stripe API: https://docs.stripe.com/api/checkout/sessions/create

    Player has already selected the pack in BuyCreditsModal.

    LOW-LIFT FLOW:
    - Only creates Stripe session (no local database state)
    - Player can dismiss QR without consequence
    - StripePaymentIntent record created ONLY on webhook (after actual payment)

    Authentication: Requires both Box API key AND Player JWT token.

    Args:
            request: Contains pack_id (pack_5, pack_10, pack_25, pack_50, pack_100)

    Returns:
            Checkout session URL for QR code generation
    """
    start_time = perf_counter()

    # Validate pack_id
    if request.pack_id not in service.CREDIT_PACKS:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": structures.ErrorCode.VALIDATION_ERROR,
                "message": (
                    f"Invalid pack_id: {request.pack_id}. "
                    f"Valid options: {list(service.CREDIT_PACKS.keys())}"
                ),
                "retryable": False,
            },
        )

    pack = service.CREDIT_PACKS[request.pack_id]
    price_id = service.get_stripe_price_id(request.pack_id)

    if not price_id:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": structures.ErrorCode.INTERNAL_ERROR,
                "message": (
                    f"Stripe Price ID not configured for pack: {request.pack_id}"
                ),
                "retryable": False,
            },
        )

    settings = env.acquire()

    # Get StripeClient (cached singleton with connection pooling)
    try:
        stripe_client = service.get_stripe_client()
    except ValueError as e:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": structures.ErrorCode.INTERNAL_ERROR,
                "message": "Stripe API key not configured",
                "retryable": False,
            },
        ) from e

    # Create Checkout Session with SINGLE selected pack
    # NOTE: No local database record created here - payment record created
    # on webhook only
    # Use asyncio.to_thread to avoid blocking the async event loop
    # Wrapped with timeout to prevent indefinite hangs during Stripe outages
    try:
        checkout_session = await asyncio.wait_for(
            asyncio.to_thread(
                stripe_client.checkout.sessions.create,
                params={
                    "line_items": [
                        {"price": price_id, "quantity": 1},
                    ],
                    "mode": "payment",
                    "metadata": {
                        # Include identifiers for webhook to use when payment completes
                        "player_id": str(authenticated_player),
                        "box_id": str(authenticated_box.id),
                        "pack_id": request.pack_id,
                    },
                    "success_url": settings.stripe_success_url,
                    "cancel_url": settings.stripe_cancel_url,
                },
            ),
            timeout=STRIPE_API_TIMEOUT_SECONDS,
        )
    except TimeoutError as e:
        logger.exception("stripe_api_timeout", operation="checkout_session_create")
        raise HTTPException(
            status_code=status.HTTP_504_GATEWAY_TIMEOUT,
            detail={
                "code": structures.ErrorCode.PAYMENT_SERVICE_TIMEOUT,
                "message": "Payment service timed out, please try again",
                "retryable": True,
            },
        ) from e
    except StripeAPIConnectionError as e:
        logger.exception("stripe_connection_failed", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail={
                "code": structures.ErrorCode.PAYMENT_SERVICE_UNAVAILABLE,
                "message": "Payment service temporarily unavailable",
                "retryable": True,
            },
        ) from e
    except StripeRateLimitError as e:
        logger.warning("stripe_rate_limited", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_429_TOO_MANY_REQUESTS,
            headers={"Retry-After": "60"},
            detail={
                "code": structures.ErrorCode.RATE_LIMITED,
                "message": "Too many requests, please try again later",
                "retryable": True,
            },
        ) from e
    except StripeInvalidRequestError as e:
        logger.exception("stripe_invalid_request", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": structures.ErrorCode.INVALID_PAYMENT_REQUEST,
                "message": "Invalid payment request",
                "retryable": False,
            },
        ) from e
    except StripeAuthenticationError as e:
        logger.exception("stripe_auth_failed", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": structures.ErrorCode.INTERNAL_ERROR,
                "message": "Payment service configuration error",
                "retryable": False,
            },
        ) from e
    except StripeError as e:
        logger.exception(
            "stripe_checkout_creation_failed", error=str(e), error_type=type(e).__name__
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": structures.ErrorCode.INTERNAL_ERROR,
                "message": "Failed to create payment session",
                "retryable": True,
            },
        ) from e

    elapsed_ms = (perf_counter() - start_time) * 1000
    logger.info(
        "checkout_session_created",
        session_id=checkout_session.id,
        player_id=str(authenticated_player),
        box_id=str(authenticated_box.id),
        pack_id=request.pack_id,
        amount_cents=pack["amount_cents"],
        elapsed_ms=round(elapsed_ms, 2),
    )

    return structures.CheckoutSessionResponse(
        session_id=checkout_session.id,
        session_url=checkout_session.url,
    )


@router.get("/checkout/{session_id}/status")
async def get_checkout_status(
    session_id: str,
    db_service: dependencies.Database,
) -> structures.CheckoutStatusResponse:
    """Check payment completion status by Stripe session ID.

    Used by clients to poll for payment completion. This is more efficient
    than polling credit balance (single indexed lookup vs full aggregation).

    Returns:
            - status="pending" if payment not yet processed
            - status="completed" with credits_granted if payment succeeded
            - status="failed" if payment failed
    """
    result = await db_service.session.execute(
        select(defs.StripePaymentIntent).where(
            defs.StripePaymentIntent.stripe_session_id == session_id
        )
    )
    payment = result.scalar_one_or_none()

    if not payment:
        return structures.CheckoutStatusResponse(
            session_id=session_id,
            status="pending",
        )

    return structures.CheckoutStatusResponse(
        session_id=session_id,
        status="completed" if payment.status == "succeeded" else "failed",
        credits_granted=payment.credits_purchased + payment.bonus_credits,
        completed_at=payment.completed_at,
    )


@router.post("/webhook")
# decomposition tracked in docs/architecture-roadmap.md
async def stripe_webhook(  # noqa: C901, PLR0911, PLR0912, PLR0915
    request: Request,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> dict:
    """Handle Stripe webhooks for Checkout Session completions.

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
    start_time = perf_counter()
    payload = await request.body()
    signature = request.headers.get("Stripe-Signature")

    if not signature:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Missing Stripe-Signature header",
        )

    settings = env.acquire()

    # 1. Verify signature (OUTSIDE transaction - no DB locks)
    # Explicit 300s tolerance for replay protection
    try:
        event = stripe.Webhook.construct_event(
            payload=payload,
            sig_header=signature,
            secret=settings.stripe_webhook_secret,
            tolerance=300,  # 5 minute replay window
        )
    except stripe.error.SignatureVerificationError as e:
        logger.exception(
            "webhook_signature_invalid",
            error=str(e),
            client_ip=request.client.host if request.client else "unknown",
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

    # Only handle checkout.session.completed
    if event.type != "checkout.session.completed":
        return {"status": "ignored", "event_type": event.type}

    session_data = event.data.object

    # 2. Claim idempotency EARLY (before Stripe API call)
    # This prevents retry storms: if we timeout later, we return 200
    # because we've already claimed this event. The event stays
    # marked as processed=False and can be retried via admin endpoint.
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

    if result.rowcount == 0:
        logger.info("webhook_already_processed", event_id=event.id)
        return {"status": "already_processed"}

    logger.info("webhook_claimed", event_id=event.id, webhook_id=str(webhook_event_id))

    # 3. Retrieve line items (network I/O - can timeout)
    # Stripe API: https://docs.stripe.com/api/checkout/sessions/retrieve
    # NOTE: line_items are NOT included in webhook payload - explicit retrieval required
    # See: https://docs.stripe.com/api/checkout/sessions/line_items
    # Use asyncio.to_thread to avoid blocking the async event loop
    # Wrapped with timeout to prevent indefinite hangs during Stripe outages
    try:
        stripe_client = service.get_stripe_client()
        checkout_session = await asyncio.wait_for(
            asyncio.to_thread(
                stripe_client.checkout.sessions.retrieve,
                session_data.id,
                params={"expand": ["line_items.data.price"]},
            ),
            timeout=STRIPE_API_TIMEOUT_SECONDS,
        )
        logger.info("webhook_session_retrieved", session_id=session_data.id)
    except TimeoutError:
        # We've already claimed idempotency, so return 200 to prevent Stripe retries.
        # Event stays as processed=False and can be investigated/retried via admin.
        logger.exception(
            "stripe_api_timeout",
            operation="webhook_session_retrieve",
            session_id=session_data.id,
            webhook_id=str(webhook_event_id),
        )
        await db_service.session.execute(
            update(defs.StripeWebhookEvent)
            .where(defs.StripeWebhookEvent.id == webhook_event_id)
            .values(processing_error="Stripe API timeout during session retrieval")
        )
        await db_service.session.commit()
        return {
            "status": "timeout_claimed",
            "event_id": event.id,
            "webhook_id": str(webhook_event_id),
        }
    except StripeAPIConnectionError as e:
        # Same pattern: claimed idempotency, return 200, mark error for investigation
        logger.exception(
            "webhook_session_retrieval_connection_failed",
            session_id=session_data.id,
            error=str(e),
        )
        await db_service.session.execute(
            update(defs.StripeWebhookEvent)
            .where(defs.StripeWebhookEvent.id == webhook_event_id)
            .values(processing_error=f"Stripe connection error: {e}")
        )
        await db_service.session.commit()
        return {
            "status": "connection_error_claimed",
            "event_id": event.id,
            "webhook_id": str(webhook_event_id),
        }
    except StripeError as e:
        logger.exception(
            "webhook_session_retrieval_failed", session_id=session_data.id, error=str(e)
        )
        await db_service.session.execute(
            update(defs.StripeWebhookEvent)
            .where(defs.StripeWebhookEvent.id == webhook_event_id)
            .values(processing_error=f"Stripe error: {e}")
        )
        await db_service.session.commit()
        return {
            "status": "stripe_error_claimed",
            "event_id": event.id,
            "webhook_id": str(webhook_event_id),
        }

    # Validate line items exist
    if not checkout_session.line_items or not checkout_session.line_items.data:
        logger.error("webhook_no_line_items", session_id=session_data.id)
        await db_service.session.execute(
            update(defs.StripeWebhookEvent)
            .where(defs.StripeWebhookEvent.id == webhook_event_id)
            .values(processing_error="No line items in session")
        )
        await db_service.session.commit()
        return {"status": "no_line_items", "event_id": event.id}

    # Extract metadata
    try:
        player_id = UUID(session_data.metadata["player_id"])
        box_id = UUID(session_data.metadata["box_id"])
        pack_id = session_data.metadata.get("pack_id")
    except (KeyError, ValueError) as e:
        logger.exception(
            "webhook_invalid_metadata", session_id=session_data.id, error=str(e)
        )
        await db_service.session.execute(
            update(defs.StripeWebhookEvent)
            .where(defs.StripeWebhookEvent.id == webhook_event_id)
            .values(processing_error=f"Invalid metadata: {e}")
        )
        await db_service.session.commit()
        return {"status": "invalid_metadata", "event_id": event.id}

    # Get credits from Price metadata (type-safe validation)
    line_item = checkout_session.line_items.data[0]
    price = line_item.price

    try:
        price_metadata = structures.StripePriceMetadata.from_stripe_metadata(
            price.metadata
        )
    except (ValueError, TypeError) as e:
        logger.exception(
            "webhook_price_invalid_metadata", price_id=price.id, error=str(e)
        )
        await db_service.session.execute(
            update(defs.StripeWebhookEvent)
            .where(defs.StripeWebhookEvent.id == webhook_event_id)
            .values(processing_error=f"Price metadata error: {e}")
        )
        await db_service.session.commit()
        return {"status": "invalid_price_metadata", "event_id": event.id}

    credit_amount = price_metadata.credits
    bonus = price_metadata.bonus_credits
    logger.info("webhook_credits_extracted", credits=credit_amount, bonus=bonus)

    # 4. Process payment and create credit event using extracted pure function
    try:
        result = await service.issue_credits_for_payment(
            event_id=event.id,
            session_id=session_data.id,
            payment_intent_id=session_data.payment_intent or "",
            player_id=player_id,
            box_id=box_id,
            credits=credit_amount,
            bonus_credits=bonus,
            amount_cents=session_data.amount_total,
            pack_id=pack_id or "",
            price_id=price.id,
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
    else:
        elapsed_ms = (perf_counter() - start_time) * 1000
        logger.info("webhook_completed", elapsed_ms=round(elapsed_ms, 2))
        return result


# Admin endpoints (localhost only)
admin_router = APIRouter(prefix="/admin/payments", tags=["Admin: Payments"])


@admin_router.get("/reconciliation")
async def get_reconciliation_report(
    _: dependencies.RequireLocalhost,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.ReconciliationReport:
    """Find payment/credit mismatches for investigation.

    Returns:
    - Payments that succeeded but have no credit event
    - Credit events that reference non-existent payments
    """
    return await service.build_reconciliation_report(db_service, now)


@admin_router.post("/{payment_id}/retry-credit")
async def retry_credit_issuance(
    payment_id: UUID,
    _: dependencies.RequireLocalhost,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.RetryResult:
    """Manually issue credits for a payment that failed to credit.

    Use this when a payment succeeded but the credit/earn event was not created.
    """
    return await service.retry_credit_issuance(payment_id, db_service, now)
