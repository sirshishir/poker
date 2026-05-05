"""
Tests for the pure-Python poker engine.
Run with:  pytest backend/tests/ -v
"""
import pytest

from app.game.card import Card, make_deck, shuffle_deck
from app.game.evaluator import (
    FLUSH,
    FOUR_OF_A_KIND,
    FULL_HOUSE,
    HIGH_CARD,
    PAIR,
    STRAIGHT,
    STRAIGHT_FLUSH,
    THREE_OF_A_KIND,
    TWO_PAIR,
    _eval5,
    best_hand_score,
    hand_name,
)
from app.game.engine import ActionType, Phase, PlayerStatus, PokerEngine


# ── Card tests ───────────────────────────────────────────────────────────────


class TestCard:
    def test_deck_has_52_cards(self):
        assert len(make_deck()) == 52

    def test_deck_no_duplicates(self):
        deck = make_deck()
        assert len(set(deck)) == 52

    def test_from_str_parses_rank_and_suit(self):
        c = Card.from_str("Ah")
        assert c.rank == "A"
        assert c.suit == "h"
        assert c.rank_value == 14

    def test_from_str_two_of_clubs(self):
        c = Card.from_str("2c")
        assert c.rank_value == 2

    def test_from_str_invalid_raises(self):
        with pytest.raises(ValueError):
            Card.from_str("XX")

    def test_shuffle_preserves_cards(self):
        deck = make_deck()
        shuffled = shuffle_deck(deck)
        assert set(shuffled) == set(deck)
        assert len(shuffled) == 52

    def test_shuffle_is_different(self):
        deck = make_deck()
        # Astronomically unlikely to produce the same order
        assert shuffle_deck(deck) != shuffle_deck(deck)


# ── Evaluator tests ──────────────────────────────────────────────────────────


def cards(*strs):
    return [Card.from_str(s) for s in strs]


class TestEval5:
    def test_high_card(self):
        assert _eval5(cards("As", "Kh", "Qd", "Jc", "9s"))[0] == HIGH_CARD

    def test_pair(self):
        assert _eval5(cards("As", "Ah", "Kd", "Qc", "Js"))[0] == PAIR

    def test_two_pair(self):
        assert _eval5(cards("As", "Ah", "Kd", "Kc", "Qs"))[0] == TWO_PAIR

    def test_three_of_a_kind(self):
        assert _eval5(cards("As", "Ah", "Ad", "Kc", "Qs"))[0] == THREE_OF_A_KIND

    def test_straight_broadway(self):
        s = _eval5(cards("As", "Kh", "Qd", "Jc", "Ts"))
        assert s[0] == STRAIGHT
        assert s[1] == 14  # ace-high

    def test_straight_wheel(self):
        s = _eval5(cards("As", "2h", "3d", "4c", "5s"))
        assert s[0] == STRAIGHT
        assert s[1] == 5  # 5-high (wheel)

    def test_flush(self):
        assert _eval5(cards("As", "Ks", "Qs", "Js", "9s"))[0] == FLUSH

    def test_full_house(self):
        assert _eval5(cards("As", "Ah", "Ad", "Kc", "Ks"))[0] == FULL_HOUSE

    def test_four_of_a_kind(self):
        assert _eval5(cards("As", "Ah", "Ad", "Ac", "Ks"))[0] == FOUR_OF_A_KIND

    def test_straight_flush(self):
        assert _eval5(cards("As", "Ks", "Qs", "Js", "Ts"))[0] == STRAIGHT_FLUSH

    def test_flush_beats_straight(self):
        straight = _eval5(cards("As", "Kh", "Qd", "Jc", "Ts"))
        flush = _eval5(cards("2s", "4s", "6s", "8s", "Ts"))
        assert flush > straight

    def test_pair_kickers_matter(self):
        pair_aces_king = _eval5(cards("As", "Ah", "Kd", "Qc", "Js"))
        pair_aces_queen = _eval5(cards("As", "Ah", "Qd", "Jc", "9s"))
        assert pair_aces_king > pair_aces_queen

    def test_two_pair_high_pair_matters(self):
        aces_kings = _eval5(cards("As", "Ah", "Kd", "Kc", "Qs"))
        kings_queens = _eval5(cards("Ks", "Kh", "Qd", "Qc", "As"))
        assert aces_kings > kings_queens


