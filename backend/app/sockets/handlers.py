from __future__ import annotations

import logging
from typing import Any

import socketio

from ..game.engine import ActionType, Phase
from ..services.bot import BOT_NAMES, decide as bot_decide, is_bot_id, make_bot_id
from ..services.room_manager import RoomManager

log = logging.getLogger(__name__)


def register_handlers(sio: socketio.AsyncServer, room_manager: RoomManager) -> None:  # noqa: C901

    # ── Connection lifecycle ─────────────────────────────────────────────────

    @sio.event
    async def connect(sid: str, environ: dict, auth: Any) -> None:
        log.info("Client connected: %s", sid)

    @sio.event
    async def disconnect(sid: str) -> None:
        log.info("Client disconnected: %s", sid)
        code = room_manager.get_player_room_code(sid)
        if code:
            engine = room_manager.get_room(code)
            if engine:
                try:
                    engine.remove_player(sid)
                except ValueError:
                    pass  # mid-hand removal is silently skipped
            room_manager.leave_room(sid)
            await sio.emit("player_left", {"player_id": sid}, room=code)

    # ── Room events ──────────────────────────────────────────────────────────

    @sio.event
    async def create_room(sid: str, data: dict) -> None:
        try:
            big_blind = int(data.get("big_blind", 20))
            small_blind = int(data.get("small_blind", 10))
            code = room_manager.create_room(big_blind=big_blind, small_blind=small_blind)
            room_manager.join_room(sid, code)
            await sio.enter_room(sid, code)
            await sio.emit("room_created", {"code": code, "big_blind": big_blind, "small_blind": small_blind}, to=sid)
        except Exception as exc:
            await _error(sio, sid, exc)

    @sio.event
    async def join_room(sid: str, data: dict) -> None:
        try:
            code = str(data.get("code") or data.get("room_code", "")).upper()
            engine = room_manager.join_room(sid, code)
            await sio.enter_room(sid, code)
            await sio.emit("joined_room", {"player_id": sid, "room_code": code}, to=sid)
            await sio.emit("player_joined", {"player_id": sid}, room=code)
            await sio.emit("game_state", engine.public_state(), to=sid)
        except Exception as exc:
            await _error(sio, sid, exc)

    @sio.event
    async def leave_room(sid: str, data: dict) -> None:
        code = room_manager.leave_room(sid)
        if code:
            await sio.leave_room(sid, code)
            await sio.emit("player_left", {"player_id": sid}, room=code)

    # ── Seat events ──────────────────────────────────────────────────────────

    @sio.event
    async def sit_down(sid: str, data: dict) -> None:
        try:
            engine = _require_room(room_manager, sid)
            code = room_manager.get_player_room_code(sid)
            engine.add_player(
                player_id=sid,
                seat=int(data["seat"]),
                buy_in=int(data["buy_in"]),
            )
            await sio.emit("player_sat_down", {"player_id": sid}, room=code)
            await sio.emit("game_state", engine.public_state(), room=code)
        except Exception as exc:
            await _error(sio, sid, exc)

    @sio.event
    async def stand_up(sid: str, data: dict) -> None:
        try:
            engine = _require_room(room_manager, sid)
            code = room_manager.get_player_room_code(sid)
            engine.remove_player(sid)
            await sio.emit("game_state", engine.public_state(), room=code)
        except Exception as exc:
            await _error(sio, sid, exc)

    # ── Game flow ────────────────────────────────────────────────────────────

    @sio.event
    async def start_game(sid: str, data: dict) -> None:
        try:
            engine = _require_room(room_manager, sid)
            code = room_manager.get_player_room_code(sid)
            events = engine.start_hand()
            await _dispatch_events(sio, events, engine, code)
            await _run_bots(sio, engine, code)
        except Exception as exc:
            await _error(sio, sid, exc)

    @sio.event
    async def player_action(sid: str, data: dict) -> None:
        try:
            engine = _require_room(room_manager, sid)
            code = room_manager.get_player_room_code(sid)
            action = ActionType(data["action"])
            amount = int(data.get("amount", 0))
            events = engine.process_action(sid, action, amount)
            await _dispatch_events(sio, events, engine, code)
            await _run_bots(sio, engine, code)
        except Exception as exc:
            await _error(sio, sid, exc)

    @sio.event
    async def request_next_hand(sid: str, data: dict) -> None:
        try:
            engine = _require_room(room_manager, sid)
            code = room_manager.get_player_room_code(sid)
            if engine.phase != Phase.HAND_COMPLETE:
                raise ValueError("Current hand is not complete")
            events = engine.start_hand()
            await _dispatch_events(sio, events, engine, code)
            await _run_bots(sio, engine, code)
        except Exception as exc:
            await _error(sio, sid, exc)

    # ── Play vs Bots ─────────────────────────────────────────────────────────

    @sio.event
    async def play_with_bots(sid: str, data: dict) -> None:
        try:
            name = str(data.get("name") or "Player").strip() or "Player"
            bot_count = max(1, min(6, int(data.get("bot_count", 1))))
            buy_in = max(1, int(data.get("buy_in", 1000)))

            code = room_manager.create_room(big_blind=20, small_blind=10)
            room_manager.join_room(sid, code)
            await sio.enter_room(sid, code)
            engine = room_manager.get_room(code)
            if engine is None:
                raise RuntimeError("Failed to create room")

            # Seat the human at seat 0
            engine.add_player(player_id=sid, seat=0, buy_in=buy_in, name=name)
            # Seat bots at seats 1..N
            for i in range(bot_count):
                bot_id = make_bot_id(i)
                engine.add_player(
                    player_id=bot_id,
                    seat=i + 1,
                    buy_in=buy_in,
                    name=BOT_NAMES[i % len(BOT_NAMES)],
                    is_bot=True,
                )

            await sio.emit("joined_room", {"player_id": sid, "room_code": code}, to=sid)
            await sio.emit("game_state", engine.public_state(), to=sid)

            # Start the first hand and run any bot turns until human's turn or hand ends
            events = engine.start_hand()
            await _dispatch_events(sio, events, engine, code)
            await _run_bots(sio, engine, code)
        except Exception as exc:
            await _error(sio, sid, exc)


