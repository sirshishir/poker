from __future__ import annotations

from fastapi import APIRouter, Depends, HTTPException, Request

from ..models.schemas import CreateRoomRequest, HealthResponse, RoomCreatedResponse
from ..services.room_manager import RoomManager

router = APIRouter()


def get_room_manager(request: Request) -> RoomManager:
    return request.app.state.room_manager


@router.get("/health", response_model=HealthResponse)
async def health(rm: RoomManager = Depends(get_room_manager)) -> HealthResponse:
    return HealthResponse(status="ok", active_rooms=rm.active_room_count())


@router.post("/rooms", response_model=RoomCreatedResponse, status_code=201)
async def create_room(
    body: CreateRoomRequest, rm: RoomManager = Depends(get_room_manager)
) -> RoomCreatedResponse:
    code = rm.create_room(big_blind=body.big_blind, small_blind=body.small_blind)
    return RoomCreatedResponse(
        code=code, big_blind=body.big_blind, small_blind=body.small_blind
    )


@router.get("/rooms/{code}")
async def get_room(code: str, rm: RoomManager = Depends(get_room_manager)) -> dict:
    engine = rm.get_room(code)
    if not engine:
        raise HTTPException(status_code=404, detail=f"Room {code!r} not found")
    return engine.public_state()


@router.delete("/rooms/{code}", status_code=204, response_model=None)
async def delete_room(code: str, rm: RoomManager = Depends(get_room_manager)):
    engine = rm.get_room(code)
    if not engine:
        raise HTTPException(status_code=404, detail=f"Room {code!r} not found")
    rm.delete_room(code)
