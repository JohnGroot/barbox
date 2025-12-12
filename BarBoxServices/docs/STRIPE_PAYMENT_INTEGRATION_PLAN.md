# Stripe Payment Integration - Implementation Plan

## Executive Summary

Comprehensive Stripe payment integration for BarBox arcade platform using **QR code mobile payments** with **Checkout Sessions** for in-game credit pack selection.

**Payment Method:** Stripe Checkout Sessions - Player selects pack in-game, then scans QR to pay
- Player selects desired pack ($5, $10, $25, $50, $100) in BuyCreditsModal
- QR code generated from unique Checkout Session URL
- Players scan QR code → opens Stripe-hosted payment page (pack already selected)
- Native Apple Pay/Google Pay support for fast checkout
- Pre-selection in arcade app provides clear UX (player knows exact cost before scanning)

**Architecture:** Payment-centric design with `StripePaymentIntent` as source of truth. Webhooks use atomic transactions for reliability. Credits are global (spendable at any location) with venue tracking for revenue attribution. Phase 1 includes reconciliation tooling. Low-lift checkout flow creates no local state until payment is confirmed.

**Credit Conversion:** 1,000 credits = $1 USD (e.g., $5 pack = 5,000 credits)

**Timeline:** 5 weeks to production-ready system (includes session management updates and comprehensive webhook implementation)
**Risk Level:** LOW-MEDIUM (Checkout Sessions API is well-documented, webhook implementation has been thoroughly reviewed)

---

## Confirmed Requirements

### Payment Methods
1. ✅ **Checkout Sessions** - Primary payment method with Apple Pay/Google Pay support
   - Player selects credit pack in BuyCreditsModal (in-game)
   - QR code generated from unique Checkout Session URL
   - Stripe-hosted payment page with pre-selected pack
   - Simpler UX: player knows exact amount before scanning

### Business Requirements
- ✅ **Credit Packs:** $5, $10, $25, $50, $100 with volume discounts (1,000 credits = $1)
  - Higher packs get bonus credits (e.g., $25 = 28,000 credits, $50 = 60,000 credits, $100 = 125,000 credits)
- ✅ **Minimum Purchase:** $5 (prevents high % fees on small transactions)
- ✅ **Hardware:** None required - players use their own phones
- ✅ **Connectivity:** Backend server internet connection (already available)
- ✅ **Refunds:** Manual support process (not automated initially)
- ✅ **Multi-Location:** Testing available across multiple locations

### Technical Requirements (From Architecture Review)
- ✅ **Event-Sourced Credits:** Webhooks emit `credit/earn` events (NEVER direct DB writes)
- ✅ **Payment-First Architecture:** `StripePaymentIntent` is source of truth; sessions used only as event containers
- ✅ **Credit Session Handling:** Prefer active lobby session; create ephemeral "payment" session if player logged out
- ✅ **Global Credits:** Stripe-purchased credits are spendable at any location (not location-scoped)
- ✅ **Venue Tracking:** Payment records include `box_id` for revenue attribution in bookkeeping
- ✅ **Atomic Transactions:** All webhook DB operations wrapped in single transaction
- ✅ **Atomic Idempotency:** Use `INSERT ON CONFLICT` pattern to prevent race conditions
- ✅ **Reconciliation Tooling:** Admin endpoints for detecting payment/credit mismatches (Phase 1)
- ✅ **Low-Lift Checkout:** No local state created until webhook confirms payment
- ✅ **Synchronous UX:** Poll actual balance (not payment status) for confirmation
- ✅ **Cache Invalidation:** Force refresh CreditService after payment
- ✅ **Security:** Webhook signature verification, Box+Player authentication

---

## Architecture Overview

### Data Flow (Checkout Sessions with In-Game Pack Selection)

```
┌─────────────┐  1. Click "Buy Credits"  ┌─────────────────┐
│   Player    │─────────────────────────▶│ BuyCreditsModal │
└─────────────┘                          └────────┬────────┘
                                                  │
                2. Player SELECTS pack            │ (existing UI)
                   (e.g., $25 = 28,000 credits)   │
                                                  ▼
┌───────────────────────┐  3. POST /payments/checkout/create
│ StripePaymentService  │     with selected pack_id
│ - Request checkout    │────────────────────────────────────┐
│   session             │                                    │
└───────────────────────┘                                    ▼
                                         ┌───────────────────────────┐
                                         │ Backend                   │
                                         │ - Get/create lobby session│
                                         │ - Create Checkout Session │
                                         │   with SINGLE line_item   │
                                         │ - Include metadata        │
                                         └─────────────┬─────────────┘
                                                       │
                4. Return {session_url, session_id}    │
┌───────────────────────┐◀─────────────────────────────┘
│ BuyCreditsModal       │
│ - Generate QR code    │  5. Display QR
│ - Show instructions   │     "Scan to pay $25"
└───────────────────────┘
            │
            │ 6. Player scans QR with phone
            ▼
┌───────────────────────┐
│ Stripe Checkout Page  │  7. Player confirms & pays
│ - Shows $25 charge    │     (Apple Pay / Google Pay)
│ - Pre-selected pack   │
└───────────────────────┘
            │
            │ 8. Payment completed
            ▼
┌───────────────────────┐  9. checkout.session.completed
│ Stripe Webhook        │────────────────────────────────┐
└───────────────────────┘                                │
                                                         ▼
                         ┌───────────────────────────────────────┐
                         │ Backend /payments/webhook             │
                         │ - Verify signature                    │
                         │ - Atomic idempotency claim            │
                         │ - BEGIN TRANSACTION                   │
                         │   - Create StripePaymentIntent FIRST  │
                         │   - Get/create credit session         │
                         │   - Emit credit/earn event (global)   │
                         │   - Link payment to credit event      │
                         │ - COMMIT TRANSACTION                  │
                         └───────────────────────────────────────┘
            │
            │ 10. Balance polling detects increase
            ▼
┌───────────────────────┐
│ BuyCreditsModal       │  11. Show success
│ - "Added 28,000       │      (actual amount)
│    credits!"          │
└───────────────────────┘
```

