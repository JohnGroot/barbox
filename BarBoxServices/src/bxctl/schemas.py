"""Shared pydantic mixins composed by feature request/response models."""

from uuid import UUID

from pydantic import BaseModel


class Named(BaseModel):
    name: str


class Tagged(BaseModel):
    tag: str


class Identifiable(BaseModel):
    id: UUID
