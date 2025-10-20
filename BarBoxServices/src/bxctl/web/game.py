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
async def get_leaderboard(
    game_id: structures.GameTag,
    db_service: dependencies.Database,
) -> dict:
    if game_id == "carrom":
        sql = """
        SELECT bs.player_id, SUM((bse.payload->>'points')) AS total_score
        FROM box_session_event bse
        JOIN box_session bs ON bse.session_id = bs.id
        WHERE bse.type = 'play/score'
        AND (bse.payload->>'game') = :game_tag
        GROUP BY bs.player_id
        """
        result = await db_service.get_many_raw(sql, {"game_tag": game_id})
    return {"game_id": game_id, "leaderboard": [dict(t) for t in result.tuples()]}