**Total UX Time:** 10-20 seconds with Apple/Google Pay (pack already selected), 25-50 seconds with manual card entry

### Key Architectural Principles

1. **Payment-First Architecture** - `StripePaymentIntent` is source of truth; credits derived from confirmed payments
2. **Single Credit Source** - ONLY `credit/earn` events add credits (via webhook, never client)
3. **Event Sourcing Integrity** - Webhooks emit events, never mutate tables directly
4. **Atomic Transactions** - All webhook DB operations in single transaction (prevents partial failures)
5. **Atomic Idempotency** - Use `INSERT ON CONFLICT` pattern (prevents race conditions between threads)
6. **Credit Session Handling** - Prefer active lobby session; create ephemeral "payment" session if player logged out
7. **Global Credits** - Stripe credits spendable at any location (not location-scoped)
8. **Venue Tracking** - Payment records include `box_id` for revenue attribution (bookkeeping)
9. **Low-Lift Checkout** - No local state created until webhook confirms payment (QR dismissal is free)
10. **Reconciliation Links** - Payment records reference credit events via FK for easy auditing
11. **In-Game Pack Selection** - Credit amount determined by player in BuyCreditsModal before QR generation
12. **Synchronous Balance Polling** - Client polls for ANY balance increase, with exponential backoff
13. **Cache Invalidation** - Force refresh after payment to show credits immediately

---

## Credit Pack Definitions

### Credit Conversion: 1,000 credits = $1 USD

| Price | Base Credits | Bonus Credits | Total Credits | Bonus % |
|-------|-------------|---------------|---------------|---------|
| $5    | 5,000       | 0             | 5,000         | 0%      |
| $10   | 10,000      | 0             | 10,000        | 0%      |
| $25   | 25,000      | 3,000         | 28,000        | 12%     |
| $50   | 50,000      | 10,000        | 60,000        | 20%     |
| $100  | 100,000     | 25,000        | 125,000       | 25%     |

### Stripe Price Metadata Format
```json
{
  "credits": "25000",
  "bonus_credits": "3000"
}
```

---

## Implementation Phases

### Phase 1: Backend Foundation (Weeks 1-2)

**Goal:** Stripe API integration, Checkout Sessions, Stripe Prices, webhook handling

**Database Schema:**
```python
# src/bxctl/db/defs.py

class StripePaymentIntent(Base):
    """Payment record - SOURCE OF TRUTH for Stripe payments"""
    __tablename__ = "stripe_payment_intent"

    id: Mapped[UUID] = mapped_column(primary_key=True)
    created_at: Mapped[datetime] = mapped_column(default=lambda: datetime.now(UTC))

    # Stripe identifiers
    stripe_session_id: Mapped[str] = mapped_column(String, unique=True, index=True)
    stripe_payment_intent_id: Mapped[str] = mapped_column(String, unique=True, index=True)

    # BarBox identifiers (NO session FK - payments are independent of session lifecycle)
    player_id: Mapped[Annotated[UUID, fk_to(Player)]]
    box_id: Mapped[Annotated[UUID, fk_to(Box)]]  # Where payment initiated (for revenue attribution)

    # Payment details
    amount_cents: Mapped[int]
    credits_purchased: Mapped[int]  # Extracted from Stripe Price metadata
    bonus_credits: Mapped[int] = mapped_column(default=0)
    selected_price_id: Mapped[str | None]  # Which Stripe Price was selected

    # Payment method tracking
    payment_method: Mapped[str]  # 'checkout_session'
    payment_method_type: Mapped[str | None]  # 'card', 'apple_pay', 'google_pay' (for analytics)

    # Status tracking
    status: Mapped[str]  # 'pending', 'processing', 'succeeded', 'failed', 'refunded'
    completed_at: Mapped[datetime | None]

    # Reconciliation links (nullable - set after credit event issued)
    credit_event_id: Mapped[UUID | None]  # FK to BoxSessionEvent for reconciliation
    credited_to_session_id: Mapped[UUID | None]  # Audit trail only (not FK)
    credited_at: Mapped[datetime | None]  # When credits were issued

    # Metadata
    metadata: Mapped[JsonObject]


class StripeWebhookEvent(Base):
    """Idempotency tracking for webhook processing"""
    __tablename__ = "stripe_webhook_event"

    id: Mapped[UUID] = mapped_column(primary_key=True)
    created_at: Mapped[datetime] = mapped_column(default=lambda: datetime.now(UTC))

    # Stripe webhook identifiers
    stripe_event_id: Mapped[str] = mapped_column(String, unique=True, index=True)
    stripe_event_type: Mapped[str]

    # Processing status
    processed: Mapped[bool] = mapped_column(default=False)
    processed_at: Mapped[datetime | None]
    processing_error: Mapped[str | None]

    # Payment intent reference
    payment_intent_id: Mapped[Annotated[UUID, fk_to(StripePaymentIntent)] | None]

    # Raw event data (for debugging/replay)
    event_data: Mapped[JsonObject]
```

**Backend Endpoints:**

