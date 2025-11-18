from fastapi import APIRouter

from bxctl import db, structures

from . import dependencies

router = APIRouter(prefix="/games", tags=["Core: Games"])


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
