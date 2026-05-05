using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Poker.Models;

namespace Poker.UI
{
    public class PlayerSeatUI : MonoBehaviour
    {
        [Header("Panels")]
        public GameObject seatPanel;
        public GameObject emptyPanel;
        public CanvasGroup seatCanvasGroup;

        [Header("Avatar")]
        public Image      avatarCircle;
        public Image      avatarBorder;
        public Image      statusBorder;

        [Header("Info")]
        public TextMeshProUGUI playerNameText;
        public TextMeshProUGUI stackText;
        public TextMeshProUGUI betText;
        public GameObject      betChip;

        [Header("Badges")]
        public GameObject dealerBadge;
        public GameObject sbBadge;
        public GameObject bbBadge;

        [Header("Hole Cards")]
        public CardUI card1;
        public CardUI card2;

        [Header("Action Bubble")]
        public GameObject      actionBubble;
        public TextMeshProUGUI actionText;
        public Image           actionBubbleBg;

        [Header("Winner")]
        public GameObject winnerGlow;
        public GameObject winnerCrown;

        [Header("Colors")]
        public Color activeColor    = new Color(0.30f, 0.85f, 0.55f);
        public Color foldedColor    = new Color(0.4f,  0.4f,  0.45f, 0.5f);
        public Color allInColor     = new Color(0.96f, 0.78f, 0.26f);
        public Color turnColor      = new Color(0.96f, 0.78f, 0.26f);
        public Color heroColor      = new Color(0.79f, 0.48f, 1f);
        public Color foldActionCol  = new Color(0.85f, 0.3f,  0.2f);
        public Color callActionCol  = new Color(0.30f, 0.88f, 0.60f);
        public Color raiseActionCol = new Color(0.49f, 0.25f, 1f);
        public Color checkActionCol = new Color(0.39f, 0.69f, 0.94f);

        public int seatIndex { get; set; }

        // Action time before auto-fold; matches the server / BettingPanelUI
        // default. Used to drive the radial countdown ring on the active
        // player's avatar.
        public float turnTimeSeconds = 30f;

        private bool _visualsApplied;
        private string _avatarKey;
        private TextMeshProUGUI _avatarIconTmp;
        private Image _balanceChipIcon;
        private Image _betCoinIcon;
        // Yellow radial countdown ring shown on whoever's turn it is.
        private Image _countdownRing;
        private bool _isTurn;
        private float _turnTimer;

        void OnEnable() { ApplyVisuals(); }

        void Update()
        {
            if (!_isTurn || _countdownRing == null) return;
            _turnTimer -= Time.deltaTime;
            if (_turnTimer < 0f) _turnTimer = 0f;
            float t = Mathf.Clamp01(_turnTimer / Mathf.Max(0.0001f, turnTimeSeconds));
            _countdownRing.fillAmount = t;
            _countdownRing.color = Color.Lerp(
                new Color(1f, 0.36f, 0.20f, 1f),
                new Color(1f, 0.84f, 0.18f, 1f),
                t);
        }

        void ApplyVisuals()
        {
            if (_visualsApplied) return;
            _visualsApplied = true;

            // Build the entire USS-spec slot layout: avatar disc + emoji icon,
            // info card (name + gold-balance row), bet pill below. We construct
            // missing pieces so the slot always renders correctly regardless of
            // prefab wiring.
            BuildLayout();
        }

