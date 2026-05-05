from __future__ import annotations

import random
import uuid
from typing import Tuple

from ..game.engine import ActionType, PokerEngine

BOT_NAMES = ["Alice", "Bob", "Carla", "Dan", "Eve", "Finn", "Gina", "Hank"]
_BOT_TAG = "_bot_"


def make_bot_id(index: int) -> str:
    name = BOT_NAMES[index % len(BOT_NAMES)]
    return f"{name}{_BOT_TAG}{uuid.uuid4().hex[:6]}"


def is_bot_id(player_id: str) -> bool:
    return _BOT_TAG in player_id


def decide(engine: PokerEngine, bot_id: str) -> Tuple[ActionType, int]:
    """Pick a simple action for `bot_id`. Assumes it's the bot's turn."""
    player = next(p for p in engine.players if p.player_id == bot_id)
    to_call = max(0, engine.current_bet - player.bet_this_round)
    can_check = to_call == 0
    pot = max(1, engine.pot)
    pot_odds = to_call / (pot + to_call) if to_call else 0.0

    r = random.random()

    if can_check:
        # Mostly check; occasionally make a small probe bet
        if r < 0.75 or player.stack < engine.big_blind * 2:
            return ActionType.CHECK, 0
        raise_to = min(player.stack + player.bet_this_round,
                       engine.current_bet + engine.big_blind * random.choice([2, 3]))
        return ActionType.RAISE, raise_to

    # Facing a bet
    if pot_odds > 0.45:
        # Expensive call — usually fold
        if r < 0.65:
            return ActionType.FOLD, 0
        if r < 0.95 or player.stack <= to_call:
            return ActionType.CALL, 0
        return ActionType.RAISE, engine.current_bet + engine.min_raise

    # Cheap-ish call
    if r < 0.20:
        return ActionType.FOLD, 0
    if r < 0.85 or player.stack <= to_call:
        return ActionType.CALL, 0
    raise_to = min(player.stack + player.bet_this_round,
                   engine.current_bet + engine.min_raise * random.choice([1, 2]))
    return ActionType.RAISE, raise_to
