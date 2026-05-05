using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Poker.Models;
using Poker.Network;

namespace Poker.UI
{
    public class PokerTableUI : MonoBehaviour
    {
        public static PokerTableUI Instance { get; private set; }

        // ── Community cards ───────────────────────────────────────────────
        [Header("Community Cards")]
        public CardUI[] communityCards = new CardUI[5];
        public RectTransform communityCardsRoot;

        // ── Player seats ──────────────────────────────────────────────────
        [Header("Player Seats (index = visual slot, NOT server seat)")]
        public PlayerSeatUI[] seatSlots = new PlayerSeatUI[7];

        // ── Pot / Phase ───────────────────────────────────────────────────
        [Header("Pot and Phase")]
        public TextMeshProUGUI potText;
        public TextMeshProUGUI phaseText;
        public TextMeshProUGUI roomCodeText;
        public GameObject potChipIcon;

        // ── Betting panel ─────────────────────────────────────────────────
        [Header("Betting Panel")]
        public BettingPanelUI bettingPanel;

        // ── Lobby ─────────────────────────────────────────────────────────
        [Header("Lobby")]
        public GameObject lobbyPanel;
        public TMP_InputField playerNameInput;
        public TMP_InputField roomCodeInput;
        public TMP_InputField buyInInput;
        public Button createRoomButton;
        public Button joinRoomButton;
        public Button playButton;
        public TMP_InputField botCountInput;
        public Button sitDownButton;
        public Button startGameButton;
        public TextMeshProUGUI statusText;

        // ── Overlays ──────────────────────────────────────────────────────
        [Header("Overlays")]
        public GameObject winnerBanner;
        public TextMeshProUGUI winnerText;
        public GameObject handResultPanel;
        public TextMeshProUGUI handResultText;
        public Button nextHandButton;
        public TextMeshProUGUI handNameText; // "Full House", "Flush", etc.

        // ── Deck (for deal animations) ────────────────────────────────────
        [Header("Deck Anchor")]
        public RectTransform deckAnchor;

        // ── Internal state ────────────────────────────────────────────────
        private GameState _state;
        private GamePhase _lastPhase = GamePhase.WAITING;
        private int _lastCommunityCount = 0;
        private bool _pendingSitDown;
        private int _pendingBuyIn = 1000;
        // Maps server seat index -> visual slot index
        // Hero is always visual slot 0 (bottom center)
        private Dictionary<int, int> _seatToSlot = new();
        // Track per-player previous bet to animate chip fly to pot
        private Dictionary<string, int> _prevBet = new();
        // Track which players had hole cards already shown (for deal animation)
        private HashSet<string> _dealtPlayers = new();
        // Stagger index: increments per dealt card so animations queue around the table
        private int _dealIndex = 0;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            var gsm = GameStateManager.Instance;
            gsm.OnGameStateUpdated += OnStateUpdated;
            gsm.OnShowdown         += OnShowdown;
            gsm.OnError            += OnError;
            gsm.OnHandStarted      += OnHandStarted;

            var sio = SocketIOClient.Instance;
            sio.OnConnected    += () => SetStatus("Connected");
            sio.OnDisconnected += () => SetStatus("Disconnected");
            sio.Connect();

            createRoomButton?.onClick.AddListener(OnCreateRoom);
            joinRoomButton?.onClick.AddListener(OnJoinRoom);
            playButton?.onClick.AddListener(OnPlayWithBots);
            sitDownButton?.onClick.AddListener(OnSitDown);
            startGameButton?.onClick.AddListener(() => gsm.StartGame());
            // Hand auto-advances now — hide the legacy NEXT HAND button if it's wired up.
            if (nextHandButton) nextHandButton.gameObject.SetActive(false);

            ShowLobby(true);
            ResetCommunityCards();

            // Hide all seat slots until a game state populates them
            for (int i = 0; i < seatSlots.Length; i++)
                if (seatSlots[i]) seatSlots[i].gameObject.SetActive(false);

            ApplyHudVisuals();
            EnsureResponsiveCanvas();
            EnsureLeaveRoomButton();
            // Build the table container BEFORE the event log so we can
            // size each pane consistently with the same constants.
            EnsureTableContainer();
            EnsureEventLog();
            EnsureWinnerBanner();
            EnsureTableBackdrop();
            // EnsureTableLayout removed from Start — its felt block sets
            // sizeDelta=Vector2.zero AFTER offsetMin/Max, which (with
            // stretched anchors) FORCED the felt to fill the entire pane
            // and ignored every inset value we passed. RelayoutTablePane
            // is now the single source of truth for the table layout.
            // Pulsing gold rim removed by user request (2026-05).
            // RemoveGoldRingPulse() destroys any pre-existing one from
            // earlier sessions so it can't linger after a hot reload.
            RemoveGoldRingPulse();
            LogSpriteAvailability();
            // Final master pass — guarantees seats, betting panel, felt,
            // and decorations live inside TableContainer with correct
            // anchors, no matter what scene/prefab data was baked in.
            RelayoutTablePane();
            // Schedule another pass next frame so anything constructed in
            // sibling components' Awake/Start (e.g. BettingPanelUI's
            // EnforceLayout, which runs in its own Awake) gets reparented
            // immediately after.
            StartCoroutine(RelayoutNextFrame());
        }

        IEnumerator RelayoutNextFrame()
        {
            yield return null; // wait one frame for sibling Awake/Start
            RelayoutTablePane();
        }

        // ── Master layout: idempotent, runs at startup AND on every state
        // update so the table pane can never drift out of TableContainer.
        // This is the SINGLE SOURCE OF TRUTH for "where does each table-
        // related element live and how is it anchored". Called from
        // Start() and OnStateUpdated().
        //
        // RADIAL LAYOUT (right pane, ~1495 × 1050):
        //   ┌────────────────────────────────────┐
        //   │  TOP_BUFFER (30 px)                │
        //   │  ┌──────────────────────────────┐  │
        //   │  │       FELT AREA              │  │  ← 6 ring seats orbit
        //   │  │   (preserveAspect PNG)       │  │    around full ellipse
        //   │  │                              │  │    on a 360/7 schedule
        //   │  └──────────────────────────────┘  │  ← HERO at felt's
        //   │  BUTTONS BAND (~105 px)            │    bottom rim, slot 0
        //   └────────────────────────────────────┘
        //
        // Unity has no built-in flex/grid layout for a circle, so we compute
        // ring positions manually — but every dimension is derived from the
        // container's actual rect and the felt sprite's actual aspect, so
        // the layout adapts to any pane size. Clamps include the cards-above
        // and name-plate-below extents so seats never overflow the rect.
        void RelayoutTablePane()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;
            var container = canvas.transform.Find("TableContainer") as RectTransform;
            if (container == null) return;

            // ── BAND CONSTANTS (px in canvas reference space) ─────────────
            // A seat's renderable footprint is wider than its avatar circle:
            // cards stack ABOVE the avatar (~130 px), the name plate sits
            // BELOW (~30 px). Clamps and ring radii must include those, or
            // top-row seats overflow the container ceiling.
            const float TOP_BUFFER     = 30f;   // empty above the felt
            const float BUTTONS_BAND   = 105f;  // betting strip at bottom
            const float FELT_INSET_X   = 80f;   // left/right inset on felt rect
            const float halfSeatW      = 75f;   // avatar half-width
            const float halfSeatH      = 60f;   // avatar half-height
            const float cardsAbove     = 130f;  // cards extend this far above avatar
            const float plateBelow     = 35f;   // name plate this far below avatar
            const float margin         = 10f;
            const float seatGap        = 30f;   // gap from felt rim to avatar EDGE
            // Default sprite aspect for the table PNG (1.84:1). Recomputed
            // from the actual sprite below if it's loaded.
            const float DEFAULT_ASPECT = 1.84f;

            // NOTE: do NOT call Canvas.ForceUpdateCanvases() here — it
            // synchronously walks the entire canvas tree and was a major
            // source of mid-game stalls when state updates arrive in bursts.
            // If rect.size hasn't been computed yet we just use the known
            // canvas reference dimensions as a fallback.
            float paneW = container.rect.width;
            float paneH = container.rect.height;
            if (paneW < 100f) paneW = 1495f;
            if (paneH < 100f) paneH = 1050f;