```python
# web/payments.py

from sqlalchemy.exc import IntegrityError

# Credit pack definitions with correct amounts (1,000 credits = $1)
CREDIT_PACKS = {
    "pack_5": {"price_id": env.STRIPE_PRICE_5_CREDITS, "credits": 5000, "bonus": 0, "amount_cents": 500},
    "pack_10": {"price_id": env.STRIPE_PRICE_10_CREDITS, "credits": 10000, "bonus": 0, "amount_cents": 1000},
    "pack_25": {"price_id": env.STRIPE_PRICE_25_CREDITS, "credits": 25000, "bonus": 3000, "amount_cents": 2500},
    "pack_50": {"price_id": env.STRIPE_PRICE_50_CREDITS, "credits": 50000, "bonus": 10000, "amount_cents": 5000},
    "pack_100": {"price_id": env.STRIPE_PRICE_100_CREDITS, "credits": 100000, "bonus": 25000, "amount_cents": 10000},
}


STALE_SESSION_HOURS = 24  # Sessions older than this are considered stale


async def get_or_create_credit_session(
    player_id: UUID,
    box_id: UUID,
    db_service: dependencies.Database,
    now: datetime,
) -> UUID:
    """
    Get session for credit event attribution.

    Strategy:
    1. Prefer existing active lobby session (player logged in, not stale)
    2. If no active lobby, create ephemeral "payment" session

    Returns session_id only (not the full session object).
    """
    stale_threshold = now - timedelta(hours=STALE_SESSION_HOURS)

    # Check for existing active, non-stale lobby session
    result = await db_service.session.execute(
        select(db.defs.BoxSession.id).where(
            db.defs.BoxSession.host_player_id == player_id,
            db.defs.BoxSession.box_id == box_id,
            db.defs.BoxSession.session_type == "lobby",
            db.defs.BoxSession.end_time.is_(None),  # Still active
            db.defs.BoxSession.start_time > stale_threshold,  # Not stale
        )
    )
    existing_session_id = result.scalar_one_or_none()

    if existing_session_id:
        logger.info("credit_session_using_lobby", session_id=str(existing_session_id))
        return existing_session_id

    # Create ephemeral "payment" session (player logged out or stale session)
    session_id = uuid4()
    await db_service.create(
        target=db.defs.BoxSession,
        data={
            "id": session_id,
            "box_id": box_id,
            "host_player_id": player_id,
            "player_ids": [str(player_id)],
            "game_tag": "payment",
            "session_type": "payment",  # Ephemeral, just for credit event
            "start_time": now,
            "end_time": now,  # Immediately closed
        },
    )
    logger.info("credit_session_created_ephemeral", session_id=str(session_id))

    return session_id


@router.post("/payments/checkout/create")
async def create_checkout_session(
    request: structures.CheckoutSessionRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    authenticated_player: dependencies.AuthenticatedPlayer,
) -> structures.CheckoutSessionResponse:
    """
    Create Stripe Checkout Session for a specific credit pack.
    Player has already selected the pack in BuyCreditsModal.

    LOW-LIFT FLOW:
    - Only creates Stripe session (no local database state)
    - Player can dismiss QR without consequence
    - StripePaymentIntent record created ONLY on webhook (after actual payment)

    Authentication: Requires both Box API key AND Player JWT token.
    """
    # Validate pack_id
    if request.pack_id not in CREDIT_PACKS:
        raise HTTPException(status_code=400, detail=f"Invalid pack_id: {request.pack_id}")

    pack = CREDIT_PACKS[request.pack_id]

    # Create Checkout Session with SINGLE selected pack
    # NOTE: No local database record created here - payment record created on webhook only
    checkout_session = stripe.checkout.Session.create(
        line_items=[
            {"price": pack["price_id"], "quantity": 1},
        ],
        mode="payment",
        metadata={
            # Include identifiers for webhook to use when payment completes
            "player_id": str(authenticated_player),
            "box_id": str(authenticated_box.id),
            "pack_id": request.pack_id,
        },
        success_url=env.STRIPE_SUCCESS_URL,  # Configurable via environment
        cancel_url=env.STRIPE_CANCEL_URL,
    )

    return structures.CheckoutSessionResponse(
        session_id=checkout_session.id,
        session_url=checkout_session.url,
    )


@router.post("/payments/webhook")
async def stripe_webhook(
    request: Request,
    db_service: dependencies.Database,
    now: dependencies.Now,
):
    """
    Handle Stripe webhooks for Checkout Session completions.

    CRITICAL SAFETY PATTERNS:
    1. Signature verification
    2. Atomic idempotency (INSERT ON CONFLICT - prevents race conditions)
    3. Transaction wrapping (all-or-nothing - prevents partial failures)
    4. Payment-first architecture (StripePaymentIntent is source of truth)
    5. Global credits with venue tracking (box_id for revenue attribution)
    """
    payload = await request.body()
    signature = request.headers.get("Stripe-Signature")

    if not signature:
        raise HTTPException(status_code=400, detail="Missing Stripe-Signature header")

    # 1. Verify signature
    try:
        event = stripe.Webhook.construct_event(
            payload, signature, env.STRIPE_WEBHOOK_SECRET
        )
    except stripe.error.SignatureVerificationError as e:
        logger.error("webhook_signature_invalid", error=str(e))
        raise HTTPException(status_code=400, detail="Invalid signature")
    except ValueError as e:
        logger.error("webhook_payload_invalid", error=str(e))
        raise HTTPException(status_code=400, detail="Invalid payload")

    if event.type != "checkout.session.completed":
        return {"status": "ignored", "event_type": event.type}

    session_data = event.data.object

    # 2. ATOMIC IDEMPOTENCY - Use INSERT ON CONFLICT (prevents race conditions)
    from sqlalchemy.dialects.sqlite import insert as sqlite_insert

    webhook_event_id = uuid4()
    stmt = sqlite_insert(db.defs.StripeWebhookEvent).values(
        id=webhook_event_id,
        stripe_event_id=event.id,
        stripe_event_type=event.type,
        processed=False,  # Will be set to True after success
        event_data=event.to_dict(),
    ).on_conflict_do_nothing(index_elements=['stripe_event_id'])

    result = await db_service.session.execute(stmt)
    await db_service.session.commit()

    if result.rowcount == 0:
        logger.info("webhook_already_claimed", event_id=event.id)
        return {"status": "already_processed"}

    logger.info("webhook_claimed", event_id=event.id, webhook_id=str(webhook_event_id))

    # 3. Process in single transaction (all-or-nothing)
    try:
        async with db_service.session.begin():
            # 3a. Retrieve line items via API (not in webhook payload)
            checkout_session = stripe.checkout.Session.retrieve(
                session_data.id,
                expand=['line_items.data.price']
            )
            logger.info("webhook_session_retrieved", session_id=session_data.id)

            # 3b. Validate line items exist
            if not checkout_session.line_items or not checkout_session.line_items.data:
                raise ValueError("No line items in session")

            # 3c. Extract metadata
            player_id = UUID(session_data.metadata["player_id"])
            box_id = UUID(session_data.metadata["box_id"])
            pack_id = session_data.metadata.get("pack_id")

            # 3d. Get credits from Price metadata
            line_item = checkout_session.line_items.data[0]
            price = line_item.price

            if "credits" not in price.metadata:
                raise ValueError(f"Price {price.id} missing credits metadata")

            credits = int(price.metadata["credits"])
            bonus = int(price.metadata.get("bonus_credits", "0"))
            logger.info("webhook_credits_extracted", credits=credits, bonus=bonus)

            # 3e. Get/create session for credit event (prefer lobby, else ephemeral)
            credit_session_id = await get_or_create_credit_session(
                player_id=player_id,
                box_id=box_id,
                db_service=db_service,
                now=now,
            )

            # 3f. Create payment record FIRST (source of truth)
            payment_intent_id = uuid4()
            credit_event_id = uuid4()

            db_service.session.add(db.defs.StripePaymentIntent(
                id=payment_intent_id,
                stripe_session_id=session_data.id,
                stripe_payment_intent_id=session_data.payment_intent,
                player_id=player_id,
                box_id=box_id,  # For venue revenue attribution
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
                metadata={"pack_id": pack_id},
            ))

            # 3g. Emit credit/earn event (global credits, references payment)
            db_service.session.add(db.defs.BoxSessionEvent(
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
            ))

            # 3h. Mark webhook as processed
            await db_service.session.execute(
                update(db.defs.StripeWebhookEvent)
                .where(db.defs.StripeWebhookEvent.id == webhook_event_id)
                .values(processed=True, processed_at=now)
            )

        logger.info(
            "payment_completed",
            player_id=str(player_id),
            session_id=str(credit_session_id),
            credits=credits + bonus,
            amount_cents=session_data.amount_total,
        )
        return {"status": "success"}

    except Exception as e:
        # Mark webhook as failed (allows investigation/retry)
        await db_service.session.execute(
            update(db.defs.StripeWebhookEvent)
            .where(db.defs.StripeWebhookEvent.id == webhook_event_id)
            .values(processing_error=str(e))
        )
        await db_service.session.commit()
        logger.error("webhook_processing_failed", event_id=event.id, error=str(e))
        raise HTTPException(status_code=500, detail="Processing failed")
```

