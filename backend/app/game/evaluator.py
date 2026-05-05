from __future__ import annotations

from collections import Counter
from itertools import combinations
from typing import List, Tuple

from .card import Card

# Hand rank constants — higher value beats lower.
HIGH_CARD = 0
PAIR = 1
TWO_PAIR = 2
THREE_OF_A_KIND = 3
STRAIGHT = 4
FLUSH = 5
FULL_HOUSE = 6
FOUR_OF_A_KIND = 7
STRAIGHT_FLUSH = 8

HAND_NAMES = {
    STRAIGHT_FLUSH: "Straight Flush",
    FOUR_OF_A_KIND: "Four of a Kind",
    FULL_HOUSE: "Full House",
    FLUSH: "Flush",
    STRAIGHT: "Straight",
    THREE_OF_A_KIND: "Three of a Kind",
    TWO_PAIR: "Two Pair",
    PAIR: "Pair",
    HIGH_CARD: "High Card",
}


def _eval5(cards: List[Card]) -> Tuple[int, ...]:
    """Score a 5-card hand as a comparable tuple. Higher = better hand."""
    rv = sorted((c.rank_value for c in cards), reverse=True)
    suits = [c.suit for c in cards]

    is_flush = len(set(suits)) == 1
    is_wheel = set(rv) == {14, 5, 4, 3, 2}  # A-2-3-4-5
    is_straight = is_wheel or (len(set(rv)) == 5 and rv[0] - rv[4] == 4)
    straight_high = 5 if is_wheel else rv[0]

    counts = Counter(rv)
    # Sort groups by (count desc, rank desc) for consistent ordering
    groups = sorted(counts.items(), key=lambda x: (x[1], x[0]), reverse=True)
    g = [rank for rank, _ in groups]
    c = [cnt for _, cnt in groups]

    if is_straight and is_flush:
        return (STRAIGHT_FLUSH, straight_high)
    if c[0] == 4:
        return (FOUR_OF_A_KIND, g[0], g[1])
    if c[0] == 3 and c[1] == 2:
        return (FULL_HOUSE, g[0], g[1])
    if is_flush:
        return (FLUSH, *rv)
    if is_straight:
        return (STRAIGHT, straight_high)
    if c[0] == 3:
        return (THREE_OF_A_KIND, g[0], g[1], g[2])
    if c[0] == 2 and c[1] == 2:
        return (TWO_PAIR, g[0], g[1], g[2])
    if c[0] == 2:
        return (PAIR, g[0], g[1], g[2], g[3])
    return (HIGH_CARD, *rv)


def best_hand_score(hole: List[Card], board: List[Card]) -> Tuple[int, ...]:
    """Best 5-card score from 2 hole cards + up to 5 board cards."""
    all_cards = hole + board
    return max(_eval5(list(combo)) for combo in combinations(all_cards, 5))


def best_hand_cards(hole: List[Card], board: List[Card]) -> List[Card]:
    """Return the actual 5-card combination forming the best hand."""
    all_cards = hole + board
    best_combo = max(combinations(all_cards, 5), key=lambda combo: _eval5(list(combo)))
    return list(best_combo)


def hand_name(score: Tuple[int, ...]) -> str:
    return HAND_NAMES.get(score[0], "Unknown")