            // ── 1. FELT (PNG) — middle band ───────────────────────────────
            var feltT = FindDescendant(canvas.transform, "TableFelt")
                     ?? FindDescendant(canvas.transform, "TableBackdrop");
            if (feltT != null && feltT.parent != container)
            {
                feltT.SetParent(container, false);
                feltT.SetAsFirstSibling();
            }
            float feltVisW = 0f, feltVisH = 0f, feltCenterY = 0f;
            if (feltT != null)
            {
                feltT.anchorMin = new Vector2(0, 0);
                feltT.anchorMax = new Vector2(1, 1);
                feltT.pivot     = new Vector2(0.5f, 0.5f);
                feltT.anchoredPosition = Vector2.zero;
                // Felt RECT spans (left+FELT_INSET_X, bottom+HERO+BUTTONS)
                // up to (right-FELT_INSET_X, top-TOP_BUFFER). Do NOT touch
                // sizeDelta after these — with stretched anchors, sizeDelta
                // overrides offsetMin/Max.
                feltT.offsetMin = new Vector2(FELT_INSET_X, BUTTONS_BAND);
                feltT.offsetMax = new Vector2(-FELT_INSET_X, -TOP_BUFFER);
                feltT.localScale = Vector3.one;
                feltT.localRotation = Quaternion.identity;

                // Compute the felt PNG's actual VISUAL size after preserveAspect
                // letterboxing — the seat ring orbits this, NOT the rect.
                float feltRectW = paneW - 2f * FELT_INSET_X;
                float feltRectH = paneH - TOP_BUFFER - BUTTONS_BAND;
                float aspect = DEFAULT_ASPECT;
                var feltImg = feltT.GetComponent<Image>();
                if (feltImg != null && feltImg.sprite != null)
                {
                    var sr = feltImg.sprite.rect;
                    if (sr.height > 0.1f) aspect = sr.width / sr.height;
                }
                float rectAspect = feltRectW / Mathf.Max(1f, feltRectH);
                if (aspect >= rectAspect)
                {
                    // Sprite wider than rect → letterboxed top/bottom
                    feltVisW = feltRectW;
                    feltVisH = feltRectW / aspect;
                }
                else
                {
                    // Sprite taller than rect → pillarboxed left/right
                    feltVisH = feltRectH;
                    feltVisW = feltRectH * aspect;
                }
                // Felt RECT center Y inside the pane (pane center is 0).
                // top    =  paneH/2 - TOP_BUFFER
                // bottom = -paneH/2 + BUTTONS_BAND
                // centerY = (top + bottom) / 2
                //         = (BUTTONS_BAND - TOP_BUFFER) / 2
                feltCenterY = (BUTTONS_BAND - TOP_BUFFER) * 0.5f;
            }

            // ── 2. SeatHolder — covers the full pane so we can clamp seats
            // against the pane edges no matter where on the ring they sit.
            var holder = container.Find("SeatHolder") as RectTransform;
            if (holder == null)
            {
                var go = new GameObject("SeatHolder", typeof(RectTransform));
                go.transform.SetParent(container, false);
                holder = (RectTransform)go.transform;
            }
            holder.anchorMin = new Vector2(0, 0);
            holder.anchorMax = new Vector2(1, 1);
            holder.offsetMin = Vector2.zero;
            holder.offsetMax = Vector2.zero;
            holder.SetAsLastSibling();

            // ── 3. Place each seat ────────────────────────────────────────
            // Slot 0   = HERO, sits at the felt's bottom rim, cards on felt.
            // Slots 1-6 = orbit the FULL ellipse on a 360/7 schedule with
            //             the bottom slot reserved for hero. This keeps the
            //             two lower-side seats close to the hero (no empty
            //             corners) and pulls the top seats off the ceiling.
            //
            // Ring radii are derived from the felt's actual visual size so
            // the avatars sit just outside the rim regardless of pane size
            // or sprite aspect. Radii include the avatar half-extent + a
            // small breathing gap.
            float ringRX = feltVisW * 0.5f + halfSeatW + seatGap;
            float ringRY = feltVisH * 0.5f + halfSeatH + seatGap;
            // Cap horizontally so side seats can't escape the pane (cards
            // hang off the avatar vertically, NOT horizontally — width
            // budget is just halfSeatW + margin).
            float maxRX = paneW * 0.5f - halfSeatW - margin;
            if (ringRX > maxRX) ringRX = maxRX;
            // Vertical clamp budget MUST include card stack above + name plate
            // below, otherwise top seats render outside the container.
            float upperExtent = halfSeatH + cardsAbove;   // ~190
            float lowerExtent = halfSeatH + plateBelow;   // ~95
            float maxRY = paneH * 0.5f - upperExtent - margin;
            if (ringRY > maxRY) ringRY = maxRY;

            // Hero sits as low as possible: name plate clears the buttons
            // band, avatar sits at-or-just-below the felt visual bottom rim.
            float heroY = -paneH * 0.5f + BUTTONS_BAND + lowerExtent + margin;

            for (int i = 0; i < seatSlots.Length; i++)
            {
                var slot = seatSlots[i];
                if (slot == null) continue;
                var rt = slot.transform as RectTransform;
                if (rt == null) continue;

                if (rt.parent != holder) rt.SetParent(holder, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

                Vector2 pos;
                if (i == 0)
                {
                    // Hero — centered horizontally, anchored at heroY.
                    pos = new Vector2(0f, heroY);
                }
                else
                {
                    // 6 ring seats on a 360/7 schedule. Slot 0 (skipped here)
                    // would be at -90° (bottom) — that position is the hero.
                    // Stepping 360/7 ≈ 51.4° per slot gives:
                    //   slot 1: -38.6° (lower-right)
                    //   slot 2:  12.9° (right)
                    //   slot 3:  64.3° (upper-right)
                    //   slot 4: 115.7° (upper-left)
                    //   slot 5: 167.1° (left)
                    //   slot 6: 218.6° (lower-left)
                    float angleDeg = -90f + (360f / 7f) * i;
                    float a = angleDeg * Mathf.Deg2Rad;
                    pos = new Vector2(Mathf.Cos(a) * ringRX,
                                      feltCenterY + Mathf.Sin(a) * ringRY);

                    // The lower-side seats (slots 1 and 6) land at -38.6°
                    // and 218.6° on a fairly flat ellipse, where the felt
                    // rim curves in tightly — without help they end up
                    // hugging the felt edge while there's empty space next
                    // to the hero. Push them further DOWN and OUTWARD into
                    // the empty corners. The pane-edge clamp will catch any
                    // overshoot in X.
                    if (i == 1 || i == seatSlots.Length - 1)
                    {
                        pos.y -= 75f;
                        pos.x += (i == 1 ? 60f : -60f);
                    }
                }

                // Final clamp inside pane bounds — uses CARD-INCLUSIVE
                // vertical extents so cards above the avatar can't poke
                // through the container's top edge.
                float minX = -paneW * 0.5f + halfSeatW + margin;
                float maxX =  paneW * 0.5f - halfSeatW - margin;
                float minY = -paneH * 0.5f + lowerExtent + margin;
                float maxY =  paneH * 0.5f - upperExtent - margin;
                pos.x = Mathf.Clamp(pos.x, minX, maxX);
                pos.y = Mathf.Clamp(pos.y, minY, maxY);
                rt.anchoredPosition = pos;
            }

            // ── 4. BettingPanel — pinned to the buttons band at the bottom.
            // BettingPanelUI.EnforceLayout anchors to canvas-relative coords
            // by default; we override that here every layout pass.
            if (bettingPanel != null)
            {
                var bpRT = bettingPanel.GetComponent<RectTransform>();
                if (bpRT != null)
                {
                    if (bpRT.parent != container) bpRT.SetParent(container, false);
                    bpRT.anchorMin = new Vector2(0, 0);
                    bpRT.anchorMax = new Vector2(1, 0);
                    bpRT.pivot     = new Vector2(0.5f, 0);
                    bpRT.offsetMin = new Vector2(40, 15);
                    bpRT.offsetMax = new Vector2(-40, 0);
                    bpRT.sizeDelta = new Vector2(bpRT.sizeDelta.x, BUTTONS_BAND - 25f);
                    bpRT.anchoredPosition = new Vector2(bpRT.anchoredPosition.x, 15);
                    bpRT.SetAsLastSibling(); // above seats so buttons get clicks
                }
            }

            // ── 5. Felt decorations (pot, community cards, phase, deck) ──
            // Centered on the felt's VISUAL center (not rect center) since
            // preserveAspect letterboxing shifts the rendered image.
            // potRow sits ~55% of the visual radius above center; community
            // cards sit at center; phase text sits ~55% below center.
            if (feltT != null && feltVisH > 1f)
            {
                var potRow = feltT.Find("PotRow") as RectTransform;
                if (potRow != null)
                {
                    potRow.anchorMin = potRow.anchorMax = new Vector2(0.5f, 0.5f);
                    potRow.pivot = new Vector2(0.5f, 0.5f);
                    potRow.anchoredPosition = new Vector2(0f, feltVisH * 0.28f);
                    potRow.sizeDelta = new Vector2(260f, 56f);
                }
                var ccRow = feltT.Find("CommunityCards") as RectTransform;
                if (ccRow != null)
                {
                    ccRow.anchorMin = ccRow.anchorMax = new Vector2(0.5f, 0.5f);
                    ccRow.pivot = new Vector2(0.5f, 0.5f);
                    ccRow.anchoredPosition = Vector2.zero;
                    ccRow.sizeDelta = new Vector2(540f, 120f);
                }
                var phaseT = feltT.Find("PhaseText") as RectTransform;
                if (phaseT != null)
                {
                    phaseT.anchorMin = phaseT.anchorMax = new Vector2(0.5f, 0.5f);
                    phaseT.pivot = new Vector2(0.5f, 0.5f);
                    phaseT.anchoredPosition = new Vector2(0f, -feltVisH * 0.32f);
                }
                var deck = feltT.Find("DeckAnchor") as RectTransform;
                if (deck != null) deck.anchoredPosition = Vector2.zero;
            }
        }

