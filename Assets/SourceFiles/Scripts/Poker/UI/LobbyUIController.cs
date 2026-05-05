using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;
using Poker.Models;
using Poker.Network;

namespace Poker.UI
{
    // UGUI-based lobby panel.  Self-bootstraps via RuntimeInitializeOnLoadMethod —
    // creates a dedicated Screen Space Overlay canvas with a high sortingOrder so
    // it always renders above the existing PokerCanvas.
    //
    // Visual spec: Assets/Resources/UI/PokerLobby.uss (design reference only).
    public class LobbyUIController : MonoBehaviour
    {
        public static LobbyUIController Instance { get; private set; }

        // ── Color palette (from PokerLobby.uss) ───────────────────────────────
        static readonly Color BG_DARK         = HexColor("#060810");
        static readonly Color PANEL_SURFACE   = HexColor("#0E0C26", 0.97f);
        static readonly Color BORDER_SUBTLE   = HexColor("#FFFFFF", 0.09f);
        static readonly Color GOLD_BAR        = HexColor("#D4A820");
        static readonly Color GOLD_TEXT       = HexColor("#F5D060");
        static readonly Color GOLD_CHIP       = HexColor("#C89A20");
        static readonly Color ACCENT_PURPLE   = HexColor("#C97AFF");
        static readonly Color TAB_ACTIVE_BG   = HexColor("#461EA0", 0.68f);
        static readonly Color TAB_BG_SUBTLE   = HexColor("#FFFFFF", 0.04f);
        static readonly Color TEXT_NAME_DIM   = HexColor("#DDE0F5");
        static readonly Color TEXT_MUTED      = new Color(140f/255f, 130f/255f, 180f/255f, 0.70f);
        static readonly Color TEXT_PLACEHOLDER= new Color(130f/255f, 120f/255f, 170f/255f, 0.45f);
        static readonly Color INPUT_BG        = HexColor("#FFFFFF", 0.05f);
        static readonly Color INPUT_BORDER    = HexColor("#FFFFFF", 0.09f);
        static readonly Color BTN_CREATE_BG   = HexColor("#225C30");
        static readonly Color BTN_CREATE_TX   = HexColor("#7FFFA8");
        static readonly Color BTN_JOIN_BG     = HexColor("#173C6B");
        static readonly Color BTN_JOIN_TX     = HexColor("#7BC4FF");
        static readonly Color BTN_BOTS_BG     = HexColor("#6B2C0B");
        static readonly Color BTN_BOTS_TX     = HexColor("#FFB86C");
        static readonly Color STATUS_ONLINE   = HexColor("#4DE09A");
        static readonly Color STATUS_WAITING  = HexColor("#F5C842");
        static readonly Color STATUS_OFFLINE  = HexColor("#FF6060");
        static readonly Color SUIT_RED        = HexColor("#CC4433");
        static readonly Color SUIT_BLK        = HexColor("#8888AA");
        static readonly Color DIVIDER         = new Color(180f/255f, 120f/255f, 255f/255f, 0.14f);

        const int PANEL_WIDTH = 400;
        const int MAX_PLAYERS = 6;

        // ── State refs (built in BuildUI) ─────────────────────────────────────
        Canvas             _canvas;
        GameObject         _root;             // full-screen background container
        RectTransform      _panel;            // the 400-wide card

        Button             _tabCreate, _tabJoin, _tabBots;
        Image              _tabCreateBg, _tabJoinBg, _tabBotsBg;
        TMP_Text           _tabCreateTxt, _tabJoinTxt, _tabBotsTxt;

        GameObject         _contentCreate, _contentJoin, _contentBots, _contentWaiting;
        TMP_InputField     _nameCreate, _buyinCreate;
        TMP_InputField     _nameJoin,   _buyinJoin, _roomCodeInput;
        TMP_InputField     _nameBots,   _buyinBots;
        Button             _createBtn, _joinBtn, _botsPlayBtn, _botsInc, _botsDec, _cancelBtn;
        TMP_Text           _botsCountTxt, _waitingCodeTxt, _waitingCountTxt;
        RectTransform      _waitingDotsRow;
        RectTransform      _spinner;

        Image              _statusDot;
        TMP_Text           _statusLabel;

        enum Tab { Create, Join, Bots }
        Tab   _currentTab = Tab.Create;
        bool  _inWaiting;
        bool  _pendingSitDown;
        int   _pendingBuyIn = 1000;
        int   _botCount     = 3;
        float _spinAngle;

        // ── Bootstrap ─────────────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (SceneManager.GetActiveScene().name != "PokerTable") return;
            if (FindFirstObjectByType<LobbyUIController>() != null) return;

