from fastapi import APIRouter

from bxctl import db, structures

from . import dependencies

router = APIRouter(prefix="/player")


@router.post("/", status_code=201)
async def register_player(
    new_player: structures.PlayerCreate,
    db_service: dependencies.Database,
) -> structures.PlayerDetail:
    return await db_service.create(
        target=db.defs.Player,
        data=new_player,
        read_as=structures.PlayerDetail,
    )