        // ── Layout constants shared by the two top-level panes ───────────
        // Log pane on the LEFT + 15px gap + table pane on the RIGHT, all
        // inset by 15px from the canvas edges.
        public const float LOG_PANE_WIDTH   = 380f;
        public const float PANE_PADDING     = 15f;
        public const float PANE_GAP         = 15f;
        // X-offset applied to every table element so that the table is
        // centered inside the RIGHT pane rather than the whole canvas.
        // Canvas is 1920 wide centered at 0; the right pane spans from
        // (-960 + 15 + 380 + 15) = -550 to (960 - 15) = +945.
        // Center = (-550 + 945) / 2 ≈ +197.5 — round to +205.
        public const float TABLE_X_OFFSET   = 205f;

        // Subtle rounded-rect frame for the table area. The table felt,
        // seats, pot, community cards, etc. are shifted left so they sit
        // inside this frame rather than straddling the event log.
        void EnsureTableContainer()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;
            if (canvas.transform.Find("TableContainer") != null) return;

            var go = new GameObject("TableContainer",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(canvas.transform, false);
            go.transform.SetAsFirstSibling(); // behind everything else

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // 15px from screen right/top/bottom; on the left leave room
            // for the log pane + the gap between panes.
            rt.offsetMin = new Vector2(LOG_PANE_WIDTH + PANE_GAP + PANE_PADDING,
                                       PANE_PADDING);
            rt.offsetMax = new Vector2(-PANE_PADDING, -PANE_PADDING);

            var img = go.GetComponent<Image>();
            img.sprite = SpriteFactory.Pill();
            img.type = Image.Type.Sliced;
            // Soft inner felt so it looks like a recessed frame.
            img.color = new Color(0.04f, 0.02f, 0.10f, 0.55f);
            img.raycastTarget = false;

            // HARD VISUAL CLIP: any descendant of TableContainer is clipped
            // to its bounds — felt SVG, seats, ring pulse, debug overlays,
            // anything. This is the foolproof guarantee that nothing inside
            // the right pane can ever visibly overflow into the left log
            // pane, regardless of sprite padding or layout bugs.
            if (go.GetComponent<RectMask2D>() == null)
                go.AddComponent<RectMask2D>();

            // Subtle rim so the division reads.
            var ring = new GameObject("Rim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ring.transform.SetParent(go.transform, false);
            var rrt = ring.GetComponent<RectTransform>();
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;
            var rImg = ring.GetComponent<Image>();
            rImg.sprite = SpriteFactory.PillRing();
            rImg.type = Image.Type.Sliced;
            rImg.color = new Color(0.45f, 0.32f, 0.78f, 0.40f);
            rImg.raycastTarget = false;
        }

        // Re-center the felt, pot, community card row, and seat ring around
        // the canvas. The procedural table layout had the felt 2.74:1 wide
        // and seats placed for that aspect — the SVG felt is 1.84:1 so seats
        // need to move inward and the card slots re-centered.
        void EnsureTableLayout()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;

            var container = canvas.transform.Find("TableContainer") as RectTransform;

            // STRAY-FELT CLEANUP: nuke or reparent any felt/backdrop-like
            // GameObjects that live OUTSIDE TableContainer. A second copy
            // of the felt (left over from the prefab, an old auto-build
            // run, or a sibling layer) would render at canvas-center and
            // visually overflow into the log pane regardless of what we
            // do to the canonical TableFelt. Walk the canvas, find any
            // node whose name contains felt/backdrop/table that is NOT
            // a descendant of TableContainer, and reparent it under the
            // container so the RectMask2D clips it. We never touch
            // TableContainer itself.
            if (container != null)
            {
                var strayNames = new[] { "TableFelt", "TableBackdrop", "FeltBackdrop", "TableBg", "TableImage" };
                foreach (var n in strayNames)
                {
                    // Find ALL matches at any depth; loop until none remain
                    // outside the container (FindDescendant returns first hit).
                    for (int safety = 0; safety < 8; safety++)
                    {
                        var stray = FindDescendantOutside(canvas.transform, n, container);
                        if (stray == null) break;
                        Debug.LogWarning($"[Poker] Stray '{stray.name}' found at '{GetPath(stray)}' — reparenting under TableContainer");
                        stray.SetParent(container, false);
                        stray.SetAsFirstSibling();
                    }
                }
            }

            // The felt may be named "TableFelt" (existing in scene) or
            // "TableBackdrop" (auto-built by EnsureTableBackdrop when the
            // scene didn't have one). Search recursively in case it's nested.
            var feltT = FindDescendant(canvas.transform, "TableFelt")
                     ?? FindDescendant(canvas.transform, "TableBackdrop");

            // ALWAYS reparent and reposition the seats — even if no felt
            // exists, we still need the seats inside the table pane.
            ReparentAndPositionSeats(canvas);

            if (feltT == null) return;

            // Reparent the felt UNDER TableContainer and stretch-fit it to
            // the container with a small inset for the outer rim. With
            // preserveAspect=true on the felt's Image component, the SVG
            // will render letterboxed inside this rect — guaranteeing it
            // never extends past the container, regardless of the sprite's
            // native dimensions or any prefab-baked sizeDelta.
            if (container != null)
            {
                feltT.SetParent(container, false);
                feltT.SetAsFirstSibling(); // behind SeatHolder
                feltT.anchorMin = new Vector2(0, 0);
                feltT.anchorMax = new Vector2(1, 1);
                feltT.pivot     = new Vector2(0.5f, 0.5f);
                feltT.offsetMin = new Vector2(30, 30);
                feltT.offsetMax = new Vector2(-30, -30);
                feltT.localScale     = Vector3.one;
                feltT.localRotation  = Quaternion.identity;
                // Hard-zero sizeDelta so the stretched anchors actually
                // dictate the size (a prefab-baked sizeDelta of e.g.
                // (1600, 880) would otherwise add to the anchor span and
                // explode the rect outside the container).
                feltT.sizeDelta = Vector2.zero;
                // Re-enforce Image settings so a prefab-baked override can't
                // make the sprite render larger than this rect.
                var feltImg = feltT.GetComponent<Image>();
                if (feltImg != null)
                {
                    feltImg.preserveAspect = true;
                    feltImg.type = Image.Type.Simple;
                }
                // NOTE: removed Canvas.ForceUpdateCanvases() here — it
                // ran on every state update and was freezing the main
                // thread during showdown bursts.
                Debug.Log($"[Poker] Felt '{feltT.name}' reparented under TableContainer; rect={feltT.rect.size}, container={container.rect.size}");
            }
            else
            {
                // No container — fall back to canvas-relative positioning.
                feltT.anchorMin = feltT.anchorMax = new Vector2(0.5f, 0.5f);
                feltT.pivot = new Vector2(0.5f, 0.5f);
                feltT.anchoredPosition = new Vector2(TABLE_X_OFFSET, 0f);
                feltT.sizeDelta = new Vector2(1040f, 620f);
            }

            // Pot pill above the community cards.
            var potRow = feltT.Find("PotRow") as RectTransform;
            if (potRow != null)
            {
                potRow.anchorMin = potRow.anchorMax = new Vector2(0.5f, 0.5f);
                potRow.pivot = new Vector2(0.5f, 0.5f);
                potRow.anchoredPosition = new Vector2(0f, 110f);
                potRow.sizeDelta = new Vector2(280f, 64f);
            }

            // Community cards centered on the felt.
            var ccRow = feltT.Find("CommunityCards") as RectTransform;
            if (ccRow != null)
            {
                ccRow.anchorMin = ccRow.anchorMax = new Vector2(0.5f, 0.5f);
                ccRow.pivot = new Vector2(0.5f, 0.5f);
                ccRow.anchoredPosition = new Vector2(0f, -10f);
                ccRow.sizeDelta = new Vector2(620f, 140f);
            }

            // Phase text below the cards (THE FLOP / TURN / RIVER).
            var phaseT = feltT.Find("PhaseText") as RectTransform;
            if (phaseT != null)
            {
                phaseT.anchorMin = phaseT.anchorMax = new Vector2(0.5f, 0.5f);
                phaseT.pivot = new Vector2(0.5f, 0.5f);
                phaseT.anchoredPosition = new Vector2(0f, -130f);
            }

            // DeckAnchor at table center for chip-fly origin.
            var deck = feltT.Find("DeckAnchor") as RectTransform;
            if (deck != null) deck.anchoredPosition = Vector2.zero;

        }

