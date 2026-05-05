from __future__ import annotations

import asyncio
import json
import logging

from fastapi import WebSocket, WebSocketDisconnect

from ..game.engine import ActionType, Phase
from ..services.bot import BOT_NAMES, decide as bot_decide, is_bot_id, make_bot_id
from ..services.room_manager import RoomManager
from .ws_manager import WebSocketManager

log = logging.getLogger(__name__)


async def websocket_endpoint(
    websocket: WebSocket,
    ws_manager: WebSocketManager,
    room_manager: RoomManager,
) -> None:
    player_id = await ws_manager.accept(websocket)
    await ws_manager.send(player_id, "connected", {"player_id": player_id})
    log.info("Player connected: %s", player_id)

    try:
        while True:
            raw = await websocket.receive_text()
            try:
                msg = json.loads(raw)
                event = msg.get("event", "")
                data = msg.get("data") or {}
                await _dispatch(ws_manager, room_manager, player_id, event, data)
            except json.JSONDecodeError:
                await ws_manager.send(player_id, "error", {"message": "Invalid JSON"})
    except WebSocketDisconnect:
        log.info("Player disconnected: %s", player_id)
        await _on_disconnect(ws_manager, room_manager, player_id)


# ── Event dispatch ───────────────────────────────────────────────────────────


async def _dispatch(
    wm: WebSocketManager,
    rm: RoomManager,
    pid: str,
    event: str,
    data: dict,
) -> None:
    try:
        match event:
            case "create_room":
                await _create_room(wm, rm, pid, data)
            case "join_room":
                await _join_room(wm, rm, pid, data)
            case "leave_room":
                await _leave_room(wm, rm, pid)
            case "sit_down":
                await _sit_down(wm, rm, pid, data)
            case "stand_up":
                await _stand_up(wm, rm, pid)
            case "start_game":
                await _start_game(wm, rm, pid)
            case "player_action":
                await _player_action(wm, rm, pid, data)
            case "request_next_hand":
                await _request_next_hand(wm, rm, pid)
            case "play_with_bots":
                await _play_with_bots(wm, rm, pid, data)
            case _:
                await wm.send(pid, "error", {"message": f"Unknown event: {event!r}"})
    except Exception as exc:
        log.warning("Error handling %r for %s: %s", event, pid, exc)
        await wm.send(pid, "error", {"message": str(exc)})


# ── Handlers ─────────────────────────────────────────────────────────────────


async def _create_room(wm: WebSocketManager, rm: RoomManager, pid: str, data: dict) -> None:
    bb = int(data.get("big_blind", 20))
    sb = int(data.get("small_blind", 10))
    code = rm.create_room(big_blind=bb, small_blind=sb)
    wm.enter_room(pid, code)
    rm.join_room(pid, code)
    await wm.send(pid, "room_created", {"code": code, "big_blind": bb, "small_blind": sb})


async def _join_room(wm: WebSocketManager, rm: RoomManager, pid: str, data: dict) -> None:
    code = str(data.get("code", "")).upper()
    engine = rm.join_room(pid, code)
    wm.enter_room(pid, code)
    await wm.broadcast(code, "player_joined", {"player_id": pid})
    await wm.send(pid, "room_state", engine.public_state())


async def _leave_room(wm: WebSocketManager, rm: RoomManager, pid: str) -> None:
    code = wm.leave_room(pid)
    rm.leave_room(pid)
    if code:
        await wm.broadcast(code, "player_left", {"player_id": pid})


async def _sit_down(wm: WebSocketManager, rm: RoomManager, pid: str, data: dict) -> None:
    engine = _require_engine(rm, pid)
    code = wm.get_room_code(pid)
    engine.add_player(player_id=pid, seat=int(data["seat"]), buy_in=int(data["buy_in"]))
    await wm.broadcast(code, "room_state", engine.public_state())


async def _stand_up(wm: WebSocketManager, rm: RoomManager, pid: str) -> None:
    engine = _require_engine(rm, pid)
    code = wm.get_room_code(pid)
    engine.remove_player(pid)
    await wm.broadcast(code, "room_state", engine.public_state())


async def _start_game(wm: WebSocketManager, rm: RoomManager, pid: str) -> None:
    engine = _require_engine(rm, pid)
    code = wm.get_room_code(pid)
    events = engine.start_hand()
    await _dispatch_events(wm, events, engine, code)
    await _run_bots(wm, engine, code)


