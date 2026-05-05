from __future__ import annotations

import secrets
from dataclasses import dataclass
from typing import List

RANKS = "23456789TJQKA"
SUITS = "cdhs"  # clubs, diamonds, hearts, spades
RANK_VALUES: dict[str, int] = {r: i + 2 for i, r in enumerate(RANKS)}


@dataclass(frozen=True)
class Card:
    rank: str
    suit: str

    def __str__(self) -> str:
        return f"{self.rank}{self.suit}"

    def __repr__(self) -> str:
        return str(self)

    @classmethod
    def from_str(cls, s: str) -> Card:
        if len(s) != 2:
            raise ValueError(f"Invalid card string: {s!r}")
        rank = s[0].upper()
        suit = s[1].lower()
        if rank not in RANK_VALUES:
            raise ValueError(f"Invalid rank: {rank!r}")
        if suit not in SUITS:
            raise ValueError(f"Invalid suit: {suit!r}")
        return cls(rank=rank, suit=suit)

    @property
    def rank_value(self) -> int:
        return RANK_VALUES[self.rank]


def make_deck() -> List[Card]:
    return [Card(rank=r, suit=s) for r in RANKS for s in SUITS]


def shuffle_deck(deck: List[Card]) -> List[Card]:
    """Fisher-Yates shuffle via secrets.SystemRandom — cryptographically secure."""
    d = list(deck)
    secrets.SystemRandom().shuffle(d)
    return d
