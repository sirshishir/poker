from __future__ import annotations

from typing import Literal, Optional

from pydantic import BaseModel, Field, field_validator


class CreateRoomRequest(BaseModel):
    big_blind: int = Field(default=20, ge=2)
    small_blind: int = Field(default=10, ge=1)
    max_players: int = Field(default=9, ge=2, le=9)

    @field_validator("small_blind")
    @classmethod
    def sb_less_than_bb(cls, v: int, info) -> int:
        bb = info.data.get("big_blind", 20)
        if v >= bb:
            raise ValueError("small_blind must be less than big_blind")
        return v


class JoinRoomRequest(BaseModel):
    code: str = Field(min_length=6, max_length=6)

    @field_validator("code")
    @classmethod
    def uppercase_code(cls, v: str) -> str:
        return v.upper()


class SitDownRequest(BaseModel):
    seat: int = Field(ge=1, le=9)
    buy_in: int = Field(ge=1)


class PlayerActionRequest(BaseModel):
    action: Literal["fold", "check", "call", "raise"]
    amount: Optional[int] = Field(default=0, ge=0)


# ── Response shapes ──────────────────────────────────────────────────────────


class RoomCreatedResponse(BaseModel):
    code: str
    big_blind: int
    small_blind: int


class HealthResponse(BaseModel):
    status: str
    active_rooms: int
