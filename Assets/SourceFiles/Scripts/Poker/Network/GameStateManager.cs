using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Poker.Models;

namespace Poker.Network
{
    public class GameStateManager : MonoBehaviour
    {
        public static GameStateManager Instance { get; private set; }

        public GameState CurrentState { get; private set; }
        public string MyPlayerId { get; private set; }
        public string RoomCode { get; private set; }

        // Hole cards arrive privately via the `cards_dealt` event but the
        // public `room_state` broadcast strips them, so cache them here and
        // merge into the hero's player object on each state update.
        private List<Card> _myHoleCards;

        public event Action<GameState> OnGameStateUpdated;
        public event Action<string> OnError;
        public event Action OnHandStarted;
        public event Action<ShowdownResult> OnShowdown;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            var sio = SocketIOClient.Instance;
            // Backend assigns a player_id on the `connected` event right after
            // the WS handshake. SocketIOClient already stashes it as Sid; we
            // mirror it here so callers can pull it off GameStateManager.
            sio.On("connected", json =>
            {
                var c = JsonUtility.FromJson<ConnectedData>(json);
                if (!string.IsNullOrEmpty(c?.player_id)) MyPlayerId = c.player_id;
            });
            // The plain-WS backend uses `room_state` for both initial state and
            // ongoing updates; there is no separate `game_state` channel.
            sio.On("room_state",     OnGameStateEvent);
            sio.On("room_created",   OnRoomCreated);
            sio.On("hand_started",   _ => { _myHoleCards = null; OnHandStarted?.Invoke(); });
            sio.On("showdown",       OnShowdownEvent);
            sio.On("error",          OnErrorEvent);
            sio.On("cards_dealt",    OnCardsDealt);
            // `player_joined` / `player_left` are bookkeeping events; the next
            // `room_state` broadcast will carry the visible state change.
            sio.On("player_joined",  _ => { });
            sio.On("player_left",    _ => { });
        }

        // ── Room creation (now over WS, not REST) ────────────────────────────

        public void CreateRoom(string playerName, int smallBlind = 10, int bigBlind = 20)
        {
            string payload = $"{{\"small_blind\":{smallBlind},\"big_blind\":{bigBlind}}}";
            SocketIOClient.Instance.Emit("create_room", payload);
        }

        void OnRoomCreated(string json)
        {
            var resp = JsonUtility.FromJson<RoomResponse>(json);
            if (!string.IsNullOrEmpty(resp?.code)) RoomCode = resp.code;
        }

        public void JoinRoom(string playerName, string code)
        {
            RoomCode = code;
            string payload = $"{{\"code\":\"{code}\"}}";
            SocketIOClient.Instance.Emit("join_room", payload);
        }

        public void SitDown(int seat, int buyIn = 1000)
        {
            string payload = $"{{\"room_code\":\"{RoomCode}\",\"seat\":{seat},\"buy_in\":{buyIn}}}";
            SocketIOClient.Instance.Emit("sit_down", payload);
        }

        public void StartGame()
        {
            SocketIOClient.Instance.Emit("start_game", $"{{\"room_code\":\"{RoomCode}\"}}");
        }

        public void PlayWithBots(string playerName, int botCount, int buyIn = 1000)
        {
            string safeName = (playerName ?? "Player").Replace("\"", "");
            string payload = $"{{\"name\":\"{safeName}\",\"bot_count\":{botCount},\"buy_in\":{buyIn}}}";
            SocketIOClient.Instance.Emit("play_with_bots", payload);
        }

        public void SendAction(ActionType action, int amount = 0)
        {
            string act = action.ToString().ToLower();
            string payload = $"{{\"room_code\":\"{RoomCode}\",\"action\":\"{act}\",\"amount\":{amount}}}";
            SocketIOClient.Instance.Emit("player_action", payload);
        }

        public void RequestNextHand()
        {
            SocketIOClient.Instance.Emit("request_next_hand", $"{{\"room_code\":\"{RoomCode}\"}}");
        }

        public void LeaveRoom()
        {
            if (string.IsNullOrEmpty(RoomCode)) return;
            SocketIOClient.Instance.Emit("leave_room", $"{{\"room_code\":\"{RoomCode}\"}}");
            RoomCode = null;
            CurrentState = null;
        }

        // ── Socket handlers ──────────────────────────────────────────────────

        void OnCardsDealt(string json)
        {
            var p = JsonUtility.FromJson<HoleCardsPayload>(json);
            _myHoleCards = p?.hole_cards;
            // The next room_state broadcast will trigger OnGameStateEvent and
            // the cached cards get merged in there.
        }

        void OnGameStateEvent(string json)
        {
            CurrentState = JsonUtility.FromJson<GameState>(json);
            if (CurrentState != null)
            {
                CurrentState.my_player_id = MyPlayerId;
                // Merge hero's private hole cards into the public state so the
                // seat UI can show the actual cards.
                if (_myHoleCards != null && CurrentState.players != null)
                {
                    foreach (var pl in CurrentState.players)
                    {
                        if (pl != null && pl.id == MyPlayerId)
                        {
                            pl.hole_cards = _myHoleCards;
                            break;
                        }
                    }
                }
            }
            OnGameStateUpdated?.Invoke(CurrentState);
        }

        void OnShowdownEvent(string json)
        {
            var result = JsonUtility.FromJson<ShowdownResult>(json);
            OnShowdown?.Invoke(result);
        }

        void OnErrorEvent(string json)
        {
            var err = JsonUtility.FromJson<ErrorData>(json);
            OnError?.Invoke(err.message);
        }

        void RequestStateRefresh()
        {
            StartCoroutine(GetRoomState());
        }

        IEnumerator GetRoomState()
        {
            string url = $"{SocketIOClient.Instance.serverUrl}/api/rooms/{RoomCode}";
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
                OnGameStateEvent(req.downloadHandler.text);
        }

        [Serializable] class RoomResponse { public string code; }
        [Serializable] class ConnectedData { public string player_id; }
        [Serializable] class ErrorData { public string message; }
        [Serializable] class HoleCardsPayload { public List<Card> hole_cards; }
    }
}