            var go = new GameObject("LobbyUI");
            DontDestroyOnLoad(go);
            go.AddComponent<LobbyUIController>();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Suppress the legacy in-scene UGUI lobbyPanel BEFORE PokerTableUI.Start()
            // calls ShowLobby(true) on it.
            var ptui = FindFirstObjectByType<PokerTableUI>();
            if (ptui != null && ptui.lobbyPanel != null)
            {
                ptui.lobbyPanel.SetActive(false);
                ptui.lobbyPanel = null;
            }

            BuildUI();
            EnsureEventSystem();
        }

        IEnumerator Start()
        {
            yield return null; // give other Start()s a chance to wire up

            ShowPanel(true);
            SwitchTab(Tab.Create, force: true);
            SetStatus("Connecting...", StatusState.Offline);

            var gsm = GameStateManager.Instance;
            if (gsm != null)
            {
                gsm.OnGameStateUpdated += OnStateUpdated;
                gsm.OnError            += OnError;
            }
            var sio = SocketIOClient.Instance;
            if (sio != null)
            {
                sio.OnConnected    += () => SetStatus("Connected",    StatusState.Online);
                sio.OnDisconnected += () => SetStatus("Disconnected", StatusState.Offline);
                if (sio.IsConnected) SetStatus("Connected", StatusState.Online);
            }
        }

        void Update()
        {
            if (_inWaiting && _spinner != null)
            {
                _spinAngle = (_spinAngle + 360f * Time.deltaTime / 0.9f) % 360f;
                _spinner.localRotation = Quaternion.Euler(0f, 0f, -_spinAngle);
            }
        }

        // ── UI construction ───────────────────────────────────────────────────
        void BuildUI()
        {
            // Canvas with very high sort order so it always covers the table.
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode             = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution     = new Vector2(1280, 720);
            scaler.screenMatchMode         = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight      = 0.5f;
            scaler.referencePixelsPerUnit  = 100f;
            gameObject.AddComponent<GraphicRaycaster>();

            // Full-screen dark background — kills the table behind it.
            _root = NewImage("Background", transform, BG_DARK);
            var rootRT = _root.GetComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            // Centred 400-wide card
            var panelGO = NewImage("Panel", _root.transform, PANEL_SURFACE);
            _panel = panelGO.GetComponent<RectTransform>();
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(PANEL_WIDTH, 0); // height fits content

            // Border (1px) — Outline component fakes a stroke.
            var outline = panelGO.AddComponent<Outline>();
            outline.effectColor    = BORDER_SUBTLE;
            outline.effectDistance = new Vector2(1, -1);

            var panelVlg = panelGO.AddComponent<VerticalLayoutGroup>();
            panelVlg.padding      = new RectOffset(0, 0, 0, 0);
            panelVlg.spacing      = 0;
            panelVlg.childAlignment       = TextAnchor.UpperCenter;
            // childControlWidth=false: direct children use their own stretch anchors so they
            // always span the panel width (set explicitly below). VLG only handles vertical.
            panelVlg.childControlWidth    = false;
            panelVlg.childControlHeight   = true;
            panelVlg.childForceExpandWidth  = false;
            panelVlg.childForceExpandHeight = false;
            var panelFitter = panelGO.AddComponent<ContentSizeFitter>();
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildTopBar(_panel);
            var inner = BuildPanelInner(_panel);

            BuildHeader(inner);
            BuildDivider(inner);
            BuildTabs(inner);

            _contentCreate  = BuildCreateContent(inner);
            _contentJoin    = BuildJoinContent(inner);
            _contentBots    = BuildBotsContent(inner);
            _contentWaiting = BuildWaitingContent(inner);

            BuildStatusBar(inner);

            // Force a layout pass so all rects are computed before first frame.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
        }

        void BuildTopBar(RectTransform parent)
        {
            var bar = NewImage("TopBar", parent, GOLD_BAR);
            PinToPanelWidth(bar.GetComponent<RectTransform>());
            var le  = bar.AddComponent<LayoutElement>();
            le.preferredHeight = 3;
            le.flexibleHeight  = 0;
        }

