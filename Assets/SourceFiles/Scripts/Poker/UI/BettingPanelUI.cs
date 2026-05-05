using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Poker.Models;
using Poker.Network;

namespace Poker.UI
{
    public class BettingPanelUI : MonoBehaviour
    {
        [Header("Action Buttons")]
        public Button foldButton;
        public Button checkCallButton;
        public Button raiseButton;
        public Button allInButton;

        [Header("Button Labels")]
        public TextMeshProUGUI checkCallLabel;
        public TextMeshProUGUI raiseLabel;

        [Header("Raise Slider")]
        public GameObject raisePanel;
        public Slider raiseSlider;
        public TextMeshProUGUI raiseAmountText;
        public Button confirmRaiseButton;
        public Button cancelRaiseButton;

        [Header("Preset Raise Buttons")]
        public Button halfPotButton;
        public Button potButton;
        public Button twoxButton;

        [Header("Timer")]
        public Image timerBar;
        public float actionTimeSeconds = 30f;

        private GameState _state;
        private int _raiseAmount;
        private float _timerValue;
        private bool _timerActive;

        void Awake()
        {
            EnforceLayout();
            ApplyButtonVisuals();
        }

        // Modern, high-contrast palette per latest UX feedback:
        //   FOLD  → red (danger / quit)
        //   CHECK → green (safe pass)
        //   CALL  → green (slightly deeper, matches CHECK family)
        //   RAISE → green (deepest, "go aggressive")
        //   ALLIN → orange/amber (hot accent, risk)
        // All text is pure white for maximum legibility on saturated fills.
        // Borders are a brighter shade of the same hue so the rim reads as
        // glow rather than as a separate color.
        static readonly Color USS_FOLD_BG     = Hex(0xC8, 0x2E, 0x42); // crimson
        static readonly Color USS_FOLD_BORDER = Hex(0xFF, 0x6A, 0x82);
        static readonly Color USS_FOLD_TEXT   = Color.white;
        static readonly Color USS_CHECK_BG     = Hex(0x1F, 0xA8, 0x6E); // emerald
        static readonly Color USS_CHECK_BORDER = Hex(0x5C, 0xE6, 0xA8);
        static readonly Color USS_CHECK_TEXT   = Color.white;
        static readonly Color USS_CALL_BG     = Hex(0x16, 0x9C, 0x60); // forest emerald
        static readonly Color USS_CALL_BORDER = Hex(0x4E, 0xE0, 0x9C);
        static readonly Color USS_CALL_TEXT   = Color.white;
        static readonly Color USS_RAISE_BG     = Hex(0x0E, 0x88, 0x52); // deep green
        static readonly Color USS_RAISE_BORDER = Hex(0x40, 0xD0, 0x90);
        static readonly Color USS_RAISE_TEXT   = Color.white;
        // ALL IN gets a hot accent so it stands out as the riskiest move.
        static readonly Color USS_ALLIN_BG     = Hex(0xE8, 0x82, 0x2C); // amber
        static readonly Color USS_ALLIN_BORDER = Hex(0xFF, 0xC0, 0x70);
        static readonly Color USS_ALLIN_TEXT   = Color.white;