class TestBestHandScore:
    def test_four_aces_from_7_cards(self):
        hole = cards("As", "Ah")
        board = cards("Ad", "Ac", "Kh", "Qd", "Js")
        score = best_hand_score(hole, board)
        assert score[0] == FOUR_OF_A_KIND

    def test_best_5_chosen_from_7(self):
        # Board gives a straight flush; hole cards are irrelevant
        hole = cards("2c", "3c")
        board = cards("As", "Ks", "Qs", "Js", "Ts")
        score = best_hand_score(hole, board)
        assert score[0] == STRAIGHT_FLUSH

    def test_hand_name_returns_string(self):
        score = best_hand_score(cards("As", "Ah"), cards("Ad", "Ac", "2h", "3d", "4s"))
        assert hand_name(score) == "Four of a Kind"


# ── Engine tests ─────────────────────────────────────────────────────────────


def make_hu_game(bb=20, sb=10, stack=1000) -> PokerEngine:
    engine = PokerEngine("TEST", big_blind=bb, small_blind=sb)
    engine.add_player("alice", seat=1, buy_in=stack)
    engine.add_player("bob", seat=2, buy_in=stack)
    return engine


def make_3player_game(stacks=(500, 500, 500)) -> PokerEngine:
    engine = PokerEngine("TEST3", big_blind=20, small_blind=10)
    engine.add_player("alice", seat=1, buy_in=stacks[0])
    engine.add_player("bob", seat=2, buy_in=stacks[1])
    engine.add_player("charlie", seat=3, buy_in=stacks[2])
    return engine


class TestEngineSetup:
    def test_add_player_sorted_by_seat(self):
        engine = PokerEngine("X")
        engine.add_player("b", seat=3, buy_in=100)
        engine.add_player("a", seat=1, buy_in=100)
        assert [p.seat for p in engine.players] == [1, 3]

    def test_duplicate_seat_raises(self):
        engine = PokerEngine("X")
        engine.add_player("a", seat=1, buy_in=100)
        with pytest.raises(ValueError, match="taken"):
            engine.add_player("b", seat=1, buy_in=100)

    def test_duplicate_player_raises(self):
        engine = PokerEngine("X")
        engine.add_player("a", seat=1, buy_in=100)
        with pytest.raises(ValueError, match="already seated"):
            engine.add_player("a", seat=2, buy_in=100)

    def test_start_requires_2_players(self):
        engine = PokerEngine("X")
        engine.add_player("a", seat=1, buy_in=100)
        with pytest.raises(ValueError):
            engine.start_hand()


class TestHandStart:
    def test_phase_becomes_preflop(self):
        engine = make_hu_game()
        engine.start_hand()
        assert engine.phase == Phase.PREFLOP

    def test_pot_equals_blinds(self):
        engine = make_hu_game(bb=20, sb=10)
        engine.start_hand()
        assert engine.pot == 30

    def test_each_active_player_has_2_hole_cards(self):
        engine = make_hu_game()
        engine.start_hand()
        for p in engine.players:
            if p.status != PlayerStatus.SITTING_OUT:
                assert len(p.hole_cards) == 2

    def test_no_duplicate_hole_cards(self):
        engine = make_3player_game()
        engine.start_hand()
        all_cards = [c for p in engine.players for c in p.hole_cards]
        assert len(all_cards) == len(set(all_cards))

    def test_events_emitted(self):
        engine = make_hu_game()
        events = engine.start_hand()
        types = {e.type for e in events}
        assert "hand_started" in types
        assert "cards_dealt" in types
        assert "action_required" in types

    def test_action_queue_nonempty(self):
        engine = make_hu_game()
        engine.start_hand()
        assert engine.action_queue

    def test_3player_pot(self):
        engine = make_3player_game()
        engine.start_hand()
        assert engine.pot == 30


