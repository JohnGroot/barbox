# Stripe Payment Integration - Implementation Plan

## Executive Summary

Comprehensive Stripe payment integration for BarBox arcade platform using **QR code mobile payments** with **Payment Links** for player-selectable credit packs.

**Payment Method:** Stripe Payment Links - Single QR code per purchase, player selects credit pack amount on mobile device
- Players scan QR code → opens Stripe-hosted checkout page
- Player selects desired pack ($5, $10, $25, $50, $100) on their phone
- Native Apple Pay/Google Pay support for fast checkout
- No pre-selection required in arcade app

**Architecture:** Payment Links + webhook reconciliation that maintains BarBox's event-sourced credit system integrity. Player scans one QR code, selects pack amount on mobile, pays with wallet, and credits appear immediately via event emission. Uses lazy session creation to ensure lobby session exists for credit attribution.

**Timeline:** 5 weeks to production-ready system (includes session management updates and comprehensive webhook implementation)
**Risk Level:** MEDIUM (webhook complexity and distributed payment flow require careful implementation, but Payment Links API is well-documented)

---

## Confirmed Requirements

### Payment Methods
1. ✅ **Payment Links** - Primary payment method with Apple Pay/Google Pay support
   - Single QR code per purchase
   - Player selects credit pack amount on mobile device (Stripe-hosted UI)
   - No pre-selection required in arcade app

### Business Requirements
- ✅ **Credit Packs:** $5, $10, $25, $50, $100 with volume discounts
  - Higher packs get bonus credits (e.g., $25 = 28 credits, $50 = 60 credits, $100 = 125 credits)
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

### Data Flow (Payment Links with Mobile Pack Selection)

```
┌─────────────┐  1. Click "Buy Credits"  ┌─────────────────┐
│   Player    │─────────────────────────▶│ BuyCreditsModal │
└─────────────┘  (no pack pre-selected)  └────────┬────────┘
                                                   │
                2. ProcessPurchaseAsync            │
                ┌──────────────────────────────────┘
                │
┌───────────────▼────────┐  3. Request Payment Link
│ StripePaymentService   │  (authenticated with JWT)
│ - Has player session   │
│ - Request payment link │
└───────────┬────────────┘
            │ 4. POST /payments/link/create
┌───────────▼────────────┐
│ Backend                │  5. Get/Create Session   ┌──────────────┐
│ /payments/link/create  │────────────────────────▶ │ SessionMgr   │
│ - Call get_or_create   │  Lazy lobby session      └──────────────┘
│   _lobby_session()     │  creation if needed
│ - Create Payment Link  │  6. Create Payment Link  ┌──────────────┐
│                        │─────────────────────────▶│ Stripe API   │
└───────────┬────────────┘  with ALL 5 packs +      └──────────────┘
            │               metadata (player_id,
            │               box_id, session_id)
            │ 7. Return {payment_link_url, session_id}
┌───────────▼────────────┐
│ BuyCreditsModal        │  8. Display QR code
│ - Generate QR from URL │     "Scan to choose"
│ - Show QR code         │     "credits & pay"
└────────────────────────┘
            │ 9. Player scans QR with phone
┌───────────▼────────────┐
│ Player's Phone         │  10. Opens browser
│ - Safari / Chrome      │─────────────────────────┐
│ - Stripe-hosted page   │                         │
│ - Shows 5 pack options:│  11. Player SELECTS     │
│   • $5 = 5 credits     │      desired pack       │
│   • $10 = 10 credits   │      (e.g., $25)        │
│   • $25 = 28 credits   │                         │
│   • $50 = 60 credits   │                         │
│   • $100 = 125 credits │                         │
└────────────────────────┘                         │
            │                                       │
            │ 12. Player taps Apple/Google Pay      │
            │◀──────────────────────────────────────┘
            │
            │ 13. Payment Authorized
┌───────────▼────────────┐
│ Stripe Webhook         │ 14. checkout.session.  ┌──────────────┐
│ checkout.session.      │     completed          │ Backend      │
│ completed              │───────────────────────▶│ /webhook     │
└────────────────────────┘                        └──────┬───────┘
                                                         │
                15. Extract session data                 │
                ┌────────────────────────────────────────┘
┌───────────────▼────────┐
│ Backend                │  16. Process Payment
│ - Verify signature     │──────────────────┐
│ - Check idempotency    │                  │
│ - Get metadata:        │◀─────────────────┘
│   * player_id          │
│   * box_id             │
│   * session_id         │
│ - Validate/create      │
│   lobby session        │
│ - Extract credits from │
│   selected Price       │
│ - Emit credit/earn     │
│   to session_id        │
└───────────┬────────────┘
            │ 17. Credit/earn event in box_session_event
┌───────────▼────────────┐
│ Client Polling         │  18. GET /player/{id}/credits?box_id={box_id}
│ - Poll for ANY balance │──────────────────┐
│   increase (not        │                  │
│   specific amount)     │                  │
│ - Force cache refresh  │◀─────────────────┘
│ - Timeout 180s         │
└───────────┬────────────┘
            │ 19. Balance increased detected
┌───────────▼────────────┐
│ PaymentService         │  20. Success
│ - Calculate credits    │      (actual amount
│   received             │       received)
│ - Invalidate cache     │
└───────────┬────────────┘
            │ 21. Credits added signal
┌───────────▼────────────┐
│ BuyCreditsModal        │
│ - Hide QR code         │
│ - Show success         │
│ - "Added X credits!"   │
│ - Update balance UI    │
└────────────────────────┘
```