**Stripe CLI Commands for Price Creation:**

```bash
# Create $5 pack: 5,000 credits (no bonus)
stripe prices create \
  --unit-amount=500 \
  --currency=usd \
  --product=prod_BarBoxCredits \
  --metadata[credits]=5000 \
  --metadata[bonus_credits]=0

# Create $10 pack: 10,000 credits (no bonus)
stripe prices create \
  --unit-amount=1000 \
  --currency=usd \
  --product=prod_BarBoxCredits \
  --metadata[credits]=10000 \
  --metadata[bonus_credits]=0

# Create $25 pack: 28,000 credits (25k base + 3k bonus)
stripe prices create \
  --unit-amount=2500 \
  --currency=usd \
  --product=prod_BarBoxCredits \
  --metadata[credits]=25000 \
  --metadata[bonus_credits]=3000

# Create $50 pack: 60,000 credits (50k base + 10k bonus)
stripe prices create \
  --unit-amount=5000 \
  --currency=usd \
  --product=prod_BarBoxCredits \
  --metadata[credits]=50000 \
  --metadata[bonus_credits]=10000

# Create $100 pack: 125,000 credits (100k base + 25k bonus)
stripe prices create \
  --unit-amount=10000 \
  --currency=usd \
  --product=prod_BarBoxCredits \
  --metadata[credits]=100000 \
  --metadata[bonus_credits]=25000
```

**Tasks:**
- [x] Add `stripe` Python SDK to `pyproject.toml` (`stripe>=6.0.0`)
- [x] Create database models in `db/defs.py` (StripePaymentIntent, StripeWebhookEvent)
- [ ] Create 5 Stripe Prices with metadata (via Stripe CLI or Dashboard):
  - $5 pack: `metadata = {"credits": "5000", "bonus_credits": "0"}`
  - $10 pack: `metadata = {"credits": "10000", "bonus_credits": "0"}`
  - $25 pack: `metadata = {"credits": "25000", "bonus_credits": "3000"}`
  - $50 pack: `metadata = {"credits": "50000", "bonus_credits": "10000"}`
  - $100 pack: `metadata = {"credits": "100000", "bonus_credits": "25000"}`
- [x] Implement `get_or_create_credit_session()` helper function
  - Check for active, non-stale lobby session (24-hour staleness threshold)
  - Create ephemeral "payment" session if player logged out
  - Return session_id for credit event attribution
