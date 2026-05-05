using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Poker.Models;
using Poker.Network;

namespace Poker.UI
{
    // Left-side scrolling event log: bets, folds, raises, winners.
    // Builds its own UI hierarchy on Awake so it works without scene setup.
    public class EventLogUI : MonoBehaviour
    {
        public static EventLogUI Instance { get; private set; }

        [Header("Layout")]
        public float panelWidth = 380f;
        // Inset from the canvas edges; matches PokerTableUI's pane padding
        // so the log panel and the table panel are perfectly aligned.
        public float edgePadding = 15f;
        public float entryFadeIn = 0.18f;
        public int   maxEntries = 80;

        // Color palette (aesthetic) — saturated, modern, all on a deep
        // navy/purple gradient for readability against the felt.
        public Color titleColor   = new Color(1f,    0.86f, 0.30f);
        public Color foldColor    = new Color(1f,    0.46f, 0.42f);
        public Color callColor    = new Color(0.55f, 0.85f, 1f);
        public Color raiseColor   = new Color(0.45f, 0.95f, 0.65f);
        public Color checkColor   = new Color(0.82f, 0.86f, 0.95f);
        public Color systemColor  = new Color(0.70f, 0.72f, 0.82f);
        public Color winnerColor  = new Color(1f,    0.86f, 0.22f);
        public Color winnerSubCol = new Color(1f,    1f,    1f);

        private RectTransform _content;
        private ScrollRect    _scroll;

        // Track the latest game state so we can resolve player_id → name.
        private GameState _state;

        // Snapshot of the previous state, used to detect actions by diff.
        // The backend only emits room_state with full player snapshots; it
        // does NOT emit a discrete "player_action" message. So we infer the
        // action from what changed between two consecutive states.
        struct PlayerSnap { public string status; public int currentBet; }
        private readonly Dictionary<string, PlayerSnap> _prevPlayers = new();
        private int    _prevTableBet;
        private string _prevCurrentPlayerId;
        private string _prevPhase;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            BuildUI();
            HookEvents();
            AddTitle();
        }

        bool _hooksInstalled;
        void HookEvents()
        {
            if (_hooksInstalled) return;
            var gsm = GameStateManager.Instance;
            // Wait until GameStateManager is alive — if we Start before it,
            // the singleton can be null for the first frame.
            if (gsm == null)
            {
                StartCoroutine(RetryHookNextFrame());
                return;
            }
            gsm.OnGameStateUpdated += OnStateForLog;
            gsm.OnHandStarted      += () => { _prevPlayers.Clear(); _prevTableBet = 0; _prevCurrentPlayerId = null; AppendSeparator(); };
            // Showdown winner is already logged by PokerTableUI.OnShowdown
            // calling EventLogUI.Instance.LogWinner — don't double-log here.
            _hooksInstalled = true;
            Debug.Log("[Poker] EventLogUI hooks installed");
        }

        System.Collections.IEnumerator RetryHookNextFrame()
        {
            yield return null;
            HookEvents();
        }

