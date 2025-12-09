# Stripe Payment Integration - Implementation Plan

## Executive Summary

Comprehensive Stripe payment integration for BarBox arcade platform using **QR code mobile payments** with **Checkout Sessions** for in-game credit pack selection.

**Payment Method:** Stripe Checkout Sessions - Player selects pack in-game, then scans QR to pay
- Player selects desired pack ($5, $10, $25, $50, $100) in BuyCreditsModal
- QR code generated from unique Checkout Session URL
- Players scan QR code → opens Stripe-hosted payment page (pack already selected)
- Native Apple Pay/Google Pay support for fast checkout
- Pre-selection in arcade app provides clear UX (player knows exact cost before scanning)

**Architecture:** Checkout Sessions + webhook reconciliation that maintains BarBox's event-sourced credit system integrity. Player selects pack in-game, scans QR code, pays on mobile with wallet, and credits appear immediately via event emission. Uses lazy session creation to ensure lobby session exists for credit attribution.

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
- ✅ **Lobby Session Creation:** Backend creates lobby session on-demand if not exists (lazy creation pattern)
- ✅ **Synchronous UX:** Poll actual balance (not payment status) for confirmation
- ✅ **Webhook Reconciliation:** Idempotency tracking + error handling + session validation
- ✅ **Cache Invalidation:** Force refresh CreditService after payment
- ✅ **Security:** Webhook signature verification, Box+Player authentication
- ✅ **Location Tracking:** Payment metadata includes `box_id` for location mapping in bookkeeping scripts

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
                         │ - Record webhook event FIRST          │
                         │ - Retrieve line_items via API         │
                         │ - Extract credits from Price metadata │
                         │ - Emit credit/earn event              │
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

1. **Single Credit Source** - ONLY `credit/earn` events add credits
2. **Event Sourcing Integrity** - Webhooks emit events, never mutate tables directly
3. **Lazy Lobby Session Creation** - Backend creates lobby session on-demand if player doesn't have one
4. **In-Game Pack Selection** - Credit amount determined by player in BuyCreditsModal before QR generation
5. **Synchronous Balance Polling** - Client polls for ANY balance increase, with exponential backoff
6. **Webhook Robustness** - Record webhook FIRST (prevents race conditions), then emit credit event
7. **Cache Invalidation** - Force refresh after payment to show credits immediately
8. **Box-Based Location Tracking** - All payments include `box_id` for location mapping in bookkeeping scripts

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
    """Track Stripe payments for webhook verification and reconciliation"""
    __tablename__ = "stripe_payment_intent"

    id: Mapped[UUID] = mapped_column(primary_key=True)
    created_at: Mapped[datetime] = mapped_column(default=lambda: datetime.now(UTC))

    # Stripe identifiers
    stripe_session_id: Mapped[str] = mapped_column(String, unique=True, index=True)
    stripe_payment_intent_id: Mapped[str] = mapped_column(String, unique=True, index=True)

    # BarBox identifiers (CRITICAL for event emission)
    player_id: Mapped[Annotated[UUID, fk_to(Player)]]
    box_id: Mapped[Annotated[UUID, fk_to(Box)]]  # For location mapping in bookkeeping
    session_id: Mapped[Annotated[UUID, fk_to(BoxSession)]]  # Lobby session (created on-demand)

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


async def get_or_create_lobby_session(
    player_id: UUID,
    box_id: UUID,
    db_service: dependencies.Database,
    now: datetime,
) -> db.defs.BoxSession:
    """
    Get existing active lobby session or create new one for player.

    Lobby sessions are long-lived and persist while player is logged in.
    All payment events are emitted to the active lobby session.
    """
    # Check for existing active lobby session
    result = await db_service.session.execute(
        select(db.defs.BoxSession).where(
            db.defs.BoxSession.host_player_id == player_id,
            db.defs.BoxSession.box_id == box_id,
            db.defs.BoxSession.session_type == "lobby",
            db.defs.BoxSession.end_time.is_(None),  # Still active
        )
    )
    existing_session = result.scalar_one_or_none()

    if existing_session:
        logger.info("lobby_session_reused", session_id=str(existing_session.id))
        return existing_session

    # Create new lobby session
    session_id = uuid4()
    await db_service.create(
        target=db.defs.BoxSession,
        data={
            "id": session_id,
            "box_id": box_id,
            "host_player_id": player_id,
            "player_ids": [str(player_id)],
            "game_tag": "lobby",
            "session_type": "lobby",
            "start_time": now,
        },
    )
    logger.info("lobby_session_created", session_id=str(session_id))

    result = await db_service.session.execute(
        select(db.defs.BoxSession).where(db.defs.BoxSession.id == session_id)
    )
    return result.scalar_one()