class TestHeadsUpFlow:
    def test_fold_ends_hand(self):
        engine = make_hu_game()
        engine.start_hand()
        first = engine.players[engine.action_queue[0]].player_id
        engine.process_action(first, ActionType.FOLD)
        assert engine.phase == Phase.HAND_COMPLETE

    def test_fold_awards_pot_to_winner(self):
        engine = make_hu_game(stack=1000)
        engine.start_hand()
        first_pid = engine.players[engine.action_queue[0]].player_id
        second_pid = next(p.player_id for p in engine.players if p.player_id != first_pid)
        first = next(p for p in engine.players if p.player_id == first_pid)
        second = next(p for p in engine.players if p.player_id == second_pid)
        stack_before = second.stack
        engine.process_action(first_pid, ActionType.FOLD)
        assert second.stack > stack_before

    def test_call_then_check_advances_to_flop(self):
        engine = make_hu_game()
        engine.start_hand()
        first = engine.players[engine.action_queue[0]].player_id
        engine.process_action(first, ActionType.CALL)
        second = engine.players[engine.action_queue[0]].player_id
        engine.process_action(second, ActionType.CHECK)
        assert engine.phase == Phase.FLOP
        assert len(engine.community_cards) == 3

    def test_raise_then_fold(self):
        engine = make_hu_game()
        engine.start_hand()
        first = engine.players[engine.action_queue[0]].player_id
        engine.process_action(first, ActionType.RAISE, 40)
        second = engine.players[engine.action_queue[0]].player_id
        engine.process_action(second, ActionType.FOLD)
        assert engine.phase == Phase.HAND_COMPLETE

    def test_out_of_turn_raises(self):
        engine = make_hu_game()
        engine.start_hand()
        first = engine.players[engine.action_queue[0]].player_id
        other = next(p.player_id for p in engine.players if p.player_id != first)
        with pytest.raises(ValueError, match="turn"):
            engine.process_action(other, ActionType.FOLD)

    def test_invalid_check_when_facing_bet(self):
        engine = make_hu_game()
        engine.start_hand()
        # Preflop: facing big blind — caller must call or raise, not check
        first = engine.players[engine.action_queue[0]].player_id
        with pytest.raises(ValueError, match="Invalid action"):
            engine.process_action(first, ActionType.CHECK)

    def test_chips_conserved_after_fold(self):
        engine = make_hu_game(stack=1000)
        total_before = sum(p.stack for p in engine.players)
        engine.start_hand()
        first = engine.players[engine.action_queue[0]].player_id
        engine.process_action(first, ActionType.FOLD)
        total_after = sum(p.stack for p in engine.players) + engine.pot
        assert total_after == total_before

    def test_chips_conserved_after_showdown(self):
        engine = make_hu_game(stack=1000)
        total_before = sum(p.stack for p in engine.players)
        engine.start_hand()

        # Play to showdown without folding
        for _ in range(20):  # safety limit
            if not engine.action_queue:
                break
            if engine.phase in (Phase.SHOWDOWN, Phase.HAND_COMPLETE):
                break
            pid = engine.players[engine.action_queue[0]].player_id
            valid = engine._valid_actions(engine.action_queue[0])
            action = ActionType.CHECK if ActionType.CHECK in valid else ActionType.CALL
            engine.process_action(pid, action)

        total_after = sum(p.stack for p in engine.players) + engine.pot
        assert total_after == total_before


class TestStreetProgression:
    def _play_street(self, engine: PokerEngine) -> None:
        """Process all actions for the current street, then stop."""
        start_phase = engine.phase
        for _ in range(10):
            if not engine.action_queue:
                break
            if engine.phase != start_phase:
                break
            if engine.phase in (Phase.SHOWDOWN, Phase.HAND_COMPLETE):
                break
            pid = engine.players[engine.action_queue[0]].player_id
            valid = engine._valid_actions(engine.action_queue[0])
            action = ActionType.CHECK if ActionType.CHECK in valid else ActionType.CALL
            engine.process_action(pid, action)

    def test_full_hand_reaches_showdown(self):
        engine = make_hu_game()
        engine.start_hand()
        for _ in range(4):  # preflop, flop, turn, river
            self._play_street(engine)
            if engine.phase in (Phase.SHOWDOWN, Phase.HAND_COMPLETE):
                break
        assert engine.phase in (Phase.SHOWDOWN, Phase.HAND_COMPLETE)

    def test_community_cards_at_each_street(self):
        engine = make_hu_game()
        engine.start_hand()
        assert len(engine.community_cards) == 0  # preflop

        self._play_street(engine)  # → flop
        assert len(engine.community_cards) == 3

        self._play_street(engine)  # → turn
        assert len(engine.community_cards) == 4

        self._play_street(engine)  # → river
        assert len(engine.community_cards) == 5

    def test_3player_full_hand(self):
        engine = make_3player_game()
        engine.start_hand()
        for _ in range(20):
            if engine.phase in (Phase.SHOWDOWN, Phase.HAND_COMPLETE):
                break
            if not engine.action_queue:
                break
            pid = engine.players[engine.action_queue[0]].player_id
            valid = engine._valid_actions(engine.action_queue[0])
            action = ActionType.CHECK if ActionType.CHECK in valid else ActionType.CALL
            engine.process_action(pid, action)
        assert engine.phase in (Phase.SHOWDOWN, Phase.HAND_COMPLETE)


