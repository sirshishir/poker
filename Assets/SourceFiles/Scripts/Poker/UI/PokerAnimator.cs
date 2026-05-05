using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Poker.UI
{
    /// <summary>
    /// Handles all game animations: card deal fly-in, card flip reveal,
    /// chip toss, seat pulse, winner celebration.
    /// </summary>
    public class PokerAnimator : MonoBehaviour
    {
        public static PokerAnimator Instance { get; private set; }

        [Header("Deal Settings")]
        public RectTransform deckPosition;   // Where cards fly from
        public float dealDuration = 0.55f;             // slower, more natural arc
        public float dealDelayBetweenCards = 0.22f;    // gives the eye time to track each card
        public AnimationCurve dealCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public float dealSpinDegrees = 540f;           // cards spin while flying
        public float dealLandBounce  = 0.08f;          // overshoot fraction on landing

        [Header("Flip Settings")]
        public float flipDuration = 0.42f;

        [Header("Chip Settings")]
        public float chipMoveDuration = 0.65f;
        public float chipArcHeight    = 110f;          // peak height of the chip arc
        public float chipSpinDegrees  = 720f;          // chip tumbles while flying
        public GameObject chipFlyPrefab; // Optional - falls back to simple label

        [Header("Glow Colors")]
        public Color activeGlowColor  = new Color(1f, 0.9f, 0.1f, 1f);
        public Color winnerGlowColor  = new Color(0.2f, 1f, 0.4f, 1f);
        public Color heroGlowColor    = new Color(0.3f, 0.7f, 1f, 1f);

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ── Card deal (fly from deck position) ────────────────────────────

        public void DealCard(RectTransform card, Vector2 targetAnchoredPos, float delay, Action onDone = null)
        {
            StartCoroutine(DealCardRoutine(card, targetAnchoredPos, delay, onDone));
        }

        IEnumerator DealCardRoutine(RectTransform card, Vector2 target, float delay, Action onDone)
        {
            yield return new WaitForSeconds(delay);

            Vector2 start = deckPosition != null ? deckPosition.anchoredPosition : Vector2.zero;
            card.anchoredPosition = start;
            card.localScale = Vector3.one * 0.4f;
            card.localEulerAngles = new Vector3(0, 0, -dealSpinDegrees);
            card.gameObject.SetActive(true);

            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime / dealDuration;
                float p = Mathf.Clamp01(t);
                float ease = dealCurve.Evaluate(p);
                // Curved flight path: small lateral arc so cards don't travel
                // in straight lines like teleports.
                Vector2 pos = Vector2.Lerp(start, target, ease);
                pos.y += Mathf.Sin(p * Mathf.PI) * 22f;
                card.anchoredPosition = pos;
                // Land slightly overshot (1 + bounce) then settle in tail of curve.
                float scale = Mathf.Lerp(0.4f, 1f + dealLandBounce, ease);
                if (p > 0.85f) scale = Mathf.Lerp(1f + dealLandBounce, 1f, (p - 0.85f) / 0.15f);
                card.localScale = Vector3.one * scale;
                card.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(-dealSpinDegrees, 0f, ease));
                yield return null;
            }
            card.anchoredPosition = target;
            card.localScale = Vector3.one;
            card.localEulerAngles = Vector3.zero;
            onDone?.Invoke();
        }

        // ── Card flip reveal (scale X to 0 then back, swap content) ───────

        public void FlipReveal(RectTransform card, Action onMidFlip, Action onDone = null)
        {
            StartCoroutine(FlipRoutine(card, onMidFlip, onDone));
        }

        IEnumerator FlipRoutine(RectTransform card, Action onMid, Action onDone)
        {
            // 3D-style flip via Y-axis rotation. Combined with a small lift
            // (Z translation faked through scale) it feels like a real card
            // turning over rather than a sprite squashing.
            float half = flipDuration * 0.5f;
            float t = 0;
            Vector3 baseScale = card.localScale;
            while (t < 1f)
            {
                t += Time.deltaTime / half;
                float p = Mathf.Clamp01(t);
                card.localEulerAngles = new Vector3(0, p * 90f, 0);
                float lift = 1f + Mathf.Sin(p * Mathf.PI) * 0.06f;
                card.localScale = new Vector3(baseScale.x * lift, baseScale.y * lift, baseScale.z);
                yield return null;
            }
            onMid?.Invoke();
            t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime / half;
                float p = Mathf.Clamp01(t);
                card.localEulerAngles = new Vector3(0, 90f - p * 90f, 0);
                float lift = 1f + Mathf.Sin((1f - p) * Mathf.PI) * 0.06f;
                card.localScale = new Vector3(baseScale.x * lift, baseScale.y * lift, baseScale.z);
                yield return null;
            }
            card.localScale = baseScale;
            card.localEulerAngles = Vector3.zero;
            onDone?.Invoke();
        }

        // ── Reveal community cards one by one ─────────────────────────────

        public void RevealCommunityCards(CardUI[] cards, int fromIndex, int toIndex, float delayBetween = 0.2f)
        {
            StartCoroutine(RevealSequence(cards, fromIndex, toIndex, delayBetween));
        }

        IEnumerator RevealSequence(CardUI[] cards, int from, int to, float gap)
        {
            for (int i = from; i <= to && i < cards.Length; i++)
            {
                var card = cards[i];
                if (card == null) continue;
                var rt = card.GetComponent<RectTransform>();
                if (rt != null)
                    FlipReveal(rt, () => card.ShowFaceUp(), null);
                yield return new WaitForSeconds(gap);
            }
        }

        // ── Seat pulse (active turn highlight) ────────────────────────────

        private readonly Dictionary<Image, Coroutine> _pulseRoutines = new Dictionary<Image, Coroutine>();

        public void PulseSeat(Image border, bool active)
        {
            if (border == null) return;
            if (_pulseRoutines.TryGetValue(border, out var existing) && existing != null)
            {
                StopCoroutine(existing);
                _pulseRoutines.Remove(border);
            }
            if (active)
                _pulseRoutines[border] = StartCoroutine(PulseRoutine(border, activeGlowColor));
            // When inactive, leave color as-is — PlayerSeatUI sets border color each refresh.
        }

        IEnumerator PulseRoutine(Image border, Color target)
        {
            Color dim = new Color(target.r * 0.4f, target.g * 0.4f, target.b * 0.4f, target.a);
            while (border != null && border.gameObject.activeSelf)
            {
                float t = (Mathf.Sin(Time.time * 4f) + 1f) * 0.5f;
                border.color = Color.Lerp(dim, target, t);
                yield return null;
            }
        }

        // ── Winner celebration ─────────────────────────────────────────────

        public void CelebrateSeat(RectTransform seat, float duration = 3f)
        {
            StartCoroutine(CelebrateRoutine(seat, duration));
        }

        IEnumerator CelebrateRoutine(RectTransform seat, float dur)
        {
            float end = Time.time + dur;
            Vector3 orig = seat.localScale;
            while (Time.time < end)
            {
                float t = (Mathf.Sin(Time.time * 8f) + 1f) * 0.5f;
                seat.localScale = Vector3.Lerp(orig, orig * 1.06f, t);
                yield return null;
            }
            seat.localScale = orig;
        }

        // ── Chip fly to pot ───────────────────────────────────────────────

        public void FlyChipToPot(Vector2 from, Vector2 potPos, Transform uiParent, int amount)
        {
            StartCoroutine(ChipFlyRoutine(from, potPos, uiParent, amount));
        }

        IEnumerator ChipFlyRoutine(Vector2 from, Vector2 to, Transform parent, int amount)
        {
            var chip = new GameObject("FlyingChip");
            chip.transform.SetParent(parent, false);
            var rt = chip.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(46, 46);
            rt.anchoredPosition = from;
            var img = chip.AddComponent<UnityEngine.UI.Image>();
            // Use the project's chip sprite so it actually looks like a chip
            // (red/white striped disk) rather than a flat purple square.
            // Pick a chip color matching the bet size (1K silver → 1M gold).
            var chipSprite = SpriteFactory.ChipFor(amount);
            if (chipSprite != null)
            {
                img.sprite = chipSprite;
                img.color = Color.white;
                img.preserveAspect = true;
            }
            else img.color = new Color(0.85f, 0.18f, 0.25f);

            // Float a small amount label above the chip while it flies.
            var label = new GameObject("Label");
            label.transform.SetParent(chip.transform, false);
            var lRT = label.AddComponent<RectTransform>();
            lRT.anchorMin = new Vector2(0.5f, 1f);
            lRT.anchorMax = new Vector2(0.5f, 1f);
            lRT.pivot     = new Vector2(0.5f, 0f);
            lRT.sizeDelta = new Vector2(80, 20);
            lRT.anchoredPosition = new Vector2(0, 4);
            var tmp = label.AddComponent<TextMeshProUGUI>();
            tmp.text = $"${amount}";
            tmp.fontSize = 16;
            tmp.color = new Color(1f, 0.86f, 0.25f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.outlineColor = new Color(0, 0, 0, 0.85f);
            tmp.outlineWidth = 0.25f;

            // Slight randomness so successive bets don't trace the same line.
            Vector2 jitter = new Vector2(UnityEngine.Random.Range(-12f, 12f),
                                         UnityEngine.Random.Range(-8f, 8f));
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / chipMoveDuration;
                float p = Mathf.Clamp01(t);
                // Quadratic bezier through a control point above the midpoint
                // — gives a nicer ballistic arc than sin-bump linear lerp.
                Vector2 mid = (from + to) * 0.5f + new Vector2(0, chipArcHeight) + jitter;
                Vector2 a = Vector2.Lerp(from, mid, p);
                Vector2 b = Vector2.Lerp(mid, to, p);
                rt.anchoredPosition = Vector2.Lerp(a, b, p);
                rt.localEulerAngles = new Vector3(0, 0, p * chipSpinDegrees);
                rt.localScale = Vector3.one * Mathf.Lerp(1.1f, 0.85f, p);
                yield return null;
            }
            Destroy(chip);
        }

        // ── Float-up action label ─────────────────────────────────────────

        public void ShowFloatingText(string text, Vector2 worldPos, Transform parent, Color color)
        {
            StartCoroutine(FloatTextRoutine(text, worldPos, parent, color));
        }

        IEnumerator FloatTextRoutine(string text, Vector2 startPos, Transform parent, Color color)
        {
            var go = new GameObject("FloatText");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = startPos;
            rt.sizeDelta = new Vector2(200, 50);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 28; tmp.color = color;
            tmp.fontStyle = FontStyles.Bold; tmp.alignment = TextAlignmentOptions.Center;

            float dur = 1.2f, t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                rt.anchoredPosition = startPos + new Vector2(0, t * 60f);
                tmp.color = new Color(color.r, color.g, color.b, 1f - t);
                yield return null;
            }
            Destroy(go);
        }
    }
}