        static Color Hex(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        void ApplyButtonVisuals()
        {
            StyleButton(foldButton,      USS_FOLD_BG,  USS_FOLD_BORDER,  USS_FOLD_TEXT,  "FOLD");
            // checkCallButton text label flips between CHECK and CALL,
            // and RefreshButtons() swaps the bg/border/text accordingly.
            StyleButton(checkCallButton, USS_CHECK_BG, USS_CHECK_BORDER, USS_CHECK_TEXT, null);
            StyleButton(raiseButton,     USS_RAISE_BG, USS_RAISE_BORDER, USS_RAISE_TEXT, null);
            StyleButton(allInButton,     USS_ALLIN_BG, USS_ALLIN_BORDER, USS_ALLIN_TEXT, "ALL IN");
        }

        static void StyleButton(Button b, Color fill, Color border, Color text, string label)
        {
            if (b == null) return;

            // Disable every prefab UI effect on the button (Outline, Shadow,
            // gradient overlays via BaseMeshEffect subclasses). These were
            // producing the bright colored outlines (red around FOLD, teal
            // around CALL, etc.) seen in the live screenshot.
            var effects = b.GetComponentsInChildren<UnityEngine.UI.BaseMeshEffect>(true);
            foreach (var e in effects) if (e != null) e.enabled = false;

            // Strip any prefab-baked child Image whose name suggests it's a
            // background/fill/outline overlay. We'll rebuild the border as
            // a controlled "BtnBorder" child below.
            for (int i = b.transform.childCount - 1; i >= 0; i--)
            {
                var ch = b.transform.GetChild(i);
                string nm = ch.name;
                if (nm == "BtnBg" || nm == "Background" || nm == "ButtonBg"
                    || nm == "Outline" || nm == "Border" || nm == "Glow")
                {
                    ch.gameObject.SetActive(false);
                }
            }

            var img = b.GetComponent<Image>();
            if (img == null) img = b.gameObject.AddComponent<Image>();
            img.sprite = SpriteFactory.Pill();
            img.type = Image.Type.Sliced;
            img.color = fill;
            // Disable Selectable's color tint pipeline entirely so the
            // Inspector-set bright normalColor (red/teal/green/purple in
            // the prefab) can never override our Image.color via
            // DoStateTransition. With Transition.None, nothing pokes
            // canvasRenderer.color after we set it, and Image.color is
            // exactly what paints. We re-pin canvasRenderer to white so
            // any leftover state from the previous run gets cleared too.
            b.transition = Selectable.Transition.None;
            b.targetGraphic = img;
            img.canvasRenderer.SetColor(Color.white);

            // Also clear canvasRenderer on every CHILD Graphic so a
            // bright-tinted child Image (border, label background) from
            // an older prefab can't bleed through.
            var childGraphics = b.GetComponentsInChildren<Graphic>(true);
            foreach (var g in childGraphics)
            {
                if (g == null || g == img) continue;
                g.canvasRenderer.SetColor(Color.white);
            }

            // Pill border overlay — uses a ring sprite so it draws ONLY
            // the rim and never covers the text label below. (Previously
            // we used a solid Pill at full alpha which made the labels
            // invisible.)
            var bT = b.transform.Find("BtnBorder");
            RectTransform brt;
            if (bT == null)
            {
                var go = new GameObject("BtnBorder",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(b.transform, false);
                brt = (RectTransform)go.transform;
                brt.anchorMin = Vector2.zero;
                brt.anchorMax = Vector2.one;
                brt.offsetMin = brt.offsetMax = Vector2.zero;
            }
            else brt = (RectTransform)bT;
            var bImg = brt.GetComponent<Image>();
            if (bImg == null) bImg = brt.gameObject.AddComponent<Image>();
            bImg.sprite = SpriteFactory.PillRing();
            bImg.type = Image.Type.Sliced;
            bImg.color = new Color(border.r, border.g, border.b, 0.9f);
            bImg.raycastTarget = false;

            var tmp = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                tmp.color = text;
                tmp.fontSize = 22;
                tmp.fontStyle = FontStyles.Bold;
                tmp.characterSpacing = 6f;
                tmp.alignment = TextAlignmentOptions.Center;
                if (!string.IsNullOrEmpty(label)) tmp.text = label;
                // Force the label to render on top of the border + fill so
                // it's never hidden by sibling order from the prefab.
                tmp.transform.SetAsLastSibling();
            }

            var colors = b.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor     = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor    = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            b.colors = colors;
        }

        void Start()
        {
            foldButton?.onClick.AddListener(OnFold);
            checkCallButton?.onClick.AddListener(OnCheckCall);
            raiseButton?.onClick.AddListener(OnRaiseToggle);
            allInButton?.onClick.AddListener(OnAllIn);
            confirmRaiseButton?.onClick.AddListener(OnConfirmRaise);
            cancelRaiseButton?.onClick.AddListener(OnCancelRaise);
            halfPotButton?.onClick.AddListener(() => SetRaisePreset(0.5f));
            potButton?.onClick.AddListener(() => SetRaisePreset(1f));
            twoxButton?.onClick.AddListener(() => SetRaisePreset(2f));
            raiseSlider?.onValueChanged.AddListener(OnSliderChanged);

            if (raisePanel) raisePanel.SetActive(false);
            gameObject.SetActive(false);
        }

        // Force the panel to a known-good layout at runtime so it's visible regardless
        // of saved scene state. Stretched bottom strip with four spaced buttons.
        void EnforceLayout()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) return;
            // If we're already a child of TableContainer (PokerTableUI's
            // RelayoutTablePane reparented us), don't overwrite that anchor
            // — TableContainer's RelayoutTablePane is the single source of
            // truth for our placement.
            bool insideTablePane = rt.parent != null && rt.parent.name == "TableContainer";
            if (!insideTablePane)
            {
                // Anchor to the bottom edge and make the strip span only the
                // table pane (canvas width minus the left-side log). This keeps
                // the action buttons centered under the table rather than
                // straddling behind the event log.
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 0);
                rt.pivot = new Vector2(0.5f, 0);
                // Shift center to match the table's X offset (right-side pane),
                // and squeeze the strip so it doesn't overlap the log.
                rt.anchoredPosition = new Vector2(PokerTableUI.TABLE_X_OFFSET, 0);
                rt.sizeDelta = new Vector2(
                    -(PokerTableUI.LOG_PANE_WIDTH + PokerTableUI.PANE_GAP +
                      PokerTableUI.PANE_PADDING * 2),
                    200);
            }