- [x] Implement `/payments/checkout/create` endpoint (LOW-LIFT)
  - Accept `pack_id` in request body
  - Create Stripe Checkout Session only (no local database state)
  - Include metadata (player_id, box_id, pack_id)
  - Return session URL for QR code generation
- [x] Implement `/payments/webhook` endpoint (ATOMIC)
  - Use `INSERT ON CONFLICT` for atomic idempotency
  - Wrap all DB operations in single transaction
  - Create `StripePaymentIntent` record FIRST (source of truth)
  - Emit `credit/earn` event with `global: True` and payment reference
  - Link payment to credit event via `credit_event_id` FK
- [ ] Implement reconciliation endpoints (PHASE 1 REQUIRED):
  - `GET /admin/payments/reconciliation` - find payment/credit mismatches
  - `POST /admin/payments/{id}/retry-credit` - manual credit issuance
- [x] Add DTOs to `structures.py`:
  - `CheckoutSessionRequest` with `pack_id`
  - `CheckoutSessionResponse` with `session_url`, `session_id`
  - `ReconciliationReport` with missing_credits, orphan_credits
  - `RetryResult` with status, credits
- [x] Configure environment variables in `env.py`:
  - `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`
  - `STRIPE_SUCCESS_URL`, `STRIPE_CANCEL_URL`
  - `STRIPE_PRICE_*` for each credit pack
- [x] Write Hurl integration tests:
  - Checkout session creation (low-lift, no DB state)
  - Webhook processing (atomic, all-or-nothing)
  - Idempotency race condition simulation
  - Reconciliation endpoint tests

**Deliverable:** Backend can create Checkout Sessions, process webhooks atomically, and reconcile payment/credit mismatches

---

### Phase 2: Godot Client Core (Week 3)

**Goal:** QR code display (from Checkout Session URL) and balance polling in Godot

**Implementation:**

```csharp
// BarBoxApp/_Core/Scripts/Autoloads/_Infrastructure/StripePaymentService.cs

public partial class StripePaymentService : Node, IPaymentService
{
    private EventService _eventService;
    private CreditService _creditService;
    private QRCodeCache _qrCache;

    private const float QR_POLL_TIMEOUT = 120.0f;  // 2 minutes (pack already selected)
    private const float POLL_INTERVAL = 1.0f;

    public override async Task<PaymentResult> ProcessPurchaseAsync(Guid playerId, CreditPack pack)
    {
        // 1. Request Checkout Session from backend (with selected pack)
        var request = new CheckoutSessionRequest { PackId = pack.PackId };
        var response = await _eventService.PostAsync<CheckoutSessionRequest, CheckoutSessionResponse>(
            "/payments/checkout/create",
            request
        );

        if (!response.IsSuccess(out var sessionData))
            return PaymentResult.Failure("Failed to create checkout session");

        // 2. Generate QR code from Checkout Session URL
        var qrTexture = _qrCache.GetOrCreateQRCode(
            sessionData.SessionUrl,
            sessionData.SessionId
        );

        if (qrTexture == null)
            return PaymentResult.Failure("Failed to generate QR code");

        // 3. Display QR code modal with pack info
        ShowQRCodeModal(qrTexture, pack);

        // 4. Get initial balance before payment
        var initialBalance = await _creditService.GetBalanceAsync(playerId);

        // 5. Poll for balance increase
        var confirmed = await PollForBalanceIncreaseWithBackoff(
            playerId,
            initialBalance.Value,
            QR_POLL_TIMEOUT
        );

        HideQRCodeModal();

        // 6. Clear cache (unique URL per session)
        _qrCache.ClearPaymentCache(sessionData.SessionUrl);

        if (confirmed)
        {
            var newBalance = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
            var creditsAdded = newBalance.Value - initialBalance.Value;
            return PaymentResult.Success(creditsAdded);
        }

        return PaymentResult.Timeout("Payment not detected within 2 minutes");
    }

    private void ShowQRCodeModal(Texture2D qrTexture, CreditPack pack)
    {
        _qrCodeDisplay.Texture = qrTexture;
        _qrCodeDisplay.Visible = true;

        _instructionsLabel.Text = $"Scan to pay ${pack.Price:F2}\n" +
                                  $"for {pack.TotalCredits:N0} credits";
    }

    protected async Task<bool> PollForBalanceIncreaseWithBackoff(
        Guid playerId,
        int initialBalance,
        float timeoutSeconds
    )
    {
        var startTime = Time.GetTicksMsec();
        float currentInterval = POLL_INTERVAL;
        const float MAX_INTERVAL = 5.0f;
        const float BACKOFF_MULTIPLIER = 1.5f;

        while ((Time.GetTicksMsec() - startTime) / 1000.0f < timeoutSeconds)
        {
            var balanceResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);

            if (balanceResult.IsSuccess(out var currentBalance) && currentBalance > initialBalance)
            {
                return true;
            }

            await DelayAsync(currentInterval);
            currentInterval = Math.Min(currentInterval * BACKOFF_MULTIPLIER, MAX_INTERVAL);
        }

        return false;
    }
}
```

**CreditPack Struct (Updated for 1,000 credits = $1):**

