from collections.abc import AsyncIterator
from datetime import UTC, datetime
from typing import Annotated
from uuid import UUID

from fastapi import Depends, Header, HTTPException, Request, status
from jose import JWTError
from sqlalchemy import select
from structlog import get_logger

from bxctl import db, errors
from bxctl.db.defs import BoxSession

from . import auth

logger = get_logger()


async def _acquire_crud() -> AsyncIterator[db.service.CRUD]:
    async with db.connectivity.db_session() as session:
        yield db.service.CRUD(session)


Database = Annotated[db.service.CRUD, Depends(_acquire_crud)]
# Dependency params below default to `= None` only so params without a
# default (e.g. path params) can come first; FastAPI always injects a real
# CRUD instance before the function body runs. That default is invisible
# to ty through Annotated[..., Depends(...)], so it infers `@Todo | None`
# for these params and flags every attribute access / pass-through below.


def _current_timestamp() -> datetime:
    return datetime.now(tz=UTC)


Now = Annotated[datetime, Depends(_current_timestamp, use_cache=False)]


def _get_request_id(request: Request) -> str:
    """Get request ID from request state (added by middleware)."""
    return getattr(request.state, "request_id", "unknown")


RequestId = Annotated[str, Depends(_get_request_id)]


# Box Authentication (Deterministic API Key)


async def _verify_box_api_key_header(box_id: UUID, x_box_api_key: str | None) -> None:
    """Shared 401 checks for all box-auth flavors: header presence, then key
    validity via deterministic derivation (HMAC(server_secret, box_id))."""
    if not x_box_api_key:
        logger.warning("box_auth_missing_api_key", box_id=str(box_id))
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "code": errors.ErrorCode.UNAUTHORIZED,
                "message": "Missing X-Box-API-Key header",
            },
        )

    if not auth.verify_box_api_key(x_box_api_key, box_id):
        logger.warning("box_auth_invalid_key", box_id=str(box_id))
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "code": errors.ErrorCode.UNAUTHORIZED,
                "message": "Invalid API key",
            },
        )


async def verify_registration_secret_header(x_registration_secret: str | None) -> None:
    """Shared 401 checks for minting a key for a new box_id: header presence,
    then secret validity via constant-time compare. Does NOT gate the
    idempotent "box already exists, return its key" recovery path."""
    if not x_registration_secret:
        logger.warning("box_registration_missing_secret")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "code": errors.ErrorCode.UNAUTHORIZED,
                "message": "Missing X-Registration-Secret header",
            },
        )

    if not auth.verify_registration_secret(x_registration_secret):
        logger.warning("box_registration_invalid_secret")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "code": errors.ErrorCode.UNAUTHORIZED,
                "message": "Invalid registration secret",
            },
        )


async def _verify_new_box_registration(
    x_registration_secret: Annotated[str | None, Header()] = None,
) -> None:
    """Route-level dependency for endpoints that always mint a new box_id
    (e.g. POST /box/). PUT /box/{box_id}'s conditional create-or-recover
    endpoint checks the secret inline instead, since only its create branch
    needs it."""
    await verify_registration_secret_header(x_registration_secret)


RegistrationSecretRequired = Annotated[None, Depends(_verify_new_box_registration)]


async def _fetch_box_or_404(
    box_id: UUID, db_service: "db.service.CRUD", not_found_message: str
) -> db.defs.Box:
    """Shared box lookup + 404 for the two auth flavors where a missing box
    means "not registered yet" (as opposed to the session-scoped flavor,
    where a missing box behind a valid session is a data-integrity error,
    not a registration gap - that one stays a 500, handled by its caller)."""
    result = await db_service.session.execute(
        select(db.defs.Box).where(db.defs.Box.id == box_id)
    )
    box = result.scalar_one_or_none()

    if not box:
        logger.warning("box_auth_box_not_found", box_id=str(box_id))
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={
                "code": "BOX_NOT_FOUND",
                "message": not_found_message,
                "details": {"box_id": str(box_id)},
            },
        )

    return box


async def _get_authenticated_box_by_session(
    session_id: UUID,  # From path parameter (for session-scoped endpoints)
    x_box_api_key: Annotated[str | None, Header()] = None,
    db_service: Database = None,
) -> db.defs.Box:
    """Verify box API key for session-scoped operations.

    For endpoints that operate on sessions (not boxes directly), we need to:
    1. Look up the session to find its box_id
    2. Derive the expected API key from that box_id
    3. Verify the provided key matches

    Args:
            session_id: Session ID from request path
            x_box_api_key: API key from X-Box-API-Key header
            db_service: Database service

    Returns:
            Authenticated Box model (the box that owns this session)

    Raises:
            HTTPException: 404 if session not found, 401 if API key missing or invalid
    """
    # Fetch session and its box in one round trip (outer join: the box side
    # is None either because the session itself doesn't exist, or - a data
    # integrity error - because the session references a box that's gone).
    result = await db_service.session.execute(  # ty: ignore[possibly-missing-attribute]
        select(BoxSession, db.defs.Box)
        .outerjoin(db.defs.Box, db.defs.Box.id == BoxSession.box_id)
        .where(BoxSession.id == session_id)
    )
    row = result.one_or_none()

    if row is None:
        logger.warning("box_auth_session_not_found", session_id=str(session_id))
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={
                "code": "SESSION_NOT_FOUND",
                "message": f"Session '{session_id}' does not exist.",
                "details": {"session_id": str(session_id)},
            },
        )

    session, box = row

    await _verify_box_api_key_header(session.box_id, x_box_api_key)

    if not box:
        logger.error(
            "box_auth_box_missing_for_session",
            session_id=str(session_id),
            box_id=str(session.box_id),
        )
        raise HTTPException(
            status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
            detail="Session references non-existent box",
        )

    logger.debug(
        "box_authenticated_via_session", box_id=str(box.id), session_id=str(session_id)
    )
    return box