        void BuildLayout()
        {
            // SeatPanel: parent for everything except hole cards
            if (seatPanel == null)
            {
                var spT = transform.Find("SeatPanel");
                if (spT == null)
                {
                    var go = new GameObject("SeatPanel", typeof(RectTransform));
                    go.transform.SetParent(transform, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(180, 220);
                    seatPanel = go;
                }
                else seatPanel = spT.gameObject;
            }
            if (seatCanvasGroup == null)
                seatCanvasGroup = seatPanel.GetComponent<CanvasGroup>()
                                 ?? seatPanel.AddComponent<CanvasGroup>();

            var seatRT = (RectTransform)seatPanel.transform;
            seatRT.sizeDelta = new Vector2(180, 220);

            // Strip any prefab-baked Image on SeatPanel so the green/teal
            // background can't bleed through. The procedural pieces below
            // provide all visual surfaces we want.
            var seatBg = seatPanel.GetComponent<Image>();
            if (seatBg != null) seatBg.enabled = false;

            // Aggressively hide prefab visual children that conflict with the
            // procedural USS layout. These are full-rect or large stretched
            // Images/TMPs from the original prefab — leaving them visible
            // produces the giant green rectangles + duplicate avatar emoji
            // we saw on screen. Game-logic GameObjects (DealerBadge, SBBadge,
            // BBBadge, ActionBubble, WinnerGlow, WinnerCrown, HoleCards) are
            // intentionally left intact.
            string[] prefabConflicts = {
                "StatusBorder", "SeatBg", "AvatarBorder", "InfoBg", "BetChip"
            };
            foreach (var n in prefabConflicts)
            {
                var ch = seatPanel.transform.Find(n);
                if (ch != null) ch.gameObject.SetActive(false);
            }
            // The prefab field 'betChip' may still point to the now-disabled
            // "BetChip" GameObject. Drop the reference so the BetPill block
            // below creates a fresh procedural pill instead of trying to
            // reuse the disabled prefab one.
            if (betChip != null && betChip.name == "BetChip") betChip = null;

            // ── HoleCards: keep inside the 180×220 seat panel ──────────────
            // The original prefab anchored hole cards far above the seat,
            // which made opponent cards (e.g. Carla at the top of the
            // canvas) overflow off-screen. Pin HoleCards to a known slot
            // within the panel; PokerTableUI.SetCardSize() then sizes the
            // children. Hero cards are special-cased: SetCardSize keeps the
            // hero offset, but we still want the HoleCards container near
            // the bottom of the seat so they read as belonging to the
            // hero rather than floating away.
            var hcT = seatPanel.transform.Find("HoleCards") as RectTransform;
            if (hcT != null)
            {
                hcT.anchorMin = hcT.anchorMax = new Vector2(0.5f, 0.5f);
                hcT.pivot = new Vector2(0.5f, 0.5f);
                // Position hole cards 5px above the avatar disc. The avatar
                // holder is at y=60 with height 96, so its top edge is at
                // y=108. Card box is 72×50 with pivot at center (y=25 from
                // bottom), so center y = 108 + 5 + 25 = 138.
                hcT.anchoredPosition = new Vector2(0, 138);
                hcT.sizeDelta = new Vector2(72, 50);
            }
            // Also size the root PlayerSeat rect to match so emptyPanel +
            // seat slot picker don't render outside the visible seat.
            var rootRT = transform as RectTransform;
            if (rootRT != null) rootRT.sizeDelta = new Vector2(180, 220);

            // Hide emptyPanel by default — it'll be turned on by SetEmpty().
            if (emptyPanel != null) emptyPanel.SetActive(false);

            // ── Avatar holder (top of seat) ────────────────────────────────
            var avatarHolder = EnsureChild(seatPanel.transform, "AvatarHolder",
                new Vector2(0, 60), new Vector2(96, 96));

            // Avatar disc — slightly inset so the status ring shows around it.
            avatarCircle = EnsureCircleImage(avatarHolder, "AvatarDisc", 0.88f);
            avatarCircle.sprite = SpriteFactory.Circle();
            avatarCircle.type = Image.Type.Simple;
            avatarCircle.preserveAspect = true;
            avatarCircle.color = new Color(0.16f, 0.12f, 0.30f);

            // Status ring (turn/fold/all-in tint, pulses on turn) — sits at
            // the holder's outer edge, drawn on top so its color reads clearly.
            statusBorder = EnsureRing(avatarHolder, "StatusRing", 1f,
                new Color(0.30f, 0.85f, 0.55f, 0.95f));
            avatarBorder = statusBorder; // alias; legacy callers tint this

            // ── Countdown ring (yellow, radial fill) ───────────────────────
            // Only visible while it's this player's turn. Drains from full
            // (1.0) to empty (0.0) over `turnTimeSeconds`, providing the
            // modern "circular timer" affordance the design calls for.
            // Slightly larger than the status ring so both are visible —
            // the status ring stays the colored "active player" tint, the
            // countdown ring is always saturated yellow on top of it.
            _countdownRing = EnsureCircleImage(avatarHolder, "CountdownRing", 1.10f);
            // Use a thicker ring sprite so the radial fill reads at a glance.
            _countdownRing.sprite = SpriteFactory.Ring();
            _countdownRing.color = new Color(1f, 0.84f, 0.18f, 1f); // amber-yellow
            _countdownRing.type = Image.Type.Filled;
            _countdownRing.fillMethod = Image.FillMethod.Radial360;
            _countdownRing.fillOrigin = (int)Image.Origin360.Top;
            _countdownRing.fillClockwise = true;
            _countdownRing.fillAmount = 1f;
            _countdownRing.raycastTarget = false;
            _countdownRing.gameObject.SetActive(false);

            // Emoji icon overlay (TMP text)
            var iconT = avatarHolder.Find("AvatarIcon");
            if (iconT == null)
            {
                var iconGO = new GameObject("AvatarIcon", typeof(RectTransform));
                iconGO.transform.SetParent(avatarHolder, false);
                iconT = iconGO.transform;
                var irt = (RectTransform)iconT;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = Vector2.zero;
                irt.sizeDelta = new Vector2(80, 80);
            }
            _avatarIconTmp = iconT.GetComponent<TextMeshProUGUI>();
            if (_avatarIconTmp == null) _avatarIconTmp = iconT.gameObject.AddComponent<TextMeshProUGUI>();
            _avatarIconTmp.fontSize = 44;
            _avatarIconTmp.fontStyle = FontStyles.Bold;
            _avatarIconTmp.alignment = TextAlignmentOptions.Center;
            _avatarIconTmp.enableWordWrapping = false;
            _avatarIconTmp.raycastTarget = false;
            _avatarIconTmp.color = Color.white;
            _avatarIconTmp.text = "?";

            // ── Info card (name + balance row) ─────────────────────────────
            var infoCard = EnsureChild(seatPanel.transform, "InfoCard",
                new Vector2(0, -22), new Vector2(160, 52));
            var infoBg = infoCard.GetComponent<Image>();
            if (infoBg == null) infoBg = infoCard.gameObject.AddComponent<Image>();
            infoBg.sprite = SpriteFactory.Pill();
            infoBg.type = Image.Type.Sliced;
            infoBg.color = new Color(0.024f, 0.031f, 0.078f, 0.88f); // surface dark
            infoBg.raycastTarget = false;

            // Subtle white border
            var infoBorder = EnsureRect(infoCard, "Border");
            var ibImg = infoBorder.GetComponent<Image>();
            if (ibImg == null) ibImg = infoBorder.gameObject.AddComponent<Image>();
            ibImg.sprite = SpriteFactory.Pill();
            ibImg.type = Image.Type.Sliced;
            ibImg.color = new Color(1f, 1f, 1f, 0.10f);
            ibImg.raycastTarget = false;
            // Border slightly outside fill via separate sprite — keep both stacked

            // Name (top of info card). If a prefab-wired TMP lives outside
            // our InfoCard, hide it so two name labels don't render.
            if (playerNameText != null && playerNameText.transform.parent != infoCard)
            {
                playerNameText.gameObject.SetActive(false);
                playerNameText = null;
            }
            if (playerNameText == null)
                playerNameText = EnsureTMP(infoCard, "Name", new Vector2(0, 11),
                    new Vector2(150, 22), 18, FontStyles.Bold,
                    new Color(0.866f, 0.878f, 0.961f));

            // Balance row: gold chip + amount, centered horizontally
            var balanceRow = EnsureChild(infoCard, "BalanceRow",
                new Vector2(0, -10), new Vector2(120, 22));

            _balanceChipIcon = EnsureCircleImage(balanceRow, "Chip", 1f);
            var bcRT = (RectTransform)_balanceChipIcon.transform;
            bcRT.anchorMin = bcRT.anchorMax = new Vector2(0f, 0.5f);
            bcRT.pivot = new Vector2(0f, 0.5f);
            bcRT.anchoredPosition = new Vector2(8, 0);
            bcRT.sizeDelta = new Vector2(16, 16);
            _balanceChipIcon.color = new Color(0.784f, 0.604f, 0.125f); // gold

            if (stackText != null && stackText.transform.parent != balanceRow)
            {
                stackText.gameObject.SetActive(false);
                stackText = null;
            }
            if (stackText == null)
                stackText = EnsureTMP(balanceRow, "Balance", Vector2.zero,
                    new Vector2(80, 22), 16, FontStyles.Bold,
                    new Color(0.961f, 0.784f, 0.259f));
            var stRT = (RectTransform)stackText.transform;
            stRT.anchorMin = stRT.anchorMax = new Vector2(0f, 0.5f);
            stRT.pivot = new Vector2(0f, 0.5f);
            stRT.anchoredPosition = new Vector2(28, 0);
            stRT.sizeDelta = new Vector2(86, 22);
            stackText.alignment = TextAlignmentOptions.Left;

            // ── Bet pill (below info card, only visible when betting) ──────
            // If a prefab-wired betChip exists outside seatPanel (e.g. an
            // orange chip icon at a different anchor), hide it so the new
            // pill below is the only bet visualization.
            if (betChip != null && betChip.transform.parent != seatPanel.transform)
            {
                betChip.SetActive(false);
                betChip = null;
            }
            if (betChip == null || betChip.name != "BetPill")
            {
                var existing = seatPanel.transform.Find("BetPill");
                if (existing != null) betChip = existing.gameObject;
                else
                {
                    var bp = new GameObject("BetPill",
                        typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    bp.transform.SetParent(seatPanel.transform, false);
                    var brt = (RectTransform)bp.transform;
                    brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                    brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.anchoredPosition = new Vector2(0, -88);
                    brt.sizeDelta = new Vector2(100, 28);
                    betChip = bp;
                }
            }
            // Ensure the bet pill rect has the right size/position even when reused.
            {
                var brt = (RectTransform)betChip.transform;
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.anchoredPosition = new Vector2(0, -62);
                brt.sizeDelta = new Vector2(100, 30);
            }
            var betBg = betChip.GetComponent<Image>();
            if (betBg == null) betBg = betChip.AddComponent<Image>();
            betBg.sprite = SpriteFactory.Pill();
            betBg.type = Image.Type.Sliced;
            betBg.color = new Color(0.075f, 0.047f, 0.188f, 1f); // #130C30
            betBg.raycastTarget = false;

            _betCoinIcon = EnsureCircleImage(betChip.transform, "Coin", 1f);
            var coinRT = (RectTransform)_betCoinIcon.transform;
            coinRT.anchorMin = coinRT.anchorMax = new Vector2(0f, 0.5f);
            coinRT.pivot = new Vector2(0f, 0.5f);
            coinRT.anchoredPosition = new Vector2(6, 0);
            coinRT.sizeDelta = new Vector2(20, 20);
            _betCoinIcon.color = new Color(0.533f, 0.188f, 0.933f); // purple coin

            if (betText == null)
                betText = EnsureTMP(betChip.transform, "Amount", Vector2.zero,
                    new Vector2(60, 22), 15, FontStyles.Bold,
                    new Color(0.769f, 0.667f, 1f)); // text purple light
            var btRT = (RectTransform)betText.transform;
            btRT.anchorMin = btRT.anchorMax = new Vector2(0f, 0.5f);
            btRT.pivot = new Vector2(0f, 0.5f);
            btRT.anchoredPosition = new Vector2(30, 0);
            btRT.sizeDelta = new Vector2(64, 22);
            betText.alignment = TextAlignmentOptions.Left;

            // ── Dealer / Small Blind / Big Blind badges ───────────────────
            // Auto-built so they sit DIRECTLY beneath the player amount,
            // ABOVE the bet pill, regardless of any prefab-baked position.
            // The three badges share a single anchor — only one can be
            // active at a time (RefreshFromState toggles the right one).
            // Purple gradient per design request.
            //
            // If the prefab-wired references point at GameObjects that
            // aren't children of seatPanel (e.g. floating yellow "D" tokens
            // anchored in the scene), DROP them so we build clean local
            // versions instead.
            if (dealerBadge != null && dealerBadge.transform.parent != seatPanel.transform)
            { dealerBadge.SetActive(false); dealerBadge = null; }
            if (sbBadge != null && sbBadge.transform.parent != seatPanel.transform)
            { sbBadge.SetActive(false); sbBadge = null; }
            if (bbBadge != null && bbBadge.transform.parent != seatPanel.transform)
            { bbBadge.SetActive(false); bbBadge = null; }

            const float BADGE_Y = -58f;       // between info card (bottom -48) and bet pill (-88)
            Vector2 badgeSize = new Vector2(28f, 22f);
            Color badgeBgColor = new Color(0.392f, 0.255f, 0.722f, 1f);  // #6441B8 purple
            Color badgeRimColor = new Color(0.671f, 0.553f, 0.961f, 1f); // #AB8DF5 lighter rim
            Color badgeTextColor = new Color(1f, 0.95f, 1f, 1f);

            dealerBadge = EnsureBadge(seatPanel.transform, "DealerBadge", "D",
                BADGE_Y, badgeSize, badgeBgColor, badgeRimColor, badgeTextColor);
            sbBadge = EnsureBadge(seatPanel.transform, "SBBadge", "SB",
                BADGE_Y, new Vector2(34f, 22f), badgeBgColor, badgeRimColor, badgeTextColor);
            bbBadge = EnsureBadge(seatPanel.transform, "BBBadge", "BB",
                BADGE_Y, new Vector2(34f, 22f), badgeBgColor, badgeRimColor, badgeTextColor);

            // Start hidden — RefreshFromState will toggle the right one on.
            dealerBadge.SetActive(false);
            sbBadge.SetActive(false);
            bbBadge.SetActive(false);
        }

        // Build (or reuse) a small purple pill badge with a centered label.
        // Used for the D / SB / BB tokens beneath the seat's name plate.
        static GameObject EnsureBadge(Transform parent, string name, string label,
                                      float yPos, Vector2 size, Color bg, Color rim, Color text)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null)
            {
                go = new GameObject(name,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
            }
            else
            {
                go = t.gameObject;
            }
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, yPos);
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            img.sprite = SpriteFactory.Pill();
            img.type = Image.Type.Sliced;
            img.color = bg;
            img.raycastTarget = false;

            // Lighter rim for contrast against the felt.
            var rimT = go.transform.Find("Rim");
            GameObject rimGO;
            if (rimT == null)
            {
                rimGO = new GameObject("Rim",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                rimGO.transform.SetParent(go.transform, false);
            }
            else rimGO = rimT.gameObject;
            var rrt = (RectTransform)rimGO.transform;
            rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
            rrt.offsetMin = rrt.offsetMax = Vector2.zero;
            var rimImg = rimGO.GetComponent<Image>();
            if (rimImg == null) rimImg = rimGO.AddComponent<Image>();
            rimImg.sprite = SpriteFactory.PillRing();
            rimImg.type = Image.Type.Sliced;
            rimImg.color = new Color(rim.r, rim.g, rim.b, 0.55f);
            rimImg.raycastTarget = false;

            // Label
            var labT = go.transform.Find("Label");
            TextMeshProUGUI tmp;
            if (labT == null)
            {
                var labGO = new GameObject("Label", typeof(RectTransform));
                labGO.transform.SetParent(go.transform, false);
                tmp = labGO.AddComponent<TextMeshProUGUI>();
            }
            else
            {
                tmp = labT.GetComponent<TextMeshProUGUI>();
                if (tmp == null) tmp = labT.gameObject.AddComponent<TextMeshProUGUI>();
            }
            var trt = (RectTransform)tmp.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            tmp.text = label;
            tmp.fontSize = 13;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = text;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            return go;
        }

        // ── Layout helpers ────────────────────────────────────────────────

        static Transform EnsureChild(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                t = go.transform;
            }
            var rt = (RectTransform)t;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return t;
        }