```csharp
// BarBoxApp/_Core/Scripts/Autoloads/_Infrastructure/IPaymentService.cs

public struct CreditPack
{
    public string PackId { get; init; }          // e.g., "pack_25"
    public int Credits { get; init; }            // e.g., 25000
    public int BonusCredits { get; init; }       // e.g., 3000
    public decimal Price { get; init; }          // e.g., 25.00m
    public string PriceId { get; init; }         // Stripe Price ID

    public int TotalCredits => Credits + BonusCredits;

    public string DisplayName => BonusCredits > 0
        ? $"{TotalCredits:N0} Credits (${Price:F2}) - {BonusCredits:N0} bonus!"
        : $"{Credits:N0} Credits (${Price:F2})";
}

// Available packs
public static readonly CreditPack[] AvailablePacks = new[]
{
    new CreditPack { PackId = "pack_5", Credits = 5000, BonusCredits = 0, Price = 5.00m },
    new CreditPack { PackId = "pack_10", Credits = 10000, BonusCredits = 0, Price = 10.00m },
    new CreditPack { PackId = "pack_25", Credits = 25000, BonusCredits = 3000, Price = 25.00m },
    new CreditPack { PackId = "pack_50", Credits = 50000, BonusCredits = 10000, Price = 50.00m },
    new CreditPack { PackId = "pack_100", Credits = 100000, BonusCredits = 25000, Price = 100.00m },
};
```

**QR Code Caching Notes (Checkout Sessions):**

Each Checkout Session generates a **unique URL**, so:
- **Cache hit rate for new purchases:** ~0% (unique URL each time)
- **Cache hit rate for retries/timeouts:** ~99% (same session URL)
- **Clear cache after each payment attempt** (success or timeout)

```csharp
// Cache strategy for Checkout Sessions
// - Short TTL (session expires in 24h, but we clear after 2min timeout)
// - Clear immediately after payment completion or timeout
// - No cross-attempt caching (each purchase = new session URL)
```

**Tasks:**
- [x] Add QRCoder NuGet package to `BarBoxApp.csproj` (`QRCoder >= 1.6.0`)
- [x] Implement `QRCodeCache.cs` autoload service
- [x] Implement `StripePaymentService.cs` class
  - Accept `CreditPack` parameter (pack selected in-game)
  - Request Checkout Session with `pack_id`
  - Generate QR from session URL
  - Poll for balance increase (120s timeout - pack already selected)
- [x] Update `BuyCreditsModal.cs`:
  - Keep existing pack selection UI
  - After pack selection, show QR code with "Scan to pay $X"
  - Clear instructions (pack already chosen)
- [x] Update `PaymentService.cs` for Checkout Sessions
- [x] Add DTOs to `BackendStructures.cs`
- [ ] Test QR scanability on real hardware

**Deliverable:** Godot client can display Checkout Session QR codes and poll for payment completion

---

### Phase 3: Testing & Production Hardening (Week 4)

**Goal:** Comprehensive testing and production readiness

**Testing Strategy:**

**Local Development:**
```bash
# 1. Create Stripe Prices with correct metadata (one-time setup)
# Use Stripe CLI commands from Phase 1

# 2. Start backend with test keys
cd BarBoxServices
export STRIPE_SECRET_KEY=sk_test_...
export STRIPE_WEBHOOK_SECRET=whsec_...
sh scripts/dev.sh

# 3. Forward webhooks to localhost
stripe listen --forward-to localhost:8000/payments/webhook

# 4. Test with Stripe test mode
# - Select credit pack in BuyCreditsModal (e.g., $25)
# - Scan QR code with phone
# - Confirm payment (Apple Pay / test card)
# - Verify credits appear in Godot app
# - Verify success message shows "Added 28,000 credits!"
```

**Integration Tests (Hurl):**
```
test/02-feature/payments/
├── checkout-session-creation.hurl      # Test Checkout Session endpoint
├── webhook-checkout-completed.hurl     # Test webhook with line_items retrieval
├── webhook-idempotency.hurl            # Test duplicate webhook handling
├── webhook-race-condition.hurl         # Test concurrent webhook protection
└── payment-box-tracking.hurl           # Test box_id in metadata
```

**Real Device Testing:**
- [ ] Test with iPhone (Apple Pay) - select $25 pack, verify 28,000 credits
- [ ] Test with Android phone (Google Pay) - select $10 pack, verify 10,000 credits
- [ ] Test manual card entry fallback
- [ ] Verify QR code displays with correct amount ("Scan to pay $25")
- [ ] Test payment timeout scenarios
- [ ] Test duplicate webhook handling

**Tasks:**
- [x] Write Hurl integration tests
- [ ] Test with Stripe CLI webhook forwarding
- [ ] Test with real phones (iPhone + Android)
- [ ] Verify metadata flows correctly through webhooks
- [ ] Load test: 100 concurrent Checkout Session creations
- [ ] Verify race condition protection in webhook handler

**Deliverable:** Fully tested payment system ready for production

---

### Phase 4: Production Deployment & Monitoring

**Goal:** Deploy to production with monitoring and operational procedures

**Security:**
- [ ] Rate limiting on payment endpoints
- [ ] Audit logging for all payment events
- [ ] Review webhook signature verification
- [ ] Validate no secrets in client code
- [ ] Complete PCI SAQ-A questionnaire

**Monitoring:**
- [ ] Stripe webhook delivery success rate
- [ ] Payment success rate dashboard
- [ ] Payment timeout rate monitoring

**Edge Cases:**
- [ ] Refund workflow (manual process)
- [ ] Manual credit adjustment endpoint
- [ ] Checkout Session expiration handling

**Deployment:**
- [ ] Set up production Stripe account
- [ ] Create production Prices with correct metadata
- [ ] Configure production webhook endpoint URL
- [ ] Deploy to first location
- [ ] Monitor first week of transactions

**Deliverable:** Production-ready QR code payment system

---

## Critical Implementation Details

### 1. Webhook Line Items Retrieval (CRITICAL)

**Problem:** Webhook payload does NOT auto-expand line_items.

**Solution:** Explicit API call to retrieve line_items:

```python
# WRONG - line_items not in webhook payload
line_item = session_data.line_items.data[0]  # AttributeError!

# CORRECT - retrieve via API with expand
checkout_session = stripe.checkout.Session.retrieve(
    session_data.id,
    expand=['line_items.data.price']
)
line_item = checkout_session.line_items.data[0]
```

### 2. Race Condition Prevention (CRITICAL)

**Problem:** Two webhooks could pass idempotency check simultaneously.

**Solution:** Record webhook FIRST with database constraint:

```python
# CORRECT ORDER:
# 1. Record webhook event FIRST (database constraint prevents duplicates)
try:
    await db_service.create(target=StripeWebhookEvent, data={...})
except IntegrityError:
    return {"status": "already_processed"}  # Caught by DB constraint

# 2. THEN emit credit event (only if webhook recorded successfully)
await db_service.create(target=BoxSessionEvent, data={...})
```

### 3. QR Code Caching for Checkout Sessions

**Key Difference from Payment Links:**
- Payment Links: Reusable URL, high cache hit rate
- Checkout Sessions: Unique URL per session, low cache hit rate

**Cache Strategy:**
```csharp
// Each Checkout Session has unique URL
// Cache only helps for retries during same payment attempt
// Clear cache after payment completes or times out

_qrCache.ClearPaymentCache(sessionData.SessionUrl);  // Always clear after attempt
```

---

## Implementation Notes (December 2025)

### Actual Implementation Values

| Setting | Planned | Implemented | Notes |
|---------|---------|-------------|-------|
| Poll timeout | 120s | 120s | Matches plan |
| Initial poll interval | 1.0s | 1.0s | Matches plan |
| Max poll interval | 5.0s | 3.0s | Slightly faster polling |
| Poll backoff | 1.5x | +0.5s linear | Simpler linear increase |
| QR cache max entries | 50 | 20 | Smaller cache, sufficient for typical usage |
| QR module size | 10px | 10px | Matches plan |
| QR display size | 250x250 | 450x450 | 80% larger for better scanability |

### Thread Safety Patterns

**QRCodeCache:**
- Uses `lock(_cacheLock)` for all dictionary operations
- Prevents race conditions in concurrent QR code requests
- LRU eviction is O(n) but acceptable for 20-entry cache

**StripePaymentService:**
- Implements `IDisposable` for proper cleanup
- Disposes `CancellationTokenSource` on payment cancel/complete
- Adds `IsInstanceValid()` checks after async boundaries

### Key Implementation Decisions

1. **Stripe session URL validation** - Validates URL starts with `https://checkout.stripe.com/` before generating QR code (security measure)

2. **Balance polling strategy** - Polls for ANY balance increase rather than specific amount, simplifying detection logic

3. **Event-driven QR display** - Uses `OnQRCodeReady` event to decouple QR generation from UI display

4. **Provider selection** - `FORCE_STRIPE_PROVIDER` constant allows testing Stripe in development without modifying production code path

---

## Cost Analysis

### Stripe Transaction Fees

**QR Code Payments (Online Rate):**
- 2.9% + $0.30 per transaction
- $5 purchase = $0.45 (9%)
- $25 purchase = $1.03 (4.1%)
- $100 purchase = $3.20 (3.2%)

### Hardware Costs
- **QR Code Approach: $0** (players use their own phones)
- Alternative S700 Reader: $349 per location (saved)

**Savings: $1,745 upfront hardware costs (5 locations)**

---

## Risk Assessment

### Overall Risk: LOW (after architecture review mitigations)

| Risk | Severity | Status | Mitigation |
|------|----------|--------|------------|
| Webhook line_items not expanded | HIGH | ✅ MITIGATED | Explicit Session.retrieve() with expand |
| Race conditions in webhook | HIGH | ✅ MITIGATED | Atomic `INSERT ON CONFLICT` idempotency |
| Duplicate credits | HIGH | ✅ MITIGATED | DB constraint + atomic transaction |
| Credit loss on partial failure | HIGH | ✅ MITIGATED | Transaction wrapping (all-or-nothing) |
| Session lifecycle coupling | MEDIUM | ✅ MITIGATED | Payment-centric architecture |
| Stale session detection | MEDIUM | ✅ MITIGATED | 24-hour staleness threshold |
| QR code scanning issues | LOW | ✅ MITIGATED | Large QR codes, clear instructions |
| Orphan checkout sessions | LOW | N/A | Low-lift flow - no local state created |

### Additional Issues to Address

| Issue | Priority | Fix |
|-------|----------|-----|
| Hardcoded success/cancel URLs | Medium | Move to `env.py` (done in updated code) |
| Missing logging for credit calculation | Medium | Add info-level logs (done in updated code) |
| HTTP 400 for data errors | Low | Use 500 for unexpected data |
| Rate limiting on checkout create | High | Add before production |

---

## Recommendation

✅ **PROCEED WITH PAYMENT-CENTRIC CHECKOUT SESSIONS IMPLEMENTATION**

This plan:
1. **Payment-first architecture** - `StripePaymentIntent` is source of truth
2. **Atomic transactions** - All webhook operations succeed or fail together
3. **Global credits** - Spendable at any location, venue-tracked for revenue
4. **Low-lift checkout** - No local state until payment confirmed
5. **Phase 1 reconciliation** - Admin endpoints before production
6. **In-game pack selection** - Player knows exact cost before scanning QR
7. **Fast UX** - 10-20 seconds with Apple/Google Pay
8. **Correct credit amounts** - 1,000 credits = $1 USD

**Why Checkout Sessions Over Payment Links:**
- Payment Links purchase ALL line_items together (cannot do mutually exclusive selection)
- Checkout Sessions allow SINGLE pre-selected pack
- Simpler UX (player knows amount before scanning)
- Each session has unique URL (proper single-use behavior)

