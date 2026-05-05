from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Dict, List, Optional, Set

from .card import Card, make_deck, shuffle_deck
from .evaluator import best_hand_cards, best_hand_score, hand_name


class Phase(str, Enum):
    WAITING = "waiting"
    PREFLOP = "preflop"
    FLOP = "flop"
    TURN = "turn"
    RIVER = "river"
    SHOWDOWN = "showdown"
    HAND_COMPLETE = "hand_complete"


class PlayerStatus(str, Enum):
    ACTIVE = "active"
    FOLDED = "folded"
    ALL_IN = "all_in"
    SITTING_OUT = "sitting_out"


class ActionType(str, Enum):
    FOLD = "fold"
    CHECK = "check"
    CALL = "call"
    RAISE = "raise"


@dataclass
class Player:
    player_id: str
    seat: int
    stack: int
    status: PlayerStatus = PlayerStatus.ACTIVE
    hole_cards: List[Card] = field(default_factory=list)
    bet_this_round: int = 0
    total_bet: int = 0
    name: Optional[str] = None
    is_bot: bool = False


@dataclass
class SidePot:
    amount: int
    eligible: List[str]  # player_ids who can win this pot


@dataclass
class GameEvent:
    type: str
    data: dict


_PHASE_SEQUENCE = {
    Phase.PREFLOP: Phase.FLOP,
    Phase.FLOP: Phase.TURN,
    Phase.TURN: Phase.RIVER,
    Phase.RIVER: Phase.SHOWDOWN,
}


