from collections.abc import AsyncIterator
from datetime import UTC, datetime
from typing import Annotated
from uuid import UUID

from fastapi import Depends, Header, HTTPException, Request, status
from jose import JWTError
from sqlalchemy import select
from structlog import get_logger

from bxctl import db
from . import auth

logger = get_logger()


async def _acquire_crud() -> AsyncIterator[db.service.CRUD]:
	async with db.connectivity.db_session() as session:
		yield db.service.CRUD(session)


Database = Annotated[db.service.CRUD, Depends(_acquire_crud)]


def _current_timestamp() -> datetime:
	return datetime.now(tz=UTC)


Now = Annotated[datetime, Depends(_current_timestamp, use_cache=False)]


def _get_request_id(request: Request) -> str:
	"""Get request ID from request state (added by middleware)."""
	return getattr(request.state, "request_id", "unknown")


RequestId = Annotated[str, Depends(_get_request_id)]


# Box Authentication

async def _get_authenticated_box_by_api_key(
	x_box_api_key: Annotated[str | None, Header()] = None,
	db_service: Database = None,  # type: ignore
) -> db.defs.Box:
	"""Verify box API key and return authenticated box.

	Uses SHA256 lookup hash for fast database query, then verifies with bcrypt.
	This avoids fetching all boxes and checking bcrypt against each one.

	Performance optimization:
	1. Compute SHA256(api_key) for fast lookup
	2. Query database for box with matching api_key_hash_lookup
	3. Verify with bcrypt only for the single matched box

	Args:
		x_box_api_key: API key from X-Box-API-Key header (required)
		db_service: Database service

	Returns:
		Authenticated Box model

	Raises:
		HTTPException: 401 if API key missing, invalid, or not found
	"""
	# Check if API key header is present
	if not x_box_api_key:
		logger.warning("box_auth_missing_api_key")
		raise HTTPException(
			status_code=status.HTTP_401_UNAUTHORIZED,
			detail="Missing X-Box-API-Key header"
		)

	# Compute lookup hash for fast database query
	lookup_hash = auth.hash_api_key_lookup(x_box_api_key)

	# Query for box with matching lookup hash
	result = await db_service.session.execute(
		select(db.defs.Box).where(db.defs.Box.api_key_hash_lookup == lookup_hash)
	)
	box = result.scalar_one_or_none()

	# If no box found with this lookup hash, API key is invalid
	if not box:
		logger.warning("box_auth_invalid_key_no_match")
		raise HTTPException(
			status_code=status.HTTP_401_UNAUTHORIZED,
			detail="Invalid API key"
		)

	# Verify with bcrypt (defense in depth - prevents SHA256 collision attacks)
	if not auth.verify_api_key(x_box_api_key, box.api_key_hash):
		logger.warning("box_auth_invalid_key_bcrypt_mismatch", box_id=str(box.id))
		raise HTTPException(
			status_code=status.HTTP_401_UNAUTHORIZED,
			detail="Invalid API key"
		)

	logger.info("box_authenticated", box_id=str(box.id))
	return box


async def _get_authenticated_box(
	box_id: UUID,  # From path parameter
	x_box_api_key: Annotated[str | None, Header()] = None,
	db_service: Database = None,  # type: ignore
) -> db.defs.Box:
	"""Verify box API key and return authenticated box.

	Requires box_id in path and validates that the API key matches that specific box.

	Args:
		box_id: Box ID from request path
		x_box_api_key: API key from X-Box-API-Key header
		db_service: Database service

	Returns:
		Authenticated Box model

	Raises:
		HTTPException: 404 if box not found, 401 if API key missing or invalid
	"""
	# Check if API key header is present
	if not x_box_api_key:
		logger.warning("box_auth_missing_api_key", box_id=str(box_id))
		raise HTTPException(
			status_code=status.HTTP_401_UNAUTHORIZED,
			detail="Missing X-Box-API-Key header"
		)

	# Fetch box from database
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
				"message": f"Box '{box_id}' does not exist in the database. Please register the box first using PUT /box/{{box_id}} before creating sessions.",
				"details": {"box_id": str(box_id)},
			},
		)

	# Verify API key
	if not auth.verify_api_key(x_box_api_key, box.api_key_hash):
		logger.warning("box_auth_invalid_key", box_id=str(box_id))
		raise HTTPException(
			status_code=status.HTTP_401_UNAUTHORIZED,
			detail="Invalid API key"
		)

	logger.info("box_authenticated", box_id=str(box_id))
	return box


BoxAuthenticated = Annotated[db.defs.Box, Depends(_get_authenticated_box_by_api_key)]
BoxAuthenticatedWithPath = Annotated[db.defs.Box, Depends(_get_authenticated_box)]


# Player Authentication

async def _get_authenticated_player(
	authorization: Annotated[str | None, Header()] = None,
	db_service: Database = None,  # type: ignore
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
			detail=f"Invalid token: {str(e)}",
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
			detail="Admin endpoints only accessible from localhost"
		)

	logger.info("admin_access_granted", client_host=client_host)


RequireLocalhost = Annotated[None, Depends(_require_localhost)]
