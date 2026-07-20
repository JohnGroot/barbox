"""Stripe payment integration endpoints.

Implements Checkout Sessions for credit purchases with QR code mobile payments.
See service.py for the business logic (credit issuance, session attribution,
reconciliation) - this module is the thin FastAPI/Stripe-SDK glue layer.
"""

import asyncio
from time import perf_counter
from uuid import UUID

from fastapi import APIRouter, HTTPException, Request, status
from sqlalchemy import select
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

from bxctl import env, errors
from bxctl.app import dependencies
from bxctl.db import defs
from bxctl.db.defs import PaymentStatus

from . import packs, schemas, service, webhook

# Retry-After header value returned on Stripe rate-limit errors
RATE_LIMIT_RETRY_AFTER_SECONDS = "60"

logger = get_logger()
router = APIRouter(prefix="/payments", tags=["Core: Payments"])


@router.post("/checkout/create", status_code=201)
async def create_checkout_session(
    request: schemas.CheckoutSessionRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    authenticated_player: dependencies.AuthenticatedPlayer,
) -> schemas.CheckoutSessionResponse:
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

    if request.pack_id not in packs.CREDIT_PACKS:
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": errors.ErrorCode.VALIDATION_ERROR,
                "message": (
                    f"Invalid pack_id: {request.pack_id}. "
                    f"Valid options: {list(packs.CREDIT_PACKS.keys())}"
                ),
                "retryable": False,
            },
        )

    pack = packs.CREDIT_PACKS[request.pack_id]
    price_id = packs.get_stripe_price_id(request.pack_id)

    if not price_id:
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": errors.ErrorCode.INTERNAL_ERROR,
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
                "code": errors.ErrorCode.INTERNAL_ERROR,
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
            timeout=service.STRIPE_API_TIMEOUT_SECONDS,
        )
    except TimeoutError as e:
        logger.exception("stripe_api_timeout", operation="checkout_session_create")
        raise HTTPException(
            status_code=status.HTTP_504_GATEWAY_TIMEOUT,
            detail={
                "code": errors.ErrorCode.PAYMENT_SERVICE_TIMEOUT,
                "message": "Payment service timed out, please try again",
                "retryable": True,
            },
        ) from e
    except StripeAPIConnectionError as e:
        logger.exception("stripe_connection_failed", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail={
                "code": errors.ErrorCode.PAYMENT_SERVICE_UNAVAILABLE,
                "message": "Payment service temporarily unavailable",
                "retryable": True,
            },
        ) from e
    except StripeRateLimitError as e:
        logger.warning("stripe_rate_limited", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_429_TOO_MANY_REQUESTS,
            headers={"Retry-After": RATE_LIMIT_RETRY_AFTER_SECONDS},
            detail={
                "code": errors.ErrorCode.RATE_LIMITED,
                "message": "Too many requests, please try again later",
                "retryable": True,
            },
        ) from e
    except StripeInvalidRequestError as e:
        logger.exception("stripe_invalid_request", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail={
                "code": errors.ErrorCode.INVALID_PAYMENT_REQUEST,
                "message": "Invalid payment request",
                "retryable": False,
            },
        ) from e
    except StripeAuthenticationError as e:
        logger.exception("stripe_auth_failed", error=str(e))
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail={
                "code": errors.ErrorCode.INTERNAL_ERROR,
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
                "code": errors.ErrorCode.INTERNAL_ERROR,
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
        amount_cents=pack.amount_cents,
        elapsed_ms=round(elapsed_ms, 2),
    )

    return schemas.CheckoutSessionResponse(
        session_id=checkout_session.id,
        session_url=checkout_session.url,
    )


@router.get("/checkout/{session_id}/status")
async def get_checkout_status(
    session_id: str,
    db_service: dependencies.Database,
) -> schemas.CheckoutStatusResponse:
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
        return schemas.CheckoutStatusResponse(
            session_id=session_id,
            status="pending",
        )

    return schemas.CheckoutStatusResponse(
        session_id=session_id,
        status="completed" if payment.status == PaymentStatus.SUCCEEDED else "failed",
        credits_granted=payment.credits_purchased + payment.bonus_credits,
        completed_at=payment.completed_at,
    )


@router.post("/webhook")
async def stripe_webhook(
    request: Request,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> dict:
    """Handle Stripe webhooks for Checkout Session completions.

    Signature verification, idempotency claiming, and per-event-type
    processing live in webhook.py; this endpoint verifies the request and
    dispatches to the handler registered for the event type.
    """
    start_time = perf_counter()
    payload = await request.body()
    signature = request.headers.get("Stripe-Signature")
    client_host = request.client.host if request.client else "unknown"

    event = webhook.construct_verified_event(payload, signature, client_host)

    handler = webhook.EVENT_HANDLERS.get(event.type)
    if handler is None:
        return {"status": "ignored", "event_type": event.type}

    result = await handler(event, db_service, now)

    # Completion is logged only when credits were actually issued -
    # already-processed and post-claim abort outcomes return here too.
    if result.get("status") == "success":
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
) -> schemas.ReconciliationReport:
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
) -> schemas.RetryResult:
    """Manually issue credits for a payment that failed to credit.

    Use this when a payment succeeded but the credit/earn event was not created.
    """
    return await service.retry_credit_issuance(payment_id, db_service, now)
