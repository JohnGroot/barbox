"""Stripe payment integration endpoints.

Implements Checkout Sessions for credit purchases with QR code mobile payments.
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

import asyncio
import threading
from datetime import UTC, datetime, timedelta
from time import perf_counter
from uuid import UUID, uuid4

import stripe
from stripe import StripeClient
from stripe import (
	APIConnectionError as StripeAPIConnectionError,
	AuthenticationError as StripeAuthenticationError,
	InvalidRequestError as StripeInvalidRequestError,
	RateLimitError as StripeRateLimitError,
	StripeError,
)
from fastapi import APIRouter, HTTPException, Request, status
from sqlalchemy import insert, select, update
from sqlalchemy.dialects.sqlite import insert as sqlite_insert
from structlog import get_logger

from bxctl import env, structures
from bxctl.db import defs
from . import dependencies

# Timeout for Stripe API calls to prevent indefinite hangs during outages
STRIPE_API_TIMEOUT_SECONDS = 30

logger = get_logger()
router = APIRouter(prefix="/payments", tags=["Core: Payments"])


# Thread-local storage for StripeClient instances
# Each thread gets its own client to avoid race conditions with requests.Session
_stripe_client_local = threading.local()


def _get_stripe_client() -> StripeClient:
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
			raise ValueError("STRIPE_SECRET_KEY not configured")
		_stripe_client_local.client = StripeClient(api_key=settings.stripe_secret_key)
	return _stripe_client_local.client

# Credit pack definitions (1,000 credits = $1 USD)
# NOTE: This is REFERENCE DATA ONLY for pack validation and lookup.
# The SOURCE OF TRUTH for credit amounts is Stripe Price metadata,
# which is fetched during webhook processing via StripePriceMetadata.from_stripe_metadata().
CREDIT_PACKS = {
	"pack_5": {"credits": 5000, "bonus": 0, "amount_cents": 500},
	"pack_10": {"credits": 10000, "bonus": 0, "amount_cents": 1000},
	"pack_25": {"credits": 25000, "bonus": 3000, "amount_cents": 2500},
	"pack_50": {"credits": 50000, "bonus": 10000, "amount_cents": 5000},
	"pack_100": {"credits": 100000, "bonus": 25000, "amount_cents": 10000},
}

# Session staleness threshold (24 hours)
STALE_SESSION_HOURS = 24


def _get_stripe_price_id(pack_id: str) -> str:
	"""Get Stripe Price ID for a credit pack from environment."""
	settings = env.acquire()
	price_map = {
		"pack_5": settings.stripe_price_5_credits,
		"pack_10": settings.stripe_price_10_credits,
		"pack_25": settings.stripe_price_25_credits,
		"pack_50": settings.stripe_price_50_credits,
		"pack_100": settings.stripe_price_100_credits,
	}
	return price_map.get(pack_id, "")


async def get_or_create_credit_session(
	player_id: UUID,
	box_id: UUID,
	db_service: dependencies.Database,
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
		select(defs.BoxSession.id).where(
			defs.BoxSession.host_player_id == player_id,
			defs.BoxSession.box_id == box_id,
			defs.BoxSession.session_type == "lobby",
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
	session_id = uuid4()
	db_service.session.add(defs.BoxSession(
		id=session_id,
		box_id=box_id,
		host_player_id=player_id,
		player_ids=[str(player_id)],
		game_tag="payment",
		session_type="payment",  # Ephemeral, just for credit event
		start_time=now,
		end_time=now,  # Immediately closed
	))
	logger.info("credit_session_created_ephemeral", session_id=str(session_id))

	return session_id


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
	if request.pack_id not in CREDIT_PACKS:
		raise HTTPException(
			status_code=status.HTTP_400_BAD_REQUEST,
			detail={
				"code": structures.ErrorCode.VALIDATION_ERROR,
				"message": f"Invalid pack_id: {request.pack_id}. Valid options: {list(CREDIT_PACKS.keys())}",
				"retryable": False,
			},
		)

	pack = CREDIT_PACKS[request.pack_id]
	price_id = _get_stripe_price_id(request.pack_id)

	if not price_id:
		raise HTTPException(
			status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
			detail={
				"code": structures.ErrorCode.INTERNAL_ERROR,
				"message": f"Stripe Price ID not configured for pack: {request.pack_id}",
				"retryable": False,
			},
		)

	settings = env.acquire()

	# Get StripeClient (cached singleton with connection pooling)
	try:
		stripe_client = _get_stripe_client()
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
	# NOTE: No local database record created here - payment record created on webhook only
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
	except asyncio.TimeoutError:
		logger.error("stripe_api_timeout", operation="checkout_session_create")
		raise HTTPException(
			status_code=status.HTTP_504_GATEWAY_TIMEOUT,
			detail={
				"code": "PAYMENT_SERVICE_TIMEOUT",
				"message": "Payment service timed out, please try again",
				"retryable": True,
			},
		)
	except StripeAPIConnectionError as e:
		logger.error("stripe_connection_failed", error=str(e))
		raise HTTPException(
			status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
			detail={
				"code": "PAYMENT_SERVICE_UNAVAILABLE",
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
				"code": "RATE_LIMITED",
				"message": "Too many requests, please try again later",
				"retryable": True,
			},
		) from e
	except StripeInvalidRequestError as e:
		logger.error("stripe_invalid_request", error=str(e))
		raise HTTPException(
			status_code=status.HTTP_400_BAD_REQUEST,
			detail={
				"code": "INVALID_PAYMENT_REQUEST",
				"message": "Invalid payment request",
				"retryable": False,
			},
		) from e
	except StripeAuthenticationError as e:
		logger.error("stripe_auth_failed", error=str(e))
		raise HTTPException(
			status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
			detail={
				"code": structures.ErrorCode.INTERNAL_ERROR,
				"message": "Payment service configuration error",
				"retryable": False,
			},
		) from e
	except StripeError as e:
		logger.error("stripe_checkout_creation_failed", error=str(e), error_type=type(e).__name__)
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
		select(defs.StripePaymentIntent)
		.where(defs.StripePaymentIntent.stripe_session_id == session_id)
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
async def stripe_webhook(
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
		logger.error(
			"webhook_signature_invalid",
			error=str(e),
			client_ip=request.client.host if request.client else "unknown",
		)
		raise HTTPException(
			status_code=status.HTTP_400_BAD_REQUEST,
			detail="Invalid signature",
		) from e
	except ValueError as e:
		logger.error("webhook_payload_invalid", error=str(e))
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
	stmt = sqlite_insert(defs.StripeWebhookEvent).values(
		id=webhook_event_id,
		created_at=now,
		stripe_event_id=event.id,
		stripe_event_type=event.type,
		processed=False,
		event_data=event.to_dict(),
	).on_conflict_do_nothing(index_elements=["stripe_event_id"])

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
		stripe_client = _get_stripe_client()
		checkout_session = await asyncio.wait_for(
			asyncio.to_thread(
				stripe_client.checkout.sessions.retrieve,
				session_data.id,
				params={"expand": ["line_items.data.price"]},
			),
			timeout=STRIPE_API_TIMEOUT_SECONDS,
		)
		logger.info("webhook_session_retrieved", session_id=session_data.id)
	except asyncio.TimeoutError:
		# We've already claimed idempotency, so return 200 to prevent Stripe retries.
		# Event stays as processed=False and can be investigated/retried via admin.
		logger.error(
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
		return {"status": "timeout_claimed", "event_id": event.id, "webhook_id": str(webhook_event_id)}
	except StripeAPIConnectionError as e:
		# Same pattern: claimed idempotency, return 200, mark error for investigation
		logger.error("webhook_session_retrieval_connection_failed", session_id=session_data.id, error=str(e))
		await db_service.session.execute(
			update(defs.StripeWebhookEvent)
			.where(defs.StripeWebhookEvent.id == webhook_event_id)
			.values(processing_error=f"Stripe connection error: {e}")
		)
		await db_service.session.commit()
		return {"status": "connection_error_claimed", "event_id": event.id, "webhook_id": str(webhook_event_id)}
	except StripeError as e:
		logger.error("webhook_session_retrieval_failed", session_id=session_data.id, error=str(e))
		await db_service.session.execute(
			update(defs.StripeWebhookEvent)
			.where(defs.StripeWebhookEvent.id == webhook_event_id)
			.values(processing_error=f"Stripe error: {e}")
		)
		await db_service.session.commit()
		return {"status": "stripe_error_claimed", "event_id": event.id, "webhook_id": str(webhook_event_id)}

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
		logger.error("webhook_invalid_metadata", session_id=session_data.id, error=str(e))
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
		price_metadata = structures.StripePriceMetadata.from_stripe_metadata(price.metadata)
	except (ValueError, TypeError) as e:
		logger.error("webhook_price_invalid_metadata", price_id=price.id, error=str(e))
		await db_service.session.execute(
			update(defs.StripeWebhookEvent)
			.where(defs.StripeWebhookEvent.id == webhook_event_id)
			.values(processing_error=f"Price metadata error: {e}")
		)
		await db_service.session.commit()
		return {"status": "invalid_price_metadata", "event_id": event.id}

	credits = price_metadata.credits
	bonus = price_metadata.bonus_credits
	logger.info("webhook_credits_extracted", credits=credits, bonus=bonus)

	# 4. Process payment and create credit event
	payment_intent_id = uuid4()
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
		db_service.session.add(defs.StripePaymentIntent(
			id=payment_intent_id,
			created_at=now,
			stripe_session_id=session_data.id,
			stripe_payment_intent_id=session_data.payment_intent or "",
			player_id=player_id,
			box_id=box_id,
			amount_cents=session_data.amount_total,
			credits_purchased=credits,
			bonus_credits=bonus,
			selected_price_id=price.id,
			payment_method="checkout_session",
			payment_method_type=session_data.payment_method_types[0] if session_data.payment_method_types else None,
			status="succeeded",
			completed_at=now,
			# Reconciliation links
			credit_event_id=credit_event_id,
			credited_to_session_id=credit_session_id,
			credited_at=now,
			payment_metadata={"pack_id": pack_id},
		))

		# Emit credit/earn event (global credits, references payment)
		# Use Core INSERT to bypass MappedAsDataclass FK/relationship sync issues
		await db_service.session.execute(
			insert(defs.BoxSessionEvent).values(
				id=credit_event_id,
				session_id=credit_session_id,
				type="credit/earn",
				timestamp=now,
				payload={
					"amount": credits + bonus,
					"source": "stripe_payment",
					"global": True,  # Spendable at any location
					"stripe_payment_intent_id": str(payment_intent_id),
					"box_id": str(box_id),  # Where purchased (for bookkeeping)
				}
			)
		)

		# Mark webhook as processed
		await db_service.session.execute(
			update(defs.StripeWebhookEvent)
			.where(defs.StripeWebhookEvent.id == webhook_event_id)
			.values(processed=True, processed_at=now, payment_intent_id=payment_intent_id)
		)

		# Commit all changes
		await db_service.session.commit()

		elapsed_ms = (perf_counter() - start_time) * 1000
		logger.info(
			"payment_completed",
			player_id=str(player_id),
			session_id=str(credit_session_id),
			credits=credits + bonus,
			amount_cents=session_data.amount_total,
			payment_intent_id=str(payment_intent_id),
			elapsed_ms=round(elapsed_ms, 2),
		)
		return {"status": "success", "credits_added": credits + bonus}

	except Exception as e:
		# Mark webhook as failed (allows investigation/retry)
		# CRITICAL: Wrap in try-catch to ensure original exception is always logged
		# even if the error recording itself fails
		logger.error("webhook_processing_failed", event_id=event.id, error=str(e), error_type=type(e).__name__)
		try:
			await db_service.session.rollback()
			await db_service.session.execute(
				update(defs.StripeWebhookEvent)
				.where(defs.StripeWebhookEvent.id == webhook_event_id)
				.values(processing_error=str(e))
			)
			await db_service.session.commit()
		except Exception as recording_error:
			# Log both errors - original error is more important
			logger.error(
				"webhook_error_recording_failed",
				event_id=event.id,
				original_error=str(e),
				recording_error=str(recording_error),
			)
		raise HTTPException(
			status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
			detail="Processing failed",
		) from e


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
	# Find payments without credit events
	missing_credits_sql = """
	SELECT
		id, stripe_session_id, player_id, box_id,
		amount_cents, credits_purchased, status, credit_event_id, created_at
	FROM stripe_payment_intent
	WHERE status = 'succeeded'
	AND credit_event_id IS NULL
	ORDER BY created_at DESC
	LIMIT 100
	"""
	result = await db_service.get_many_raw(missing_credits_sql, {})
	payments_without_credits = [
		structures.PaymentMismatch(
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
		p.credits_purchased + (CREDIT_PACKS.get(f"pack_{p.amount_cents // 100}", {}).get("bonus", 0))
		for p in payments_without_credits
	)

	# Find orphan credit events (credit/earn from stripe with no payment record)
	orphan_sql = """
	SELECT bse.id, bse.session_id, bse.timestamp, bse.payload
	FROM box_session_event bse
	WHERE bse.type = 'credit/earn'
	AND json_extract(bse.payload, '$.source') = 'stripe_payment'
	AND NOT EXISTS (
		SELECT 1 FROM stripe_payment_intent spi
		WHERE spi.credit_event_id = bse.id
	)
	ORDER BY bse.timestamp DESC
	LIMIT 100
	"""
	orphan_result = await db_service.get_many_raw(orphan_sql, {})
	orphan_credit_events = [
		{
			"event_id": str(row[0]),
			"session_id": str(row[1]),
			"timestamp": row[2].isoformat() if row[2] else None,
			"payload": row[3],
		}
		for row in orphan_result.tuples()
	]

	return structures.ReconciliationReport(
		payments_without_credits=payments_without_credits,
		orphan_credit_events=orphan_credit_events,
		total_missing_credits=total_missing,
		report_generated_at=now,
	)


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
	# Find the payment
	result = await db_service.session.execute(
		select(defs.StripePaymentIntent).where(defs.StripePaymentIntent.id == payment_id)
	)
	payment = result.scalar_one_or_none()

	if not payment:
		return structures.RetryResult(
			success=False,
			credits_issued=0,
			payment_intent_id=payment_id,
			error=f"Payment {payment_id} not found",
		)

	if payment.credit_event_id:
		return structures.RetryResult(
			success=False,
			credits_issued=0,
			payment_intent_id=payment_id,
			error=f"Payment {payment_id} already has credit event {payment.credit_event_id}",
		)

	if payment.status != "succeeded":
		return structures.RetryResult(
			success=False,
			credits_issued=0,
			payment_intent_id=payment_id,
			error=f"Payment {payment_id} has status '{payment.status}', not 'succeeded'",
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
			type="credit/earn",
			timestamp=now,
			payload={
				"amount": total_credits,
				"source": "stripe_payment_retry",
				"global": True,
				"stripe_payment_intent_id": str(payment_id),
				"box_id": str(payment.box_id),
				"retry": True,
			}
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

	return structures.RetryResult(
		success=True,
		credits_issued=total_credits,
		payment_intent_id=payment_id,
	)
