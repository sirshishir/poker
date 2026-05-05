from __future__ import annotations

import json
import uuid
from typing import Dict, Optional, Set

from fastapi import WebSocket


class WebSocketManager:
    """
    Tracks live WebSocket connections and room membership.
    Thread-safety note: FastAPI runs handlers in a single event loop, so
    plain dicts are safe here without locks.
    """

    def __init__(self) -> None:
        self._sockets: Dict[str, WebSocket] = {}         # player_id → socket
        self._player_room: Dict[str, str] = {}           # player_id → room_code
        self._room_players: Dict[str, Set[str]] = {}     # room_code → player_ids

    # ── Connection lifecycle ─────────────────────────────────────────────────

    async def accept(self, websocket: WebSocket) -> str:
        await websocket.accept()
        player_id = uuid.uuid4().hex[:8].upper()
        self._sockets[player_id] = websocket
        return player_id

    def remove(self, player_id: str) -> Optional[str]:
        """Remove player from all tracking. Returns their room code or None."""
        self._sockets.pop(player_id, None)
        code = self._player_room.pop(player_id, None)
        if code:
            self._room_players.get(code, set()).discard(player_id)
        return code

    # ── Room membership ──────────────────────────────────────────────────────

    def enter_room(self, player_id: str, room_code: str) -> None:
        self._player_room[player_id] = room_code
        self._room_players.setdefault(room_code, set()).add(player_id)

    def leave_room(self, player_id: str) -> Optional[str]:
        code = self._player_room.pop(player_id, None)
        if code:
            self._room_players.get(code, set()).discard(player_id)
        return code

    def get_room_code(self, player_id: str) -> Optional[str]:
        return self._player_room.get(player_id)

    # ── Messaging ────────────────────────────────────────────────────────────

    async def send(self, player_id: str, event: str, data: dict) -> None:
        ws = self._sockets.get(player_id)
        if ws:
            try:
                await ws.send_text(json.dumps({"event": event, "data": data}))
            except Exception:
                pass

    async def broadcast(self, room_code: str, event: str, data: dict) -> None:
        msg = json.dumps({"event": event, "data": data})
        for pid in list(self._room_players.get(room_code, set())):
            ws = self._sockets.get(pid)
            if ws:
                try:
                    await ws.send_text(msg)
                except Exception:
                    pass