        static Transform EnsureRect(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                t = go.transform;
                var rt = (RectTransform)t;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            return t;
        }

        static Image EnsureCircleImage(Transform parent, string name, float scale)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null)
            {
                go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one * scale;
            }
            else go = t.gameObject;
            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            img.sprite = SpriteFactory.Circle();
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        static Image EnsureRing(Transform parent, string name, float scale, Color col)
        {
            var img = EnsureCircleImage(parent, name, scale);
            img.sprite = SpriteFactory.RingThin();
            img.color = col;
            return img;
        }

        static TextMeshProUGUI EnsureTMP(Transform parent, string name,
            Vector2 pos, Vector2 size, float fontSize, FontStyles style, Color color)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null)
            {
                go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
            }
            else go = t.gameObject;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp == null) tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            return tmp;
        }

        public void SetEmpty()
        {
            if (seatPanel)  seatPanel.SetActive(false);
            if (emptyPanel) emptyPanel.SetActive(true);
        }

        public void SetPlayer(Player player, bool isTurn, bool isMe, bool freezeCards = false)
        {
            if (seatPanel)  seatPanel.SetActive(true);
            if (emptyPanel) emptyPanel.SetActive(false);

            PlayerStatus pstat = player.GetStatus();
            bool folded = pstat == PlayerStatus.FOLDED;
            bool allIn  = pstat == PlayerStatus.ALL_IN;

            if (playerNameText) playerNameText.text = player.name;
            if (stackText)      stackText.text      = FormatChips(player.stack);

            // Avatar — flat disc tinted per-player + initial overlay. We use
            // a player initial rather than emoji because TMP has no emoji
            // font fallback by default and emoji codepoints render as missing
            // glyph squares.
            string key = string.IsNullOrEmpty(player.id) ? player.name : player.id;
            if (key != _avatarKey)
            {
                _avatarKey = key;
                if (avatarCircle != null)
                {
                    avatarCircle.sprite = SpriteFactory.Circle();
                    avatarCircle.color = SpriteFactory.AvatarBgFor(key);
                }
                if (_avatarIconTmp != null)
                {
                    string n = !string.IsNullOrEmpty(player.name) ? player.name : "?";
                    _avatarIconTmp.text = char.ToUpper(n[0]).ToString();
                }
            }

            // Bet pill — show only when player has wagered this round.
            bool hasBet = player.current_bet > 0;
            if (betChip)  betChip.SetActive(hasBet);
            if (betText && hasBet) betText.text = FormatChips(player.current_bet);

            // Badges (only if wired)
            if (dealerBadge) dealerBadge.SetActive(player.is_dealer);
            if (sbBadge)     sbBadge.SetActive(player.is_small_blind && !player.is_dealer);
            if (bbBadge)     bbBadge.SetActive(player.is_big_blind  && !player.is_dealer);

            // Border tint by state
            Color borderCol = isTurn ? turnColor
                            : isMe   ? heroColor
                            : allIn  ? allInColor
                            : activeColor;
            if (folded) borderCol = foldedColor;
            if (statusBorder) statusBorder.color = borderCol;

            // Countdown ring: drains while it's this player's turn.
            if (_isTurn != isTurn)
            {
                _isTurn = isTurn;
                if (isTurn) _turnTimer = turnTimeSeconds;
            }
            if (_countdownRing != null)
            {
                _countdownRing.gameObject.SetActive(isTurn && !folded);
                if (isTurn && !folded)
                {
                    float t = Mathf.Clamp01(_turnTimer / Mathf.Max(0.0001f, turnTimeSeconds));
                    _countdownRing.fillAmount = t;
                    // Tilt color from yellow → red as time runs low for
                    // an extra urgency cue.
                    _countdownRing.color = Color.Lerp(
                        new Color(1f, 0.36f, 0.20f, 1f),   // red at 0%
                        new Color(1f, 0.84f, 0.18f, 1f),   // yellow at 100%
                        t);
                }
            }

            if (seatCanvasGroup)
                seatCanvasGroup.alpha = folded ? 0.45f : 1f;

            if (!freezeCards) UpdateCards(player);
        }

        void UpdateCards(Player player)
        {
            bool folded   = player.GetStatus() == PlayerStatus.FOLDED;
            bool hasCards = player.hole_cards != null && player.hole_cards.Count == 2;

            if (card1) card1.gameObject.SetActive(!folded);
            if (card2) card2.gameObject.SetActive(!folded);
            if (folded) return;

            if (hasCards)
            {
                card1?.SetCard(player.hole_cards[0]);
                card2?.SetCard(player.hole_cards[1]);
            }
            else
            {
                card1?.ShowFaceDown();
                card2?.ShowFaceDown();
            }
        }

        public void ShowAction(string actionLabel)
        {
            if (actionBubble == null) return;
            actionBubble.SetActive(true);
            if (actionText) actionText.text = actionLabel.ToUpper();

            if (actionBubbleBg)
                actionBubbleBg.color = actionLabel.ToLower() switch
                {
                    "fold"  => foldActionCol,
                    "call"  => callActionCol,
                    "raise" => raiseActionCol,
                    "check" => checkActionCol,
                    "all in"=> raiseActionCol,
                    _       => new Color(0.3f, 0.3f, 0.35f)
                };

            CancelInvoke(nameof(HideAction));
            Invoke(nameof(HideAction), 2f);
        }

        void HideAction() { if (actionBubble) actionBubble.SetActive(false); }

        public void SetCardSize(Vector2 size, Vector2? offsetFromCenter = null)
        {
            ResizeCard(card1, size);
            ResizeCard(card2, size);
            if (offsetFromCenter.HasValue)
            {
                var off = offsetFromCenter.Value;
                if (card1)
                {
                    var rt = card1.GetComponent<RectTransform>();
                    if (rt) rt.anchoredPosition = new Vector2(-off.x, off.y);
                }
                if (card2)
                {
                    var rt = card2.GetComponent<RectTransform>();
                    if (rt) rt.anchoredPosition = new Vector2(off.x, off.y);
                }
            }
        }

        static void ResizeCard(CardUI card, Vector2 size)
        {
            if (card == null) return;
            var rt = card.GetComponent<RectTransform>();
            if (rt)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot     = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = size;
            }
            card.Reflow();
        }

        public void ShowWinner()
        {
            if (winnerGlow)  winnerGlow.SetActive(true);
            if (winnerCrown) winnerCrown.SetActive(true);
            CancelInvoke(nameof(HideWinner));
            Invoke(nameof(HideWinner), 5f);
        }

        void HideWinner()
        {
            if (winnerGlow)  winnerGlow.SetActive(false);
            if (winnerCrown) winnerCrown.SetActive(false);
        }

        static string FormatChips(int v)
        {
            if (v >= 1_000_000) return $"${v/1_000_000.0:F1}M";
            if (v >= 1_000)     return $"${v/1_000.0:F1}K";
            return $"${v}";
        }
    }
}