            // Make the panel container fully transparent so it blends with
            // the table felt — only the buttons themselves should be visible.
            // We keep raycastTarget OFF too so clicks fall through any empty
            // gap between buttons instead of being absorbed by the strip.
            var img = GetComponent<Image>();
            if (img != null)
            {
                img.color = new Color(0, 0, 0, 0);
                img.raycastTarget = false;
                img.enabled = false;
            }

            // Hide the timer bar visuals from the start — the design omits
            // the colored line, but auto-fold still ticks via Update().
            var timerT = transform.Find("TimerBg");
            if (timerT != null) timerT.gameObject.SetActive(false);

            PlaceBtn(foldButton,      -300, new Vector2(170, 76));
            PlaceBtn(checkCallButton,  -90, new Vector2(210, 76));
            PlaceBtn(raiseButton,      120, new Vector2(180, 76));
            PlaceBtn(allInButton,      315, new Vector2(150, 76));

            // ButtonsRow parent
            if (foldButton != null && foldButton.transform.parent != null)
            {
                var br = foldButton.transform.parent as RectTransform;
                if (br != null && br != rt)
                {
                    br.anchorMin = new Vector2(0.5f, 0);
                    br.anchorMax = new Vector2(0.5f, 0);
                    br.pivot = new Vector2(0.5f, 0.5f);
                    br.anchoredPosition = new Vector2(0, 110);
                    br.sizeDelta = new Vector2(820, 84);
                }
            }

