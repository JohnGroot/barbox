from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from enum import StrEnum
from typing import Any

from fastapi import HTTPException, status
from pydantic import BaseModel
from sqlalchemy.exc import IntegrityError
from structlog import get_logger

logger = get_logger()


class ErrorCode(StrEnum):
    """Standard error codes for structured error responses"""

    # Validation errors
    VALIDATION_ERROR = "VALIDATION_ERROR"
    INVALID_INPUT = "INVALID_INPUT"

    # Resource errors
    RESOURCE_NOT_FOUND = "RESOURCE_NOT_FOUND"
    DUPLICATE_RESOURCE = "DUPLICATE_RESOURCE"

    # Constraint errors
    FK_VIOLATION = "FK_VIOLATION"
    UNIQUE_CONSTRAINT = "UNIQUE_CONSTRAINT"

    # Auth errors
    UNAUTHORIZED = "UNAUTHORIZED"

    # Operation errors
    OPERATION_FAILED = "OPERATION_FAILED"
    INTERNAL_ERROR = "INTERNAL_ERROR"
    INSUFFICIENT_CREDITS = "INSUFFICIENT_CREDITS"

    # Payment errors (Stripe-facing; distinct from INTERNAL_ERROR because the
    # client's retry behavior differs per case - see ErrorDetail.retryable)
    PAYMENT_SERVICE_TIMEOUT = "PAYMENT_SERVICE_TIMEOUT"
    PAYMENT_SERVICE_UNAVAILABLE = "PAYMENT_SERVICE_UNAVAILABLE"
    RATE_LIMITED = "RATE_LIMITED"
    INVALID_PAYMENT_REQUEST = "INVALID_PAYMENT_REQUEST"


class ErrorDetail(BaseModel):
    """Structured error response model - the shape http_error() produces.

    request_id is attached by the global exception handler, not by
    endpoints.
    """

    code: str  # ErrorCode value
    message: str  # Human-readable error message
    details: dict[str, Any] | None = None  # Additional context
    request_id: str | None = None  # Request tracking ID
    retryable: bool = False  # Whether the client should retry the request


def http_error(
    status_code: int,
    code: ErrorCode,
    message: str,
    *,
    details: dict[str, Any] | None = None,
    retryable: bool | None = None,
) -> HTTPException:
    """Build an HTTPException carrying the ErrorDetail-shaped envelope.

    Only explicitly provided optional fields are included, so responses stay
    byte-identical to the historical hand-built dicts (no implicit
    "retryable": false key on endpoints that never sent one).
    """
    detail: dict[str, Any] = {"code": code, "message": message}
    if details is not None:
        detail["details"] = details
    if retryable is not None:
        detail["retryable"] = retryable
    return HTTPException(status_code=status_code, detail=detail)


@asynccontextmanager
async def creation_error_boundary(
    *,
    log_event_stem: str,
    conflict_message: str,
    failure_message: str,
    **log_fields: str,
) -> AsyncIterator[None]:
    """Translate resource-creation failures into the structured error envelope.

    IntegrityError becomes a 409 UNIQUE_CONSTRAINT, anything else a 500
    INTERNAL_ERROR; both log with `log_event_stem` so existing log event
    names ({stem}_integrity_error / {stem}_failed) are preserved.
    """
    integrity_event = f"{log_event_stem}_integrity_error"
    failure_event = f"{log_event_stem}_failed"
    try:
        yield
    except IntegrityError as e:
        logger.exception(
            integrity_event,
            error=str(e),
            **log_fields,
        )
        raise http_error(
            status.HTTP_409_CONFLICT,
            ErrorCode.UNIQUE_CONSTRAINT,
            conflict_message,
            details={"error": str(e.orig) if hasattr(e, "orig") else str(e)},
        ) from e
    except HTTPException:
        raise
    except Exception as e:
        logger.exception(
            failure_event,
            error=str(e),
            error_type=type(e).__name__,
            **log_fields,
        )
        raise http_error(
            status.HTTP_500_INTERNAL_SERVER_ERROR,
            ErrorCode.INTERNAL_ERROR,
            failure_message,
            details={"error_type": type(e).__name__},
        ) from e
