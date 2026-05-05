from __future__ import annotations

from typing import Dict, Optional

from ..game.engine import PokerEngine
from .room_code import generate_room_code

# Maximum rooms per manager instance (single-server MVP limit)
_MAX_ROOMS = 500


class RoomManager:
    """
    In-memory room registry for the MVP single-server deployment.
    Replace the backing store with Redis for horizontal scaling.
    """

    def __init__(self) -> None:
        self._rooms: Dict[str, PokerEngine] = {}
        self._player_room: Dict[str, str] = {}  # player_id → room_code

    # ── Room operations ──────────────────────────────────────────────────────

    def create_room(self, big_blind: int = 20, small_blind: int = 10) -> str:
        if len(self._rooms) >= _MAX_ROOMS:
            raise RuntimeError("Server is at room capacity")
        code = generate_room_code()
        while code in self._rooms:
            code = generate_room_code()
        self._rooms[code] = PokerEngine(
            room_code=code, big_blind=big_blind, small_blind=small_blind
        )
        return code

    def get_room(self, code: str) -> Optional[PokerEngine]:
        return self._rooms.get(code.upper())

    def delete_room(self, code: str) -> None:
        engine = self._rooms.pop(code.upper(), None)
        if engine:
            affected = [pid for pid, rc in self._player_room.items() if rc == code.upper()]
            for pid in affected:
                self._player_room.pop(pid, None)

    # ── Player ↔ room mapping ────────────────────────────────────────────────

    def join_room(self, player_id: str, code: str) -> PokerEngine:
        engine = self._rooms.get(code.upper())
        if not engine:
            raise ValueError(f"Room {code!r} not found")
        self._player_room[player_id] = code.upper()
        return engine

    def leave_room(self, player_id: str) -> Optional[str]:
        """Unmap player from their room. Returns the room code, or None."""
        return self._player_room.pop(player_id, None)

    def get_player_room_code(self, player_id: str) -> Optional[str]:
        return self._player_room.get(player_id)

    def get_player_engine(self, player_id: str) -> Optional[PokerEngine]:
        code = self._player_room.get(player_id)
        return self._rooms.get(code) if code else None

    # ── Stats ────────────────────────────────────────────────────────────────

    def active_room_count(self) -> int:
        return len(self._rooms)
