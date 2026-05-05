using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Poker.Network
{
    // Plain WebSocket client speaking the backend's `{event, data}` wire format.
    // The class name is kept as SocketIOClient for source compatibility with
    // earlier code that referenced SocketIOClient.Instance everywhere — the
    // transport underneath is now a vanilla WebSocket against /ws.
    public class SocketIOClient : MonoBehaviour
    {
        public static SocketIOClient Instance { get; private set; }

        [Header("Server")]
        public string serverUrl = "http://localhost:8000";

        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
        // Player ID assigned by the backend on the `connected` event.
        public string Sid => _playerId;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private string _playerId;

        private readonly Dictionary<string, Action<string>> _handlers = new();

        public event Action OnConnected;
        public event Action OnDisconnected;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Capture the player_id automatically when the server hands it out
            // so callers can read SocketIOClient.Instance.Sid right away.
            On("connected", json =>
            {
                var c = JsonUtility.FromJson<ConnectedPayload>(json);
                if (!string.IsNullOrEmpty(c?.player_id)) _playerId = c.player_id;
            });
        }

        void OnDestroy() { _ = CloseAsync(); }

        // ── Public API ──────────────────────────────────────────────────────

        public void Connect()
        {
            if (_ws != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.Connecting))
                return;
            _ = ConnectAsync();
        }

        public void Disconnect() { _ = CloseAsync(); }

        public void On(string eventName, Action<string> handler) => _handlers[eventName] = handler;

        public void Emit(string eventName, string jsonPayload = null)
        {
            if (!IsConnected) { Debug.LogWarning("[WS] Not connected"); return; }
            string data = string.IsNullOrEmpty(jsonPayload) ? "{}" : jsonPayload;
            string msg = $"{{\"event\":\"{eventName}\",\"data\":{data}}}";
            _ = SendAsync(msg);
        }

        // ── Async lifecycle ─────────────────────────────────────────────────

        async Task ConnectAsync()
        {
            string ws = ToWsUrl(serverUrl) + "/ws";
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            try
            {
                await _ws.ConnectAsync(new Uri(ws), _cts.Token);
                MainThread.Enqueue(() => OnConnected?.Invoke());
                _ = ReceiveLoop();
            }
            catch (Exception e)
            {
                Debug.LogError($"[WS] Connect failed: {e.Message}");
                MainThread.Enqueue(() => OnDisconnected?.Invoke());
            }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[16 * 1024];
            var sb = new StringBuilder();
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open)
                {
                    sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", _cts.Token);
                            MainThread.Enqueue(() => OnDisconnected?.Invoke());
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    string raw = sb.ToString();
                    DispatchMessage(raw);
                }
            }
            catch (Exception e)
            {
                if (_ws != null && _ws.State != WebSocketState.Closed)
                    Debug.LogWarning($"[WS] Receive loop ended: {e.Message}");
                MainThread.Enqueue(() => OnDisconnected?.Invoke());
            }
        }

        async Task SendAsync(string text)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WS] Send failed: {e.Message}");
            }
        }

        async Task CloseAsync()
        {
            try
            {
                _cts?.Cancel();
                if (_ws != null && _ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { /* swallow */ }
            _ws?.Dispose();
            _ws = null;
        }

        // ── Dispatch ────────────────────────────────────────────────────────

        void DispatchMessage(string raw)
        {
            // Backend wire format: {"event":"<name>","data":<object>}
            // JsonUtility can't easily peek at sibling fields with arbitrary
            // shapes for `data`, so split by hand: pull the event name first,
            // then forward the raw `data` substring to the registered handler.
            int evIdx = raw.IndexOf("\"event\"");
            int dataIdx = raw.IndexOf("\"data\"");
            if (evIdx < 0 || dataIdx < 0) return;

            int q1 = raw.IndexOf('"', raw.IndexOf(':', evIdx) + 1);
            int q2 = raw.IndexOf('"', q1 + 1);
            if (q1 < 0 || q2 < 0) return;
            string evName = raw.Substring(q1 + 1, q2 - q1 - 1);

            int colon = raw.IndexOf(':', dataIdx);
            int valStart = colon + 1;
            while (valStart < raw.Length && char.IsWhiteSpace(raw[valStart])) valStart++;
            // Find the matching brace/bracket to delimit the data value.
            string payload = ExtractJsonValue(raw, valStart);
            if (_handlers.TryGetValue(evName, out var handler))
                MainThread.Enqueue(() => handler(payload));
        }

        static string ExtractJsonValue(string s, int start)
        {
            if (start >= s.Length) return "{}";
            char first = s[start];
            if (first == '{' || first == '[')
            {
                char open = first;
                char close = first == '{' ? '}' : ']';
                int depth = 0;
                bool inStr = false;
                bool esc = false;
                for (int i = start; i < s.Length; i++)
                {
                    char c = s[i];
                    if (inStr)
                    {
                        if (esc) esc = false;
                        else if (c == '\\') esc = true;
                        else if (c == '"') inStr = false;
                        continue;
                    }
                    if (c == '"') { inStr = true; continue; }
                    if (c == open) depth++;
                    else if (c == close)
                    {
                        depth--;
                        if (depth == 0) return s.Substring(start, i - start + 1);
                    }
                }
            }
            // Primitive (string / number / null)
            int end = start;
            while (end < s.Length && s[end] != ',' && s[end] != '}' && s[end] != ']') end++;
            return s.Substring(start, end - start).Trim();
        }

        static string ToWsUrl(string http)
        {
            if (string.IsNullOrEmpty(http)) return "ws://localhost:8000";
            if (http.StartsWith("https://")) return "wss://" + http.Substring(8);
            if (http.StartsWith("http://"))  return "ws://"  + http.Substring(7);
            return http;
        }

        [Serializable] class ConnectedPayload { public string player_id; }
    }

    // Simple main-thread dispatcher
    public static class MainThread
    {
        static readonly Queue<Action> _queue = new();
        static readonly object _lock = new();

        public static void Enqueue(Action a) { lock (_lock) _queue.Enqueue(a); }

        // BOUNDED drain. Earlier this was `while(true)` which freezes the
        // main thread when the WebSocket receive thread floods the queue
        // (showdown bursts, multi-bot games, reconnect storms). Capping at
        // a fixed number per frame keeps Unity responsive even if the
        // backend is spamming us — the queue catches up over the next few
        // frames instead of locking the editor for seconds.
        const int MAX_ACTIONS_PER_FRAME = 32;
        public static void Flush()
        {
            for (int i = 0; i < MAX_ACTIONS_PER_FRAME; i++)
            {
                Action a;
                lock (_lock) { if (_queue.Count == 0) return; a = _queue.Dequeue(); }
                try { a?.Invoke(); }
                catch (Exception e) { UnityEngine.Debug.LogError($"[MainThread] {e}"); }
            }
        }
    }

    // Add to any MonoBehaviour to flush main-thread queue
    public class MainThreadDispatcher : MonoBehaviour
    {
        void Update() => MainThread.Flush();
    }
}
