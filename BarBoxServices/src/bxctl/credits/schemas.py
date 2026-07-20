from typing import Annotated
from uuid import UUID

from pydantic import BaseModel, Field


class MachinePlayerContribution(BaseModel):
    """Individual player contribution to machine credit pot"""

    player_id: UUID
    amount: int


class MachineCreditsResponse(BaseModel):
    """Machine credit pot balance and player contributions"""

    box_id: UUID
    game_tag: str
    balance: int
    contributions: list[MachinePlayerContribution]


class MachineCreditsDepositRequest(BaseModel):
    """Request body for depositing credits to machine pot"""

    box_id: UUID
    player_id: UUID
    amount: Annotated[
        int, Field(gt=0, description="Credits to deposit (must be positive)")
    ]
    lobby_session_id: UUID


class MachineCreditsConsumeRequest(BaseModel):
    """Request body for consuming credits from machine pot"""

    box_id: UUID
    amount: Annotated[
        int, Field(gt=0, description="Credits to consume (must be positive)")
    ]
    game_session_id: UUID