class PokerEngine:
    """
    Server-authoritative Texas Hold'em engine.

    All game state lives here. Callers receive lists of GameEvents and are
    responsible for routing them to connected clients. Hole cards are included
    in events; callers must ensure each player only receives their own cards.
    """

    def __init__(self, room_code: str, big_blind: int = 20, small_blind: int = 10):
        self.room_code = room_code
        self.big_blind = big_blind
        self.small_blind = small_blind
        self.players: List[Player] = []
        self.phase = Phase.WAITING
        self.community_cards: List[Card] = []
        self.pot = 0
        self.current_bet = 0
        self.min_raise = big_blind
        self.dealer_index: int = -1  # index into self.players; -1 = not yet set
        self.action_queue: List[int] = []  # player indices who still need to act
        self.deck: List[Card] = []
        self.hand_number = 0
        self._events: List[GameEvent] = []
        self.dealer_id: Optional[str] = None
        self.sb_id: Optional[str] = None
        self.bb_id: Optional[str] = None

    # ── Seat management ──────────────────────────────────────────────────────

    def add_player(
        self,
        player_id: str,
        seat: int,
        buy_in: int,
        name: Optional[str] = None,
        is_bot: bool = False,
    ) -> None:
        if self.phase not in (Phase.WAITING, Phase.HAND_COMPLETE):
            raise ValueError("Cannot add player mid-hand")
        if any(p.seat == seat for p in self.players):
            raise ValueError(f"Seat {seat} is taken")
        if any(p.player_id == player_id for p in self.players):
            raise ValueError(f"Player {player_id!r} already seated")
        if buy_in <= 0:
            raise ValueError("Buy-in must be positive")
        self.players.append(Player(
            player_id=player_id, seat=seat, stack=buy_in, name=name, is_bot=is_bot,
        ))
        self.players.sort(key=lambda p: p.seat)

    def remove_player(self, player_id: str) -> None:
        if self.phase not in (Phase.WAITING, Phase.HAND_COMPLETE):
            raise ValueError("Cannot remove player mid-hand")
        self.players = [p for p in self.players if p.player_id != player_id]

    # ── Hand lifecycle ───────────────────────────────────────────────────────

    def start_hand(self) -> List[GameEvent]:
        self._events = []

        # Reset hand state
        self.hand_number += 1
        self.community_cards = []
        self.pot = 0
        self.current_bet = 0
        self.min_raise = self.big_blind
        self.action_queue = []

        for p in self.players:
            p.hole_cards = []
            p.bet_this_round = 0
            p.total_bet = 0
            p.status = PlayerStatus.ACTIVE if p.stack > 0 else PlayerStatus.SITTING_OUT

        active_indices = self._active_indices()
        if len(active_indices) < 2:
            raise ValueError("Need at least 2 players with chips")

        # Advance dealer button
        self.dealer_index = self._next_active_seat(self.dealer_index, active_indices)

        # Determine blind positions
        n = len(active_indices)
        dealer_pos = active_indices.index(self.dealer_index)

        if n == 2:
            # Heads-up: dealer posts SB, acts first preflop
            sb_pos, bb_pos, utg_pos = dealer_pos, (dealer_pos + 1) % n, dealer_pos
        else:
            sb_pos = (dealer_pos + 1) % n
            bb_pos = (dealer_pos + 2) % n
            utg_pos = (dealer_pos + 3) % n

        sb_idx = active_indices[sb_pos]
        bb_idx = active_indices[bb_pos]
        self.dealer_id = self.players[self.dealer_index].player_id
        self.sb_id = self.players[sb_idx].player_id
        self.bb_id = self.players[bb_idx].player_id

        # Shuffle and deal hole cards
        self.deck = shuffle_deck(make_deck())
        for idx in active_indices:
            self.players[idx].hole_cards = [self.deck.pop(), self.deck.pop()]

        # Post blinds (may put players all-in)
        self._post_blind(sb_idx, self.small_blind)
        self._post_blind(bb_idx, self.big_blind)
        self.current_bet = self.big_blind
        self.min_raise = self.big_blind

        # Build preflop action queue starting from UTG, wrapping to BB (inclusive)
        self.action_queue = [
            active_indices[(utg_pos + i) % n]
            for i in range(n)
            if self.players[active_indices[(utg_pos + i) % n]].status == PlayerStatus.ACTIVE
        ]

        self.phase = Phase.PREFLOP

        self._emit("hand_started", {
            "hand_number": self.hand_number,
            "dealer_seat": self.players[self.dealer_index].seat,
            "small_blind": self.small_blind,
            "big_blind": self.big_blind,
            "pot": self.pot,
        })
        self._emit("cards_dealt", {
            "hole_cards": {
                self.players[idx].player_id: [str(c) for c in self.players[idx].hole_cards]
                for idx in active_indices
            }
        })
        self._emit("action_required", self._action_info())

        return list(self._events)

    # ── Action processing ────────────────────────────────────────────────────

    def process_action(
        self, player_id: str, action: ActionType, amount: int = 0
    ) -> List[GameEvent]:
        self._events = []

        if not self.action_queue:
            raise ValueError("No action pending")

        current_idx = self.action_queue[0]
        player = self.players[current_idx]

        if player.player_id != player_id:
            raise ValueError(f"Not {player_id!r}'s turn")

        valid = self._valid_actions(current_idx)
        if action not in valid:
            raise ValueError(f"Invalid action {action!r}; valid: {[a.value for a in valid]}")

        if action == ActionType.FOLD:
            player.status = PlayerStatus.FOLDED
            self.action_queue.pop(0)
            self._emit("player_action", {"player_id": player_id, "action": "fold"})

        elif action == ActionType.CHECK:
            self.action_queue.pop(0)
            self._emit("player_action", {"player_id": player_id, "action": "check"})

        elif action == ActionType.CALL:
            call_amount = min(
                self.current_bet - player.bet_this_round, player.stack
            )
            self._apply_bet(player, call_amount)
            self.action_queue.pop(0)
            self._emit("player_action", {
                "player_id": player_id,
                "action": "call",
                "amount": call_amount,
            })

        elif action == ActionType.RAISE:
            # `amount` is the total to-bet (new current_bet level)
            raise_to = max(amount, self.current_bet + self.min_raise)
            max_raise = player.stack + player.bet_this_round
            raise_to = min(raise_to, max_raise)

            raise_increase = raise_to - self.current_bet
            if raise_increase > 0:
                self.min_raise = max(self.min_raise, raise_increase)

            additional = raise_to - player.bet_this_round
            self._apply_bet(player, additional)
            self.current_bet = raise_to

            # Everyone else still active needs to act again
            self.action_queue = self._post_raise_queue(current_idx)
            self._emit("player_action", {
                "player_id": player_id,
                "action": "raise",
                "amount": raise_to,
            })

        self._emit("pot_update", {"pot": self.pot, "current_bet": self.current_bet})

        # Check if hand is over (only one player not folded)
        contestants = self._contestants()
        if len(contestants) == 1:
            winner = contestants[0]
            pot_won = self.pot
            winner.stack += pot_won
            self.pot = 0
            self.action_queue = []
            self.phase = Phase.HAND_COMPLETE
            self._emit("hand_complete", {
                "winners": [winner.player_id],
                "pot": pot_won,
                "reason": "all_folded",
            })
            return list(self._events)

        # Advance street when betting round is over
        if not self.action_queue:
            self._advance_street()
        else:
            self._emit("action_required", self._action_info())

        return list(self._events)

    def _apply_bet(self, player: Player, amount: int) -> None:
        player.stack -= amount
        player.bet_this_round += amount
        player.total_bet += amount
        self.pot += amount
        if player.stack == 0:
            player.status = PlayerStatus.ALL_IN

    def _post_blind(self, player_index: int, amount: int) -> None:
        p = self.players[player_index]
        actual = min(amount, p.stack)
        self._apply_bet(p, actual)

    # ── Street advancement ───────────────────────────────────────────────────

    def _advance_street(self) -> None:
        """Advance to the next street, dealing cards and setting up action queue.
        Loops automatically through streets where all players are all-in."""
        while True:
            for p in self.players:
                p.bet_this_round = 0
            self.current_bet = 0
            self.min_raise = self.big_blind

            next_phase = _PHASE_SEQUENCE.get(self.phase)
            if next_phase is None:
                return  # Already at showdown or hand_complete

            self.phase = next_phase

            if next_phase == Phase.FLOP:
                self.community_cards = [self.deck.pop() for _ in range(3)]
            elif next_phase in (Phase.TURN, Phase.RIVER):
                self.community_cards.append(self.deck.pop())

            self._emit("street_dealt", {
                "phase": self.phase.value,
                "community_cards": [str(c) for c in self.community_cards],
            })

            if next_phase == Phase.SHOWDOWN:
                self._resolve_showdown()
                return

            # Build action queue; if empty (all remaining players all-in) loop again
            self.action_queue = self._street_action_queue()
            if self.action_queue:
                self._emit("action_required", self._action_info())
                return
            # No one can act — run out the board automatically

    # ── Showdown ─────────────────────────────────────────────────────────────

    def _resolve_showdown(self) -> None:
        self.phase = Phase.SHOWDOWN
        contestants = self._contestants()
        side_pots = self._compute_side_pots(contestants)

        results = []
        for sp in side_pots:
            eligible = [p for p in contestants if p.player_id in sp.eligible]
            if len(eligible) == 1:
                eligible[0].stack += sp.amount
                results.append({
                    "winners": [eligible[0].player_id],
                    "pot": sp.amount,
                    "hand": "uncontested",
                    "best_cards": [],
                })
                continue

            scores = {
                p.player_id: best_hand_score(p.hole_cards, self.community_cards)
                for p in eligible
            }
            best_score = max(scores.values())
            winners = [pid for pid, s in scores.items() if s == best_score]

            share, remainder = divmod(sp.amount, len(winners))
            for pid in winners:
                p = next(pl for pl in eligible if pl.player_id == pid)
                p.stack += share
            # Odd chip to first winner (seat order)
            if remainder:
                first_winner = min(
                    (p for p in eligible if p.player_id in winners),
                    key=lambda p: p.seat,
                )
                first_winner.stack += remainder

            top_winner = next(p for p in eligible if p.player_id == winners[0])
            best_cards = best_hand_cards(top_winner.hole_cards, self.community_cards)

            results.append({
                "winners": winners,
                "pot": sp.amount,
                "hand": hand_name(best_score),
                "best_cards": [str(c) for c in best_cards],
            })

        # Per-player reveal entries (hole cards + their best 5).
        reveals = []
        for p in contestants:
            best5 = best_hand_cards(p.hole_cards, self.community_cards)
            reveals.append({
                "player_id": p.player_id,
                "cards": [str(c) for c in p.hole_cards],
                "best_cards": [str(c) for c in best5],
                "hand_name": hand_name(best_hand_score(p.hole_cards, self.community_cards)),
            })

        # Convenience: top-level main pot summary (last/biggest pot wins headline).
        main_result = results[-1] if results else None
        showdown_payload = {
            "reveals": reveals,
            "community_cards": [str(c) for c in self.community_cards],
            "results": results,
            "winners": main_result["winners"] if main_result else [],
            "pot": main_result["pot"] if main_result else 0,
            "hand_name": main_result["hand"] if main_result else "",
            "best_cards": main_result["best_cards"] if main_result else [],
        }
        self._emit("showdown", showdown_payload)
        self.pot = 0
        self.phase = Phase.HAND_COMPLETE
        self._emit("hand_complete", {"results": results})

    def _compute_side_pots(self, contestants: List[Player]) -> List[SidePot]:
        all_bets: Dict[str, int] = {
            p.player_id: p.total_bet for p in self.players if p.total_bet > 0
        }
        allin_players: Set[str] = {
            p.player_id for p in self.players if p.status == PlayerStatus.ALL_IN
        }
        contestant_ids: Set[str] = {p.player_id for p in contestants}

        if not allin_players:
            return [SidePot(amount=self.pot, eligible=list(contestant_ids))]

        levels = sorted({all_bets[pid] for pid in allin_players if pid in all_bets})
        side_pots: List[SidePot] = []
        prev = 0

        for level in levels:
            amount = sum(min(bet, level) - prev for bet in all_bets.values())
            eligible = [
                pid for pid, bet in all_bets.items()
                if bet >= level and pid in contestant_ids
            ]
            if amount > 0 and eligible:
                side_pots.append(SidePot(amount=amount, eligible=eligible))
            prev = level

        # Main pot: contributions above the highest all-in level
        remaining = sum(max(0, bet - prev) for bet in all_bets.values())
        if remaining > 0:
            eligible = [
                pid for pid, bet in all_bets.items()
                if bet > prev and pid in contestant_ids
            ]
            if eligible:
                side_pots.append(SidePot(amount=remaining, eligible=eligible))

        return side_pots

    # ── Queue helpers ────────────────────────────────────────────────────────

    def _post_raise_queue(self, raiser_idx: int) -> List[int]:
        n = len(self.players)
        return [
            (raiser_idx + i) % n
            for i in range(1, n)
            if self.players[(raiser_idx + i) % n].status == PlayerStatus.ACTIVE
        ]

    def _street_action_queue(self) -> List[int]:
        """Post-flop queue: first active player after dealer, going clockwise."""
        active = self._active_indices()
        if not active:
            return []
        n = len(self.players)
        start = next(
            ((self.dealer_index + i) % n for i in range(1, n + 1)
             if (self.dealer_index + i) % n in active),
            active[0],
        )
        start_pos = active.index(start)
        return [active[(start_pos + i) % len(active)] for i in range(len(active))]

    def _next_active_seat(self, from_idx: int, active_indices: List[int]) -> int:
        n = len(self.players)
        for i in range(1, n + 1):
            idx = (from_idx + i) % n
            if idx in active_indices:
                return idx
        return active_indices[0]

    # ── Queries ──────────────────────────────────────────────────────────────

    def _active_indices(self) -> List[int]:
        return [i for i, p in enumerate(self.players) if p.status == PlayerStatus.ACTIVE]

    def _contestants(self) -> List[Player]:
        """Players still in the hand (active or all-in)."""
        return [p for p in self.players if p.status in (PlayerStatus.ACTIVE, PlayerStatus.ALL_IN)]

    def _valid_actions(self, player_idx: int) -> List[ActionType]:
        p = self.players[player_idx]
        actions: List[ActionType] = [ActionType.FOLD]
        if p.bet_this_round >= self.current_bet:
            actions.append(ActionType.CHECK)
        else:
            actions.append(ActionType.CALL)
        if p.stack > 0:
            actions.append(ActionType.RAISE)
        return actions

    def _action_info(self) -> dict:
        if not self.action_queue:
            return {}
        idx = self.action_queue[0]
        p = self.players[idx]
        return {
            "player_id": p.player_id,
            "valid_actions": [a.value for a in self._valid_actions(idx)],
            "current_bet": self.current_bet,
            "to_call": max(0, self.current_bet - p.bet_this_round),
            "min_raise_to": self.current_bet + self.min_raise,
            "stack": p.stack,
        }

    def _emit(self, event_type: str, data: dict) -> None:
        self._events.append(GameEvent(type=event_type, data=data))

    # ── Public state (safe to broadcast) ────────────────────────────────────

    def public_state(self) -> dict:
        action_on = (
            self.players[self.action_queue[0]].player_id if self.action_queue else None
        )
        return {
            "room_code": self.room_code,
            "phase": self.phase.value,
            "hand_number": self.hand_number,
            "community_cards": [
                {"rank": c.rank, "suit": c.suit, "code": str(c)}
                for c in self.community_cards
            ],
            "pot": self.pot,
            "current_bet": self.current_bet,
            "min_raise": self.min_raise,
            "small_blind": self.small_blind,
            "big_blind": self.big_blind,
            "current_player_id": action_on,
            "dealer_id": self.dealer_id,
            "small_blind_id": self.sb_id,
            "big_blind_id": self.bb_id,
            "players": [
                {
                    "id": p.player_id,
                    "name": p.name or p.player_id[:8],
                    "seat": p.seat,
                    "stack": p.stack,
                    "status": p.status.value.upper(),
                    "current_bet": p.bet_this_round,
                    "total_bet_this_round": p.total_bet,
                    "is_dealer": p.player_id == self.dealer_id,
                    "is_small_blind": p.player_id == self.sb_id,
                    "is_big_blind": p.player_id == self.bb_id,
                    "is_bot": p.is_bot,
                }
                for p in self.players
            ],
        }
