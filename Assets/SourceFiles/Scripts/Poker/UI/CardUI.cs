using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Poker.Models;

namespace Poker.UI
{
    public class CardUI : MonoBehaviour
    {
        [Header("Card Roots")]
        public GameObject faceUpRoot;
        public GameObject faceDownRoot;

        [Header("Face-Up Elements")]
        public Image     cardBg;
        public TextMeshProUGUI rankTopLeft;
        public TextMeshProUGUI suitTopLeft;
        public TextMeshProUGUI rankCenter;
        public TextMeshProUGUI suitCenter;
        public TextMeshProUGUI rankBotRight;
        public TextMeshProUGUI suitBotRight;

        private bool _layoutApplied;

        public void SetCard(Card card, bool animated = false)
        {
            EnsureLayout();
            if (card == null) { ShowFaceDown(); return; }
            ApplyCard(card);

            if (animated)
            {
                if (faceDownRoot) faceDownRoot.SetActive(true);
                if (faceUpRoot)   faceUpRoot.SetActive(false);
                var rt = GetComponent<RectTransform>();
                PokerAnimator.Instance?.FlipReveal(rt, ShowFaceUp, null);
            }
            else ShowFaceUp();
        }

        // Public so PokerAnimator can call at mid-flip
        public void ApplyCard(Card card)
        {
            EnsureLayout();
            string rank = FormatRank(card.rank);
            string suit = GetSuitChar(card.suit);
            Color  col  = GetSuitColor(card.suit);
            SetT(rankTopLeft,  rank, col);
            SetT(suitTopLeft,  suit, col);
            // Hide center rank — only big center suit
            if (rankCenter)
            {
                rankCenter.text = "";
                rankCenter.gameObject.SetActive(false);
            }
            SetT(suitCenter,   suit, col);
            SetT(rankBotRight, rank, col);
            SetT(suitBotRight, suit, col);
            if (cardBg)
            {
                cardBg.color = Color.white;
                cardBg.sprite = SpriteFactory.CardFace();
                cardBg.type = Image.Type.Sliced;
                cardBg.preserveAspect = false;
                StretchToParent(cardBg.rectTransform);
            }
        }

        // Reposition/style children once at runtime so cards look modern + readable
        // regardless of scene-time layout.
        void EnsureLayout()
        {
            if (_layoutApplied) return;
            _layoutApplied = true;

            // Auto-resolve any missing inspector references from child names.
            ResolveRefs();

            var rt = GetComponent<RectTransform>();
            float w = rt.rect.width;
            // Treat anything ≥ 70px wide as "large" (hero / community cards)
            bool large = w >= 70f;

            float rankFs    = large ? 22f : 11f;
            float suitFs    = large ? 18f : 9f;
            float bigSuitFs = large ? 44f : 20f;
            Vector2 cornerOff   = large ? new Vector2(8, 6)    : new Vector2(3, 3);
            Vector2 rankSize    = large ? new Vector2(40, 28)  : new Vector2(20, 14);
            Vector2 suitSize    = large ? new Vector2(28, 24)  : new Vector2(14, 12);
            Vector2 bigSuitSize = large ? new Vector2(56, 56)  : new Vector2(22, 22);

            // Stretch face roots
            if (faceUpRoot)   StretchToParent(faceUpRoot.GetComponent<RectTransform>());
            if (faceDownRoot) StretchToParent(faceDownRoot.GetComponent<RectTransform>());

            ApplyText(rankTopLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(cornerOff.x, -cornerOff.y),
                rankSize, rankFs, FontStyles.Bold,
                TextAlignmentOptions.TopLeft, 0);
            EnableShrinkToFit(rankTopLeft, rankFs);

            ApplyText(suitTopLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(cornerOff.x, -cornerOff.y - rankSize.y - 2),
                suitSize, suitFs, FontStyles.Normal,
                TextAlignmentOptions.TopLeft, 0);

            ApplyText(suitCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, bigSuitSize, bigSuitFs, FontStyles.Normal,
                TextAlignmentOptions.Center, 0);

            // Rotate around the rect center so 180° flip stays *inside* the card.
            // anchor=(1,0) places the rect at parent's bottom-right; pivot=(0.5,0.5)
            // means anchoredPosition is the rect's center. We push the center inward
            // by (size/2 + cornerOff) so the rect lives wholly inside the card.
            ApplyText(rankBotRight,
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f),
                new Vector2(-cornerOff.x - rankSize.x * 0.5f, cornerOff.y + rankSize.y * 0.5f),
                rankSize, rankFs, FontStyles.Bold,
                TextAlignmentOptions.Center, 180);
            EnableShrinkToFit(rankBotRight, rankFs);