**Total UX Time:** 15-25 seconds with Apple/Google Pay (includes pack selection step), 30-60 seconds with manual card entry

### Key Architectural Principles

1. **Single Credit Source** - ONLY `credit/earn` events add credits
2. **Event Sourcing Integrity** - Webhooks emit events, never mutate tables directly
3. **Lazy Lobby Session Creation** - Backend creates lobby session on-demand if player doesn't have one
4. **Mobile Pack Selection** - Credit amount determined by player on mobile device, not pre-selected
5. **Synchronous Balance Polling** - Client polls for ANY balance increase, not specific amount
6. **Webhook Robustness** - Idempotency checking + error handling + session validation
7. **Cache Invalidation** - Force refresh after payment to show credits immediately
8. **Box-Based Location Tracking** - All payments include `box_id` for location mapping in bookkeeping scripts

---

## Implementation Phases

### Phase 1: Backend Foundation (Weeks 1-2)

**Goal:** Stripe API integration, Payment Links, Stripe Prices, webhook handling

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
    stripe_payment_link_id: Mapped[str | None] = mapped_column(String, index=True)

    # BarBox identifiers (CRITICAL for event emission)
    player_id: Mapped[Annotated[UUID, fk_to(Player)]]
    box_id: Mapped[Annotated[UUID, fk_to(Box)]]  # For location mapping in bookkeeping
    session_id: Mapped[Annotated[UUID, fk_to(BoxSession)]]  # Lobby session (created on-demand)

    # Payment details (determined by player selection on mobile)
    amount_cents: Mapped[int]
    credits_purchased: Mapped[int]  # Extracted from selected Stripe Price metadata
    bonus_credits: Mapped[int] = mapped_column(default=0)
    selected_price_id: Mapped[str | None]  # Which Stripe Price was selected

    # Payment method tracking
    payment_method: Mapped[str]  # 'payment_link' (primary method)
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


