from typing import NamedTuple

from bxctl import env


class CreditPack(NamedTuple):
    credits: int
    bonus: int
    amount_cents: int


# Credit pack definitions (1,000 credits = $1 USD)
# NOTE: This is REFERENCE DATA ONLY for pack validation and lookup.
# The SOURCE OF TRUTH for credit amounts is Stripe Price metadata,
# which is fetched during webhook processing via
# StripePriceMetadata.from_stripe_metadata().
CREDIT_PACKS = {
    "pack_5": CreditPack(credits=5000, bonus=0, amount_cents=500),
    "pack_10": CreditPack(credits=10000, bonus=0, amount_cents=1000),
    "pack_25": CreditPack(credits=25000, bonus=3000, amount_cents=2500),
    "pack_50": CreditPack(credits=50000, bonus=10000, amount_cents=5000),
    "pack_100": CreditPack(credits=100000, bonus=25000, amount_cents=10000),
}


def get_stripe_price_id(pack_id: str) -> str:
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