async def _get_authenticated_box(
    box_id: UUID,  # From path parameter
    x_box_api_key: Annotated[str | None, Header()] = None,
    db_service: Database = None,
) -> db.defs.Box:
    """Verify box API key using deterministic derivation.

    The API key is derived from box_id + server secret, so we:
    1. Derive expected key = HMAC(server_secret, box_id)
    2. Compare with provided key (constant-time)
    3. Return the box if valid

    No database lookup needed for auth - just derivation and comparison.

    Args:
            box_id: Box ID from request path
            x_box_api_key: API key from X-Box-API-Key header
            db_service: Database service

    Returns:
            Authenticated Box model

    Raises:
            HTTPException: 404 if box not found, 401 if API key missing or invalid
    """
    await _verify_box_api_key_header(box_id, x_box_api_key)

    # Fetch box from database (to return box details, not for auth)
    box = await _fetch_box_or_404(
        box_id,
        db_service,  # ty: ignore[invalid-argument-type]  # see Database note above
        not_found_message=(
            f"Box '{box_id}' does not exist in the database. "
            "Please register the box first using PUT /box/{box_id} "
            "before creating sessions."
        ),
    )

    logger.debug("box_authenticated", box_id=str(box_id))
    return box


BoxAuthenticatedBySession = Annotated[
    db.defs.Box, Depends(_get_authenticated_box_by_session)
]
BoxAuthenticatedWithPath = Annotated[db.defs.Box, Depends(_get_authenticated_box)]


async def _get_authenticated_box_from_header(
    x_box_id: Annotated[UUID | None, Header()] = None,
    x_box_api_key: Annotated[str | None, Header()] = None,
    db_service: Database = None,
) -> db.defs.Box:
    """Verify box API key when box_id comes from header (not path).

    For endpoints that don't have box_id in their path but need box auth.
    Client must send both X-Box-ID and X-Box-API-Key headers.

    Args:
            x_box_id: Box ID from X-Box-ID header
            x_box_api_key: API key from X-Box-API-Key header
            db_service: Database service

    Returns:
            Authenticated Box model

    Raises:
            HTTPException: 401 if headers missing or API key invalid,
            404 if box not found
    """
    if not x_box_id:
        logger.warning("box_auth_missing_box_id_header")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "code": errors.ErrorCode.UNAUTHORIZED,
                "message": "Missing X-Box-ID header",
            },
        )

    await _verify_box_api_key_header(x_box_id, x_box_api_key)

    # Fetch box from database
    box = await _fetch_box_or_404(
        x_box_id,
        db_service,  # ty: ignore[invalid-argument-type]  # see Database note above
        not_found_message=(
            f"Box '{x_box_id}' does not exist in the database. "
            "Please register the box first using PUT /box/{box_id}."
        ),
    )

    logger.debug("box_authenticated_via_header", box_id=str(box.id))
    return box


BoxAuthenticated = Annotated[db.defs.Box, Depends(_get_authenticated_box_from_header)]


# Player Authentication


async def _get_authenticated_player(
    authorization: Annotated[str | None, Header()] = None,
    db_service: Database = None,  # noqa: ARG001  # reserved for revocation checks
) -> UUID:
    """Extract and validate player from JWT.

    Args:
            authorization: Authorization header with Bearer token
            db_service: Database service

    Returns:
            Authenticated player ID

    Raises:
            HTTPException: 401 if token missing, invalid, expired, or revoked
    """
    if not authorization:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Missing Authorization header",
            headers={"WWW-Authenticate": "Bearer"},
        )

    if not authorization.startswith("Bearer "):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid Authorization header format (expected 'Bearer <token>')",
            headers={"WWW-Authenticate": "Bearer"},
        )

    token = authorization[7:]  # Remove "Bearer " prefix

    # Decode and validate JWT
    try:
        payload = auth.decode_player_jwt(token)
    except JWTError as e:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail=f"Invalid token: {e!s}",
            headers={"WWW-Authenticate": "Bearer"},
        ) from e

    # Extract player ID from claims
    player_id = UUID(payload["player_id"])

    logger.info("player_authenticated", player_id=str(player_id))
    return player_id


AuthenticatedPlayer = Annotated[UUID, Depends(_get_authenticated_player)]


# Admin Authentication (Localhost-Only)


async def _require_localhost(request: Request) -> None:
    """Restrict admin endpoints to localhost access only.

    Admin endpoints are for maintenance operations and should only be
    accessible from the server itself. This provides a simple security
    boundary without requiring separate API key management.

    Deployment pattern:
    - Cron jobs run on server: curl http://localhost:8000/admin/...
    - Remote admin access: SSH to server first, then call endpoints

    Args:
            request: FastAPI request object

    Returns:
            None (access allowed)

    Raises:
            HTTPException: 403 if request not from localhost
    """
    client_host = request.client.host if request.client else None

    # Allow localhost variants (IPv4, IPv6, hostname)
    allowed_hosts = ("127.0.0.1", "::1", "localhost")

    if client_host not in allowed_hosts:
        logger.warning("admin_access_denied", client_host=client_host)
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Admin endpoints only accessible from localhost",
        )

    logger.info("admin_access_granted", client_host=client_host)


RequireLocalhost = Annotated[None, Depends(_require_localhost)]