# ── Helpers ──────────────────────────────────────────────────────────────────


def _require_room(rm: RoomManager, sid: str):
    engine = rm.get_player_engine(sid)
    if not engine:
        raise ValueError("Not in a room")
    return engine


async def _dispatch_events(
    sio: socketio.AsyncServer, events, engine, code: str
) -> None:
    for event in events:
        if event.type == "cards_dealt":
            # Send each player their own hole cards as Card objects (animation hint)
            for player_id, card_strs in event.data["hole_cards"].items():
                cards_obj = [{"rank": c[:-1], "suit": c[-1], "code": c} for c in card_strs]
                await sio.emit("cards_dealt", {"hole_cards": cards_obj}, to=player_id)
        else:
            await sio.emit(event.type, event.data, room=code)
    # Personalised state to each connected player so each sees only their own hole cards.
    # (Broadcasting public_state to the room would clobber a player's personalised hole_cards.)
    base_state = engine.public_state()
    for p in engine.players:
        pid = p.player_id
        # Bot players never receive socket messages; skip them
        if "_bot_" in pid:
            continue
        cards = list(p.hole_cards) if p.hole_cards else []
        per_state = _personalise(base_state, pid, cards)
        await sio.emit("game_state", per_state, to=pid)


def _personalise(base_state: dict, player_id: str, hole_cards) -> dict:
    """Return a shallow copy of base_state with `hole_cards` filled in for `player_id`."""
    state = {**base_state, "players": [dict(p) for p in base_state["players"]]}
    for p in state["players"]:
        if p["id"] == player_id and hole_cards:
            p["hole_cards"] = [
                {"rank": c[:-1], "suit": c[-1], "code": c} if isinstance(c, str)
                else {"rank": c.rank, "suit": c.suit, "code": f"{c.rank}{c.suit}"}
                for c in hole_cards
            ]
    return state


async def _error(sio: socketio.AsyncServer, sid: str, exc: Exception) -> None:
    log.warning("Error for %s: %s", sid, exc)
    await sio.emit("error", {"message": str(exc)}, to=sid)


async def _run_bots(sio: socketio.AsyncServer, engine, code: str) -> None:
    """Auto-act for any bots whose turn it is, until a human acts or hand ends."""
    while engine.action_queue:
        idx = engine.action_queue[0]
        player = engine.players[idx]
        if not is_bot_id(player.player_id):
            return
        await sio.sleep(1.2)
        try:
            action, amount = bot_decide(engine, player.player_id)
            events = engine.process_action(player.player_id, action, amount)
        except Exception as exc:
            log.warning("Bot %s action failed: %s", player.player_id, exc)
            return
        await _dispatch_events(sio, events, engine, code)
