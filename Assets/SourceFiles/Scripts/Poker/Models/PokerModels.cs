using System;
using System.Collections.Generic;

namespace Poker.Models
{
    [Serializable]
    public enum GamePhase { WAITING, PREFLOP, FLOP, TURN, RIVER, SHOWDOWN, HAND_COMPLETE }

    [Serializable]
    public enum PlayerStatus { ACTIVE, FOLDED, ALL_IN, SITTING_OUT }

    [Serializable]
    public enum ActionType { FOLD, CHECK, CALL, RAISE }

    [Serializable]
    public class Card
    {
        public string rank;
        public string suit;
        public string code; // e.g. "Ah", "Kd"
    }

    [Serializable]
    public class Player
    {
        public string id;
        public string name;
        public int seat;
        public int stack;
        public int current_bet;
        public string status; // backend sends string enum name
        public bool is_dealer;
        public bool is_small_blind;
        public bool is_big_blind;
        public bool is_bot;
        public List<Card> hole_cards;
        public int total_bet_this_round;

        public PlayerStatus GetStatus()
        {
            if (string.IsNullOrEmpty(status)) return PlayerStatus.ACTIVE;
            return Enum.TryParse(status, true, out PlayerStatus s) ? s : PlayerStatus.ACTIVE;
        }
    }

    [Serializable]
    public class SidePot
    {
        public int amount;
        public List<string> eligible_players;
    }

    [Serializable]
    public class GameState
    {
        public string room_code;
        public string phase; // backend sends string enum name
        public List<Card> community_cards;
        public int pot;
        public List<SidePot> side_pots;
        public List<Player> players;
        public string current_player_id;
        public int current_bet;
        public int min_raise;
        public string dealer_id;
        public string small_blind_id;
        public string big_blind_id;
        public int small_blind;
        public int big_blind;
        public string my_player_id;
        public List<string> action_to_take;

        public GamePhase GetPhase()
        {
            if (string.IsNullOrEmpty(phase)) return GamePhase.WAITING;
            return Enum.TryParse(phase, true, out GamePhase p) ? p : GamePhase.WAITING;
        }
    }

    [Serializable]
    public class ActionRequest
    {
        public ActionType action;
        public int amount;
    }

    [Serializable]
    public class ShowdownReveal
    {
        public string player_id;
        public List<string> cards;       // e.g. ["Ah", "Kd"]
        public List<string> best_cards;  // 5-card best hand
        public string hand_name;
    }

    [Serializable]
    public class ShowdownPotResult
    {
        public List<string> winners;
        public int pot;
        public string hand;
        public List<string> best_cards;
    }

    [Serializable]
    public class ShowdownResult
    {
        public List<ShowdownReveal> reveals;
        public List<string> community_cards;  // ["Ah", "Td", ...]
        public List<ShowdownPotResult> results;
        // Top-level summary (main pot)
        public List<string> winners;
        public int pot;
        public string hand_name;
        public List<string> best_cards;
    }
}