        RectTransform BuildPanelInner(RectTransform parent)
        {
            var go = new GameObject("Inner", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            NormalizeRect(rt);
            PinToPanelWidth(rt);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(32, 32, 28, 30);
            vlg.spacing = 0;
            vlg.childControlWidth    = true;
            vlg.childControlHeight   = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            var f = go.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        // Pin a direct child of the panel to fixed PANEL_WIDTH centred horizontally.
        // VLG (childControlWidth=false) won't override sizeDelta.x.
        static void PinToPanelWidth(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(PANEL_WIDTH, rt.sizeDelta.y);
        }

        void BuildHeader(RectTransform parent)
        {
            var header = NewVertical("Header", parent, 0, new RectOffset(0,0,0,22), TextAnchor.MiddleCenter);
            // suits row
            var suits = NewHorizontal("Suits", header, 14, new RectOffset(0,0,0,6), TextAnchor.MiddleCenter);
            NewSuit(suits, "♠", SUIT_BLK);
            NewSuit(suits, "♥", SUIT_RED);
            NewSuit(suits, "♦", SUIT_RED);
            NewSuit(suits, "♣", SUIT_BLK);
            // title
            var title = NewText("Title", header, "TEXAS HOLD'EM", 34, GOLD_TEXT, FontStyles.Bold, TextAlignmentOptions.Center);
            title.characterSpacing = 8f;
            // subtitle
            var sub = NewText("Subtitle", header, "POKER NIGHT", 12, new Color(180f/255f,170f/255f,220f/255f,0.5f), FontStyles.Bold, TextAlignmentOptions.Center);
            sub.characterSpacing = 8f;
        }

        void NewSuit(RectTransform parent, string glyph, Color color)
        {
            var t = NewText("Suit", parent, glyph, 16, color * new Color(1,1,1,0.5f), FontStyles.Normal, TextAlignmentOptions.Center);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 22; le.preferredHeight = 20;
        }

        void BuildDivider(RectTransform parent)
        {
            var d = NewImage("Divider", parent, DIVIDER);
            var le = d.AddComponent<LayoutElement>();
            le.preferredHeight = 1; le.flexibleHeight = 0;
            // 22px bottom margin: add empty spacer
            NewSpacer(parent, 22);
        }

        void BuildTabs(RectTransform parent)
        {
            var bar = NewImage("Tabs", parent, TAB_BG_SUBTLE);
            var ol  = bar.AddComponent<Outline>();
            ol.effectColor = BORDER_SUBTLE; ol.effectDistance = new Vector2(1,-1);
            var rt = bar.GetComponent<RectTransform>();
            var le = bar.AddComponent<LayoutElement>();
            le.preferredHeight = 40; le.flexibleHeight = 0;
            var hlg = bar.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(3,3,3,3);
            hlg.spacing = 0;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

            (_tabCreate, _tabCreateBg, _tabCreateTxt) = MakeTab(rt, "Create");
            (_tabJoin,   _tabJoinBg,   _tabJoinTxt)   = MakeTab(rt, "Join");
            (_tabBots,   _tabBotsBg,   _tabBotsTxt)   = MakeTab(rt, "vs Bots");

            _tabCreate.onClick.AddListener(() => SwitchTab(Tab.Create));
            _tabJoin  .onClick.AddListener(() => SwitchTab(Tab.Join));
            _tabBots  .onClick.AddListener(() => SwitchTab(Tab.Bots));

            NewSpacer(parent, 22);
        }

        (Button btn, Image bg, TMP_Text txt) MakeTab(RectTransform parent, string label)
        {
            var go = new GameObject("Tab_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0,0,0,0);
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;

            var txt = NewText("Lbl", go.GetComponent<RectTransform>(), label.ToUpper(), 12, TEXT_MUTED, FontStyles.Bold, TextAlignmentOptions.Center);
            txt.characterSpacing = 4f;
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            return (btn, bg, txt);
        }

        // ── Tab content panels ────────────────────────────────────────────────
        GameObject BuildCreateContent(RectTransform parent)
        {
            var c = NewContentRoot("CreateContent", parent);
            (_nameCreate,  _) = AddField(c, "YOUR NAME", "Enter your name", false);
            (_buyinCreate, _) = AddBuyInField(c, "BUY-IN CHIPS", "1000");
            NewSpacer(c, 6);
            _createBtn = AddActionButton(c, "CREATE NEW ROOM", BTN_CREATE_BG, BTN_CREATE_TX);
            _createBtn.onClick.AddListener(OnCreateRoom);
            return c.gameObject;
        }

        GameObject BuildJoinContent(RectTransform parent)
        {
            var c = NewContentRoot("JoinContent", parent);
            (_nameJoin,  _) = AddField(c, "YOUR NAME", "Enter your name", false);
            (_roomCodeInput, _) = AddField(c, "ROOM CODE", "XXXXXX", true);
            _roomCodeInput.characterLimit = 6;
            _roomCodeInput.onValueChanged.AddListener(v => _roomCodeInput.text = (v ?? "").ToUpper());
            (_buyinJoin, _) = AddBuyInField(c, "BUY-IN CHIPS", "1000");
            NewSpacer(c, 6);
            _joinBtn = AddActionButton(c, "JOIN ROOM", BTN_JOIN_BG, BTN_JOIN_TX);
            _joinBtn.onClick.AddListener(OnJoinRoom);
            return c.gameObject;
        }

        GameObject BuildBotsContent(RectTransform parent)
        {
            var c = NewContentRoot("BotsContent", parent);
            (_nameBots,  _) = AddField(c, "YOUR NAME", "Enter your name", false);
            (_buyinBots, _) = AddBuyInField(c, "BUY-IN CHIPS", "1000");

            // bots row: label + stepper
            var row = NewImage("BotsRow", c, new Color(1,1,1,0.03f));
            var rowOl = row.AddComponent<Outline>();
            rowOl.effectColor = BORDER_SUBTLE; rowOl.effectDistance = new Vector2(1,-1);
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 50; rowLe.flexibleHeight = 0;
            var rowHlg = row.AddComponent<HorizontalLayoutGroup>();
            rowHlg.padding = new RectOffset(14,14,10,10);
            rowHlg.spacing = 0;
            rowHlg.childAlignment = TextAnchor.MiddleLeft;
            rowHlg.childControlWidth = true; rowHlg.childControlHeight = true;
            rowHlg.childForceExpandWidth = false; rowHlg.childForceExpandHeight = false;

            var lbl = NewText("Lbl", row.GetComponent<RectTransform>(), "NUMBER OF BOTS", 11, TEXT_MUTED, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            lbl.characterSpacing = 4f;
            var lblLe = lbl.gameObject.AddComponent<LayoutElement>();
            lblLe.preferredWidth = 220; lblLe.preferredHeight = 30; lblLe.flexibleWidth = 1;

            // stepper
            var stepperGO = NewImage("Stepper", row.GetComponent<RectTransform>(), new Color(1,1,1,0.06f));
            var stepperOl = stepperGO.AddComponent<Outline>();
            stepperOl.effectColor = BORDER_SUBTLE; stepperOl.effectDistance = new Vector2(1,-1);
            var stLe = stepperGO.AddComponent<LayoutElement>();
            stLe.preferredWidth = 110; stLe.preferredHeight = 30;
            var stHlg = stepperGO.AddComponent<HorizontalLayoutGroup>();
            stHlg.padding = new RectOffset(0,0,0,0);
            stHlg.childAlignment = TextAnchor.MiddleCenter;
            stHlg.childControlWidth = true; stHlg.childControlHeight = true;
            stHlg.childForceExpandWidth = false; stHlg.childForceExpandHeight = false;

            _botsDec      = MakeStepperBtn(stepperGO.GetComponent<RectTransform>(), "−");
            _botsCountTxt = MakeCountLabel(stepperGO.GetComponent<RectTransform>(), _botCount.ToString());
            _botsInc      = MakeStepperBtn(stepperGO.GetComponent<RectTransform>(), "+");

            _botsDec.onClick.AddListener(() => AdjustBotCount(-1));
            _botsInc.onClick.AddListener(() => AdjustBotCount(+1));

            NewSpacer(c, 14); // gap before button
            _botsPlayBtn = AddActionButton(c, "PLAY VS BOTS", BTN_BOTS_BG, BTN_BOTS_TX);
            _botsPlayBtn.onClick.AddListener(OnPlayWithBots);
            return c.gameObject;
        }

        Button MakeStepperBtn(RectTransform parent, string label)
        {
            var go = new GameObject("StepBtn_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var img = go.AddComponent<Image>(); img.color = new Color(0,0,0,0);
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var col = btn.colors;
            col.normalColor = new Color(1,1,1,0);
            col.highlightedColor = new Color(140f/255f, 60f/255f, 240f/255f, 0.30f);
            btn.colors = col;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 30; le.preferredHeight = 30;

            var t = NewText("Lbl", go.GetComponent<RectTransform>(), label, 18, ACCENT_PURPLE, FontStyles.Bold, TextAlignmentOptions.Center);
            var trt = t.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
            return btn;
        }

        TMP_Text MakeCountLabel(RectTransform parent, string text)
        {
            var t = NewText("Count", parent, text, 16, GOLD_CHIP, FontStyles.Bold, TextAlignmentOptions.Center);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 40; le.preferredHeight = 30;
            return t;
        }

        GameObject BuildWaitingContent(RectTransform parent)
        {
            var c = NewContentRoot("WaitingContent", parent);

            // spinner — using a thin ring image (we just rotate a transform with a partial colour)
            var spinGO = NewImage("Spinner", c, new Color(1,1,1,0.08f));
            _spinner = spinGO.GetComponent<RectTransform>();
            var spinLe = spinGO.AddComponent<LayoutElement>();
            spinLe.preferredWidth = 48; spinLe.preferredHeight = 48;
            // accent dot on top to imply rotation
            var accent = NewImage("Accent", _spinner, ACCENT_PURPLE);
            var aRt = accent.GetComponent<RectTransform>();
            aRt.anchorMin = aRt.anchorMax = new Vector2(0.5f, 1f);
            aRt.pivot = new Vector2(0.5f, 1f);
            aRt.anchoredPosition = Vector2.zero;
            aRt.sizeDelta = new Vector2(8, 8);

            NewSpacer(c, 14);
            var title = NewText("Title", c, "ROOM CREATED", 18, ACCENT_PURPLE, FontStyles.Bold, TextAlignmentOptions.Center);
            title.characterSpacing = 6f;
            var titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 24;
            NewSpacer(c, 6);

            _waitingCodeTxt = NewText("Code", c, "------", 28, GOLD_TEXT, FontStyles.Bold, TextAlignmentOptions.Center);
            _waitingCodeTxt.characterSpacing = 12f;
            var codeLe = _waitingCodeTxt.gameObject.AddComponent<LayoutElement>();
            codeLe.preferredHeight = 36;
            NewSpacer(c, 6);

            var sub = NewText("Sub", c, "Share this code with friends", 12, TEXT_MUTED, FontStyles.Normal, TextAlignmentOptions.Center);
            var subLe = sub.gameObject.AddComponent<LayoutElement>();
            subLe.preferredHeight = 18;
            NewSpacer(c, 10);

            // dots row
            var rowGO = new GameObject("PlayerDots", typeof(RectTransform));
            rowGO.transform.SetParent(c, false);
            NormalizeRect(rowGO.GetComponent<RectTransform>());
            _waitingDotsRow = rowGO.GetComponent<RectTransform>();
            var dotsHlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            dotsHlg.spacing = 6;
            dotsHlg.childAlignment = TextAnchor.MiddleCenter;
            dotsHlg.childControlWidth = true; dotsHlg.childControlHeight = true;
            dotsHlg.childForceExpandWidth = false; dotsHlg.childForceExpandHeight = false;
            var dotsLe = rowGO.AddComponent<LayoutElement>();
            dotsLe.preferredHeight = 14;
            NewSpacer(c, 8);

            _waitingCountTxt = NewText("Count", c, "0 / 6 players joined", 12, TEXT_MUTED, FontStyles.Normal, TextAlignmentOptions.Center);
            var wcLe = _waitingCountTxt.gameObject.AddComponent<LayoutElement>();
            wcLe.preferredHeight = 18;
            NewSpacer(c, 10);

            // cancel link (button with text-only style)
            var cancelGO = new GameObject("Cancel", typeof(RectTransform));
            cancelGO.transform.SetParent(c, false);
            NormalizeRect(cancelGO.GetComponent<RectTransform>());
            var cancelImg = cancelGO.AddComponent<Image>();
            cancelImg.color = new Color(0,0,0,0);
            _cancelBtn = cancelGO.AddComponent<Button>();
            _cancelBtn.transition = Selectable.Transition.None;
            var cLe = cancelGO.AddComponent<LayoutElement>();
            cLe.preferredHeight = 24;
            var cTxt = NewText("Lbl", cancelGO.GetComponent<RectTransform>(), "CANCEL ROOM", 11, new Color(140f/255f,130f/255f,180f/255f,0.5f), FontStyles.Bold, TextAlignmentOptions.Center);
            cTxt.characterSpacing = 4f;
            var cTrt = cTxt.rectTransform;
            cTrt.anchorMin = Vector2.zero; cTrt.anchorMax = Vector2.one;
            cTrt.offsetMin = Vector2.zero; cTrt.offsetMax = Vector2.zero;
            _cancelBtn.onClick.AddListener(OnCancelRoom);

            return c.gameObject;
        }

        void BuildStatusBar(RectTransform parent)
        {
            // Top divider
            NewSpacer(parent, 18);
            var divider = NewImage("StatusDivider", parent, new Color(1,1,1,0.06f));
            var dLe = divider.AddComponent<LayoutElement>();
            dLe.preferredHeight = 1; dLe.flexibleHeight = 0;
            NewSpacer(parent, 16);

            var bar = new GameObject("StatusBar", typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            NormalizeRect(bar.GetComponent<RectTransform>());
            var hlg = bar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 7;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            var le = bar.AddComponent<LayoutElement>();
            le.preferredHeight = 14;

            var dotGO = NewImage("Dot", bar.GetComponent<RectTransform>(), STATUS_OFFLINE);
            _statusDot = dotGO.GetComponent<Image>();
            var dotLe = dotGO.AddComponent<LayoutElement>();
            dotLe.preferredWidth = 7; dotLe.preferredHeight = 7;

            _statusLabel = NewText("Lbl", bar.GetComponent<RectTransform>(), "Connecting...", 11, new Color(140f/255f,130f/255f,180f/255f,0.65f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            var stLe = _statusLabel.gameObject.AddComponent<LayoutElement>();
            stLe.preferredWidth = 200; stLe.preferredHeight = 14;
        }

        // ── Field builders ────────────────────────────────────────────────────
        RectTransform NewContentRoot(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 14;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            return go.GetComponent<RectTransform>();
        }

        // Reset to top-left anchored, zero size — so the parent layout group is the
        // single source of truth for sizeDelta (no stretch anchors that compound).
        static void NormalizeRect(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }

        (TMP_InputField input, GameObject wrap) AddField(RectTransform parent, string label, string placeholder, bool codeStyle)
        {
            var wrap = new GameObject(label, typeof(RectTransform));
            wrap.transform.SetParent(parent, false);
            NormalizeRect(wrap.GetComponent<RectTransform>());
            var vlg = wrap.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 5;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

            var lbl = NewText("Lbl", wrap.GetComponent<RectTransform>(), label, 10, TEXT_MUTED, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            lbl.characterSpacing = 8f;
            lbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 14;

            var input = MakeInput(wrap.GetComponent<RectTransform>(), placeholder, codeStyle);
            return (input, wrap);
        }

        (TMP_InputField input, GameObject wrap) AddBuyInField(RectTransform parent, string label, string defaultValue)
        {
            var (input, wrap) = AddField(parent, label, defaultValue, false);
            input.text = defaultValue;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;

            // chip icon overlay (gold dot on the left)
            var inputRT = input.GetComponent<RectTransform>();
            var chipGO = NewImage("ChipIcon", inputRT, GOLD_CHIP);
            var chipRT = chipGO.GetComponent<RectTransform>();
            chipRT.anchorMin = chipRT.anchorMax = new Vector2(0f, 0.5f);
            chipRT.pivot = new Vector2(0f, 0.5f);
            chipRT.anchoredPosition = new Vector2(14, 0);
            chipRT.sizeDelta = new Vector2(16, 16);

            // shift the text area so it doesn't overlap the chip
            var textArea = input.textViewport;
            if (textArea != null)
            {
                textArea.offsetMin = new Vector2(40, textArea.offsetMin.y);
            }
            return (input, wrap);
        }

        TMP_InputField MakeInput(RectTransform parent, string placeholder, bool codeStyle)
        {
            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var img = go.AddComponent<Image>();
            img.color = INPUT_BG;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = INPUT_BORDER; ol.effectDistance = new Vector2(1,-1);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 44; le.flexibleHeight = 0;

            // Text viewport
            var view = new GameObject("Text Area", typeof(RectTransform));
            view.transform.SetParent(go.transform, false);
            view.AddComponent<RectMask2D>();
            var viewRT = view.GetComponent<RectTransform>();
            viewRT.anchorMin = Vector2.zero; viewRT.anchorMax = Vector2.one;
            viewRT.offsetMin = new Vector2(14, 8);
            viewRT.offsetMax = new Vector2(-14, -8);

            // Placeholder
            var phGO = new GameObject("Placeholder", typeof(RectTransform));
            phGO.transform.SetParent(view.transform, false);
            var ph = phGO.AddComponent<TextMeshProUGUI>();
            ph.text = placeholder;
            ph.color = TEXT_PLACEHOLDER;
            ph.fontSize = codeStyle ? 18 : 14;
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.fontStyle = codeStyle ? FontStyles.Bold : FontStyles.Normal;
            if (codeStyle) ph.characterSpacing = 12f;
            var phRT = ph.rectTransform;
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
            phRT.offsetMin = Vector2.zero; phRT.offsetMax = Vector2.zero;

            // Text
            var txtGO = new GameObject("Text", typeof(RectTransform));
            txtGO.transform.SetParent(view.transform, false);
            var txt = txtGO.AddComponent<TextMeshProUGUI>();
            txt.color = TEXT_NAME_DIM;
            txt.fontSize = codeStyle ? 18 : 14;
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            txt.fontStyle = codeStyle ? FontStyles.Bold : FontStyles.Normal;
            if (codeStyle) txt.characterSpacing = 12f;
            var txtRT = txt.rectTransform;
            txtRT.anchorMin = Vector2.zero; txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero; txtRT.offsetMax = Vector2.zero;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = viewRT;
            input.textComponent = txt;
            input.placeholder = ph;
            input.fontAsset = txt.font;
            input.pointSize = codeStyle ? 18 : 14;
            input.targetGraphic = img;
            input.caretColor = ACCENT_PURPLE;

            // Highlight border on focus
            input.onSelect.AddListener(_  => { ol.effectColor = new Color(160f/255f,90f/255f,255f/255f,0.55f); img.color = new Color(1,1,1,0.08f); });
            input.onDeselect.AddListener(_ => { ol.effectColor = INPUT_BORDER; img.color = INPUT_BG; });

            return input;
        }

        Button AddActionButton(RectTransform parent, string text, Color bgColor, Color textColor)
        {
            var go = new GameObject("ActionBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(textColor.r, textColor.g, textColor.b, 0.40f);
            ol.effectDistance = new Vector2(1,-1);
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            var col = btn.colors;
            col.normalColor      = Color.white;
            col.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            col.pressedColor     = new Color(0.7f, 0.7f, 0.7f, 1f);
            btn.colors = col;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 50; le.flexibleHeight = 0;

            var t = NewText("Lbl", go.GetComponent<RectTransform>(), text, 13, textColor, FontStyles.Bold, TextAlignmentOptions.Center);
            t.characterSpacing = 8f;
            var tRT = t.rectTransform;
            tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
            return btn;
        }

        // ── Tab switching ─────────────────────────────────────────────────────
        void SwitchTab(Tab tab, bool force = false)
        {
            if (_inWaiting && !force) return;
            _currentTab = tab;
            if (_contentCreate)  _contentCreate.SetActive(tab == Tab.Create);
            if (_contentJoin)    _contentJoin.SetActive(tab == Tab.Join);
            if (_contentBots)    _contentBots.SetActive(tab == Tab.Bots);
            if (_contentWaiting) _contentWaiting.SetActive(false);

            SetTabActive(_tabCreateBg, _tabCreateTxt, tab == Tab.Create);
            SetTabActive(_tabJoinBg,   _tabJoinTxt,   tab == Tab.Join);
            SetTabActive(_tabBotsBg,   _tabBotsTxt,   tab == Tab.Bots);
        }

        void SetTabActive(Image bg, TMP_Text txt, bool active)
        {
            if (bg != null)  bg.color  = active ? TAB_ACTIVE_BG : new Color(0,0,0,0);
            if (txt != null) txt.color = active ? ACCENT_PURPLE : TEXT_MUTED;
        }

        // ── Lobby actions ─────────────────────────────────────────────────────
        void OnCreateRoom()
        {
            _pendingBuyIn   = GetBuyIn(_buyinCreate);
            _pendingSitDown = true;
            _inWaiting      = false;
            GameStateManager.Instance.CreateRoom(GetName(_nameCreate));
            SetStatus("Creating room...", StatusState.Waiting);
        }

        void OnJoinRoom()
        {
            string code = (_roomCodeInput?.text ?? "").ToUpper().Trim();
            if (string.IsNullOrEmpty(code)) { SetStatus("Enter a room code", StatusState.Offline); return; }
            _pendingBuyIn   = GetBuyIn(_buyinJoin);
            _pendingSitDown = true;
            _inWaiting      = false;
            GameStateManager.Instance.JoinRoom(GetName(_nameJoin), code);
            SetStatus($"Joining {code}...", StatusState.Waiting);
        }

        void OnPlayWithBots()
        {
            _pendingSitDown = false; // server seats us inside play_with_bots
            GameStateManager.Instance.PlayWithBots(GetName(_nameBots), _botCount, GetBuyIn(_buyinBots));
            SetStatus($"Starting with {_botCount} bot{(_botCount == 1 ? "" : "s")}...", StatusState.Waiting);
        }

        void OnCancelRoom()
        {
            GameStateManager.Instance.LeaveRoom();
            _pendingSitDown = false;
            _inWaiting      = false;
            _spinAngle      = 0f;
            SwitchTab(_currentTab, force: true);
            SetStatus("Room cancelled", StatusState.Online);
        }

        void AdjustBotCount(int delta)
        {
            _botCount = Mathf.Clamp(_botCount + delta, 1, 6);
            if (_botsCountTxt != null) _botsCountTxt.text = _botCount.ToString();
        }

        // ── Waiting room ──────────────────────────────────────────────────────
        void ShowWaitingRoom(string code, int joined, int max)
        {
            _inWaiting = true;
            if (_contentCreate)  _contentCreate.SetActive(false);
            if (_contentJoin)    _contentJoin.SetActive(false);
            if (_contentBots)    _contentBots.SetActive(false);
            if (_contentWaiting) _contentWaiting.SetActive(true);

            if (_waitingCodeTxt  != null) _waitingCodeTxt.text  = code;
            if (_waitingCountTxt != null) _waitingCountTxt.text = $"{joined} / {max} players joined";

            if (_waitingDotsRow != null)
            {
                for (int i = _waitingDotsRow.childCount - 1; i >= 0; i--)
                    DestroyImmediate(_waitingDotsRow.GetChild(i).gameObject);
                for (int i = 0; i < max; i++)
                {
                    var dotGO = NewImage("Dot", _waitingDotsRow,
                        i < joined ? HexColor("#8830EE") : new Color(100f/255f, 80f/255f, 160f/255f, 0.40f));
                    var dotLE = dotGO.AddComponent<LayoutElement>();
                    dotLE.preferredWidth = 10; dotLE.preferredHeight = 10;
                    if (i < joined)
                    {
                        var ol = dotGO.AddComponent<Outline>();
                        ol.effectColor = ACCENT_PURPLE; ol.effectDistance = new Vector2(1,-1);
                    }
                }
            }
        }

        // ── Game state ────────────────────────────────────────────────────────
        void OnStateUpdated(GameState state)
        {
            if (state == null) return;
            string myId = GameStateManager.Instance?.MyPlayerId;
            bool imSeated = false;
            if (!string.IsNullOrEmpty(myId) && state.players != null)
                foreach (var p in state.players)
                    if (p?.id == myId) { imSeated = true; break; }

            if (_pendingSitDown && !imSeated && !string.IsNullOrEmpty(myId))
            {
                _pendingSitDown = false;
                SitDownOnFreeSeat(state, _pendingBuyIn);
                return;
            }

            GamePhase phase = state.GetPhase();
            bool inGame = phase != GamePhase.WAITING;

            if (inGame)   { ShowPanel(false); _inWaiting = false; return; }

            ShowPanel(true);

            if (imSeated)
            {
                string code = state.room_code ?? GameStateManager.Instance?.RoomCode ?? "------";
                int    cnt  = state.players?.Count ?? 1;
                ShowWaitingRoom(code, cnt, MAX_PLAYERS);
                SetStatus($"Room {code}  —  {cnt} / {MAX_PLAYERS} players", StatusState.Waiting);
            }
            else if (_inWaiting)
            {
                _inWaiting = false;
                SwitchTab(_currentTab, force: true);
            }
        }

        void SitDownOnFreeSeat(GameState state, int buyIn)
        {
            var occupied = new HashSet<int>();
            if (state.players != null)
                foreach (var p in state.players)
                    if (p != null && p.seat >= 0) occupied.Add(p.seat);
            int seat = 0;
            while (occupied.Contains(seat)) seat++;
            GameStateManager.Instance.SitDown(seat, buyIn);
        }

        void OnError(string msg) => SetStatus($"⚠ {msg}", StatusState.Offline);

        // ── Visibility ────────────────────────────────────────────────────────
        public void ShowPanel(bool show)
        {
            if (_root != null) _root.SetActive(show);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        string GetName(TMP_InputField f)
        {
            string raw = (f?.text ?? "").Trim();
            return string.IsNullOrEmpty(raw) ? "Player" : raw;
        }

        int GetBuyIn(TMP_InputField f)
        {
            return int.TryParse(f?.text, out int v) ? Mathf.Max(100, v) : 1000;
        }

        enum StatusState { Online, Waiting, Offline }

        void SetStatus(string msg, StatusState state)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
            if (_statusDot != null)
            {
                _statusDot.color = state switch
                {
                    StatusState.Online  => STATUS_ONLINE,
                    StatusState.Waiting => STATUS_WAITING,
                    _                   => STATUS_OFFLINE
                };
            }
        }

        // ── Generic UGUI factories ────────────────────────────────────────────
        static GameObject NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        static TMP_Text NewText(string name, RectTransform parent, string text, float size,
                                Color color, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform NewVertical(string name, RectTransform parent, float spacing,
                                         RectOffset padding, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = padding; vlg.spacing = spacing;
            vlg.childAlignment = align;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            return go.GetComponent<RectTransform>();
        }

        static RectTransform NewHorizontal(string name, RectTransform parent, float spacing,
                                           RectOffset padding, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = padding; hlg.spacing = spacing;
            hlg.childAlignment = align;
            // Control sizes so LayoutElement.preferredWidth/Height drive each child.
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 22;
            return go.GetComponent<RectTransform>();
        }

        static void NewSpacer(RectTransform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            NormalizeRect(go.GetComponent<RectTransform>());
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.flexibleHeight = 0;
        }

        static Color HexColor(string hex, float alpha = 1f)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var c)) { c.a = alpha; return c; }
            return new Color(1, 1, 1, alpha);
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }
    }
}