async def _player_action(wm: WebSocketManager, rm: RoomManager, pid: str, data: dict) -> None:
    engine = _require_engine(rm, pid)
    code = wm.get_room_code(pid)
    action = ActionType(data["action"])
    amount = int(data.get("amount", 0))
    events = engine.process_action(pid, action, amount)
    await _dispatch_events(wm, events, engine, code)
    # If the next-to-act seat is a bot, fire bot decisions until it's human again.
    await _run_bots(wm, engine, code)


async def _request_next_hand(wm: WebSocketManager, rm: RoomManager, pid: str) -> None:
    engine = _require_engine(rm, pid)
    code = wm.get_room_code(pid)
    if engine.phase != Phase.HAND_COMPLETE:
        raise ValueError("Current hand is not complete")
    events = engine.start_hand()
    await _dispatch_events(wm, events, engine, code)
    await _run_bots(wm, engine, code)


async def _play_with_bots(wm: WebSocketManager, rm: RoomManager, pid: str, data: dict) -> None:
    """Spin up a private room: human at seat 0, N bots, and start the first hand."""
    name = str(data.get("name") or "Player").strip() or "Player"
    bot_count = max(1, min(6, int(data.get("bot_count", 1))))
    buy_in = max(1, int(data.get("buy_in", 1000)))

    code = rm.create_room(big_blind=20, small_blind=10)
    rm.join_room(pid, code)
    wm.enter_room(pid, code)
    engine = rm.get_room(code)
    if engine is None:
        raise RuntimeError("Failed to create room")

    engine.add_player(player_id=pid, seat=0, buy_in=buy_in, name=name)
    for i in range(bot_count):
        engine.add_player(
            player_id=make_bot_id(i),
            seat=i + 1,
            buy_in=buy_in,
            name=BOT_NAMES[i % len(BOT_NAMES)],
            is_bot=True,
        )

    await wm.send(pid, "room_created", {"code": code, "big_blind": 20, "small_blind": 10})
    await wm.broadcast(code, "room_state", engine.public_state())

    events = engine.start_hand()
    await _dispatch_events(wm, events, engine, code)
    await _run_bots(wm, engine, code)


async def _on_disconnect(wm: WebSocketManager, rm: RoomManager, pid: str) -> None:
    code = wm.remove(pid)
    rm.leave_room(pid)
    if code:
        engine = rm.get_room(code)
        if engine:
            try:
                engine.remove_player(pid)
            except ValueError:
                pass
        await wm.broadcast(code, "player_left", {"player_id": pid})


# ── Helpers ──────────────────────────────────────────────────────────────────


def _require_engine(rm: RoomManager, pid: str):
    engine = rm.get_player_engine(pid)
    if not engine:
        raise ValueError("Not in a room")
    return engine


async def _dispatch_events(wm: WebSocketManager, events, engine, code: str) -> None:
    for ev in events:
        if ev.type == "cards_dealt":
            # Private: each player sees only their own hole cards.
            # Send rich card objects so the client can render rank/suit
            # without re-parsing the wire string.
            for player_id, card_strs in ev.data["hole_cards"].items():
                cards_obj = [
                    {"rank": c[:-1], "suit": c[-1], "code": c} for c in card_strs
                ]
                await wm.send(player_id, "cards_dealt", {"hole_cards": cards_obj})
        else:
            await wm.broadcast(code, ev.type, ev.data)
    await wm.broadcast(code, "room_state", engine.public_state())


async def _run_bots(wm: WebSocketManager, engine, code: str) -> None:
    """Auto-act for any bots whose turn it is, until a human acts or the hand ends.

    A short sleep before each bot decision gives the client time to play the
    deal/flip animation for the previous action so bot turns don't look
    instantaneous.
    """
    while engine.action_queue:
        idx = engine.action_queue[0]
        player = engine.players[idx]
        if not is_bot_id(player.player_id):
            return
        await asyncio.sleep(1.4)
        try:
            action, amount = bot_decide(engine, player.player_id)
            events = engine.process_action(player.player_id, action, amount)
        except Exception as exc:
            log.warning("Bot %s action failed: %s", player.player_id, exc)
            return
        await _dispatch_events(wm, events, engine, code)