        // Recursively find a descendant by name. Transform.Find only matches
        // direct children, so deeper nesting needs this walk.
        static RectTransform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.name == name) return c as RectTransform;
                var deep = FindDescendant(c, name);
                if (deep != null) return deep;
            }
            return null;
        }

        // Like FindDescendant but skips anything that lives inside `excludeRoot`.
        // Used to detect rogue felt/backdrop GameObjects outside the table pane.
        static RectTransform FindDescendantOutside(Transform root, string name, Transform excludeRoot)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (excludeRoot != null && c == excludeRoot) continue;
                if (c.name == name && !IsDescendantOf(c, excludeRoot))
                    return c as RectTransform;
                var deep = FindDescendantOutside(c, name, excludeRoot);
                if (deep != null) return deep;
            }
            return null;
        }

        static bool IsDescendantOf(Transform node, Transform ancestor)
        {
            if (ancestor == null) return false;
            for (var t = node; t != null; t = t.parent)
                if (t == ancestor) return true;
            return false;
        }

        static string GetPath(Transform t)
        {
            if (t == null) return "<null>";
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent)
                s = p.name + "/" + s;
            return s;
        }

        // Reparent every seat under the TableContainer's "SeatHolder" child
        // and place it on a tight ellipse. Because the seats are now children
        // of the right-side table pane, anchoredPositions are *automatically*
        // bounded by the pane — no clamps or canvas-wide math needed.
        void ReparentAndPositionSeats(Canvas canvas)
        {
            var container = canvas.transform.Find("TableContainer") as RectTransform;
            if (container == null) return;

            var holder = container.Find("SeatHolder") as RectTransform;
            if (holder == null)
            {
                var go = new GameObject("SeatHolder", typeof(RectTransform));
                go.transform.SetParent(container, false);
                holder = (RectTransform)go.transform;
                holder.anchorMin = Vector2.zero;
                holder.anchorMax = Vector2.one;
                holder.offsetMin = holder.offsetMax = Vector2.zero;
            }
            // Keep seat layer in front of the felt/backdrop.
            holder.SetAsLastSibling();

            // Compute the SeatHolder's runtime size so we can pick safe radii.
            // NOTE: removed Canvas.ForceUpdateCanvases() — too expensive on
            // a hot path. If rect hasn't been computed yet we fall back to
            // canvas reference dimensions below.
            float paneW = holder.rect.width;
            float paneH = holder.rect.height;
            // Fallback estimate if rect still hasn't sized.
            if (paneW < 100f) paneW = 1495f;
            if (paneH < 100f) paneH = 1050f;

            const float halfSeatW = 95f;
            const float halfSeatH = 115f;
            const float margin    = 12f;
            // The largest ellipse radii that keep every seat strictly inside
            // the SeatHolder rectangle.
            float seatRX = paneW * 0.5f - halfSeatW - margin;
            float seatRY = paneH * 0.5f - halfSeatH - margin - 90f; // leave room for buttons
            // Don't let the table feel cramped on huge displays.
            seatRX = Mathf.Min(seatRX, 480f);
            seatRY = Mathf.Min(seatRY, 260f);

            // Hero anchored to the bottom of the pane, above the 200px button strip.
            float heroY = -(paneH * 0.5f) + halfSeatH + 200f + margin;

            for (int i = 0; i < seatSlots.Length; i++)
            {
                var slot = seatSlots[i];
                if (slot == null) continue;
                var rt = slot.transform as RectTransform;
                if (rt == null) continue;

                // Reparent UNDER the table pane so anchored positions are
                // naturally bounded by the pane rectangle.
                rt.SetParent(holder, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

                Vector2 pos;
                if (i == 0)
                {
                    pos = new Vector2(0f, heroY);
                }
                else
                {
                    float angleDeg = -90f + (360f / seatSlots.Length) * i;
                    float a = angleDeg * Mathf.Deg2Rad;
                    pos = new Vector2(Mathf.Cos(a) * seatRX, Mathf.Sin(a) * seatRY);
                }
                // Final hard clamp inside the pane rect, regardless.
                float minX = -(paneW * 0.5f) + halfSeatW + margin;
                float maxX =  (paneW * 0.5f) - halfSeatW - margin;
                float minY = -(paneH * 0.5f) + halfSeatH + margin;
                float maxY =  (paneH * 0.5f) - halfSeatH - margin;
                pos.x = Mathf.Clamp(pos.x, minX, maxX);
                pos.y = Mathf.Clamp(pos.y, minY, maxY);
                rt.anchoredPosition = pos;
                Debug.Log($"[Poker] seat[{i}] reparented under SeatHolder ({paneW:F0}x{paneH:F0}) @ {pos}");
            }
        }

        // Replace the procedural TableFelt image with the SVG table. We swap
        // the sprite on the existing TableFelt GameObject (rather than adding
        // a new layer) so the new art truly replaces the old one — otherwise
        // the procedural felt would render on top of any backdrop we add.
        void EnsureTableBackdrop()
        {
            var tableSprite = SpriteFactory.Table();
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;

            // Remove any earlier auto-built backdrop from prior runs.
            var oldAuto = FindDescendant(canvas.transform, "TableBackdrop");
            if (oldAuto != null) Destroy(oldAuto.gameObject);

            // Prefer existing TableFelt (search recursively in case it's
            // nested under some intermediate panel); if missing, build a
            // backdrop sized to the canvas reference.
            Transform feltT = FindDescendant(canvas.transform, "TableFelt");
            Image img;
            RectTransform rt;
            if (feltT != null)
            {
                rt = feltT as RectTransform;
                img = feltT.GetComponent<Image>();
                if (img == null) img = feltT.gameObject.AddComponent<Image>();
            }
            else
            {
                var go = new GameObject("TableFelt",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(canvas.transform, false);
                rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(1600, 880);
                img = go.GetComponent<Image>();
                go.transform.SetAsFirstSibling();
                feltT = rt;
            }

            // CRITICAL: nuke procedural ellipse graphics on the felt itself.
            // EllipseUI is a MaskableGraphic that shares the GameObject's
            // CanvasRenderer with Image — both can't render at once, and
            // EllipseUI's [ExecuteAlways] re-pushes its mesh, hiding the PNG.
            var ellipseSelf = feltT.GetComponent<EllipseUI>();
            if (ellipseSelf != null) Destroy(ellipseSelf);
            // Also destroy any lingering child ellipses (procedural rim,
            // cyan/magenta neon rings drawn from a previous build).
            var childEllipses = feltT.GetComponentsInChildren<EllipseUI>(true);
            foreach (var e in childEllipses)
                if (e != null) Destroy(e.gameObject);

            // Hide any explicit child decoration GameObjects that match
            // the old procedural names (these would otherwise sit on top
            // of the PNG felt).
            string[] oldDecoNames = { "FeltRing", "FeltGlow", "OuterRing", "InnerRing", "RimGlow", "NeonRing" };
            foreach (var n in oldDecoNames)
            {
                var d = feltT.Find(n);
                if (d != null) Destroy(d.gameObject);
            }

            if (tableSprite != null)
            {
                img.sprite = tableSprite;
                img.color = Color.white;
                img.preserveAspect = true;
                img.type = Image.Type.Simple;
            }
            else
            {
                // PNG missing — fall back to a soft dark felt fill so the
                // pane still reads cleanly without the procedural neon rings.
                Debug.LogWarning("[Poker] table.png not found in Resources/ — using fallback color fill.");
                img.sprite = SpriteFactory.Pill();
                img.type = Image.Type.Sliced;
                img.color = new Color(0.10f, 0.06f, 0.20f, 1f);
                img.preserveAspect = false;
            }
            img.raycastTarget = false;
        }

        void LogSpriteAvailability()
        {
            string s(Sprite sp) => sp != null ? "OK" : "missing";
            Debug.Log(
                $"[Poker] SVG sprites — table:{s(SpriteFactory.Table())} avatar:{s(SpriteFactory.Avatar())} " +
                $"cardFace:{s(SpriteFactory.CardFace())} cardBack:{s(SpriteFactory.CardBack())} " +
                $"chip1k:{s(SpriteFactory.ChipFor(1000))} chip1m:{s(SpriteFactory.ChipFor(1_000_000))}"
            );
        }

        // Build a styled winner banner at runtime if the scene didn't ship one
        // (or only has a partial set of refs). Pinned at top-center so it never
        // hides the board and reads as a celebratory overlay.
        // Winner banner removed (user request 2026-05) — the celebration now
        // lives entirely in the right-side event log via EventLogUI.LogWinner.
        // This function destroys any pre-existing on-table banner (auto-built
        // in earlier sessions, scene-prefab-wired, etc.) and nulls the
        // references so the rest of the codebase's null-checks remain safe.
        void EnsureWinnerBanner()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;

            // Nuke the auto-built banner from any prior session.
            var auto = canvas.transform.Find("WinnerBannerAuto");
            if (auto != null) Destroy(auto.gameObject);

            // Nuke a scene-prefab-wired banner if one exists.
            if (winnerBanner != null) Destroy(winnerBanner);
            winnerBanner = null;
            winnerText = null;
            handNameText = null;
        }

        // Ensure the canvas scales for both phone and web. Without this the
        // scene-time CanvasScaler can be left in "Constant Pixel Size" mode,
        // which makes the 1920×1080 layout overflow narrow phone viewports.
        // Using ScaleWithScreenSize + match=0.5 keeps the table fitting either
        // axis: landscape phone, portrait phone, or wide desktop.
        void EnsureResponsiveCanvas()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode             = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution     = new Vector2(1920, 1080);
            scaler.screenMatchMode         = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            // 0.5 = balanced; on a portrait phone (taller than wide) the table
            // will scale down so the full 1920px stage fits horizontally.
            scaler.matchWidthOrHeight      = 0.5f;
            scaler.referencePixelsPerUnit  = 100f;
        }

        void EnsureEventLog()
        {
            if (EventLogUI.Instance != null) return;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) { Debug.LogWarning("[Poker] EventLogUI: no Canvas found"); return; }
            var go = new GameObject("EventLogUI", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.AddComponent<EventLogUI>();
            Debug.Log("[Poker] EventLogUI installed under canvas " + canvas.name);
        }

        void EnsureLeaveRoomButton()
        {
            // Find a Canvas to parent under
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;

            // Drop the legacy red "GO TO LOBBY" pill if it's still in
            // the scene from a previous build.
            var legacy = canvas.transform.Find("GoToLobbyButton");
            if (legacy != null) Destroy(legacy.gameObject);

            // Don't create twice.
            if (canvas.transform.Find("HomeIconButton") != null) return;

            // Subtle dark disc with a white house glyph — minimal, modern,
            // and reads as "go home" at a glance.
            var go = new GameObject("HomeIconButton",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(canvas.transform, false);
            var rt = go.GetComponent<RectTransform>();
            // Top-left, 20px inset.
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(20, -20);
            rt.sizeDelta = new Vector2(48, 48);

            var img = go.GetComponent<Image>();
            img.sprite = SpriteFactory.Circle();
            img.color = new Color(0.06f, 0.04f, 0.14f, 0.85f);

            var btn = go.GetComponent<Button>();
            // Selectable.Transition.None so we don't get a tint flash;
            // the hover/press affordance is the icon scale shift below
            // (handled by the EventSystem default).
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(OnLeaveRoom);

            // Subtle outer ring for extra definition against busy felt.
            var ringGO = new GameObject("Ring",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ringGO.transform.SetParent(go.transform, false);
            var rrt = ringGO.GetComponent<RectTransform>();
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;
            var rImg = ringGO.GetComponent<Image>();
            rImg.sprite = SpriteFactory.RingThin();
            rImg.color = new Color(1f, 1f, 1f, 0.20f);
            rImg.raycastTarget = false;

            // Home glyph as a TMP text (Unicode house). Falls back to "H"
            // if the font has no glyph; either way the affordance reads.
            var labelGO = new GameObject("Icon",
                typeof(RectTransform), typeof(CanvasRenderer));
            labelGO.transform.SetParent(go.transform, false);
            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = "\u2302"; // ⌂ HOUSE — present in most TMP default atlases
            tmp.fontSize = 32;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }

        void OnLeaveRoom()
        {
            // Force-return to the lobby regardless of game state. Each step
            // is null-guarded so the click always succeeds — even if we're
            // mid-deal, mid-showdown, or there's no active state at all.
            try { GameStateManager.Instance?.LeaveRoom(); } catch { }
            _state = null;
            _seatToSlot?.Clear();
            _prevBet?.Clear();
            _dealtPlayers?.Clear();
            _dealIndex = 0;
            ResetCommunityCards();
            if (seatSlots != null)
            {
                for (int i = 0; i < seatSlots.Length; i++)
                    if (seatSlots[i]) seatSlots[i].gameObject.SetActive(false);
            }
            if (winnerBanner)    winnerBanner.SetActive(false);
            if (handResultPanel) handResultPanel.SetActive(false);
            CancelInvoke(nameof(AutoNextHand));
            bettingPanel?.Hide();
            ShowLobby(true);
            SetStatus("Left room");
        }

        // Cached refs for the USS-spec pot bar (built lazily by BuildPotBar).
        TextMeshProUGUI _potLabelTMP;
        TextMeshProUGUI _potValueTMP;

        void ApplyHudVisuals()
        {
            BuildPotBar();
            if (phaseText)
            {
                phaseText.fontSize = 22;
                phaseText.fontStyle = FontStyles.Bold;
                phaseText.color = new Color(0.85f, 0.92f, 1f);
                phaseText.alignment = TextAlignmentOptions.Center;
            }
        }

        // Build the pot display per USS: dark pill with a 4-disc purple chip
        // stack on the left and a POT label + amount column on the right.
        void BuildPotBar()
        {
            // Felt may now live under TableContainer (recently reparented),
            // so the old "TableFelt/PotRow" path-find no longer matches.
            // Walk recursively from the canvas root to find PotRow regardless
            // of its current parent.
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            RectTransform potRow = null;
            if (canvas != null)
            {
                var feltT = FindDescendant(canvas.transform, "TableFelt")
                         ?? FindDescendant(canvas.transform, "TableBackdrop");
                if (feltT != null) potRow = feltT.Find("PotRow") as RectTransform;
                if (potRow == null) potRow = FindDescendant(canvas.transform, "PotRow");
            }
            if (potRow == null) return;

            potRow.sizeDelta = new Vector2(280, 64);

            // Hide prefab-baked pot children that would otherwise render
            // alongside the new ChipStack/PotLabel/PotValue produced below.
            // (The screenshot showed a 1M chip floating in a separate dark
            // pill — that was the prefab's PotChipIcon at the old anchor.)
            string[] potConflicts = { "PotChipIcon", "PotText" };
            foreach (var n in potConflicts)
            {
                var ch = potRow.Find(n);
                if (ch != null) ch.gameObject.SetActive(false);
            }
            // Drop legacy field references so nothing tries to re-show them.
            if (potChipIcon != null && potChipIcon.name == "PotChipIcon") potChipIcon = null;

            var bg = potRow.GetComponent<Image>();
            if (bg == null) bg = potRow.gameObject.AddComponent<Image>();
            bg.sprite = SpriteFactory.Pill();
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.024f, 0.031f, 0.078f, 0.88f); // surface dark
            bg.raycastTarget = false;

            // Soft purple border via a stacked pill on top
            var border = potRow.Find("Border") as RectTransform;
            if (border == null)
            {
                var bGO = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                bGO.transform.SetParent(potRow, false);
                border = (RectTransform)bGO.transform;
                border.anchorMin = Vector2.zero;
                border.anchorMax = Vector2.one;
                border.offsetMin = border.offsetMax = Vector2.zero;
            }
            var bImg = border.GetComponent<Image>();
            if (bImg == null) bImg = border.gameObject.AddComponent<Image>();
            bImg.sprite = SpriteFactory.Pill();
            bImg.type = Image.Type.Sliced;
            bImg.color = new Color(0.627f, 0.392f, 1f, 0.28f);
            bImg.raycastTarget = false;

            // Chip stack — 4 oval discs, pinned to the pill's left interior.
            var stack = potRow.Find("ChipStack") as RectTransform;
            if (stack == null)
            {
                var sGO = new GameObject("ChipStack", typeof(RectTransform));
                sGO.transform.SetParent(potRow, false);
                stack = (RectTransform)sGO.transform;
            }
            stack.anchorMin = stack.anchorMax = new Vector2(0f, 0.5f);
            stack.pivot = new Vector2(0f, 0.5f);
            stack.sizeDelta = new Vector2(40, 46);
            stack.anchoredPosition = new Vector2(18, 0);
            Color[] chipCols = {
                new Color(0.788f, 0.478f, 1f),   // top — #C97AFF
                new Color(0.608f, 0.373f, 0.878f),
                new Color(0.482f, 0.251f, 0.753f),
                new Color(0.353f, 0.157f, 0.565f), // bottom — #5A2890
            };
            for (int i = 0; i < 4; i++)
            {
                var disc = stack.Find($"Disc{i}") as RectTransform;
                if (disc == null)
                {
                    var dGO = new GameObject($"Disc{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    dGO.transform.SetParent(stack, false);
                    disc = (RectTransform)dGO.transform;
                }
                disc.anchorMin = disc.anchorMax = new Vector2(0.5f, 0f);
                disc.pivot = new Vector2(0.5f, 0f);
                disc.sizeDelta = new Vector2(36, 14);
                disc.anchoredPosition = new Vector2(0, i * 10f);
                var dImg = disc.GetComponent<Image>();
                if (dImg == null) dImg = disc.gameObject.AddComponent<Image>();
                dImg.sprite = SpriteFactory.Pill();
                dImg.type = Image.Type.Sliced;
                dImg.color = chipCols[3 - i]; // bottom-most layer first
                dImg.raycastTarget = false;
            }
            potChipIcon = stack.gameObject;

            // POT label (small, purple-muted)
            var label = potRow.Find("PotLabel") as RectTransform;
            if (label == null)
            {
                var lGO = new GameObject("PotLabel", typeof(RectTransform));
                lGO.transform.SetParent(potRow, false);
                label = (RectTransform)lGO.transform;
            }
            label.anchorMin = label.anchorMax = new Vector2(0f, 0.5f);
            label.pivot = new Vector2(0f, 0.5f);
            label.sizeDelta = new Vector2(70, 18);
            label.anchoredPosition = new Vector2(72, 10);
            _potLabelTMP = label.GetComponent<TextMeshProUGUI>();
            if (_potLabelTMP == null) _potLabelTMP = label.gameObject.AddComponent<TextMeshProUGUI>();
            _potLabelTMP.text = "POT";
            _potLabelTMP.fontSize = 14;
            _potLabelTMP.fontStyle = FontStyles.Bold;
            _potLabelTMP.characterSpacing = 8f;
            _potLabelTMP.color = new Color(0.6f, 0.502f, 0.8f); // text pot label
            _potLabelTMP.alignment = TextAlignmentOptions.Left;
            _potLabelTMP.enableWordWrapping = false;
            _potLabelTMP.raycastTarget = false;

            // POT value — promote whatever potText was assigned to, or build one.
            var value = potRow.Find("PotValue") as RectTransform;
            if (value == null)
            {
                var vGO = new GameObject("PotValue", typeof(RectTransform));
                vGO.transform.SetParent(potRow, false);
                value = (RectTransform)vGO.transform;
            }
            value.anchorMin = value.anchorMax = new Vector2(0f, 0.5f);
            value.pivot = new Vector2(0f, 0.5f);
            value.sizeDelta = new Vector2(180, 30);
            value.anchoredPosition = new Vector2(72, -10);
            _potValueTMP = value.GetComponent<TextMeshProUGUI>();
            if (_potValueTMP == null) _potValueTMP = value.gameObject.AddComponent<TextMeshProUGUI>();
            _potValueTMP.fontSize = 26;
            _potValueTMP.fontStyle = FontStyles.Bold;
            _potValueTMP.color = Color.white;
            _potValueTMP.alignment = TextAlignmentOptions.Left;
            _potValueTMP.enableWordWrapping = false;
            _potValueTMP.raycastTarget = false;
            // Route legacy potText writes through this label too.
            potText = _potValueTMP;
        }

        // Pulsing gold rim was removed by user request — the PNG felt has
        // its own static gold trim, so the animated overlay was redundant.
        // We keep a no-op cleaner so any pre-existing GoldRingPulse left
        // over in the scene from prior runs gets destroyed at startup.
        Image _goldRingPulse; // kept for binary compat with Update tween
        void RemoveGoldRingPulse()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;
            var ring = FindDescendant(canvas.transform, "GoldRingPulse");
            if (ring != null) Destroy(ring.gameObject);
            _goldRingPulse = null;
        }

        // Legacy entry point — kept around in case anything still calls it.
        // Now just a thin alias for the cleanup so we never re-add the ring.
        void EnsureGoldRingPulse() => RemoveGoldRingPulse();
        void _DEPRECATED_BuildGoldRingPulse_Unused()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas == null) return;
            // Felt has been reparented under TableContainer by EnsureTableLayout;
            // use a recursive find so we still locate it.
            var feltT = FindDescendant(canvas.transform, "TableFelt")
                     ?? FindDescendant(canvas.transform, "TableBackdrop");
            if (feltT == null) return;
            var ring = feltT.Find("GoldRingPulse") as RectTransform;
            if (ring == null)
            {
                var go = new GameObject("GoldRingPulse",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(feltT, false);
                ring = (RectTransform)go.transform;
                ring.anchorMin = Vector2.zero;
                ring.anchorMax = Vector2.one;
                ring.offsetMin = ring.offsetMax = Vector2.zero;
            }
            _goldRingPulse = ring != null ? ring.GetComponent<Image>() : null;
            if (_goldRingPulse == null) return;
            // Hand-painted golden ring sprite — outline only, transparent inside,
            // matches the felt's outer ellipse so it sits exactly on the rim.
            _goldRingPulse.sprite = MakeGoldRingSprite();
            _goldRingPulse.type = Image.Type.Simple;
            _goldRingPulse.preserveAspect = false;
            _goldRingPulse.raycastTarget = false;
            _goldRingPulse.color = new Color(0.961f, 0.784f, 0.259f, 0.6f);
        }

        static Sprite _goldRingSprite;
        static Sprite MakeGoldRingSprite()
        {
            if (_goldRingSprite != null) return _goldRingSprite;
            int w = 460, h = 250;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float cx = w * 0.5f, cy = h * 0.5f;
            // Match the felt's outer rim ratio (~436:226 from SVG viewBox 920×500).
            float rxOut = w * 0.476f;
            float ryOut = h * 0.464f;
            float rxIn  = rxOut - 4f;
            float ryIn  = ryOut - 4f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = (x - cx) / rxOut;
                    float dy = (y - cy) / ryOut;
                    float dOut = Mathf.Sqrt(dx*dx + dy*dy);
                    float dxI = (x - cx) / rxIn;
                    float dyI = (y - cy) / ryIn;
                    float dIn = Mathf.Sqrt(dxI*dxI + dyI*dyI);
                    // Antialiased ring: alpha 1 inside band, fade at both edges.
                    float aOut = Mathf.Clamp01((1f - dOut) * 60f);
                    float aIn  = Mathf.Clamp01((dIn - 1f) * 60f);
                    float a = Mathf.Min(aOut, aIn);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _goldRingSprite = Sprite.Create(tex, new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), 100f);
            return _goldRingSprite;
        }

        void Update()
        {
            // Gold ring pulse removed (user request 2026-05). Update kept
            // empty as a hook in case future per-frame animations land.
        }

        // ── Lobby handlers ────────────────────────────────────────────────

        void OnCreateRoom()
        {
            string name = GetName();
            _pendingBuyIn = GetBuyIn();
            _pendingSitDown = true;
            GameStateManager.Instance.CreateRoom(name);
            SetStatus("Creating room...");
        }

        void OnJoinRoom()
        {
            string name = GetName();
            string code = roomCodeInput ? roomCodeInput.text.ToUpper().Trim() : "";
            if (string.IsNullOrEmpty(code)) { SetStatus("Enter a room code"); return; }
            _pendingBuyIn = GetBuyIn();
            _pendingSitDown = true;
            GameStateManager.Instance.JoinRoom(name, code);
            SetStatus($"Joining {code}...");
        }

        void OnSitDown()
        {
            SitDownOnFreeSeat(GetBuyIn());
        }

        void OnPlayWithBots()
        {
            string name = GetName();
            int count = 3;
            if (botCountInput && int.TryParse(botCountInput.text, out int n))
                count = Mathf.Clamp(n, 1, 6);
            int buyIn = GetBuyIn();
            // No pending sit — server seats us at seat 0 inside play_with_bots
            _pendingSitDown = false;
            GameStateManager.Instance.PlayWithBots(name, count, buyIn);
            SetStatus($"Starting game with {count} bot{(count == 1 ? "" : "s")}...");
        }

        void SitDownOnFreeSeat(int buyIn)
        {
            int freeSeat = 0;
            if (_state?.players != null)
            {
                var taken = new HashSet<int>();
                foreach (var p in _state.players) taken.Add(p.seat);
                for (int i = 0; i < 9; i++) if (!taken.Contains(i)) { freeSeat = i; break; }
            }
            GameStateManager.Instance.SitDown(freeSeat, buyIn);
        }

        string GetName() => (playerNameInput && !string.IsNullOrWhiteSpace(playerNameInput.text))
            ? playerNameInput.text.Trim() : "Player";

        int GetBuyIn()
        {
            int buyIn = 1000;
            if (buyInInput && int.TryParse(buyInInput.text, out int b)) buyIn = b;
            return buyIn;
        }

        void ShowLobby(bool show)
        {
            if (lobbyPanel) lobbyPanel.SetActive(show);
            LobbyUIController.Instance?.ShowPanel(show);
        }

        void SetStatus(string msg) { if (statusText) statusText.text = msg; }

        // ── Main state update ─────────────────────────────────────────────

        void OnStateUpdated(GameState state)
        {
            _state = state;
            string myId = GameStateManager.Instance.MyPlayerId;

            // Check if I am seated in this state
            bool imSeated = false;
            if (state.players != null)
                foreach (var p in state.players)
                    if (p.id == myId) { imSeated = true; break; }

            // Auto-sit after joining/creating a room
            if (_pendingSitDown && !imSeated && !string.IsNullOrEmpty(myId))
            {
                _pendingSitDown = false;
                SitDownOnFreeSeat(_pendingBuyIn);
            }

            GamePhase phase = state.GetPhase();
            bool inGame = phase != GamePhase.WAITING;
            // Hide lobby once seated OR once a hand is running
            ShowLobby(!inGame && !imSeated);

            if (startGameButton)
                startGameButton.gameObject.SetActive(
                    phase == GamePhase.WAITING && imSeated &&
                    state.players != null && state.players.Count >= 2);

            // Status text while waiting
            if (phase == GamePhase.WAITING && imSeated)
            {
                int seated = state.players?.Count ?? 0;
                SetStatus(seated < 2
                    ? $"Room {state.room_code}  —  Waiting for players... ({seated}/2 min)"
                    : $"Room {state.room_code}  —  {seated} players ready");
            }

            if (roomCodeText) roomCodeText.text = $"Room  {state.room_code}";
            // Self-heal: ensure seats/felt/buttons are inside TableContainer
            // every state update — protects against any sibling component
            // re-anchoring its own RectTransform to the canvas.
            RelayoutTablePane();

            UpdatePhase(phase);
            UpdatePot(state.pot);
            UpdateCommunityCards(state);
            UpdateSeats(state);

            bool myTurn = state.current_player_id == myId;
            bool inActiveHand = inGame
                       && phase != GamePhase.SHOWDOWN
                       && phase != GamePhase.HAND_COMPLETE;
            bool canAct = inActiveHand && myTurn;

            Debug.Log($"[Poker] phase={phase} myId={myId} curr={state.current_player_id} myTurn={myTurn} canAct={canAct}");

            if (canAct)                              bettingPanel?.Show(state);
            else if (inActiveHand && imSeated)       bettingPanel?.ShowDisabled(state);
            else                                     bettingPanel?.Hide();

            // Hand auto-advances. Schedule the next hand once we hit HAND_COMPLETE
            // so players have time to read the winner banner / event log entry.
            if (phase == GamePhase.HAND_COMPLETE && !IsInvoking(nameof(AutoNextHand)))
            {
                PositionShowdownOverlays();
                Invoke(nameof(AutoNextHand), 5.5f);
            }
        }

        // ── Phase ─────────────────────────────────────────────────────────

        void UpdatePhase(GamePhase phase)
        {
            if (phaseText)
                phaseText.text = phase switch
                {
                    GamePhase.PREFLOP       => "PRE-FLOP",
                    GamePhase.FLOP          => "THE FLOP",
                    GamePhase.TURN          => "THE TURN",
                    GamePhase.RIVER         => "THE RIVER",
                    GamePhase.SHOWDOWN      => "SHOWDOWN",
                    GamePhase.HAND_COMPLETE => "HAND COMPLETE",
                    _                       => ""
                };
            _lastPhase = phase;
        }

        // ── Pot ───────────────────────────────────────────────────────────

