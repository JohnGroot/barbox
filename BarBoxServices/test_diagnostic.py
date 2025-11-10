"""
Quick diagnostic script to check what's in the database
and test the credit query conditions
"""
import asyncio
import sys
from pathlib import Path

# Add src to path
sys.path.insert(0, str(Path(__file__).parent / "src"))

from bxctl.db import connectivity
from uuid import UUID
from sqlalchemy import text


async def main():
    """Run diagnostic queries"""
    async with connectivity.db_session() as session:
        # Check what events exist
        print("\n=== All Credit Events ===")
        result = await session.execute(text("""
            SELECT
                bse.id,
                bse.session_id,
                bse.type,
                bse.payload,
                bs.host_player_id,
                typeof(bs.host_player_id) as host_player_id_type
            FROM box_session_event bse
            JOIN box_session bs ON bse.session_id = bs.id
            WHERE bse.type IN ('credit/earn', 'credit/spend')
        """))

        for row in result.all():
            print(f"Event ID: {row[0]}")
            print(f"Session ID: {row[1]}")
            print(f"Type: {row[2]}")
            print(f"Payload: {row[3]}")
            print(f"Host Player ID: {row[4]} (type: {row[5]})")
            print()

        # Test the filters with string conversion
        player_id = UUID("06f25980-a5b0-2d84-f050-79903e7187ab")
        location_id = "JohnGrootMachine"

        print(f"\n=== Testing Filters ===")
        print(f"Player ID (UUID): {player_id}")
        print(f"Player ID (str): {str(player_id)}")
        print(f"Location ID: {location_id}")

        # Test with str(player_id)
        result_str = await session.execute(text("""
            SELECT COUNT(*) as count
            FROM box_session_event bse
            JOIN box_session bs ON bse.session_id = bs.id
            WHERE bs.host_player_id = :player_id
            AND bse.type IN ('credit/earn', 'credit/spend')
        """), {"player_id": str(player_id)})
        count_str = result_str.scalar()
        print(f"\nWith str(player_id): {count_str} matches")

        # Test with UUID directly
        result_uuid = await session.execute(text("""
            SELECT COUNT(*) as count
            FROM box_session_event bse
            JOIN box_session bs ON bse.session_id = bs.id
            WHERE bs.host_player_id = :player_id
            AND bse.type IN ('credit/earn', 'credit/spend')
        """), {"player_id": player_id})
        count_uuid = result_uuid.scalar()
        print(f"With UUID directly: {count_uuid} matches")

        # Test location filter
        result_location = await session.execute(text("""
            SELECT COUNT(*) as count
            FROM box_session_event bse
            WHERE bse.type IN ('credit/earn', 'credit/spend')
            AND json_extract(bse.payload, '$.location_id') = :location_id
        """), {"location_id": location_id})
        count_location = result_location.scalar()
        print(f"With location_id filter: {count_location} matches")


if __name__ == "__main__":
    asyncio.run(main())