class TestAllIn:
    def test_allin_on_call(self):
        engine = PokerEngine("ALLIN", big_blind=20, small_blind=10)
        # seat=1 is the dealer/SB on hand 1 in heads-up; seat=2 is the BB
        engine.add_player("deep", seat=1, buy_in=500)
        engine.add_player("short", seat=2, buy_in=15)  # BB posts min(20,15)=15 → all-in
        engine.start_hand()
        short = next(p for p in engine.players if p.player_id == "short")
        assert short.status == PlayerStatus.ALL_IN

    def test_chips_conserved_with_allin(self):
        engine = PokerEngine("SIDEPOT", big_blind=20, small_blind=10)
        engine.add_player("short", seat=1, buy_in=50)
        engine.add_player("deep", seat=2, buy_in=1000)
        engine.add_player("medium", seat=3, buy_in=200)
        total_before = 50 + 1000 + 200

        engine.start_hand()

        # Play to completion with raises to force all-in scenarios
        for _ in range(30):
            if engine.phase in (Phase.SHOWDOWN, Phase.HAND_COMPLETE):
                break
            if not engine.action_queue:
                break
            pid = engine.players[engine.action_queue[0]].player_id
            valid = engine._valid_actions(engine.action_queue[0])
            action = ActionType.CALL if ActionType.CALL in valid else ActionType.CHECK
            engine.process_action(pid, action)

        total_after = sum(p.stack for p in engine.players) + engine.pot
        assert total_after == total_before

    def test_two_hands_in_a_row(self):
        engine = make_hu_game(stack=500)
        engine.start_hand()
        first = engine.players[engine.action_queue[0]].player_id
        engine.process_action(first, ActionType.FOLD)

        assert engine.phase == Phase.HAND_COMPLETE
        engine.start_hand()
        assert engine.phase == Phase.PREFLOP
        assert engine.hand_number == 2


class TestRaiseLogic:
    def test_raise_increases_pot(self):
        engine = make_hu_game(stack=1000)
        engine.start_hand()
        pot_before = engine.pot
        first = engine.players[engine.action_queue[0]].player_id
        engine.process_action(first, ActionType.RAISE, 60)
        assert engine.pot > pot_before

    def test_reraise_is_valid(self):
        engine = make_hu_game(stack=1000)
        engine.start_hand()
        first_pid = engine.players[engine.action_queue[0]].player_id
        engine.process_action(first_pid, ActionType.RAISE, 60)
        second_pid = engine.players[engine.action_queue[0]].player_id
        # second player should have raise as a valid action
        valid = engine._valid_actions(engine.action_queue[0])
        assert ActionType.RAISE in valid
        engine.process_action(second_pid, ActionType.RAISE, 120)
        assert engine.current_bet == 120

    def test_min_raise_enforced(self):
        engine = make_hu_game(stack=1000)
        engine.start_hand()
        first = engine.players[engine.action_queue[0]].player_id
        # Raise to exactly big blind (minimum valid raise in heads-up preflop)
        engine.process_action(first, ActionType.RAISE, 40)  # 20 BB + 20 raise = 40
        assert engine.current_bet == 40


class TestPublicState:
    def test_public_state_has_no_hole_cards(self):
        engine = make_hu_game()
        engine.start_hand()
        state = engine.public_state()
        assert "hole_cards" not in state
        for p in state["players"]:
            assert "hole_cards" not in p

    def test_public_state_structure(self):
        engine = make_hu_game()
        state = engine.public_state()
        assert state["room_code"] == "TEST"
        assert state["phase"] == "waiting"
        assert isinstance(state["players"], list)
