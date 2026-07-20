from enum import StrEnum
from typing import Any

from pydantic import BaseModel


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
    """Structured error response model"""

    code: str  # ErrorCode value
    message: str  # Human-readable error message
    details: dict[str, Any] | None = None  # Additional context
    request_id: str | None = None  # Request tracking ID
    retryable: bool = False  # Whether the client should retry the request