            ApplyText(suitBotRight,
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f),
                new Vector2(-cornerOff.x - suitSize.x * 0.5f, cornerOff.y + rankSize.y + 2 + suitSize.y * 0.5f),
                suitSize, suitFs, FontStyles.Normal,
                TextAlignmentOptions.Center, 180);
        }

        void ResolveRefs()
        {
            if (faceUpRoot == null)
            {
                var t = transform.Find("FaceUp");
                if (t != null) faceUpRoot = t.gameObject;
            }
            if (faceDownRoot == null)
            {
                var t = transform.Find("FaceDown");
                if (t != null) faceDownRoot = t.gameObject;
            }
            // Build FaceUp if missing entirely
            if (faceUpRoot == null)
            {
                var go = new GameObject("FaceUp", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                faceUpRoot = go;
            }
            // Build a styled FaceDown root if missing — a beautiful blue diamond
            // back so cards visibly exist before the flip animation reveals them.
            if (faceDownRoot == null)
            {
                var go = new GameObject("FaceDown", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                faceDownRoot = go;
            }
            // Apply the CardBack sprite to whichever Image is on the FaceDown
            // root (or its descendants). Scene prefabs often nest the visual on
            // a child, so we search instead of adding a fresh Image — that way
            // we never end up with two stacked images.
            var backImg = faceDownRoot.GetComponent<Image>()
                          ?? faceDownRoot.GetComponentInChildren<Image>(true);
            if (backImg == null)
            {
                backImg = faceDownRoot.AddComponent<Image>();
            }
            backImg.sprite = SpriteFactory.CardBack();
            backImg.type = Image.Type.Sliced;
            backImg.color = Color.white;
            backImg.preserveAspect = false;
            backImg.raycastTarget = false;
            Transform face = faceUpRoot.transform;

            if (cardBg == null)
            {
                var t = face.Find("CardBg");
                if (t != null) cardBg = t.GetComponent<Image>();
                if (cardBg == null)
                {
                    var go = new GameObject("CardBg", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(face, false);
                    go.transform.SetAsFirstSibling();
                    cardBg = go.GetComponent<Image>();
                }
            }
            // CardBg must render BEHIND text
            cardBg.transform.SetAsFirstSibling();

            if (rankTopLeft  == null) rankTopLeft  = FindOrCreateTMP(face, "RankTopLeft");
            if (suitTopLeft  == null) suitTopLeft  = FindOrCreateTMP(face, "SuitTopLeft");
            if (rankCenter   == null) rankCenter   = FindTMP(face, "RankCenter");
            if (suitCenter   == null) suitCenter   = FindOrCreateTMP(face, "SuitCenter");
            if (rankBotRight == null) rankBotRight = FindOrCreateTMP(face, "RankBotRight");
            if (suitBotRight == null) suitBotRight = FindOrCreateTMP(face, "SuitBotRight");
        }

        static TextMeshProUGUI FindTMP(Transform parent, string name)
        {
            var t = parent.Find(name);
            return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
        }

        static TextMeshProUGUI FindOrCreateTMP(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                var tmp = existing.GetComponent<TextMeshProUGUI>();
                if (tmp != null) return tmp;
                return existing.gameObject.AddComponent<TextMeshProUGUI>();
            }
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TextMeshProUGUI>();
        }

        static void StretchToParent(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        static void EnableShrinkToFit(TextMeshProUGUI t, float maxFs)
        {
            if (t == null) return;
            t.enableAutoSizing = true;
            t.fontSizeMin = Mathf.Max(8, maxFs * 0.6f);
            t.fontSizeMax = maxFs;
            t.overflowMode = TextOverflowModes.Truncate;
        }

        static void ApplyText(TextMeshProUGUI t, Vector2 aMin, Vector2 aMax, Vector2 pivot,
            Vector2 pos, Vector2 size, float fs, FontStyles style, TextAlignmentOptions align, float zRot)
        {
            if (t == null) return;
            var rt = t.rectTransform;
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            rt.localEulerAngles = new Vector3(0, 0, zRot);
            t.fontSize = fs;
            t.fontStyle = style;
            t.alignment = align;
            t.enableAutoSizing = false;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            t.gameObject.SetActive(true);
        }

        public void ShowFaceUp()
        {
            if (faceDownRoot) faceDownRoot.SetActive(false);
            if (faceUpRoot)   faceUpRoot.SetActive(true);
        }

        // Force a reflow: call after the parent RectTransform has resized so corner
        // text font sizes / positions adapt to the new card size.
        public void Reflow()
        {
            _layoutApplied = false;
            EnsureLayout();
        }

        public void ShowFaceDown()
        {
            EnsureLayout();
            if (faceUpRoot)   faceUpRoot.SetActive(false);
            if (faceDownRoot) faceDownRoot.SetActive(true);
        }

        public void SetEmpty() => gameObject.SetActive(false);
        public void Show()     => gameObject.SetActive(true);

        static void SetT(TextMeshProUGUI t, string v, Color c)
        {
            if (t == null) return; t.text = v; t.color = c;
        }

        static string FormatRank(string r) => r switch
        {
            "T" => "10", "J" => "J", "Q" => "Q", "K" => "K", "A" => "A", _ => r
        };
        static Color  GetSuitColor(string s) => (s=="h"||s=="d") ? new Color(0.85f,0.08f,0.08f) : new Color(0.06f,0.06f,0.06f);
        static string GetSuitChar(string s) => s switch { "h"=>"♥","d"=>"♦","c"=>"♣","s"=>"♠",_=>s };
    }
}