            BuildRaisePanelLayout();
        }

        // ---------------------------------------------------------------
        // Raise panel — built per PokerUI.uss spec:
        //   .raise-panel              440px wide, dark surface,
        //                             rgba(10,6,28,0.98) bg,
        //                             rgba(130,70,255,0.40) border,
        //                             16px radius
        //   .raise-panel-header       "RAISE TO" left + "BET <amount>" right
        //   .raise-slider             4px tracker, 20px purple thumb
        //   .slider-markers-row       MIN / ½ POT / POT / ALL IN labels
        //   .quick-bets-row           4 preset chips
        //   .confirm-raise-btn        big "RAISE TO X" button
        //
        // We construct procedurally on top of whatever raisePanel object
        // was wired up (or auto-create one parented to the betting panel
        // if none exists), so prefab variations can't break the layout.
        // ---------------------------------------------------------------
        void BuildRaisePanelLayout()
        {
            // Auto-create the panel if not pre-wired in the scene.
            if (raisePanel == null)
            {
                var go = new GameObject("RaisePanel",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(transform, false);
                raisePanel = go;
            }

            // Strip any prefab-baked children (header chips, BET icon,
            // duplicate preset buttons, etc.) so only our procedural
            // layout below shows. Anything we own ourselves (Header /
            // Slider / Markers / Presets / ConfirmRaiseBtn / PanelBorder)
            // is preserved by name. We also walk the hierarchy recursively
            // because some prefabs nest the BET icon and small "½ Pot /
            // Pot / 2×" chips inside an inner row container that wouldn't
            // be caught by a top-level pass.
            var ownNames = new System.Collections.Generic.HashSet<string> {
                "Header", "Slider", "Markers", "Presets", "ConfirmRaiseBtn",
                "PanelBorder"
            };
            // Names of preset / icon things that the prefab is known to
            // ship and that visually duplicate our own controls. We hide
            // any descendant matching one of these (case-insensitive
            // substring match) regardless of depth.
            string[] hideHints = {
                "preset", "halfpot", "half_pot", "halfbet",
                "potbtn", "pot_btn", "twox", "2x", "twoxbtn",
                "betchip", "beticon", "icon", "betsquare",
                "quickbet", "quick_bet"
            };
            for (int i = raisePanel.transform.childCount - 1; i >= 0; i--)
            {
                var ch = raisePanel.transform.GetChild(i);
                if (!ownNames.Contains(ch.name)) ch.gameObject.SetActive(false);
            }
            // Recursive sweep on whatever is still active.
            HideMatchingDescendants(raisePanel.transform, ownNames, hideHints);

            var rprt = raisePanel.GetComponent<RectTransform>();
            if (rprt == null) rprt = raisePanel.AddComponent<RectTransform>();
            rprt.anchorMin = new Vector2(0.5f, 0);
            rprt.anchorMax = new Vector2(0.5f, 0);
            rprt.pivot = new Vector2(0.5f, 0);
            // Sit the panel above the action button row (action row is at
            // y≈110, height 84, so 110+42=152 is the top of the row).
            rprt.anchoredPosition = new Vector2(0, 210);
            rprt.sizeDelta = new Vector2(440, 280);

            // Panel surface — green to match the new action palette per
            // user request ("raise toggle background green").
            var surface = raisePanel.GetComponent<Image>();
            if (surface == null) surface = raisePanel.AddComponent<Image>();
            surface.sprite = SpriteFactory.Pill();
            surface.type = Image.Type.Sliced;
            // Deep forest green with mild transparency so the felt subtly
            // shows through, keeping the panel feeling integrated.
            surface.color = new Color(14f / 255f, 80f / 255f, 60f / 255f, 0.96f);
            surface.raycastTarget = true;

            // Border overlay (bright emerald rim)
            EnsureBorder(rprt, "PanelBorder",
                new Color(80f / 255f, 230f / 255f, 160f / 255f, 0.9f));

            // ---------- Header row ----------
            var header = EnsureChildRect(rprt, "Header",
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -16), new Vector2(-32, 28));
            // Wipe any prefab-baked icons/labels that aren't ours.
            ClearForeignChildren(header, "TitleLabel", "BetPrefix", "BetAmount");
            // "RAISE TO"
            EnsureLabel(header, "TitleLabel", "RAISE TO",
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(0, 0), new Vector2(180, 28),
                14, FontStyles.Bold,
                Color.white, TextAlignmentOptions.Left);
            // "BET <amount>" — built as two parts so amount can be styled big
            EnsureLabel(header, "BetPrefix", "BET",
                new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(-110, 0), new Vector2(60, 28),
                12, FontStyles.Bold,
                new Color(0.85f, 1f, 0.92f, 1f), TextAlignmentOptions.Right);
            var amountLabel = EnsureLabel(header, "BetAmount", "$0",
                new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f),
                new Vector2(0, 0), new Vector2(140, 32),
                22, FontStyles.Bold,
                Color.white, TextAlignmentOptions.Right);
            raiseAmountText = amountLabel;

            // ---------- Slider ----------
            // The slider is a thin tracker with a circular thumb. We rebuild
            // the visual underneath whatever Slider component is wired up,
            // creating one if needed.
            var sliderRoot = EnsureChildRect(rprt, "Slider",
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -68), new Vector2(-40, 24));
            ClearForeignChildren(sliderRoot, "Track", "FillArea", "HandleArea", "Handle");
            BuildSlider(sliderRoot);

            // ---------- Slider markers ----------
            var markers = EnsureChildRect(rprt, "Markers",
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -94), new Vector2(-40, 16));
            ClearForeignChildren(markers, "MinMark", "HalfMark", "PotMark", "AllInMark");
            BuildMarker(markers, "MinMark",   "MIN",   0.00f);
            BuildMarker(markers, "HalfMark",  "½ POT", 0.33f);
            BuildMarker(markers, "PotMark",   "POT",   0.66f);
            BuildMarker(markers, "AllInMark", "ALL IN", 1.00f);

            // ---------- Quick-bet preset row ----------
            var presets = EnsureChildRect(rprt, "Presets",
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -132), new Vector2(-32, 36));
            // Hide every prefab-shipped preset chip so only our four
            // procedurally-built ones remain.
            ClearForeignChildren(presets, "HalfPot", "Pot", "TwoX", "AllIn");
            halfPotButton = BuildPresetButton(presets, "HalfPot", "½ POT", -156);
            potButton     = BuildPresetButton(presets, "Pot",     "POT",    -52);
            twoxButton    = BuildPresetButton(presets, "TwoX",    "2× POT",  52);
            var allInPreset = BuildPresetButton(presets, "AllIn",  "ALL IN", 156);
            // ALL IN preset reuses the all-in handler.
            if (allInPreset != null)
            {
                allInPreset.onClick.RemoveAllListeners();
                allInPreset.onClick.AddListener(OnAllIn);
            }

            // ---------- Confirm raise button ----------
            var confirmGo = EnsureChildRect(rprt, "ConfirmRaiseBtn",
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                new Vector2(0, 24), new Vector2(-32, 48));
            var confirmBtn = confirmGo.GetComponent<Button>();
            if (confirmBtn == null) confirmBtn = confirmGo.gameObject.AddComponent<Button>();
            confirmRaiseButton = confirmBtn;
            // Strip prefab labels/icons that would render duplicate text.
            ClearForeignChildren(confirmBtn.transform, "Label", "BtnBorder");
            // Style as the active/raise variant (#3C1888 fill, #A060FF border)
            // with white "RAISE TO X" text.
            EnsureLabel((RectTransform)confirmBtn.transform, "Label", "RAISE TO $0",
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero,
                18, FontStyles.Bold,
                Color.white, TextAlignmentOptions.Center);
            // Confirm = saturated green (matches RAISE family) with white text.
            StyleButton(confirmBtn,
                Hex(0x0E, 0x88, 0x52), Hex(0x40, 0xD0, 0x90),
                Color.white, null);
        }

        // ---------- Helpers ----------

        // Disable every direct child of `parent` whose name isn't in the
        // `keep` whitelist. Used to scrub prefab-baked siblings out of
        // our owned subcontainers (Presets, Header, Markers, etc.) so
        // only our procedurally-built children remain visible.
        static void ClearForeignChildren(Transform parent, params string[] keep)
        {
            if (parent == null) return;
            var keepSet = new System.Collections.Generic.HashSet<string>(keep);
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var ch = parent.GetChild(i);
                if (!keepSet.Contains(ch.name)) ch.gameObject.SetActive(false);
            }
        }

        // Recursively hide any descendant whose name contains a hide-hint,
        // skipping subtrees rooted at one of our owned containers so we
        // never disable our own freshly-built children.
        static void HideMatchingDescendants(Transform root,
            System.Collections.Generic.HashSet<string> ownedRoots,
            string[] hideHints)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var ch = root.GetChild(i);
                // Don't traverse into our own subtrees.
                if (ownedRoots.Contains(ch.name)) continue;
                string ln = ch.name.ToLowerInvariant();
                bool match = false;
                for (int h = 0; h < hideHints.Length; h++)
                {
                    if (ln.Contains(hideHints[h])) { match = true; break; }
                }
                if (match)
                {
                    ch.gameObject.SetActive(false);
                    continue;
                }
                HideMatchingDescendants(ch, ownedRoots, hideHints);
            }
        }

        static RectTransform EnsureChildRect(RectTransform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var t = parent.Find(name) as RectTransform;
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                t = (RectTransform)go.transform;
            }
            t.anchorMin = anchorMin;
            t.anchorMax = anchorMax;
            t.pivot = pivot;
            t.anchoredPosition = anchoredPos;
            t.sizeDelta = sizeDelta;
            return t;
        }

        static TextMeshProUGUI EnsureLabel(RectTransform parent, string name, string text,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta,
            float fontSize, FontStyles style, Color color, TextAlignmentOptions align)
        {
            var t = parent.Find(name) as RectTransform;
            TextMeshProUGUI tmp;
            if (t == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(parent, false);
                t = (RectTransform)go.transform;
                tmp = go.AddComponent<TextMeshProUGUI>();
            }
            else
            {
                tmp = t.GetComponent<TextMeshProUGUI>();
                if (tmp == null) tmp = t.gameObject.AddComponent<TextMeshProUGUI>();
            }
            t.anchorMin = anchorMin;
            t.anchorMax = anchorMax;
            t.pivot = pivot;
            t.anchoredPosition = anchoredPos;
            if (anchorMin != anchorMax) t.sizeDelta = sizeDelta; // stretched
            else t.sizeDelta = sizeDelta;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = align;
            tmp.characterSpacing = 4f;
            tmp.raycastTarget = false;
            return tmp;
        }

        static void EnsureBorder(RectTransform parent, string name, Color color)
        {
            var t = parent.Find(name) as RectTransform;
            Image img;
            if (t == null)
            {
                var go = new GameObject(name,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
                t = (RectTransform)go.transform;
                img = go.GetComponent<Image>();
            }
            else
            {
                img = t.GetComponent<Image>();
                if (img == null) img = t.gameObject.AddComponent<Image>();
            }
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.pivot = new Vector2(0.5f, 0.5f);
            t.offsetMin = Vector2.zero;
            t.offsetMax = Vector2.zero;
            // Ring sprite so the border draws ONLY a rim and the fill
            // underneath shows through.
            img.sprite = SpriteFactory.PillRing();
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
        }

        void BuildSlider(RectTransform root)
        {
            // Track (4px tall thin bar centered)
            var trackRect = EnsureChildRect(root, "Track",
                new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(0, 4));
            var trackImg = trackRect.GetComponent<Image>();
            if (trackImg == null) trackImg = trackRect.gameObject.AddComponent<Image>();
            trackImg.sprite = SpriteFactory.Pill();
            trackImg.type = Image.Type.Sliced;
            trackImg.color = new Color(20f / 255f, 60f / 255f, 40f / 255f, 0.7f);
            trackImg.raycastTarget = false;

            // Fill (purple gradient surrogate)
            var fillArea = EnsureChildRect(trackRect, "FillArea",
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var fillRect = EnsureChildRect(fillArea, "Fill",
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var fillImg = fillRect.GetComponent<Image>();
            if (fillImg == null) fillImg = fillRect.gameObject.AddComponent<Image>();
            fillImg.sprite = SpriteFactory.Pill();
            fillImg.type = Image.Type.Sliced;
            fillImg.color = new Color(80f / 255f, 230f / 255f, 160f / 255f, 1f);
            fillImg.raycastTarget = false;

            // Handle (20px circular thumb)
            var handleArea = EnsureChildRect(root, "HandleArea",
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-20, 0));
            var handleRect = EnsureChildRect(handleArea, "Handle",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(20, 20));
            var handleImg = handleRect.GetComponent<Image>();
            if (handleImg == null) handleImg = handleRect.gameObject.AddComponent<Image>();
            handleImg.sprite = SpriteFactory.Circle();
            handleImg.color = new Color(80f / 255f, 230f / 255f, 160f / 255f, 1f);

            var slider = root.GetComponent<Slider>();
            if (slider == null) slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = true;
            raiseSlider = slider;
        }

        static void BuildMarker(RectTransform parent, string name, string text, float t01)
        {
            // 4 markers, evenly spaced left-to-right via anchor min=max.
            var rt = EnsureChildRect(parent, name,
                new Vector2(t01, 0.5f), new Vector2(t01, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(80, 14));
            var lbl = rt.GetComponent<TextMeshProUGUI>();
            if (lbl == null) lbl = rt.gameObject.AddComponent<TextMeshProUGUI>();
            lbl.text = text;
            lbl.fontSize = 10;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color = new Color(0.78f, 1f, 0.88f, 0.85f);
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.characterSpacing = 2f;
            lbl.raycastTarget = false;
        }

        Button BuildPresetButton(RectTransform parent, string name, string label, float x)
        {
            var rt = EnsureChildRect(parent, name,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x, 0), new Vector2(96, 32));
            var img = rt.GetComponent<Image>();
            if (img == null) img = rt.gameObject.AddComponent<Image>();
            img.sprite = SpriteFactory.Pill();
            img.type = Image.Type.Sliced;
            img.color = new Color(20f / 255f, 100f / 255f, 60f / 255f, 0.55f);

            var btn = rt.GetComponent<Button>();
            if (btn == null) btn = rt.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;

            // Border
            EnsureBorder(rt, "Border",
                new Color(80f / 255f, 220f / 255f, 150f / 255f, 0.9f));

            // Label
            EnsureLabel(rt, "Label", label,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero,
                12, FontStyles.Bold,
                Color.white, TextAlignmentOptions.Center);

            return btn;
        }

        static void PlaceBtn(Button b, float x, Vector2 size)
        {
            if (b == null) return;
            var rt = b.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
            rt.sizeDelta = size;
        }

        void Update()
        {
            if (!_timerActive) return;
            _timerValue -= Time.deltaTime;
            float t = Mathf.Clamp01(_timerValue / actionTimeSeconds);
            // Timer bar visual is hidden (it produced an unwanted green/red
            // line between the table and the buttons). The countdown still
            // ticks so auto-fold continues to work.
            if (_timerValue <= 0) { _timerActive = false; OnFold(); }
        }

        public void Show(GameState state)
        {
            _state = state;
            gameObject.SetActive(true);
            ApplyButtonVisuals();
            _timerValue = actionTimeSeconds;
            _timerActive = true;
            if (raisePanel) raisePanel.SetActive(false);
            SetInteractable(true);
            // Hide the timer bar entirely — design omits the line.
            if (timerBar)
            {
                timerBar.gameObject.SetActive(false);
                if (timerBar.transform.parent != null)
                    timerBar.transform.parent.gameObject.SetActive(false);
            }
            RefreshButtons();
        }

        // Visible but non-interactive — used when it's not the hero's
        // turn. Buttons keep their normal CHECK/CALL/RAISE/ALL IN labels
        // so the action set the player will see is predictable; they're
        // just disabled until the turn comes around. The countdown for
        // the active player is shown as a yellow ring on their avatar
        // (see PlayerSeatUI.SetPlayer / countdownRing), not in this strip.
        public void ShowDisabled(GameState state)
        {
            _state = state;
            gameObject.SetActive(true);
            ApplyButtonVisuals();
            _timerActive = false;
            if (raisePanel) raisePanel.SetActive(false);
            SetInteractable(false);
            if (timerBar) { timerBar.fillAmount = 0; timerBar.gameObject.SetActive(false); }
            RefreshButtons();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            _timerActive = false;
        }

        void SetInteractable(bool v)
        {
            // Use a CanvasGroup on the panel root for visible "dimmed
            // disabled" feedback — Selectable.Transition.None means the
            // built-in disabledColor pipeline is bypassed, so we need
            // an alpha cue ourselves.
            var cg = GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            cg.alpha = v ? 1f : 0.45f;
            cg.interactable = v;
            cg.blocksRaycasts = v;
            if (foldButton)      foldButton.interactable = v;
            if (checkCallButton) checkCallButton.interactable = v;
            if (raiseButton)     raiseButton.interactable = v;
            if (allInButton)     allInButton.interactable = v;
        }

        void RefreshButtons()
        {
            if (_state == null) return;
            int toCall = _state.current_bet - GetMyBet();
            bool canCheck = toCall <= 0;

            // Check/Call button: swap palette by state.
            if (checkCallLabel)
                checkCallLabel.text = canCheck ? "CHECK" : $"CALL  ${toCall}";
            if (checkCallButton != null)
            {
                // Re-style the whole button so background, border, and
                // text all flip together between CHECK and CALL palettes
                // exactly per the USS spec.
                if (canCheck)
                    StyleButton(checkCallButton, USS_CHECK_BG, USS_CHECK_BORDER, USS_CHECK_TEXT, null);
                else
                    StyleButton(checkCallButton, USS_CALL_BG,  USS_CALL_BORDER,  USS_CALL_TEXT,  null);
            }

            // Raise button
            int minRaise = _state.min_raise > 0 ? _state.min_raise : _state.big_blind;
            if (raiseLabel) raiseLabel.text = $"RAISE\n${minRaise}+";

            // Configure slider
            var me = GetMyPlayer();
            if (me != null && raiseSlider)
            {
                int stackLeft = me.stack;
                raiseSlider.minValue = minRaise + _state.current_bet;
                raiseSlider.maxValue = me.stack + me.current_bet;
                raiseSlider.value = Mathf.Min(minRaise + _state.current_bet, raiseSlider.maxValue);
                _raiseAmount = (int)raiseSlider.value;
                UpdateRaiseLabel();
            }
        }

        void OnSliderChanged(float val)
        {
            _raiseAmount = Mathf.RoundToInt(val);
            UpdateRaiseLabel();
        }

        void UpdateRaiseLabel()
        {
            if (raiseAmountText) raiseAmountText.text = $"${_raiseAmount}";
            // Also push the amount into the confirm button label so the
            // call-to-action stays in sync as the slider moves.
            if (confirmRaiseButton != null)
            {
                var lbl = confirmRaiseButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (lbl != null) lbl.text = $"RAISE TO ${_raiseAmount}";
            }
        }

        void SetRaisePreset(float potFraction)
        {
            if (_state == null) return;
            int pot = _state.pot;
            int raise = Mathf.RoundToInt(pot * potFraction);
            raise = Mathf.Max(raise, _state.min_raise + _state.current_bet);
            var me = GetMyPlayer();
            if (me != null) raise = Mathf.Min(raise, me.stack + me.current_bet);
            if (raiseSlider) raiseSlider.value = raise;
        }

        void OnFold() { GameStateManager.Instance?.SendAction(ActionType.FOLD); Hide(); }

        void OnCheckCall()
        {
            int toCall = _state.current_bet - GetMyBet();
            if (toCall <= 0)
                GameStateManager.Instance?.SendAction(ActionType.CHECK);
            else
                GameStateManager.Instance?.SendAction(ActionType.CALL, toCall);
            Hide();
        }

        void OnRaiseToggle()
        {
            if (raisePanel == null) return;
            bool willOpen = !raisePanel.activeSelf;
            raisePanel.SetActive(willOpen);
            if (willOpen)
            {
                // Re-apply slider bounds & confirm-button label so the
                // panel reflects the current state every time it opens.
                RefreshButtons();
                UpdateRaiseLabel();
                // Re-bind in case sub-buttons were rebuilt.
                if (confirmRaiseButton != null)
                {
                    confirmRaiseButton.onClick.RemoveAllListeners();
                    confirmRaiseButton.onClick.AddListener(OnConfirmRaise);
                }
                if (halfPotButton != null)
                {
                    halfPotButton.onClick.RemoveAllListeners();
                    halfPotButton.onClick.AddListener(() => SetRaisePreset(0.5f));
                }
                if (potButton != null)
                {
                    potButton.onClick.RemoveAllListeners();
                    potButton.onClick.AddListener(() => SetRaisePreset(1f));
                }
                if (twoxButton != null)
                {
                    twoxButton.onClick.RemoveAllListeners();
                    twoxButton.onClick.AddListener(() => SetRaisePreset(2f));
                }
                if (raiseSlider != null)
                {
                    raiseSlider.onValueChanged.RemoveAllListeners();
                    raiseSlider.onValueChanged.AddListener(OnSliderChanged);
                }
            }
        }

        void OnConfirmRaise()
        {
            GameStateManager.Instance?.SendAction(ActionType.RAISE, _raiseAmount);
            Hide();
        }

        void OnCancelRaise()
        {
            if (raisePanel) raisePanel.SetActive(false);
        }

        void OnAllIn()
        {
            var me = GetMyPlayer();
            if (me == null) return;
            GameStateManager.Instance?.SendAction(ActionType.RAISE, me.stack + me.current_bet);
            Hide();
        }

        int GetMyBet()
        {
            var me = GetMyPlayer();
            return me?.current_bet ?? 0;
        }

        Player GetMyPlayer()
        {
            if (_state?.players == null) return null;
            string myId = GameStateManager.Instance?.MyPlayerId;
            return _state.players.Find(p => p.id == myId);
        }
    }
}