---

## Next Steps (Post-Implementation)

### Pre-Production Checklist

1. **Stripe Configuration:**
   - [ ] Create 5 Stripe Prices with correct metadata (use CLI commands in Phase 1)
   - [ ] Configure production webhook endpoint URL
   - [ ] Set environment variables on production server

2. **Production Testing:**
   - [ ] Test with Stripe CLI webhook forwarding
   - [ ] Test QR code scanability on real iPhone (Apple Pay)
   - [ ] Test QR code scanability on real Android (Google Pay)
   - [ ] Validate credit amounts match expected values

3. **Remaining Implementation:**
   - [ ] Implement reconciliation admin endpoints
   - [ ] Add rate limiting on payment endpoints
   - [ ] Set up payment monitoring dashboard

4. **Deployment:**
   - [ ] Deploy to first location
   - [ ] Monitor first week of transactions
   - [ ] Verify webhook delivery success rate

---

## Appendix A: Why Checkout Sessions Over Payment Links

### The Problem with Payment Links

**Payment Links with multiple line_items purchase ALL items together:**

```python
# FUNDAMENTALLY BROKEN - charges $190 for ALL packs!
payment_link = stripe.PaymentLink.create(
    line_items=[
        {"price": price_5, "quantity": 1},
        {"price": price_10, "quantity": 1},
        {"price": price_25, "quantity": 1},
        {"price": price_50, "quantity": 1},
        {"price": price_100, "quantity": 1},
    ],
)
# Customer would be charged $5 + $10 + $25 + $50 + $100 = $190!
```

**Stripe Payment Links do NOT support mutually exclusive product selection.** All `line_items` are purchased together.

### The Solution: Checkout Sessions

```python
# CORRECT - single pack, selected in-game
checkout_session = stripe.checkout.Session.create(
    line_items=[
        {"price": selected_pack_price_id, "quantity": 1},  # SINGLE pack
    ],
    mode="payment",
    metadata={"pack_id": "pack_25", ...},
)
```

**Benefits:**
- Pack selected in BuyCreditsModal (existing UI)
- Single line_item per session
- Player knows exact amount before scanning
- Unique URL per session (proper single-use)

---

## Appendix B: Architecture Review Findings (December 2025)

### Issues Identified and Resolved

| Issue | Original State | Resolution |
|-------|---------------|------------|
| Payment Links API Limitation | Cannot do mutually exclusive selection | ✅ Use Checkout Sessions |
| Credit Conversion | 1:1 was wrong | ✅ Corrected to 1,000:1 |
| Webhook line_items | Not auto-expanded | ✅ Explicit Session.retrieve() with expand |
| Idempotency race condition | Check-then-insert pattern | ✅ Atomic `INSERT ON CONFLICT` pattern |
| Non-atomic operations | Separate DB inserts could partially fail | ✅ Transaction wrapping (all-or-nothing) |
| Session lifecycle coupling | Payments tied to lobby sessions | ✅ Payment-centric architecture |
| Stale session detection | Only checked `end_time IS NULL` | ✅ 24-hour staleness threshold |
| Payment-credit relationship | JSON payload only | ✅ `credit_event_id` FK for reconciliation |
| Low-lift checkout | Local state created on QR generation | ✅ No local state until webhook |

### Validated Patterns
- Event sourcing integrity (credit/earn events only)
- Credit session handling (prefer lobby, else ephemeral)
- Balance polling with exponential backoff
- QR code caching with proper cleanup
- Global credits with venue tracking

### Key Architecture Decisions
- **Payment-First:** `StripePaymentIntent` is source of truth
- **Atomic Transactions:** All webhook DB operations in single transaction
- **Global Credits:** Spendable at any location (not location-scoped)
- **Venue Tracking:** `box_id` preserved for revenue attribution
- **Low-Lift Checkout:** No local state until payment confirmed
- **Phase 1 Reconciliation:** Admin endpoints before production

---

## Appendix C: QRCoder Library & Performance

### Why QRCoder
- Zero network latency (client-side generation)
- Cross-platform compatible
- Fast caching for retries

### Performance Targets
- First generation: < 20ms
- Cache hit: < 1ms
- Memory: ~200KB per entry, 50 entry max (~10MB)

### Cache Strategy for Checkout Sessions
- Each session has unique URL
- ~0% hit rate on new purchases
- ~99% hit rate on retries
- Clear after each payment attempt

---

## Appendix D: Important Clarifications

### Global Credits vs. Venue Tracking

Credits purchased via Stripe are **global for spending** but **venue-tracked for revenue attribution**:

```
Player buys $25 credits at Venue A
├── StripePaymentIntent.box_id = venue_a_id (for revenue reports)
├── credit/earn event with global: True (spendable anywhere)
└── Player can spend credits at Venue A, B, C, etc.
```

**Bookkeeping Query:**
```sql
-- Revenue generated per venue
SELECT box_id, SUM(amount_cents) as revenue
FROM stripe_payment_intent
WHERE status = 'succeeded'
GROUP BY box_id
```

### Low-Lift Checkout Flow

When a player selects a pack and gets a QR code, it's **non-binding**:

| Action | Local Database State |
|--------|---------------------|
| Player selects pack | None |
| QR code displayed | None (Stripe session only) |
| Player dismisses QR | None (session expires in Stripe) |
| Player completes payment | `StripePaymentIntent` + `credit/earn` event created |

**Implications:**
- No cleanup needed for abandoned checkouts
- No "pending payment" state to manage
- Stripe handles session expiration automatically
- Only confirmed payments create local records