        // Diff successive states to figure out which action just occurred.
        // The "current_player_id" advances every time the previous player
        // acted, so we attribute the action to whoever was current_player
        // BEFORE this update. Folds, calls, raises, and checks are inferred
        // from changes to that player's status / current_bet.
        void OnStateForLog(GameState state)
        {
            if (state == null) return;
            _state = state;
            if (state.players == null) { _prevPhase = state.phase; return; }

            // Hand-complete via fold-out (only one non-folded player)
            string newPhase = state.phase ?? "";
            if ((newPhase == "HAND_COMPLETE" || newPhase == "SHOWDOWN")
                && _prevPhase != newPhase
                && newPhase == "HAND_COMPLETE")
            {
                int active = 0;
                Player solo = null;
                foreach (var p in state.players)
                    if (!string.Equals(p.status, "FOLDED", System.StringComparison.OrdinalIgnoreCase))
                    { active++; solo = p; }
                if (active == 1 && solo != null)
                {
                    Append($"<b>{solo.name}</b> wins <color=#FFE066>{FormatChips(state.pot)}</color> — everyone folded",
                        winnerColor, 18, FontStyles.Bold);
                }
            }

            // Diff per-player to detect action
            // The acting player is the one whose snapshot just changed. We
            // log every change rather than relying on prevCurrentPlayerId
            // because the server can update the snapshot in batched bursts.
            foreach (var p in state.players)
            {
                if (string.IsNullOrEmpty(p.id)) continue;
                if (!_prevPlayers.TryGetValue(p.id, out var prev))
                {
                    // First time we see this player — just record and skip.
                    _prevPlayers[p.id] = new PlayerSnap { status = p.status, currentBet = p.current_bet };
                    continue;
                }

                bool wasFolded = string.Equals(prev.status, "FOLDED", System.StringComparison.OrdinalIgnoreCase);
                bool isFolded  = string.Equals(p.status,    "FOLDED", System.StringComparison.OrdinalIgnoreCase);

                if (!wasFolded && isFolded)
                {
                    Append($"<color=#FF7A7A>\u2716</color>  <b>{p.name}</b> folds", foldColor, 16);
                }
                else if (p.current_bet > prev.currentBet)
                {
                    int delta = p.current_bet - prev.currentBet;
                    if (p.current_bet > _prevTableBet && _prevTableBet > 0)
                    {
                        Append($"<color=#9CF5A5>\u25B2</color>  <b>{p.name}</b> raises to <color=#9CF5A5>{FormatChips(p.current_bet)}</color>",
                            raiseColor, 16);
                    }
                    else
                    {
                        Append($"<color=#A8D6FF>\u25CF</color>  <b>{p.name}</b> calls <color=#A8D6FF>{FormatChips(delta)}</color>",
                            callColor, 16);
                    }
                }
                else if (_prevCurrentPlayerId == p.id
                         && state.current_player_id != p.id
                         && p.current_bet == prev.currentBet
                         && !isFolded)
                {
                    // The turn passed off this player without them folding or
                    // adding chips → they checked.
                    Append($"<color=#C8D0E8>\u2713</color>  <b>{p.name}</b> checks", checkColor, 16);
                }

                _prevPlayers[p.id] = new PlayerSnap { status = p.status, currentBet = p.current_bet };
            }

            _prevTableBet = state.current_bet;
            _prevCurrentPlayerId = state.current_player_id;
            _prevPhase = newPhase;
        }

        // ── UI construction ───────────────────────────────────────────────────

