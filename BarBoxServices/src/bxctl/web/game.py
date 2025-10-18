from fastapi import APIRouter

from bxctl import db, structures

from . import dependencies

router = APIRouter(prefix="/game")


@router.post("/", status_code=201)
async def register_game(
    new_game: structures.GameCreate,
    db_service: dependencies.Database,
) -> structures.GameDetail:
    return await db_service.create(
        target=db.defs.Game,
        data=new_game,
        read_as=structures.GameDetail,
    )


@router.get("/leaderboard/{game_id}")
def get_leaderboard(game_id: structures.GameTag) -> dict:
    # logic will be different per-game.
    return {"game_id": game_id, "leaderboard": []}