@router.post("/payments/link/create")
async def create_payment_link(
    authenticated_box: dependencies.BoxAuthenticated,
    authenticated_player: dependencies.AuthenticatedPlayer,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> structures.PaymentLinkResponse:
    """
    Create Stripe Payment Link with all credit pack options.
    Player selects desired pack on mobile device.

    Authentication: Requires both Box API key AND Player JWT token.
    All context comes from headers - no request body needed.

    Automatically creates lobby session if player doesn't have one active.
    """
    # Get or create lobby session for player
    lobby_session = await get_or_create_lobby_session(
        player_id=authenticated_player,
        box_id=authenticated_box.id,
        db_service=db_service,
        now=now,
    )

    # Create Payment Link with ALL 5 credit packs as line items
    payment_link = stripe.PaymentLink.create(
        line_items=[
            {"price": env.STRIPE_PRICE_5_CREDITS, "quantity": 1},
            {"price": env.STRIPE_PRICE_10_CREDITS, "quantity": 1},
            {"price": env.STRIPE_PRICE_25_CREDITS, "quantity": 1},
            {"price": env.STRIPE_PRICE_50_CREDITS, "quantity": 1},
            {"price": env.STRIPE_PRICE_100_CREDITS, "quantity": 1},
        ],
        metadata={
            "player_id": str(authenticated_player),
            "box_id": str(authenticated_box.id),
            "session_id": str(lobby_session.id),
        },
        after_completion={
            "type": "hosted_confirmation",
            "hosted_confirmation": {
                "custom_message": "Credits added! Return to the game."
            }
        }
    )

    return structures.PaymentLinkResponse(
        payment_link_id=payment_link.id,
        url=payment_link.url,
        session_id=lobby_session.id,  # Return session ID for client tracking
    )


@router.post("/payments/webhook")
async def stripe_webhook(
    request: Request,
    db_service: dependencies.Database,
    now: dependencies.Now,
):
    """
    Handle Stripe webhooks for Payment Link completions.
    Emits credit/earn event to player's lobby session (creates if needed).
    """
    payload = await request.body()
    signature = request.headers.get("Stripe-Signature")

    if not signature:
        raise HTTPException(status_code=400, detail="Missing Stripe-Signature header")

    # Verify signature
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

    # Check idempotency - has this event been processed before?
    existing_event = await db_service.session.execute(
        select(db.defs.StripeWebhookEvent).where(
            db.defs.StripeWebhookEvent.stripe_event_id == event.id
        )
    )
    if existing_event.scalar_one_or_none():
        logger.info("webhook_already_processed", event_id=event.id)
        return {"status": "already_processed"}

    # Handle checkout.session.completed
    if event.type == "checkout.session.completed":
        session_data = event.data.object

        # Extract metadata
        try:
            player_id = UUID(session_data.metadata["player_id"])
            box_id = UUID(session_data.metadata["box_id"])
            original_session_id = UUID(session_data.metadata["session_id"])
        except (KeyError, ValueError) as e:
            logger.error("webhook_invalid_metadata", error=str(e))
            raise HTTPException(status_code=400, detail="Invalid metadata")

        # Validate/create lobby session (handles session expiration gracefully)
        lobby_session = await get_or_create_lobby_session(
            player_id=player_id,
            box_id=box_id,
            db_service=db_service,
            now=now,
        )

        # Extract credits from selected Price (player chose on mobile)
        line_item = session_data.line_items.data[0]
        selected_price = line_item.price
        credits = int(selected_price.metadata["credits"])
        bonus = int(selected_price.metadata.get("bonus_credits", "0"))

        # CRITICAL: Emit credit/earn event to lobby session
        event_id = uuid4()
        await db_service.create(
            target=db.defs.BoxSessionEvent,
            data={
                "id": event_id,
                "session_id": lobby_session.id,
                "type": "credit/earn",
                "timestamp": now,
                "payload": {
                    "box_id": str(box_id),  # For location mapping in bookkeeping
                    "amount": credits + bonus,
                    "reason": f"Stripe payment - ${session_data.amount_total / 100:.2f}",
                    "stripe_session_id": session_data.id,
                    "stripe_payment_intent_id": session_data.payment_intent,
                    "selected_price_id": selected_price.id,
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
                "stripe_payment_link_id": session_data.metadata.get("payment_link_id"),
                "player_id": player_id,
                "box_id": box_id,
                "session_id": lobby_session.id,
                "amount_cents": session_data.amount_total,
                "credits_purchased": credits,
                "bonus_credits": bonus,
                "selected_price_id": selected_price.id,
                "payment_method": "payment_link",
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

    # Record webhook processing
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

    return {"status": "success"}
```

**Tasks:**
- [ ] Add `stripe` Python SDK to `pyproject.toml` (`stripe>=6.0.0`)
- [ ] Create database models in `db/defs.py` (StripePaymentIntent, StripeWebhookEvent)
- [ ] Create 5 Stripe Prices in Stripe Dashboard with metadata:
  - $5 pack: `metadata = {"credits": "5", "bonus_credits": "0"}`
  - $10 pack: `metadata = {"credits": "10", "bonus_credits": "0"}`
  - $25 pack: `metadata = {"credits": "25", "bonus_credits": "3"}`
  - $50 pack: `metadata = {"credits": "50", "bonus_credits": "10"}`
  - $100 pack: `metadata = {"credits": "100", "bonus_credits": "25"}`
- [ ] Implement `get_or_create_lobby_session()` helper function
  - Check for existing active lobby session
  - Create new lobby session if not found
  - Return validated session
- [ ] Implement `/payments/link/create` endpoint in `web/payments.py`
  - No request body needed (all context from auth headers)
  - Call `get_or_create_lobby_session()` for lazy session creation
  - Create Payment Link with ALL 5 prices as line items
  - Include metadata (player_id, box_id, session_id)
  - Return Payment Link URL + session_id
- [ ] Implement `/payments/webhook` endpoint for `checkout.session.completed` events
  - Webhook signature verification with error handling
  - Idempotency checking via StripeWebhookEvent table
  - Extract and validate metadata (player_id, box_id, session_id)
  - Call `get_or_create_lobby_session()` to handle session expiration
  - Extract credits from selected Price metadata
  - Emit `credit/earn` event with box_id in payload (not location_id)
  - Record payment and webhook event in database
- [ ] Add payment DTOs to `structures.py` (PaymentLinkResponse with session_id)
- [ ] Configure environment variables in `env.py`:
  - `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`
  - Stripe Price IDs (STRIPE_PRICE_5_CREDITS, etc.)
- [ ] Update `GET /player/{id}/credits` to accept `box_id` parameter instead of `location_id`
- [ ] Write Hurl integration tests

**Deliverable:** Backend can create Payment Links and process webhooks correctly, extracting credits from player-selected packs

---

### Phase 2: Godot Client Core (Week 3)

**Goal:** QR code display (from Payment Link URL) and balance polling (any increase) in Godot

**Implementation:**

```csharp
// BarBoxApp/_Core/Scripts/Autoloads/_Infrastructure/StripePaymentLinkService.cs

public partial class StripePaymentLinkService : Node, IPaymentService
{
    private EventService _eventService;
    private CreditService _creditService;
    private SessionManager _sessionManager;

    private const float QR_POLL_TIMEOUT = 180.0f;  // 3 minutes for mobile UX + selection
    private const float POLL_INTERVAL = 1.0f;  // Start with 1s interval (exponential backoff)

    public override async Task<PaymentResult> ProcessPurchaseAsync(Guid playerId)
    {
        // 1. Request Payment Link from backend (no request body needed)
        // Authentication via JWT token + Box API key in headers
        // Backend creates lobby session automatically if needed
        var response = await _eventService.PostAsync<PaymentLinkResponse>(
            "/payments/link/create"
        );

        if (!response.IsSuccess(out var paymentLinkData))
            return PaymentResult.Failure("Failed to create payment link");

        // 2. Display QR code (client-side QR generation from URL)
        ShowQRCodeModal(paymentLinkData.Url);

        // 3. Get initial balance before payment
        var initialBalance = await _creditService.GetBalanceAsync(playerId);

        // 4. Poll for ANY balance increase (don't know pack selection yet)
        var confirmed = await PollForBalanceIncreaseWithBackoff(
            playerId,
            initialBalance.Value,
            QR_POLL_TIMEOUT
        );

        HideQRCodeModal();

        if (confirmed)
        {
            // Get new balance to show actual credits received
            var newBalance = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
            var creditsAdded = newBalance.Value - initialBalance.Value;
            return PaymentResult.Success(creditsAdded);
        }

        return PaymentResult.Timeout("Payment not detected within 3 minutes");
    }

    private void ShowQRCodeModal(string paymentLinkUrl)
    {
        // Generate QR code from Payment Link URL (client-side)
        // Using Godot's built-in Image/Texture generation or a QR library
        var qrImage = GenerateQRCode(paymentLinkUrl);
        var texture = ImageTexture.CreateFromImage(qrImage);

        _qrCodeDisplay.Texture = texture;
        _qrCodeDisplay.Visible = true;

        _instructionsLabel.Text = "Scan with your phone\n" +
                                  "Choose credits & pay";
    }

    private Image GenerateQRCode(string url)
    {
        // TODO: Implement QR code generation
        // Option 1: Use a C# QR library (e.g., QRCoder)
        // Option 2: Use Godot addon for QR generation
        // Option 3: Request pre-generated QR from backend (simpler)

        // For now, placeholder:
        return new Image();
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
            // CRITICAL: Force cache refresh to get latest balance
            var balanceResult = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);

            // Check for ANY balance increase (not specific amount)
            if (balanceResult.IsSuccess(out var currentBalance) && currentBalance > initialBalance)
            {
                return true;
            }

            // Exponential backoff: 1s, 1.5s, 2.25s, 3.375s, 5s, 5s, ...
            await Task.Delay((int)(currentInterval * 1000));
            currentInterval = Math.Min(currentInterval * BACKOFF_MULTIPLIER, MAX_INTERVAL);
        }

        return false;
    }
}
```

**Tasks:**
- [ ] Implement `StripePaymentLinkService.cs` class
- [ ] Add QR code generation in client (from Payment Link URL)
  - Option A: Use C# QR library (e.g., QRCoder NuGet package)
  - Option B: Use Godot QR addon
  - Option C: Request pre-generated QR from backend (simplest)
- [ ] Update `BuyCreditsModal.cs` to display QR code
  - Show "Scan with your phone - Choose credits & pay" instructions
  - Remove pack pre-selection UI (no longer needed)
- [ ] Implement exponential backoff polling (180s timeout)
  - Start at 1s interval, increase to max 5s
  - Reduces server load from ~240 requests to ~50 requests
  - Display actual credits received in success message
- [ ] Update payment endpoint call to send no request body
  - Remove PaymentLinkRequest (empty body)
  - Backend handles session creation automatically
- [ ] Update `PaymentService.cs` provider selection for Payment Links
- [ ] Add payment DTOs to `BackendStructures.cs` (PaymentLinkResponse with session_id)
- [ ] Add loading/success/error states to modal
- [ ] Add timeout warning message after 2 minutes of waiting

**Deliverable:** Godot client can display Payment Link QR codes and poll for payment completion with any pack amount

---

### Phase 3: Testing & Production Hardening (Week 4)

**Goal:** Comprehensive testing and production readiness for Payment Links

**Testing Strategy:**

**Local Development:**
```bash
# 1. Create Stripe Prices with metadata (one-time setup)
# In Stripe Dashboard or via API:
# - $5 pack: metadata = {"credits": "5", "bonus_credits": "0"}
# - $10 pack: metadata = {"credits": "10", "bonus_credits": "0"}
# - $25 pack: metadata = {"credits": "25", "bonus_credits": "3"}
# - $50 pack: metadata = {"credits": "50", "bonus_credits": "10"}
# - $100 pack: metadata = {"credits": "100", "bonus_credits": "25"}

# 2. Start backend with test keys
cd BarBoxServices
export STRIPE_SECRET_KEY=sk_test_...
export STRIPE_WEBHOOK_SECRET=whsec_...
sh scripts/dev.sh

# 3. Forward webhooks to localhost
stripe listen --forward-to localhost:8000/payments/webhook

# 4. Test with Stripe test mode
# - Request Payment Link from app (no pack pre-selection)
# - Scan QR code with phone
# - Select credit pack on Stripe-hosted page (e.g., $25 pack)
# - Complete payment with test card (4242 4242 4242 4242) or Apple Pay test
# - Verify credits appear in Godot app
# - Verify actual amount received is displayed (e.g., "Added 28 credits!")
```

**Integration Tests (Hurl):**
```
test/02-feature/payments/
├── payment-link-creation.hurl           # Test Payment Link endpoint (no request body)
├── webhook-payment-link-completed.hurl  # Test webhook with Price metadata extraction
├── webhook-idempotency.hurl             # Test duplicate webhook handling
├── payment-box-tracking.hurl            # Test box_id in metadata
└── multi-pack-selection.hurl            # Test different pack selections
```

**Real Device Testing:**
- [ ] Test with iPhone (Apple Pay)
  - Scan QR code, select $25 pack, pay with Apple Pay
  - Verify credits appear in app
  - Verify success message shows "Added 28 credits!"
- [ ] Test with Android phone (Google Pay)
  - Scan QR code, select $10 pack, pay with Google Pay
  - Verify credits appear in app
- [ ] Test manual card entry fallback
  - Select $5 pack, enter test card manually
- [ ] Test pack selection UX on mobile
  - Verify all 5 packs displayed clearly
  - Verify bonus amounts shown correctly
- [ ] Verify QR code displays clearly on arcade screen
  - Test QR size, contrast, instructions
- [ ] Test payment timeout scenarios (player doesn't complete payment)
- [ ] Test cancelled payments (player backs out of Stripe page)
- [ ] Test duplicate webhook handling

**Tasks:**
- [ ] Write Hurl integration tests for all endpoints
- [ ] Test with Stripe CLI webhook forwarding
- [ ] Test with real phones (iPhone + Android)
- [ ] Verify metadata flows correctly through webhooks (box_id, player_id, session_id)
- [ ] Test core edge cases:
  - Payment cancelled mid-flow
  - Webhook delivery delayed
  - Duplicate webhooks
- [ ] Load test: 100 concurrent QR code generations
- [ ] Verify no race conditions in webhook processing

**Deliverable:** Fully tested payment system ready for production

---

### Phase 4: Production Deployment & Monitoring

**Goal:** Deploy to production with monitoring and operational procedures

**Security:**
- [ ] Rate limiting on payment endpoints (FastAPI-Limiter)
- [ ] Audit logging for all payment events
- [ ] Review webhook signature verification implementation
- [ ] Validate no secrets in client code
- [ ] Complete PCI SAQ-A questionnaire

**Monitoring:**
- [ ] Stripe webhook delivery success rate tracking
- [ ] Payment success rate dashboard
- [ ] Basic logging of payment events for customer support
- [ ] Monitor payment timeout rates (players not completing purchase)

**Edge Cases:**
- [ ] Refund workflow (manual process via customer support)
- [ ] Manual credit adjustment for failed webhooks (customer support endpoint)
- [ ] Handle Payment Link expiration (regenerate if needed)
- [ ] Player changes mind on pack selection (cancel and restart)

**Documentation:**
- [ ] Operations runbook: Payment disputes
- [ ] Operations runbook: Manual refunds
- [ ] Operations runbook: Webhook replay
- [ ] Architecture documentation
- [ ] Player-facing QR code payment instructions

**Deployment:**
- [ ] Set up production Stripe account
- [ ] Configure production webhook endpoint URL
- [ ] Test with live Stripe keys
- [ ] Deploy to first location
- [ ] Monitor first week of transactions
- [ ] Roll out to remaining locations

**Deliverable:** Production-ready QR code payment system

---

## Critical Implementation Details

### 1. Webhook Event Sourcing with Payment Links (CRITICAL)
```python
# ✅ CORRECT: Webhook emits credit/earn event, extracting credits from selected Price
@router.post("/payments/webhook")
async def stripe_webhook(...):
    # Verify signature
    event = stripe.Webhook.construct_event(payload, signature, secret)

    # Check idempotency
    if await webhook_already_processed(event.id):
        return {"status": "already_processed"}

    # Handle checkout.session.completed
    if event.type == "checkout.session.completed":
        session = event.data.object  # Session from Payment Link

        # Extract metadata from session
        player_id = UUID(session.metadata["player_id"])
        box_id = UUID(session.metadata["box_id"])
        session_id = UUID(session.metadata["session_id"])  # Lobby session from metadata

        # CRITICAL: Extract credits from selected Price metadata (player chose on mobile)
        line_item = session.line_items.data[0]
        selected_price = line_item.price
        credits = int(selected_price.metadata["credits"])
        bonus = int(selected_price.metadata.get("bonus_credits", "0"))

        # Validate/create lobby session (handles expiration gracefully)
        lobby_session = await get_or_create_lobby_session(
            player_id=player_id,
            box_id=box_id,
            db_service=db_service,
            now=now,
        )

        # CRITICAL: Emit credit/earn event to lobby session
        await db_service.create(
            target=db.defs.BoxSessionEvent,
            data={
                "session_id": lobby_session.id,
                "type": "credit/earn",
                "timestamp": datetime.now(UTC),
                "payload": {
                    "box_id": str(box_id),  # For bookkeeping scripts to map to location
                    "amount": credits + bonus,
                    "reason": f"Stripe Payment Link - ${session.amount_total / 100:.2f}",
                    "stripe_session_id": session.id,
                    "selected_price_id": selected_price.id
                }
            }
        )

    # Record webhook processing
    await record_webhook_event(event.id)
```

### 2. Balance Polling Pattern for Payment Links (CRITICAL)
```csharp
// ✅ CORRECT: Poll for ANY balance increase (don't know pack selection upfront)
// Note: 120s timeout for mobile payment flow (includes pack selection step)
private async Task<bool> PollForBalanceIncrease(
    Guid playerId,
    int initialBalance,
    float timeoutSeconds = 120.0f  // 2 minutes for mobile payment + selection
)
{
    var startTime = Time.GetTicksMsec();

    while ((Time.GetTicksMsec() - startTime) / 1000.0f < timeoutSeconds)
    {
        // Force cache refresh to get latest balance from event aggregation
        var currentBalance = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);

        // Check for ANY increase (player selected pack on mobile)
        if (currentBalance.IsSuccess(out var balance) && balance > initialBalance)
        {
            return true;
        }

        await DelayAsync(0.5f);
    }

    return false;
}

// Usage in ProcessPurchaseAsync:
var initialBalance = await _creditService.GetBalanceAsync(playerId);
var confirmed = await PollForBalanceIncrease(playerId, initialBalance.Value);

if (confirmed)
{
    var newBalance = await _creditService.GetBalanceAsync(playerId, forceRefresh: true);
    var creditsAdded = newBalance.Value - initialBalance.Value;
    return PaymentResult.Success(creditsAdded);  // Show actual amount received
}
```

### 3. Lazy Session Creation (CRITICAL)
```python
# Backend creates lobby session automatically if needed
@router.post("/payments/link/create")
async def create_payment_link(
    authenticated_box: dependencies.BoxAuthenticated,
    authenticated_player: dependencies.AuthenticatedPlayer,
    db_service: dependencies.Database,
    now: dependencies.Now,
):
    # Get or create lobby session (lazy creation pattern)
    lobby_session = await get_or_create_lobby_session(
        player_id=authenticated_player,
        box_id=authenticated_box.id,
        db_service=db_service,
        now=now,
    )

    # Create Payment Link with all metadata
    payment_link = stripe.PaymentLink.create(
        line_items=[
            {"price": price_id, "quantity": 1}
            for price_id in CREDIT_PACK_PRICE_IDS  # All 5 packs
        ],
        metadata={
            "player_id": str(authenticated_player),
            "box_id": str(authenticated_box.id),  # For bookkeeping
            "session_id": str(lobby_session.id),
        }
    )

    return structures.PaymentLinkResponse(
        payment_link_id=payment_link.id,
        url=payment_link.url,
        session_id=lobby_session.id,
    )
```

**Why Lazy Creation?**
- Simplifies client logic (no pre-creation needed)
- Handles session expiration gracefully
- Webhook can recreate session if needed
- Player always has valid session when logged in

---

## Testing Strategy

### Local Development
```bash
# 1. Start backend with test keys
cd BarBoxServices
export STRIPE_SECRET_KEY=sk_test_...
export STRIPE_WEBHOOK_SECRET=whsec_...
export STRIPE_TEST_MODE=true
sh scripts/dev.sh

# 2. Forward webhooks to localhost
stripe listen --forward-to localhost:8000/payments/webhook

# 3. Test with test cards
# Success: 4242 4242 4242 4242
# Declined: 4000 0000 0000 0002
```

### Integration Tests (Hurl)
```
test/02-feature/payments/
├── payment-link-creation.hurl        # Test Payment Link endpoint (no request body)
├── qr-code-generation.hurl           # Test QR code generation
├── webhook-checkout-completed.hurl   # Test webhook with Price metadata
├── webhook-idempotency.hurl          # Test duplicate webhook handling
└── payment-box-tracking.hurl         # Test box_id in metadata
```

### QR Code Testing
- [ ] QR code generation for all credit pack amounts
- [ ] QR code displays correctly on arcade screen
- [ ] QR code scannable from 1-2 feet away
- [ ] Session expiration handling (30 minutes)
- [ ] Apple Pay payment flow (iPhone)
- [ ] Google Pay payment flow (Android)
- [ ] Manual card entry fallback
- [ ] Payment declined handling
- [ ] Cancelled payment (user backs out)

### Failure Scenarios (C#)
```csharp
[Test] Payment_EventServiceNotReady_ReturnsFailure()
[Test] Payment_WebhookDelayed_CreditsStillAppear()
[Test] Payment_DuplicateWebhook_NoDuplicateCredits()
[Test] Payment_NetworkFailure_Retries()
[Test] QR_UserCancels_NoCharge()
[Test] QR_PaymentDeclined_ProperErrorMessage()
[Test] QR_TwoPlayersScan_OnlyOneGetsCredits()
```

### Multi-Box Testing
- [ ] Test at Box A, verify credits tracked with box_id A
- [ ] Test at Box B, verify credits tracked with box_id B
- [ ] Verify bookkeeping scripts can map box_id to location
- [ ] Test backend connectivity at each box
- [ ] Validate QR codes work at all boxes

---

## Timeline & Milestones

| Weeks | Phase | Milestone | Deliverable |
|-------|-------|-----------|-------------|
| 1-2 | Backend Foundation | Backend Payment Links ready | Payment Link creation + webhook + Price metadata extraction + lazy session creation working |
| 3 | Godot Core | QR code display + polling working | Client displays QR codes and polls for balance increase with exponential backoff |
| 4 | Testing & Production | Production-ready system | All tests pass, webhook idempotency verified, customer support procedures documented |
| 5 | Deployment & Monitoring | First location live | Deployed to first location, monitoring in place, bookkeeping scripts tested |

**Total: 5 weeks to production-ready system**

*Additional week accounts for session management updates, comprehensive webhook implementation, and bookkeeping integration*

---

## Cost Analysis

### Stripe Transaction Fees

**QR Code Payments (Online Rate):**
- 2.9% + $0.30 per transaction
- $5 purchase = $0.45 (9%)
- $25 purchase = $1.03 (4.1%)
- $100 purchase = $3.20 (3.2%)

**Comparison to Card-Present Rates:**
- Card-present (M2/S700): 2.7% + $0.05
- Difference per $25 transaction: ~$0.30 more for QR code
- Annual difference at 1000 transactions/month: ~$3,600/year

### Hardware Costs
- **QR Code Approach: $0** (players use their own phones)
- Alternative M2 Reader: $59 per location (saved)
- Alternative S700 Reader: $349 per location (saved)

**Savings: $295 - $1,745 upfront hardware costs (5 locations)**

### Monthly Operational Estimate
Assumptions:
- 5 locations × 200 transactions/month = 1000 total transactions
- Average transaction: $30
- 100% QR code usage

**Fees:**
- QR Code: 1000 × ($30 × 0.029 + $0.30) = **$1,170/month**

**Connectivity:**
- Backend server internet: Already covered (no additional cost)

**Grand Total: ~$1,170/month operational costs**

**Comparison:** $220/month more than M2 hardware approach, but $0 upfront + zero deployment complexity

---

## Risk Assessment

### Overall Risk: MEDIUM

| Risk | Severity | Likelihood | Mitigation |
|------|----------|------------|------------|
| Players don't have smartphones | LOW | VERY LOW | Very rare in 2025; could add hardware readers later |
| QR code scanning issues | LOW | LOW | Large QR codes, clear instructions, test on real phones |
| Players don't have Apple/Google Pay | LOW | MEDIUM | Manual card entry works (slower but functional) |
| Webhook delivery failures | MEDIUM | LOW | Idempotency table + webhook retry + manual reconciliation via customer support |
| Distributed payment flow complexity | MEDIUM | MEDIUM | Comprehensive testing, exponential backoff polling, 180s timeout |
| Payment timeout (user confusion) | MEDIUM | LOW | Clear UI instructions, 180s timeout, timeout warning messages |
| Session expiration during payment | LOW | LOW | Lazy session creation handles gracefully (get_or_create pattern) |

---

## Success Criteria

### Phase 1: Backend Foundation
- [ ] Backend creates valid Payment Links with metadata (box_id, player_id, session_id)
- [ ] Lazy session creation (get_or_create_lobby_session) works correctly
- [ ] QR codes generate correctly (base64 PNG)
- [ ] Webhooks emit credit/earn events correctly with box_id
- [ ] Idempotency prevents duplicate credits (StripeWebhookEvent table)
- [ ] Integration tests pass

### Phase 2: Godot Client
- [ ] QR codes display clearly on arcade screen
- [ ] QR codes scannable from 1-2 feet away
- [ ] Client polls balance successfully with 180s timeout and exponential backoff
- [ ] Credits appear after payment completes
- [ ] Loading/success/error states work correctly
- [ ] Actual credit amount received is displayed (not pre-selected)

### Phase 3: Testing & Production
- [ ] Apple Pay works on iPhone (< 10 seconds)
- [ ] Google Pay works on Android (< 10 seconds)
- [ ] Manual card entry works as fallback
- [ ] System handles 100 concurrent QR code generations
- [ ] Webhook delivery > 95% success rate
- [ ] No orphaned payments detected
- [ ] PCI SAQ-A compliance completed
- [ ] Payment flow intuitive for players
- [ ] Successfully deployed to first location

---

## Recommendation

✅ **PROCEED WITH PAYMENT LINKS IMPLEMENTATION**

This plan:
1. **Player-selectable packs** - Credit amount chosen on mobile device, not pre-selected in app
2. **Simplest Stripe integration** - Payment Links with hosted checkout UI (no custom web page needed)
3. **Maintains event-sourced credit integrity** - Same architecture, credits extracted from Price metadata
4. **Good UX** - 15-25 seconds with Apple/Google Pay (includes pack selection step)
5. **Comprehensive webhook handling** - Idempotency table, error handling, lazy session creation
6. **Aligns with BarBox patterns** - Lazy sessions, event sourcing, balance polling with exponential backoff
7. **Zero hardware deployment** - No readers to install, charge, or troubleshoot
8. **Simple location tracking** - Uses box_id, maps to locations via bookkeeping scripts

**Estimated effort:** 5 weeks (includes session management updates and comprehensive webhook implementation)
**Risk level:** MEDIUM (webhook complexity and distributed payment flow require careful implementation, but Payment Links API is well-documented)
**ROI:** Enables revenue generation with minimal upfront investment and flexible UX

**Why Payment Links Over Checkout Sessions:**
- Player selects pack amount on mobile (matches desired UX flow)
- Single QR code per purchase (no pack pre-selection)
- Stripe-hosted UI (no custom web page development)
- Simple Price metadata extraction in webhooks
- Can reuse Payment Links if needed (future optimization)

---

## Next Steps After Approval

1. **Stripe Account Setup:**
   - Create Stripe account (test + production)
   - Get API keys (secret key for backend)
   - Set up webhook endpoint URL
   - **Create 5 Stripe Prices with metadata:**
     - $5 pack: `metadata = {"credits": "5", "bonus_credits": "0"}`
     - $10 pack: `metadata = {"credits": "10", "bonus_credits": "0"}`
     - $25 pack: `metadata = {"credits": "25", "bonus_credits": "3"}`
     - $50 pack: `metadata = {"credits": "50", "bonus_credits": "10"}`
     - $100 pack: `metadata = {"credits": "100", "bonus_credits": "25"}`

2. **Environment Setup:**
   - Add Stripe keys to backend `.env` (`STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`)
   - Add `stripe` library to `pyproject.toml` (no QR code library needed for backend)
   - Install Stripe CLI for local webhook testing: `brew install stripe/stripe-cli/stripe`
   - Research client-side QR code generation options (C# library vs backend generation)

3. **Phase 1 Kickoff (Backend - Weeks 1-2):**
   - Create database migrations for payment tables (StripeWebhookEvent)
   - Implement `get_or_create_lobby_session()` helper function
   - Implement `/payments/link/create` endpoint (Payment Links with lazy session creation)
   - Implement `/payments/webhook` endpoint (Price metadata extraction + idempotency)
   - Write Hurl integration tests

4. **Bookkeeping Setup:**
   - Create script to map `box_id` → `location_id` for accounting
   - Example: `{"box-uuid-1": "Location A", "box-uuid-2": "Location B"}`
   - Query `BoxSessionEvent` table for `credit/earn` events
   - Extract `box_id` from event payload
   - Generate financial reports grouped by location

5. **Testing Plan:**
   - Local: Use Stripe CLI to forward webhooks
   - Test pack selection on mobile (all 5 packs visible)
   - Test with real iPhone (Apple Pay)
   - Test with real Android phone (Google Pay)
   - Validate QR codes display correctly on arcade screen
   - Verify lazy session creation works (no pre-creation needed)

---

## Appendix A: Why QR Code Instead of Hardware Readers

### Research Summary

Extensive research was conducted on payment terminal options for desktop/arcade integration:

**Options Evaluated:**
1. **Stripe Reader M2** ($59) - Mobile-only SDK, requires Android/iOS companion device
2. **Stripe Reader S700** ($349) - Server-driven desktop integration
3. **Generic USB EMV readers** ($50-150) - Complex SDK integration, high PCI compliance burden
4. **QR Code + Checkout Sessions** ($0 hardware) - Simple Stripe API

**Why Hardware Readers Were Rejected:**

1. **M2 Reader (Original Plan):**
   - ❌ NO desktop SDK - iOS/Android/React Native only
   - ❌ Requires companion Android tablet ($150) + complex local network setup
   - ❌ Two devices to manage per location (M2 + tablet)
   - ❌ Development time: 4-6 weeks

2. **S700 Smart Reader:**
   - ✅ Desktop compatible (server-driven)
   - ❌ $349 per reader ($1,745 for 5 locations)
   - ❌ Hardware deployment, maintenance, firmware updates
   - ⚠️ Only saves $0.30 per transaction vs QR code

3. **Generic USB Readers:**
   - ✅ Cheapest hardware ($50-150)
   - ❌ Complex SDK integration
   - ❌ PCI SAQ-D (most complex compliance)
   - ❌ Development time: 6+ weeks

**Why QR Code Won:**

| Criteria | QR Code | Best Hardware Alternative (S700) |
|----------|---------|----------------------------------|
| **Upfront Cost** | $0 | $1,745 (5 locations) |
| **Development Time** | 3 weeks | 2-3 weeks + hardware logistics |
| **Deployment Complexity** | Zero | Hardware setup at each location |
| **Maintenance** | Zero | Reader charging, updates, troubleshooting |
| **Transaction Fees** | 2.9% + $0.30 | 2.7% + $0.05 |
| **Annual Fee Difference** | +$3,600/year | Baseline |
| **Risk** | LOW | LOW-MEDIUM |

**Decision:** Accept $3,600/year higher fees to save $1,745 upfront + eliminate hardware complexity + ship 3 weeks faster.

**Future Flexibility:** Can add S700 readers later as secondary payment method if QR code UX proves insufficient.

---

## Appendix B: Architecture Review Findings

### Critical Issues Identified
1. **Event Sourcing Integrity** - Webhooks must emit events, never mutate credit tables
2. **Lazy Session Creation** - Backend creates lobby sessions on-demand (get_or_create pattern)
3. **Race Conditions** - Must poll balance, not intermediate payment status
4. **Cache Invalidation** - Must force refresh CreditService after payment
5. **Location Tracking** - Use box_id in metadata, map to locations via bookkeeping scripts

### Architectural Principles Validated
- Single credit source (credit/earn events only)
- Synchronous balance polling with exponential backoff for UX
- Webhook idempotency for safety (StripeWebhookEvent table)
- No dual credit paths
- Session FK constraint requires session existence

### Security Requirements
- Webhook signature verification (REQUIRED)
- Box + Player authentication
- box_id validation (authenticated box)
- API key management (backend only)

Ready to proceed with implementation! 🚀