@router.post("/payments/checkout/create")
async def create_checkout_session(
    request: structures.CheckoutSessionRequest,
    authenticated_box: dependencies.BoxAuthenticated,
    authenticated_player: dependencies.AuthenticatedPlayer,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.CheckoutSessionResponse:
    """
    Create Stripe Checkout Session for a specific credit pack.
    Player has already selected the pack in BuyCreditsModal.

    Authentication: Requires both Box API key AND Player JWT token.
    """
    # Validate pack_id
    if request.pack_id not in CREDIT_PACKS:
        raise HTTPException(status_code=400, detail=f"Invalid pack_id: {request.pack_id}")

    pack = CREDIT_PACKS[request.pack_id]

    # Get or create lobby session for player
    lobby_session = await get_or_create_lobby_session(
        player_id=authenticated_player,
        box_id=authenticated_box.id,
        db_service=db_service,
        now=now,
    )

    # Create Checkout Session with SINGLE selected pack
    checkout_session = stripe.checkout.Session.create(
        line_items=[
            {"price": pack["price_id"], "quantity": 1},
        ],
        mode="payment",
        metadata={
            "player_id": str(authenticated_player),
            "box_id": str(authenticated_box.id),
            "session_id": str(lobby_session.id),
            "pack_id": request.pack_id,
        },
        success_url="https://barbox.app/payment/success",  # Placeholder - player returns to game
        cancel_url="https://barbox.app/payment/cancel",
    )

    return structures.CheckoutSessionResponse(
        session_id=checkout_session.id,
        session_url=checkout_session.url,
        lobby_session_id=lobby_session.id,
    )


@router.post("/payments/webhook")
async def stripe_webhook(
    request: Request,
    db_service: dependencies.Database,
    now: dependencies.Now,
):
    """
    Handle Stripe webhooks for Checkout Session completions.
    Emits credit/earn event to player's lobby session (creates if needed).

    CRITICAL: This handler implements all production safety patterns:
    1. Signature verification
    2. Idempotency via database constraint (record webhook FIRST)
    3. Explicit line_items retrieval via API (not in webhook payload)
    4. Proper error handling and edge case validation
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

    # 2. Check idempotency (CORRECTED - assign result, then check)
    result = await db_service.session.execute(
        select(db.defs.StripeWebhookEvent).where(
            db.defs.StripeWebhookEvent.stripe_event_id == event.id
        )
    )
    existing = result.scalar_one_or_none()
    if existing:
        logger.info("webhook_already_processed", event_id=event.id)
        return {"status": "already_processed"}

    if event.type == "checkout.session.completed":
        session_data = event.data.object

        # 3. CRITICAL: Retrieve line items via API (not in webhook payload)
        try:
            checkout_session = stripe.checkout.Session.retrieve(
                session_data.id,
                expand=['line_items.data.price']
            )
        except stripe.error.StripeError as e:
            logger.error("webhook_stripe_api_error", error=str(e))
            raise HTTPException(status_code=502, detail="Failed to retrieve session")

        # 4. Validate line items exist
        if not checkout_session.line_items or not checkout_session.line_items.data:
            logger.error("webhook_no_line_items", session_id=session_data.id)
            raise HTTPException(status_code=400, detail="No line items in session")

        # 5. Extract metadata with validation
        try:
            player_id = UUID(session_data.metadata["player_id"])
            box_id = UUID(session_data.metadata["box_id"])
            original_session_id = UUID(session_data.metadata["session_id"])
        except (KeyError, ValueError) as e:
            logger.error("webhook_invalid_metadata", error=str(e))
            raise HTTPException(status_code=400, detail=f"Invalid metadata: {e}")

        # 6. Get credits from Price metadata
        line_item = checkout_session.line_items.data[0]
        price = line_item.price

        if "credits" not in price.metadata:
            logger.error("webhook_price_missing_credits", price_id=price.id)
            raise HTTPException(status_code=400, detail="Price missing credits metadata")

        credits = int(price.metadata["credits"])
        bonus = int(price.metadata.get("bonus_credits", "0"))

        # 7. Get or create lobby session (handles expiration gracefully)
        lobby_session = await get_or_create_lobby_session(
            player_id=player_id,
            box_id=box_id,
            db_service=db_service,
            now=now,
        )

        # 8. CRITICAL: Record webhook FIRST (prevents race conditions via DB constraint)
        try:
            await db_service.create(
                target=db.defs.StripeWebhookEvent,
                data={
                    "id": uuid4(),
                    "stripe_event_id": event.id,
                    "stripe_event_type": event.type,
                    "processed": True,
                    "processed_at": now,
                    "event_data": event.to_dict(),
                }
            )
        except IntegrityError:
            # Duplicate webhook detected via database constraint
            logger.info("webhook_duplicate_detected", event_id=event.id)
            return {"status": "already_processed"}

        # 9. THEN emit credit/earn event (after webhook recorded)
        event_id = uuid4()
        await db_service.create(
            target=db.defs.BoxSessionEvent,
            data={
                "id": event_id,
                "session_id": lobby_session.id,
                "type": "credit/earn",
                "timestamp": now,
                "payload": {
                    "amount": credits + bonus,
                    "source": "stripe_payment",
                    "box_id": str(box_id),
                    "stripe_session_id": session_data.id,
                    "transaction_id": session_data.payment_intent,
                }
            }
        )

        # Record payment in database
        await db_service.create(
            target=db.defs.StripePaymentIntent,
            data={
                "id": uuid4(),
                "stripe_session_id": session_data.id,
                "stripe_payment_intent_id": session_data.payment_intent,
                "player_id": player_id,
                "box_id": box_id,
                "session_id": lobby_session.id,
                "amount_cents": session_data.amount_total,
                "credits_purchased": credits,
                "bonus_credits": bonus,
                "selected_price_id": price.id,
                "payment_method": "checkout_session",
                "payment_method_type": session_data.payment_method_types[0] if session_data.payment_method_types else None,
                "status": "succeeded",
                "completed_at": now,
                "metadata": {"original_session_id": str(original_session_id)},
            }
        )

        logger.info(
            "payment_completed",
            player_id=str(player_id),
            session_id=str(lobby_session.id),
            credits=credits + bonus,
            amount_cents=session_data.amount_total,
        )

    return {"status": "success"}
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
- [ ] Add `stripe` Python SDK to `pyproject.toml` (`stripe>=6.0.0`)
- [ ] Create database models in `db/defs.py` (StripePaymentIntent, StripeWebhookEvent)
- [ ] Create 5 Stripe Prices with metadata (via Stripe CLI or Dashboard):
  - $5 pack: `metadata = {"credits": "5000", "bonus_credits": "0"}`
  - $10 pack: `metadata = {"credits": "10000", "bonus_credits": "0"}`
  - $25 pack: `metadata = {"credits": "25000", "bonus_credits": "3000"}`
  - $50 pack: `metadata = {"credits": "50000", "bonus_credits": "10000"}`
  - $100 pack: `metadata = {"credits": "100000", "bonus_credits": "25000"}`
- [ ] Implement `get_or_create_lobby_session()` helper function
- [ ] Implement `/payments/checkout/create` endpoint
  - Accept `pack_id` in request body
  - Create Checkout Session with SINGLE line_item
  - Include metadata (player_id, box_id, session_id, pack_id)
  - Return session URL for QR code generation
- [ ] Implement `/payments/webhook` endpoint
  - Record webhook event FIRST (prevents race conditions)
  - Retrieve line_items via explicit API call (expand=['line_items.data.price'])
  - Extract credits from Price metadata
  - Emit `credit/earn` event
- [ ] Add DTOs to `structures.py`:
  - `CheckoutSessionRequest` with `pack_id`
  - `CheckoutSessionResponse` with `session_url`, `session_id`
- [ ] Configure environment variables in `env.py`
- [ ] Write Hurl integration tests

**Deliverable:** Backend can create Checkout Sessions and process webhooks correctly

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
- [ ] Add QRCoder NuGet package to `BarBoxApp.csproj` (`QRCoder >= 1.6.0`)
- [ ] Implement `QRCodeCache.cs` autoload service
- [ ] Implement `StripePaymentService.cs` class
  - Accept `CreditPack` parameter (pack selected in-game)
  - Request Checkout Session with `pack_id`
  - Generate QR from session URL
  - Poll for balance increase (120s timeout - pack already selected)
- [ ] Update `BuyCreditsModal.cs`:
  - Keep existing pack selection UI
  - After pack selection, show QR code with "Scan to pay $X"
  - Clear instructions (pack already chosen)
- [ ] Update `PaymentService.cs` for Checkout Sessions
- [ ] Add DTOs to `BackendStructures.cs`
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
- [ ] Write Hurl integration tests
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

## Timeline & Milestones

| Weeks | Phase | Milestone | Deliverable |
|-------|-------|-----------|-------------|
| 1-2 | Backend Foundation | Backend Checkout Sessions ready | Checkout Session creation + webhook + Price metadata extraction working |
| 3 | Godot Core | QR code display + polling working | Client displays QR codes with in-game pack selection |
| 4 | Testing & Production | Production-ready system | All tests pass, webhook safety verified |
| 5 | Deployment & Monitoring | First location live | Deployed to first location, monitoring in place |

**Total: 5 weeks to production-ready system**

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

### Overall Risk: LOW-MEDIUM

| Risk | Severity | Likelihood | Mitigation |
|------|----------|------------|------------|
| Webhook line_items not expanded | HIGH | N/A | Explicit Session.retrieve() with expand |
| Race conditions in webhook | HIGH | LOW | Database constraint + reordered operations |
| Duplicate credits | HIGH | LOW | StripeWebhookEvent idempotency table |
| Session expiration | LOW | LOW | Lazy session creation handles gracefully |
| QR code scanning issues | LOW | LOW | Large QR codes, clear instructions |

---

## Recommendation

✅ **PROCEED WITH CHECKOUT SESSIONS IMPLEMENTATION**

This plan:
1. **In-game pack selection** - Player knows exact cost before scanning QR
2. **Simpler UX** - No pack selection on mobile device needed
3. **Maintains event-sourced credit integrity** - Credits via `credit/earn` events only
4. **Fast UX** - 10-20 seconds with Apple/Google Pay (pack already selected)
5. **Production-safe webhook handling** - All race conditions and edge cases handled
6. **Correct credit amounts** - 1,000 credits = $1 USD

**Why Checkout Sessions Over Payment Links:**
- Payment Links purchase ALL line_items together (cannot do mutually exclusive selection)
- Checkout Sessions allow SINGLE pre-selected pack
- Simpler UX (player knows amount before scanning)
- Each session has unique URL (proper single-use behavior)

---

## Next Steps After Approval

1. **Stripe Account Setup:**
   - Create Stripe account (test + production)
   - Get API keys
   - **Create 5 Stripe Prices with correct metadata** (use CLI commands above)

2. **Environment Setup:**
   - Add Stripe keys to backend `.env`
   - Add `stripe` library to `pyproject.toml`
   - Install Stripe CLI: `brew install stripe/stripe-cli/stripe`
   - Add QRCoder NuGet package to `BarBoxApp.csproj`

3. **Phase 1 Kickoff:**
   - Create database migrations
   - Implement `/payments/checkout/create` endpoint
   - Implement `/payments/webhook` endpoint with all safety patterns

4. **Testing Plan:**
   - Use Stripe CLI to forward webhooks
   - Test with real iPhone (Apple Pay)
   - Test with real Android phone (Google Pay)
   - Validate correct credit amounts (5,000 / 10,000 / 28,000 / 60,000 / 125,000)

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

## Appendix B: Architecture Review Findings

### Critical Issues Addressed
1. **Payment Links API Limitation** - Cannot do mutually exclusive selection → Use Checkout Sessions
2. **Credit Conversion** - 1:1 was wrong → Corrected to 1,000:1
3. **Webhook line_items** - Not auto-expanded → Explicit Session.retrieve()
4. **Idempotency race condition** - Record webhook FIRST, then emit event
5. **Operation order** - Database constraint prevents duplicate credits

### Validated Patterns
- Event sourcing integrity (credit/earn events only)
- Lazy session creation (get_or_create pattern)
- Balance polling with exponential backoff
- QR code caching with proper cleanup

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