        void BuildUI()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[Poker] EventLogUI.BuildUI: no Canvas found");
                return;
            }

            Transform parentT = transform;
            // If our own GO has no RectTransform yet, parent under the canvas instead.
            if (GetComponent<RectTransform>() == null) parentT = canvas.transform;

            // Root panel — its own pane on the LEFT side, fully separated
            // from the table pane by 15px padding on every edge.
            var rootGO = new GameObject("EventLogPanel",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rootGO.transform.SetParent(parentT, false);
            rootGO.transform.SetAsLastSibling(); // render on top
            var rootRT = rootGO.GetComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0, 0);
            rootRT.anchorMax = new Vector2(0, 1);
            rootRT.pivot     = new Vector2(0, 0.5f);
            // Left-padded by edgePadding; full vertical with edgePadding
            // top/bottom — same insets as the table container so the two
            // panes line up perfectly.
            rootRT.offsetMin = new Vector2(edgePadding, edgePadding);
            rootRT.offsetMax = new Vector2(panelWidth + edgePadding, -edgePadding);

            var bg = rootGO.GetComponent<Image>();
            bg.sprite = SpriteFactory.Pill();
            bg.type = Image.Type.Sliced;
            // Deep indigo surface (slightly lighter than the felt so the
            // pane reads as a separate sheet of paper laid on top).
            bg.color = new Color(0.07f, 0.05f, 0.16f, 0.96f);
            Debug.Log($"[Poker] EventLog panel built: parent={parentT.name} size={rootRT.rect.size}");

            // Subtle gold-purple rim around the entire pane.
            var rimGO = new GameObject("Rim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rimGO.transform.SetParent(rootGO.transform, false);
            var rimRT = rimGO.GetComponent<RectTransform>();
            rimRT.anchorMin = Vector2.zero; rimRT.anchorMax = Vector2.one;
            rimRT.offsetMin = rimRT.offsetMax = Vector2.zero;
            var rimImg = rimGO.GetComponent<Image>();
            rimImg.sprite = SpriteFactory.PillRing();
            rimImg.type = Image.Type.Sliced;
            rimImg.color = new Color(0.55f, 0.38f, 0.85f, 0.45f);
            rimImg.raycastTarget = false;

            // ── Header bar (taller, with gold underline accent) ────────
            var hdrGO = new GameObject("Header",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hdrGO.transform.SetParent(rootGO.transform, false);
            var hdrRT = hdrGO.GetComponent<RectTransform>();
            hdrRT.anchorMin = new Vector2(0, 1);
            hdrRT.anchorMax = new Vector2(1, 1);
            hdrRT.pivot     = new Vector2(0.5f, 1);
            hdrRT.anchoredPosition = Vector2.zero;
            hdrRT.sizeDelta = new Vector2(0, 56);
            // Slightly lighter strip + a gold hairline at the bottom edge.
            hdrGO.GetComponent<Image>().color = new Color(0.11f, 0.08f, 0.22f, 1f);

            // Title (gold, slightly larger, wider letter spacing)
            var hdrTxtGO = new GameObject("Label", typeof(RectTransform));
            hdrTxtGO.transform.SetParent(hdrGO.transform, false);
            var hdrTxtRT = hdrTxtGO.GetComponent<RectTransform>();
            hdrTxtRT.anchorMin = Vector2.zero; hdrTxtRT.anchorMax = Vector2.one;
            hdrTxtRT.offsetMin = new Vector2(20, 0); hdrTxtRT.offsetMax = new Vector2(-20, 0);
            var hdrTxt = hdrTxtGO.AddComponent<TextMeshProUGUI>();
            hdrTxt.text = "TABLE LOG";
            hdrTxt.fontSize = 22;
            hdrTxt.fontStyle = FontStyles.Bold;
            hdrTxt.color = titleColor;
            hdrTxt.alignment = TextAlignmentOptions.Left;
            hdrTxt.characterSpacing = 18f; // airy, premium feel

            // Spade-suit decoration on the right side of the header.
            var deco = new GameObject("Spade", typeof(RectTransform));
            deco.transform.SetParent(hdrGO.transform, false);
            // Bug: AddComponent<RectTransform> returns null because the GO
            // already has one (RectTransform is single-instance) — use Get
            // to read the existing one. The previous code crashed Start
            // with a NullReferenceException, killing all of EventLogUI's
            // setup AFTER this line (so Append was called before content
            // was built — see warning logs).
            var dRT = deco.GetComponent<RectTransform>();
            dRT.anchorMin = new Vector2(1, 0.5f); dRT.anchorMax = new Vector2(1, 0.5f);
            dRT.pivot = new Vector2(1, 0.5f);
            dRT.anchoredPosition = new Vector2(-20, 0);
            dRT.sizeDelta = new Vector2(40, 40);
            var dTmp = deco.AddComponent<TextMeshProUGUI>();
            dTmp.text = "\u2660"; // ♠
            dTmp.fontSize = 24;
            dTmp.color = new Color(1f, 0.86f, 0.30f, 0.55f);
            dTmp.alignment = TextAlignmentOptions.Center;
            dTmp.raycastTarget = false;

            // Gold accent line just below the header.
            var accGO = new GameObject("Accent",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            accGO.transform.SetParent(rootGO.transform, false);
            var accRT = accGO.GetComponent<RectTransform>();
            accRT.anchorMin = new Vector2(0, 1);
            accRT.anchorMax = new Vector2(1, 1);
            accRT.pivot = new Vector2(0.5f, 1);
            accRT.anchoredPosition = new Vector2(0, -56);
            accRT.sizeDelta = new Vector2(0, 2);
            accGO.GetComponent<Image>().color = new Color(1f, 0.78f, 0.28f, 0.85f);

            // Scroll viewport
            var viewGO = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewGO.transform.SetParent(rootGO.transform, false);
            var viewRT = viewGO.GetComponent<RectTransform>();
            viewRT.anchorMin = new Vector2(0, 0);
            viewRT.anchorMax = new Vector2(1, 1);
            viewRT.pivot     = new Vector2(0.5f, 1f);
            viewRT.offsetMin = new Vector2(0, 0);
            viewRT.offsetMax = new Vector2(0, -58); // 56 header + 2 accent
            var viewImg = viewGO.GetComponent<Image>();
            viewImg.color = new Color(1, 1, 1, 0.01f);
            var mask = viewGO.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            // Content
            var contentGO = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGO.transform.SetParent(viewGO.transform, false);
            _content = contentGO.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot     = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(0, 0);
            var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 6;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = true;
            vlg.childControlHeight     = true;  // honor each entry's preferred height
            vlg.childControlWidth      = true;
            vlg.childAlignment = TextAnchor.UpperLeft;
            var fitter = contentGO.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ScrollRect
            _scroll = rootGO.AddComponent<ScrollRect>();
            _scroll.viewport = viewRT;
            _scroll.content  = _content;
            _scroll.horizontal = false;
            _scroll.vertical   = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 24f;
        }

        void AddTitle()
        {
            Append("Welcome to the felt — good luck!", systemColor, 16, FontStyles.Italic);
        }

        // ── Public log API ────────────────────────────────────────────────────

        public void AppendSeparator()
        {
            if (_content == null) return;
            // Hairline gold separator with subtle gradient feel.
            var line = new GameObject("Sep",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(LayoutElement));
            line.transform.SetParent(_content, false);
            var le = line.GetComponent<LayoutElement>();
            le.preferredHeight = 1;
            le.flexibleWidth = 1;
            line.GetComponent<Image>().color = new Color(1f, 0.82f, 0.36f, 0.35f);
            Append("\u2756  NEW HAND  \u2756", new Color(1f, 0.85f, 0.40f, 0.95f),
                15, FontStyles.Bold);
            EnforceMaxEntries();
        }

        public TextMeshProUGUI Append(string text, Color color, int fontSize = 16,
            FontStyles style = FontStyles.Normal)
        {
            if (_content == null)
            {
                Debug.LogWarning($"[Poker] EventLogUI.Append called before content built — dropping: {text}");
                return null;
            }
            var go = new GameObject("Entry",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
            go.transform.SetParent(_content, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            // ALWAYS fully opaque — we removed the FadeIn coroutine entirely
            // because it was leaving entries invisible at alpha 0 when the
            // host briefly went inactive (e.g. during scene boot).
            var opaque = color; opaque.a = 1f;
            tmp.color = opaque;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.enableWordWrapping = true;
            tmp.richText = true;
            tmp.lineSpacing = -4f;
            tmp.characterSpacing = 1.2f;
            tmp.outlineColor = new Color(0, 0, 0, 0.55f);
            tmp.outlineWidth = 0.14f;
            var le = go.GetComponent<LayoutElement>();
            le.flexibleWidth = 1;
            le.preferredHeight = -1;
            le.minHeight = fontSize + 6; // guarantee the row reserves space

            EnforceMaxEntries();
            ScrollToBottom();
            Debug.Log($"[Poker] EventLog entry: {text}");
            return tmp;
        }

        public void LogWinner(ShowdownResult r, GameState state)
        {
            if (r == null) return;
            // Build winner names
            var names = new List<string>();
            if (state?.players != null && r.winners != null)
                foreach (var p in state.players)
                    if (r.winners.Contains(p.id)) names.Add(p.name);

            string nameStr = names.Count > 0 ? string.Join(" & ", names) : "Player";
            string handStr = string.IsNullOrEmpty(r.hand_name) ? "" : r.hand_name;
            string amount  = FormatChips(r.pot);

            // Big colored celebration title
            string celebrate = names.Count > 1
                ? $"<b>SPLIT POT</b>  ★  {nameStr}"
                : $"<b>{nameStr}</b>  WINS  <color=#FFE066>{amount}</color> !";
            Append(celebrate, winnerColor, 22, FontStyles.Bold);

            if (!string.IsNullOrEmpty(handStr))
                Append($"with a <b><color=#FFCD3C>{handStr}</color></b>", winnerSubCol, 16, FontStyles.Italic);

            // Winning 5 cards — sorted by rank ascending so a straight
            // reads naturally (3 4 5 6 7), pairs cluster together, etc.
            if (r.best_cards != null && r.best_cards.Count > 0)
            {
                var sorted = new List<string>(r.best_cards);
                sorted.Sort((a, b) => CardRankValue(a).CompareTo(CardRankValue(b)));
                string cards = "";
                foreach (var code in sorted)
                    cards += FormatCardChip(code) + " ";
                Append(cards.TrimEnd(), winnerSubCol, 22, FontStyles.Bold);
            }
        }

        // Numeric rank for sorting card codes like "Ah", "10d", "Tc", "Ks".
        static int CardRankValue(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 2) return 0;
            string rank = code.Substring(0, code.Length - 1).ToUpper();
            return rank switch
            {
                "2" => 2, "3" => 3, "4" => 4, "5" => 5, "6" => 6,
                "7" => 7, "8" => 8, "9" => 9,
                "10" => 10, "T" => 10,
                "J" => 11, "Q" => 12, "K" => 13, "A" => 14,
                _   => 0
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        string ResolveName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "Player";
            if (_state?.players != null)
                foreach (var p in _state.players)
                    if (p.id == playerId) return p.name;
            return playerId.Length > 6 ? playerId.Substring(0, 6) : playerId;
        }

        static string FormatChips(int v)
        {
            if (v >= 1_000_000) return $"${v / 1_000_000.0:F1}M";
            if (v >= 1_000)     return $"${v / 1_000.0:F1}K";
            return $"${v}";
        }

        static string FormatCardChip(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 2) return code ?? "";
            string suitCh = code.Substring(code.Length - 1).ToLower();
            string rank = code.Substring(0, code.Length - 1);
            if (rank == "T") rank = "10";
            string suitGlyph = suitCh switch { "h" => "♥", "d" => "♦", "c" => "♣", "s" => "♠", _ => suitCh };
            string suitColor = (suitCh == "h" || suitCh == "d") ? "#FF5C5C" : "#FFFFFF";
            return $"<color=#FFFFFF><b>{rank}</b></color><color={suitColor}>{suitGlyph}</color>";
        }

        void EnforceMaxEntries()
        {
            if (_content == null) return;
            // CRITICAL: Destroy() is deferred to end-of-frame in Unity, so
            // the destroyed child STILL counts in childCount until the next
            // frame. The previous `while (childCount > maxEntries) Destroy(...)`
            // loop never made progress and locked the main thread once the
            // log exceeded maxEntries — that's the "hangs mid-game" bug.
            // Fix: detach (SetParent(null)) FIRST so childCount drops on
            // the next iteration, THEN schedule Destroy. As a belt-and-
            // braces guard, also cap iterations.
            int safety = 4096;
            while (_content.childCount > maxEntries && safety-- > 0)
            {
                var first = _content.GetChild(0);
                if (first == null) break;
                first.SetParent(null, false);
                Destroy(first.gameObject);
            }
        }

        void ScrollToBottom()
        {
            // Defer one frame so layout settles before scroll snaps
            StartCoroutine(SnapNextFrame());
        }

        System.Collections.IEnumerator SnapNextFrame()
        {
            yield return null;
            if (_scroll != null) _scroll.verticalNormalizedPosition = 0f;
        }

    }
}
