from typing import Annotated
from uuid import UUID

from fastapi import APIRouter, Header, Query, status

from bxctl.app import dependencies
from bxctl.boxes import schemas, service
from bxctl.schemas import Identifiable

router = APIRouter(prefix="/box", tags=["Core: Boxes & Sessions"])


@router.post("/", status_code=201)
async def create_box(
    new_box: schemas.BoxCreate,
    db_service: dependencies.Database,
    now: dependencies.Now,
    _registration_auth: dependencies.RegistrationSecretRequired,
) -> schemas.BoxDetailWithAPIKey:
    """Create a new box and return API key.

    The API key is deterministically derived from the box ID, so it can
    always be retrieved by calling PUT /box/{box_id}.

    Requires the X-Registration-Secret header, since this always mints a key
    for a box_id that doesn't exist yet.

    Validates that:
    - Box ID is unique
    - Box tag (identifier) is unique

    Returns:
        201: Box created successfully with API key
        401: Missing or invalid X-Registration-Secret header
        409: Box already exists (duplicate ID or tag)
        500: Internal server error

    For idempotent box registration, use PUT /box/{box_id} instead.
    """
    return await service.create_box(new_box, db_service, now)


@router.put("/{box_id}", status_code=200)
async def register_box(
    box_id: UUID,
    box_data: schemas.BoxCreate,
    db_service: dependencies.Database,
    now: dependencies.Now,
    x_registration_secret: Annotated[str | None, Header()] = None,
) -> schemas.BoxDetailWithAPIKey:
    """
    Idempotent box registration - creates if not exists, always returns API key.

    This endpoint is safe to call multiple times with the same box_id.
    Used by clients to ensure box exists and retrieve the API key.

    **Authentication**: Not required for recovering an *existing* box's key
    (needed for reinstalls). Required (X-Registration-Secret header) only
    when the box_id doesn't exist yet, to prevent minting keys for
    arbitrary chosen box_ids.

    The API key is deterministically derived from the box ID, so:
    - First call: Creates the box and returns API key (requires X-Registration-Secret)
    - Subsequent calls: Returns the same API key (idempotent, no auth needed)
    - Reinstalls: Just call this endpoint again to get the key (no auth needed)

    **Name/Tag Handling**:
    - Name and tag are set on first registration and cannot be updated
    - If called with different name/tag for existing box, returns existing values
    - Warning field indicates if name/tag mismatch was detected

    Returns:
        200: BoxDetailWithAPIKey with deterministic API key and warning if applicable
        400: Box ID in path does not match request body
        401: box_id doesn't exist and X-Registration-Secret is missing/invalid
        500: Internal server error
    """
    return await service.register_box(
        box_id, box_data, db_service, now, x_registration_secret
    )


@router.post("/{box_id}/lobby/session", status_code=status.HTTP_201_CREATED)
async def create_lobby_session(
    box_id: UUID,
    authenticated_box: dependencies.BoxAuthenticatedWithPath,
    authenticated_player: dependencies.AuthenticatedPlayer,
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> schemas.BoxSessionDetail:
    """
    Create a lobby session for a logged-in player.

    **Authentication**: Requires both Box API key AND Player JWT token.

    Lobby sessions are long-lived sessions that persist while a player is logged in
    but not actively playing a game. They receive user-scoped events like:
    - credit/earn, credit/spend (credit transactions)
    - user/login, user/logout (authentication events)

    The lobby session is closed when the player logs out.

    Headers:
        X-Box-API-Key: Box API key for authentication
        Authorization: Bearer <player_jwt_token>
    """
    return await service.create_lobby_session(
        box_id, authenticated_box.id, authenticated_player, db_service, now
    )


@router.put("/{box_id}/session/{session_id}", status_code=status.HTTP_202_ACCEPTED)
async def create_box_session(  # noqa: PLR0913  # FastAPI dependency injection
    box_id: UUID,
    session_id: UUID,
    game_tag: Annotated[str, Query()],  # Required: Game type identifier
    authenticated_box: dependencies.BoxAuthenticatedWithPath,
    db_service: dependencies.Database,
    now: dependencies.Now,
    player_id: Annotated[
        UUID | None, Header()
    ] = None,  # Optional: Required for logged-in play, None for practice mode
    player_ids: Annotated[
        str | None, Query()
    ] = None,  # Optional JSON array of player IDs for multiplayer
) -> schemas.BoxSessionDetail:
    """
    Create a game session for single or multiple players, or practice mode.

    **Authentication**: Requires Box API key.

    For practice mode (anonymous gameplay):
    - Omit player_id header
    - Pass game_tag as query parameter: ?game_tag=racing
    - Session will have host_player_id=NULL, session_type="practice"

    For single-player games (Racing, Mining):
    - Pass player_id via header
    - Pass game_tag as query parameter: ?game_tag=racing
    - player_ids will be set to [player_id]

    For multiplayer games (Carrom):
    - Pass player_ids as JSON array via query parameter:
      ?player_ids=["uuid1","uuid2","uuid3"]
    - Pass game_tag as query parameter: ?game_tag=carrom
    - First player in array becomes host_player_id

    Headers:
        X-Box-API-Key: Box API key for authentication
        Player-Id: (Optional) Player UUID for logged-in play
    """
    return await service.create_game_session(
        box_id,
        session_id,
        game_tag,
        authenticated_box.id,
        db_service,
        now,
        player_id,
        player_ids,
    )


@router.post("/session/{session_id}", status_code=status.HTTP_201_CREATED)
async def add_session_event(
    event: schemas.SessionEventBase,
    session_id: UUID,
    authenticated_box: dependencies.BoxAuthenticatedBySession,  # noqa: ARG001  # auth gate only
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> Identifiable:
    """
    Add an event to a session with payload validation.

    **Authentication**: Requires Box API key.

    Events are validated against game-specific schemas when available.
    Unknown event types are rejected to prevent typos and data corruption.

    Headers:
        X-Box-API-Key: Box API key for authentication
    """
    return await service.add_session_event(event, session_id, db_service, now)


@router.post("/session/{session_id}/close", status_code=status.HTTP_200_OK)
async def close_box_session(
    session_id: UUID,
    authenticated_box: dependencies.BoxAuthenticatedBySession,  # noqa: ARG001  # auth gate only
    db_service: dependencies.Database,
    now: dependencies.Now,
) -> Identifiable:
    """
    Close an activity session by setting end_time.

    **Authentication**: Requires Box API key.

    Call this when a game exits to properly close the session.

    Headers:
        X-Box-API-Key: Box API key for authentication
    """
    return await service.close_session(session_id, db_service, now)


@router.get("/session/{session_id}", response_model=schemas.BoxSession)
async def get_box_session(
    session_id: UUID,
    db_service: dependencies.Database,
    include_events: Annotated[bool, Query()] = False,  # noqa: FBT002  # FastAPI query param
) -> schemas.BoxSession:
    return await service.get_session(
        session_id, db_service, include_events=include_events
    )