void UpdatePot(int pot)
        {
            // USS pot bar has separate POT label + value; just write the
            // formatted amount and leave the chip stack visible.
            if (_potValueTMP != null) _potValueTMP.text = pot > 0 ? FormatChips(pot) : "—";
            else if (potText) potText.text = pot > 0 ? FormatChips(pot) : "";
            // Keep the bar visible whenever a hand is being played; only the
            // value swaps to "—" on a fresh hand. PotRow may live under
            // TableContainer/TableFelt now — search recursively.
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                var potRow = FindDescendant(canvas.transform, "PotRow");
                if (potRow) potRow.gameObject.SetActive(true);
            }
        }

        // ── Community Cards ───────────────────────────────────────────────

        void ResetCommunityCards()
        {
            foreach (var c in communityCards)
                if (c) { c.ShowFaceDown(); c.gameObject.SetActive(false); }
            _lastCommunityCount = 0;
            LayoutCommunityCards();
        }

        // Distribute the 5 community cards evenly within communityCardsRoot so
        // they always fit, regardless of scene-time layout.
        void LayoutCommunityCards()
        {
            if (communityCardsRoot == null) return;
            float w = communityCardsRoot.rect.width;
            if (w <= 1f) return;
            int n = communityCards.Length;
            float spacing = 10f;
            float cardW = (w - spacing * (n - 1)) / n;
            // Cap matches HeroCardSize.x so board + hero + showdown cards
            // visually line up at the same dimensions across the table.
            cardW = Mathf.Min(cardW, 88f);
            cardW = Mathf.Max(cardW, 40f);
            float cardH = cardW * 1.4f;
            float total = cardW * n + spacing * (n - 1);
            float startX = -total * 0.5f + cardW * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var c = communityCards[i];
                if (c == null) continue;
                var rt = c.GetComponent<RectTransform>();
                if (rt == null) continue;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(cardW, cardH);
                rt.anchoredPosition = new Vector2(startX + i * (cardW + spacing), 0);
                c.Reflow();
            }
        }

        void UpdateCommunityCards(GameState state)
        {
            LayoutCommunityCards();
            int count = state.community_cards?.Count ?? 0;
            int prev  = _lastCommunityCount;

            // Show / update cards
            for (int i = 0; i < communityCards.Length; i++)
            {
                var cui = communityCards[i];
                if (cui == null) continue;

                if (state.community_cards != null && i < state.community_cards.Count)
                {
                    cui.Show();
                    if (i >= prev) // Newly revealed card
                    {
                        cui.ShowFaceDown();
                        var card = state.community_cards[i];
                        var rt = cui.GetComponent<RectTransform>();
                        if (rt != null && PokerAnimator.Instance != null)
                        {
                            // Slower per-card stagger — players need time to read
                            // each board card as it lands.
                            float delay = (i - prev) * 0.55f;
                            StartCoroutine(RevealCommunityCard(cui, card, delay));
                        }
                        else
                            cui.SetCard(card);
                    }
                    else
                        cui.SetCard(state.community_cards[i]);
                }
                else
                {
                    cui.ShowFaceDown();
                    cui.gameObject.SetActive(false);
                }
            }
            _lastCommunityCount = count;
        }

        IEnumerator RevealCommunityCard(CardUI cui, Card card, float delay)
        {
            yield return new WaitForSeconds(delay);
            cui.gameObject.SetActive(true);
            cui.ShowFaceDown();
            // Brief pause face-down so the dealer's "place card on board" beat is
            // visible before the flip starts.
            yield return new WaitForSeconds(0.18f);
            var rt = cui.GetComponent<RectTransform>();
            if (rt != null && PokerAnimator.Instance != null)
                PokerAnimator.Instance.FlipReveal(rt, () => { cui.ApplyCard(card); cui.ShowFaceUp(); }, null);
            else
                cui.SetCard(card);
        }

        // ── Seats — hero always at slot 0 (bottom center) ─────────────────

        void UpdateSeats(GameState state)
        {
            string myId = GameStateManager.Instance.MyPlayerId;

            // Build server-seat → visual-slot mapping (hero = slot 0).
            //
            // Priority FILL ORDER for the 6 ring slots (1..6 in physical
            // ring layout — see RelayoutTablePane for angles):
            //   1 = lower-right (hero's right neighbor)
            //   6 = lower-left  (hero's left  neighbor)
            //   2 = right
            //   5 = left
            //   3 = upper-right
            //   4 = upper-left
            //
            // Players are placed in this order so that with few opponents
            // the seats sit DOWN by the hero where there's the most empty
            // space, and the cramped upper row only fills when necessary.
            // To preserve "clockwise" reading order, we still walk the
            // server players clockwise starting from the player immediately
            // after the hero — that way the first server-side player after
            // hero ends up in slot 1 (right neighbor), the next on slot 6
            // (left neighbor), etc.
            _seatToSlot.Clear();
            int[] fillOrder = { 1, 6, 2, 5, 3, 4 };
            if (state.players != null)
            {
                int myServerSeat = -1;
                foreach (var p in state.players)
                    if (p.id == myId) { myServerSeat = p.seat; break; }

                if (myServerSeat >= 0)
                {
                    _seatToSlot[myServerSeat] = 0;
                    int orderIdx = 0;
                    foreach (var p in state.players)
                    {
                        if (p.seat == myServerSeat) continue;
                        int slot = orderIdx < fillOrder.Length
                            ? fillOrder[orderIdx]
                            : Mathf.Min(orderIdx + 1, seatSlots.Length - 1);
                        _seatToSlot[p.seat] = slot;
                        orderIdx++;
                    }
                }
                else
                {
                    // Spectator — no hero. Fill in a balanced order too so
                    // the lower-side seats are used first.
                    int orderIdx = 0;
                    foreach (var p in state.players)
                    {
                        int slot = orderIdx < fillOrder.Length
                            ? fillOrder[orderIdx]
                            : Mathf.Min(orderIdx + 1, seatSlots.Length - 1);
                        _seatToSlot[p.seat] = slot;
                        orderIdx++;
                    }
                }
            }

            // Hide all slots first; only show those a player is mapped to
            for (int i = 0; i < seatSlots.Length; i++)
                if (seatSlots[i]) seatSlots[i].gameObject.SetActive(false);

            // Populate slots
            if (state.players == null) return;
            var liveIds = new HashSet<string>();
            foreach (var player in state.players)
            {
                liveIds.Add(player.id);
                if (!_seatToSlot.TryGetValue(player.seat, out int slot)) continue;
                if (slot >= seatSlots.Length || seatSlots[slot] == null) continue;

                seatSlots[slot].gameObject.SetActive(true);

                bool isCurrentTurn = player.id == state.current_player_id;
                bool isMe          = player.id == myId;
                GamePhase ph = state.GetPhase();
                bool freeze = ph == GamePhase.SHOWDOWN || ph == GamePhase.HAND_COMPLETE;
                seatSlots[slot].SetPlayer(player, isCurrentTurn, isMe, freeze);
                ApplyCardSize(seatSlots[slot], isMe);

                // Pulse only the seat whose turn it is; clear pulse on others.
                if (PokerAnimator.Instance != null)
                {
                    var border = seatSlots[slot].statusBorder;
                    if (border) PokerAnimator.Instance.PulseSeat(border, isCurrentTurn);
                }

                // Chip fly animation: when bet grows, fly a chip from seat to pot
                int prev = _prevBet.TryGetValue(player.id, out var pv) ? pv : 0;
                int now  = player.current_bet;
                if (now > prev && PokerAnimator.Instance != null && potChipIcon != null)
                {
                    var seatRT = seatSlots[slot].GetComponent<RectTransform>();
                    var potRT  = potChipIcon.GetComponent<RectTransform>();
                    if (seatRT != null && potRT != null)
                    {
                        Vector2 from = seatRT.anchoredPosition;
                        Vector2 to   = potRT.anchoredPosition;
                        PokerAnimator.Instance.FlyChipToPot(from, to, transform, now - prev);
                    }
                }
                _prevBet[player.id] = now;

                // Deal animation: first time we see a player with hole cards, fly them in
                bool hasCards = player.hole_cards != null && player.hole_cards.Count == 2;
                if (hasCards && !_dealtPlayers.Contains(player.id) && PokerAnimator.Instance != null)
                {
                    AnimateDealToSeat(seatSlots[slot]);
                    _dealtPlayers.Add(player.id);
                }
            }
            // Remove players that left (so re-deal animates again next hand)
            var stale = new List<string>();
            foreach (var k in _prevBet.Keys) if (!liveIds.Contains(k)) stale.Add(k);
            foreach (var k in stale) _prevBet.Remove(k);
            stale.Clear();
            foreach (var k in _dealtPlayers) if (!liveIds.Contains(k)) stale.Add(k);
            foreach (var k in stale) _dealtPlayers.Remove(k);
        }

        // Deal pacing — tuned for "real dealer" cadence rather than a burst.
        // Each card to each seat fires in sequence, then the dealer comes back
        // around for the second card. Tweak via these constants.
        const float DEAL_PER_SEAT_GAP = 0.32f;   // gap between successive seats
        const float DEAL_ROUND_GAP    = 1.10f;   // pause before second card round
        const float DEAL_FLIGHT       = 0.55f;   // per-card flight time

        void AnimateDealToSeat(PlayerSeatUI seat)
        {
            if (seat == null || deckAnchor == null) return;
            var c1 = seat.card1?.GetComponent<RectTransform>();
            var c2 = seat.card2?.GetComponent<RectTransform>();
            Vector2 t1 = c1 != null ? c1.anchoredPosition : Vector2.zero;
            Vector2 t2 = c2 != null ? c2.anchoredPosition : Vector2.zero;
            int idx = _dealIndex++;
            float delay1 = idx * DEAL_PER_SEAT_GAP;
            float delay2 = delay1 + DEAL_ROUND_GAP;
            if (c1 != null) StartCoroutine(DealOne(c1, t1, delay1));
            if (c2 != null) StartCoroutine(DealOne(c2, t2, delay2));
        }

        IEnumerator DealOne(RectTransform card, Vector2 target, float delay)
        {
            // Convert deckAnchor world position to card's parent local space
            Vector3 deckWorld = deckAnchor.position;
            Vector2 startLocal = card.parent is RectTransform parentRT
                ? (Vector2)parentRT.InverseTransformPoint(deckWorld)
                : Vector2.zero;
            Vector2 origPos = card.anchoredPosition;
            // Hide off-screen until the delay elapses so the card doesn't sit
            // visibly at the deck before its turn.
            card.gameObject.SetActive(false);
            yield return new WaitForSeconds(delay);
            card.gameObject.SetActive(true);
            card.anchoredPosition = startLocal;
            card.localScale = Vector3.one * 0.45f;
            card.localEulerAngles = new Vector3(0, 0, UnityEngine.Random.Range(-160f, 160f));
            float dur = DEAL_FLIGHT, t = 0f;
            float spinStart = card.localEulerAngles.z;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                float p = Mathf.Clamp01(t);
                // Cubic-out ease — fast launch, soft landing
                float e = 1f - Mathf.Pow(1f - p, 3);
                Vector2 pos = Vector2.Lerp(startLocal, origPos, e);
                // Slight arc so cards visibly travel, not just blur straight in
                Vector2 mid = (startLocal + origPos) * 0.5f;
                pos += Vector2.up * Mathf.Sin(p * Mathf.PI) * 18f;
                pos.x += Mathf.Sin(p * Mathf.PI) * (mid.x > 0 ? -10f : 10f);
                card.anchoredPosition = pos;
                // Land with a tiny overshoot bounce
                float scale = Mathf.Lerp(0.45f, 1.06f, e);
                if (p > 0.85f) scale = Mathf.Lerp(1.06f, 1f, (p - 0.85f) / 0.15f);
                card.localScale = Vector3.one * scale;
                card.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(spinStart, 0f, e));
                yield return null;
            }
            card.anchoredPosition = origPos;
            card.localScale = Vector3.one;
            card.localEulerAngles = Vector3.zero;
        }

        // ── Showdown ──────────────────────────────────────────────────────

        void OnShowdown(ShowdownResult result)
        {
            if (result == null) return;

            PositionShowdownOverlays();
            RevealOpponentCards(result);

            // On-table winner banner removed by user request — celebration
            // lives in the event log only. Keep the LogWinner call so the
            // event log still shows winner name, hand name, and best cards.
            EventLogUI.Instance?.LogWinner(result, _state);

            // Celebrate winning seats
            if (_state?.players != null && result.winners != null)
            {
                foreach (var p in _state.players)
                {
                    if (!result.winners.Contains(p.id)) continue;
                    if (!_seatToSlot.TryGetValue(p.seat, out int slot)) continue;
                    if (slot >= seatSlots.Length) continue;
                    seatSlots[slot]?.ShowWinner();
                    var rt = seatSlots[slot]?.GetComponent<RectTransform>();
                    if (rt) PokerAnimator.Instance?.CelebrateSeat(rt);
                }
            }

            // Result panel is no longer used — hand auto-advances. Schedule the
            // next-hand request after a celebration window.
            if (handResultPanel) handResultPanel.SetActive(false);
            CancelInvoke(nameof(AutoNextHand));
            Invoke(nameof(AutoNextHand), 5.5f);
        }

        void AutoNextHand()
        {
            if (_state == null) return;
            if (_state.GetPhase() != GamePhase.HAND_COMPLETE
                && _state.GetPhase() != GamePhase.SHOWDOWN) return;
            if (winnerBanner)    winnerBanner.SetActive(false);
            if (handResultPanel) handResultPanel.SetActive(false);
            GameStateManager.Instance?.RequestNextHand();
        }

        // Per-seat card sizing. Hero & showdown reveals match the community
        // card aspect (5:7) and roughly the same width as a board card so the
        // table reads as a single coherent set of cards.
        static readonly Vector2 HeroCardSize     = new Vector2(88, 123);
        static readonly Vector2 OpponentSize     = new Vector2(58, 81);
        static readonly Vector2 ShowdownCardSize = new Vector2(88, 123);
        static readonly Vector2 HeroCardOffset   = new Vector2(50, 0);
        static readonly Vector2 OpponentOffset   = new Vector2(32, 0);
        static readonly Vector2 ShowdownOffset   = new Vector2(50, 0);

        void ApplyCardSize(PlayerSeatUI seat, bool isMe)
        {
            if (seat == null) return;
            GamePhase ph = _state != null ? _state.GetPhase() : GamePhase.WAITING;
            bool reveal = ph == GamePhase.SHOWDOWN || ph == GamePhase.HAND_COMPLETE;
            if (isMe)
                seat.SetCardSize(HeroCardSize, HeroCardOffset);
            else if (reveal)
                seat.SetCardSize(ShowdownCardSize, ShowdownOffset);
            else
                seat.SetCardSize(OpponentSize, OpponentOffset);
        }

        // At showdown, every non-folded player's hole cards arrive in result.reveals.
        // Map them to seat slots and flip the cards face-up so the table can read them.
        void RevealOpponentCards(ShowdownResult result)
        {
            if (result?.reveals == null || _state?.players == null) return;
            string myId = GameStateManager.Instance.MyPlayerId;
            foreach (var rev in result.reveals)
            {
                if (rev == null || string.IsNullOrEmpty(rev.player_id)) continue;
                if (rev.player_id == myId) continue; // hero already shows their own cards
                if (rev.cards == null || rev.cards.Count < 2) continue;
                Player player = null;
                foreach (var p in _state.players) if (p.id == rev.player_id) { player = p; break; }
                if (player == null) continue;
                if (!_seatToSlot.TryGetValue(player.seat, out int slot)) continue;
                if (slot >= seatSlots.Length || seatSlots[slot] == null) continue;
                var seat = seatSlots[slot];

                // Bump opponent cards up to showdown size before flipping.
                seat.SetCardSize(ShowdownCardSize, ShowdownOffset);
                Card c1 = ParseCard(rev.cards[0]);
                Card c2 = ParseCard(rev.cards[1]);
                if (seat.card1) { seat.card1.gameObject.SetActive(true); seat.card1.SetCard(c1, animated: true); }
                if (seat.card2) { seat.card2.gameObject.SetActive(true); seat.card2.SetCard(c2, animated: true); }
            }
        }

        static Card ParseCard(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 2) return null;
            string suitCh = code.Substring(code.Length - 1).ToLower();
            string rank = code.Substring(0, code.Length - 1);
            // Backend sends "10h" or "Th" — accept both
            if (rank == "10") rank = "T";
            return new Card { rank = rank, suit = suitCh, code = code };
        }

        // Position the winner banner above the table center and the result panel
        // (with NEXT HAND) below the community cards so neither hides the board.
        void PositionShowdownOverlays()
        {
            if (winnerBanner)
            {
                var rt = winnerBanner.GetComponent<RectTransform>();
                if (rt != null)
                {
                    // Pin banner under the top edge so it never covers the
                    // community cards or seats.
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0, -28);
                    rt.sizeDelta = new Vector2(820, 150);
                }
            }
            if (handResultPanel)
            {
                var rt = handResultPanel.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(0, -240);
                    rt.sizeDelta = new Vector2(360, 90);
                }
            }
        }

        // ── Events ────────────────────────────────────────────────────────

        void OnHandStarted()
        {
            ResetCommunityCards();
            _prevBet.Clear();
            _dealtPlayers.Clear();
            _dealIndex = 0;
            CancelInvoke(nameof(AutoNextHand));
            if (winnerBanner)    winnerBanner.SetActive(false);
            if (handResultPanel) handResultPanel.SetActive(false);

            // Show community card slots (face-down) when hand starts
            for (int i = 0; i < communityCards.Length; i++)
                if (communityCards[i]) communityCards[i].gameObject.SetActive(false);
        }

        void OnError(string msg) => SetStatus($"⚠ {msg}");

        // ── Helpers ───────────────────────────────────────────────────────

        static string FormatChips(int v)
        {
            if (v >= 1_000_000) return $"${v / 1_000_000.0:F1}M";
            if (v >= 1_000)     return $"${v / 1_000.0:F1}K";
            return $"${v}";
        }
    }
}
