using UnityEngine;

namespace Poker.UI
{
    // Generates simple procedural sprites at runtime so prefabs/scene state
    // don't have to ship circle/chip art. Sprites are cached on first use.
    public static class SpriteFactory
    {
        private static Sprite _circle;
        private static Sprite _ring;
        private static Sprite _ringThin;
        private static Sprite _pill;
        private static Sprite _pillRing;
        private static Sprite _chip;
        private static Sprite _cardFace;
        private static Sprite _cardBack;
        private static Sprite _cardEmpty;
        private static Sprite _cardHero;
        private static Sprite _avatar;
        private static Sprite _table;
        private static readonly Sprite[] _chipDenominations = new Sprite[6];
        private static readonly Sprite[] _bots = new Sprite[6];

        // Resources lookup paths — SVGs imported via com.unity.vectorgraphics
        // produce Sprite assets at these paths. Returns null if the package
        // isn't installed or the asset is missing, so callers can fall through
        // to the procedural fallback.
        static Sprite LoadSvg(string path)
        {
            try { return Resources.Load<Sprite>(path); }
            catch { return null; }
        }

        // White rounded-rect card face with a soft inner border. Aspect 5:7
        // so callers can stretch to any card size and the corners stay round.
        public static Sprite CardFace()
        {
            if (_cardFace != null) return _cardFace;
            _cardFace = LoadSvg("Cards/card_face");
            if (_cardFace != null) return _cardFace;
            int w = 250, h = 350; // 5:7 ratio
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float radius = 26f;
            Color paper      = new Color(0.985f, 0.985f, 0.97f);
            Color paperEdge  = new Color(0.92f,  0.92f,  0.88f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a = RoundedRectAlpha(x, y, w, h, radius);
                    if (a <= 0f) { tex.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
                    float edgeBlend = Mathf.Clamp01(InsetDist(x, y, w, h, radius) / 6f);
                    Color c = Color.Lerp(paperEdge, paper, edgeBlend);
                    c.a *= a;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            _cardFace = Sprite.Create(tex, new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(28, 28, 28, 28)); // 9-slice borders
            return _cardFace;
        }

        // Casino-blue diamond pattern with double border. The pattern repeats
        // at the texture level, so the back stays crisp at any card size.
        public static Sprite CardBack()
        {
            if (_cardBack != null) return _cardBack;
            _cardBack = LoadSvg("Cards/card_back");
            if (_cardBack != null) return _cardBack;
            int w = 250, h = 350;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float radius = 26f;
            Color deep   = new Color(0.07f, 0.16f, 0.42f);
            Color mid    = new Color(0.14f, 0.30f, 0.66f);
            Color hilite = new Color(0.35f, 0.55f, 0.90f);
            Color border = new Color(0.95f, 0.95f, 0.92f);
            float borderInset = 8f;
            float innerInset  = 14f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a = RoundedRectAlpha(x, y, w, h, radius);
                    if (a <= 0f) { tex.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
                    float inset = InsetDist(x, y, w, h, radius);
                    Color c;
                    if (inset < borderInset)         c = border;        // outer rim
                    else if (inset < innerInset)     c = deep;          // gap
                    else
                    {
                        // Interior diamond pattern
                        float u = (x - w * 0.5f) * 0.18f;
                        float v = (y - h * 0.5f) * 0.18f;
                        float d = Mathf.Abs(Mathf.Sin(u + v)) + Mathf.Abs(Mathf.Sin(u - v));
                        c = Color.Lerp(deep, mid, Mathf.Clamp01(d * 0.55f));
                        // Subtle highlight crosshatch
                        float h2 = Mathf.Sin((u + v) * 1.7f) * Mathf.Sin((u - v) * 1.7f);
                        if (h2 > 0.85f) c = Color.Lerp(c, hilite, 0.35f);
                    }
                    c.a *= a;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            _cardBack = Sprite.Create(tex, new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(28, 28, 28, 28));
            return _cardBack;
        }

        // Per-pixel coverage (0..1) of a rounded-rect at point (x,y) in a w×h
        // texture. Returns 0 outside the rect, near-1 well inside, with a
        // 1-pixel anti-aliased ring on the outer edge.
        static float RoundedRectAlpha(int x, int y, int w, int h, float r)
        {
            float l = r, b = r, rt = w - r, t = h - r;
            float cx, cy;
            if (x < l && y > t)       { cx = l;  cy = t; }
            else if (x > rt && y > t) { cx = rt; cy = t; }
            else if (x < l && y < b)  { cx = l;  cy = b; }
            else if (x > rt && y < b) { cx = rt; cy = b; }
            else
            {
                if (x < 0 || x >= w || y < 0 || y >= h) return 0f;
                return 1f;
            }
            float dx = x - cx, dy = y - cy;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(r - d);
        }

        // Distance from (x,y) to the nearest rect edge (positive inside).
        static float InsetDist(int x, int y, int w, int h, float r)
        {
            float dxL = x;
            float dxR = (w - 1) - x;
            float dyB = y;
            float dyT = (h - 1) - y;
            float orth = Mathf.Min(Mathf.Min(dxL, dxR), Mathf.Min(dyB, dyT));
            // Round corner inset
            float l = r, b = r, rt = w - r, t = h - r;
            if (x < l && y > t)       return r - Mathf.Sqrt((x - l) * (x - l) + (y - t) * (y - t));
            if (x > rt && y > t)      return r - Mathf.Sqrt((x - rt) * (x - rt) + (y - t) * (y - t));
            if (x < l && y < b)       return r - Mathf.Sqrt((x - l) * (x - l) + (y - b) * (y - b));
            if (x > rt && y < b)      return r - Mathf.Sqrt((x - rt) * (x - rt) + (y - b) * (y - b));
            return orth;
        }

        // Picks a stable bot variant from a string (e.g. player id / name).
        public static Sprite BotAvatar(string key)
        {
            int hash = 0;
            if (!string.IsNullOrEmpty(key))
                foreach (var ch in key) hash = (hash * 31 + ch) & 0x7fffffff;
            int variant = hash % _bots.Length;
            if (_bots[variant] == null) _bots[variant] = MakeBot(variant);
            return _bots[variant];
        }

        // Generic human avatar — prefers the SVG `avatar` asset (gold ring +
        // timer arc) for a single coherent player look. Falls back to a hashed
        // bot-style disc if no SVG is present.
        public static Sprite HumanAvatar(string key)
        {
            var svg = Avatar();
            if (svg != null) return svg;
            return BotAvatar("human:" + key);
        }

        public static Sprite Circle()
        {
            if (_circle != null) return _circle;
            _circle = MakeCircle(256, Color.white, 0f, 0f);
            return _circle;
        }

        public static Sprite Ring()
        {
            if (_ring != null) return _ring;
            _ring = MakeRing(256, Color.white, 0.78f, 0.96f);
            return _ring;
        }

        // Hairline ring used for avatar borders. Image.color tints to hue.
        public static Sprite RingThin()
        {
            if (_ringThin != null) return _ringThin;
            _ringThin = MakeRing(256, Color.white, 0.93f, 0.98f);
            return _ringThin;
        }

        // 9-sliced rounded rect for pill-style backgrounds (info card, bet
        // chip, pot bar). White fill so callers tint via Image.color.
        public static Sprite Pill()
        {
            if (_pill != null) return _pill;
            int w = 96, h = 48;
            float r = 22f;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a = RoundedRectAlpha(x, y, w, h, r);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _pill = Sprite.Create(tex, new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(r, r, r, r));
            return _pill;
        }

        // 9-sliced rounded-rect STROKE (transparent center). Used for
        // button/panel rims that need to remain rectangular at any aspect
        // ratio. The previous "Ring" sprite was a circle and stretched
        // into ovals on wide rects — this one preserves rounded corners
        // because it 9-slices like Pill().
        public static Sprite PillRing()
        {
            if (_pillRing != null) return _pillRing;
            int w = 96, h = 48;
            float r = 22f;
            int stroke = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float outer = RoundedRectAlpha(x, y, w, h, r);
                    // Inner shape: same rect inset by `stroke` on all
                    // sides, with a slightly tighter corner radius.
                    float inner = RoundedRectAlpha(
                        x - stroke, y - stroke,
                        w - 2 * stroke, h - 2 * stroke,
                        Mathf.Max(0f, r - stroke));
                    // Stroke = where outer is opaque AND inner is transparent.
                    float a = Mathf.Clamp01(outer - inner);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _pillRing = Sprite.Create(tex, new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(r, r, r, r));
            return _pillRing;
        }

        // Stable per-key emoji + accent color for the avatar icon. Hero gets
        // the theatrical mask to match the expected hero treatment.
        static readonly string[] _avatarEmojis = {
            "\U0001F98A", // fox
            "\U0001F431", // cat
            "\U0001F43A", // wolf
            "\U0001F981", // lion
            "\U0001F42F", // tiger
            "\U0001F43B", // bear
            "\U0001F43C", // panda
            "\U0001F47D", // alien
            "\U0001F920", // cowboy
            "\U0001F9D9", // mage
        };
        static readonly Color[] _avatarBgs = {
            new Color(0.20f, 0.16f, 0.42f),
            new Color(0.18f, 0.32f, 0.46f),
            new Color(0.36f, 0.18f, 0.36f),
            new Color(0.30f, 0.22f, 0.10f),
            new Color(0.10f, 0.28f, 0.32f),
            new Color(0.36f, 0.20f, 0.20f),
            new Color(0.18f, 0.30f, 0.20f),
        };

        public static string EmojiFor(string key, bool hero)
        {
            if (hero) return "\U0001F3AD"; // 🎭 mask for the human hero
            int hash = StableHash(key);
            return _avatarEmojis[hash % _avatarEmojis.Length];
        }

        public static Color AvatarBgFor(string key)
        {
            int hash = StableHash(key);
            return _avatarBgs[hash % _avatarBgs.Length];
        }

        static int StableHash(string s)
        {
            int h = 0;
            if (string.IsNullOrEmpty(s)) return 0;
            foreach (var ch in s) h = (h * 31 + ch) & 0x7fffffff;
            return h;
        }

        // Empty card slot — dashed outline. Used as a placeholder before a
        // street is dealt. Returns null if no SVG asset is available; callers
        // can simply hide the slot in that case.
        public static Sprite CardEmpty()
        {
            if (_cardEmpty != null) return _cardEmpty;
            _cardEmpty = LoadSvg("Cards/card_empty");
            return _cardEmpty;
        }

        // Hero (gold-bordered) card face. Falls back to plain CardFace if the
        // SVG asset isn't present.
        public static Sprite CardHero()
        {
            if (_cardHero != null) return _cardHero;
            _cardHero = LoadSvg("Cards/card_hero");
            return _cardHero != null ? _cardHero : CardFace();
        }

        // Stylized table felt + rail. Returns null if the SVG isn't present;
        // callers should fall back to a flat color background in that case.
        public static Sprite Table()
        {
            if (_table != null) return _table;
            _table = LoadSvg("table");
            return _table;
        }

        // Avatar ring with timer arc — used for human players. Falls back to
        // the procedural human disc when the SVG isn't present.
        public static Sprite Avatar()
        {
            if (_avatar != null) return _avatar;
            _avatar = LoadSvg("avatar");
            return _avatar;
        }

        // Pick the chip denomination sprite that best represents an amount.
        // Used by chip-fly animations to vary chip color by bet size.
        public static Sprite ChipFor(int amount)
        {
            int idx;
            if      (amount >= 1_000_000) idx = 5;
            else if (amount >=   500_000) idx = 4;
            else if (amount >=   100_000) idx = 3;
            else if (amount >=    25_000) idx = 2;
            else if (amount >=     5_000) idx = 1;
            else                          idx = 0;
            if (_chipDenominations[idx] != null) return _chipDenominations[idx];
            string name = idx switch {
                0 => "Chips/chip_1k",
                1 => "Chips/chip_5k",
                2 => "Chips/chip_25k",
                3 => "Chips/chip_100k",
                4 => "Chips/chip_500k",
                _ => "Chips/chip_1m",
            };
            _chipDenominations[idx] = LoadSvg(name);
            return _chipDenominations[idx] != null ? _chipDenominations[idx] : Chip();
        }

        // Poker chip: outer band, inner disc, simple notches.
        public static Sprite Chip()
        {
            if (_chip != null) return _chip;
            // Prefer the orange "1M" SVG chip when the asset is present so the
            // fallback path keeps the same look as ChipFor() at high amounts.
            _chip = LoadSvg("Chips/chip_1m");
            if (_chip != null) return _chip;
            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            Color outer = new Color(0.92f, 0.45f, 0.10f);   // orange
            Color band  = new Color(1.00f, 0.95f, 0.85f);   // cream notches
            Color inner = new Color(0.82f, 0.30f, 0.05f);   // darker disc
            float cx = size * 0.5f, cy = size * 0.5f;
            float rOuter = size * 0.48f;
            float rBand  = size * 0.38f;
            float rInner = size * 0.30f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > rOuter + 1f)
                    {
                        tex.SetPixel(x, y, new Color(0, 0, 0, 0));
                        continue;
                    }
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    int slice = Mathf.FloorToInt((angle + 360f) / 45f);
                    bool notch = (slice % 2 == 0) && d > rBand && d < rOuter - 2f;
                    Color c = d < rInner ? inner : (notch ? band : outer);
                    // soft anti-alias edge
                    float aOuter = Mathf.Clamp01(rOuter - d);
                    c.a = aOuter;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            _chip = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _chip;
        }

        static Sprite MakeCircle(int size, Color col, float innerR, float outerEdge)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float cx = size * 0.5f, cy = size * 0.5f;
            float rOut = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rOut - d);
                    var c = col; c.a *= a;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // Six color/face variants of a friendly robot face inside a disc.
        static Sprite MakeBot(int variant)
        {
            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            // Pick a palette per variant
            Color bg, body, accent;
            switch (variant % 6)
            {
                case 0: bg = new Color(0.18f, 0.42f, 0.78f); body = new Color(0.86f, 0.93f, 1f); accent = new Color(0.10f, 0.18f, 0.30f); break;
                case 1: bg = new Color(0.20f, 0.66f, 0.45f); body = new Color(0.92f, 1f, 0.94f);   accent = new Color(0.10f, 0.30f, 0.20f); break;
                case 2: bg = new Color(0.78f, 0.32f, 0.32f); body = new Color(1f, 0.92f, 0.90f);   accent = new Color(0.30f, 0.10f, 0.10f); break;
                case 3: bg = new Color(0.55f, 0.32f, 0.78f); body = new Color(0.95f, 0.90f, 1f);   accent = new Color(0.20f, 0.10f, 0.30f); break;
                case 4: bg = new Color(0.85f, 0.62f, 0.18f); body = new Color(1f, 0.96f, 0.86f);   accent = new Color(0.30f, 0.20f, 0.05f); break;
                default:bg = new Color(0.30f, 0.55f, 0.60f); body = new Color(0.92f, 0.98f, 1f);   accent = new Color(0.10f, 0.20f, 0.25f); break;
            }
            Color eyeColor = new Color(1f, 0.88f, 0.20f); // glowing yellow eyes

            float cx = size * 0.5f;
            float cy = size * 0.5f;
            float rDisc = size * 0.5f - 1f;

            // Head box dims (rounded rect)
            float headW = size * 0.46f, headH = size * 0.40f;
            float headT = cy - size * 0.04f;       // top edge of head box (above center)
            float headB = headT - headH;
            float headL = cx - headW * 0.5f;
            float headR = cx + headW * 0.5f;
            float headRound = size * 0.10f;

            // Eyes
            float eyeY = headT - headH * 0.45f;
            float eyeR = size * 0.055f;
            float eyeLx = cx - headW * 0.22f;
            float eyeRx = cx + headW * 0.22f;

            // Mouth (rounded rect)
            float mouthY = headT - headH * 0.78f;
            float mouthW = headW * 0.45f;
            float mouthH = size * 0.055f;
            float mouthL = cx - mouthW * 0.5f;
            float mouthR = cx + mouthW * 0.5f;
            float mouthT = mouthY + mouthH * 0.5f;
            float mouthB = mouthY - mouthH * 0.5f;

            // Antenna
            float antX = cx;
            float antBaseY = headT;
            float antTopY  = headT + size * 0.10f;
            float antDotR  = size * 0.045f;
            float antRodHalf = size * 0.012f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > rDisc + 1f)
                    {
                        tex.SetPixel(x, y, new Color(0, 0, 0, 0));
                        continue;
                    }
                    float discAlpha = Mathf.Clamp01(rDisc - d);
                    Color px = bg;

                    // Antenna rod
                    if (x > antX - antRodHalf && x < antX + antRodHalf
                        && y > antBaseY && y < antTopY)
                    {
                        px = accent;
                    }
                    // Antenna dot
                    float adx = x - antX, ady = y - antTopY;
                    if (adx * adx + ady * ady < antDotR * antDotR)
                    {
                        px = eyeColor;
                    }

                    // Head body (rounded rect)
                    if (InRoundedRect(x, y, headL, headR, headB, headT, headRound))
                    {
                        px = body;

                        // Eyes
                        float elx = x - eyeLx, ely = y - eyeY;
                        float erx = x - eyeRx, ery = y - eyeY;
                        if (elx * elx + ely * ely < eyeR * eyeR
                            || erx * erx + ery * ery < eyeR * eyeR)
                        {
                            px = eyeColor;
                        }

                        // Mouth
                        if (x > mouthL && x < mouthR && y > mouthB && y < mouthT)
                        {
                            px = accent;
                            // Two notches to suggest grill
                            float third = (mouthR - mouthL) / 3f;
                            if (Mathf.Abs(x - (mouthL + third)) < 1f
                                || Mathf.Abs(x - (mouthL + 2 * third)) < 1f)
                                px = body;
                        }
                    }

                    px.a *= discAlpha;
                    tex.SetPixel(x, y, px);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        static bool InRoundedRect(float x, float y, float l, float r, float b, float t, float radius)
        {
            if (x < l || x > r || y < b || y > t) return false;
            // Corner clipping
            float cx, cy;
            if (x < l + radius && y > t - radius) { cx = l + radius; cy = t - radius; }
            else if (x > r - radius && y > t - radius) { cx = r - radius; cy = t - radius; }
            else if (x < l + radius && y < b + radius) { cx = l + radius; cy = b + radius; }
            else if (x > r - radius && y < b + radius) { cx = r - radius; cy = b + radius; }
            else return true;
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        static Sprite MakeRing(int size, Color col, float innerFrac, float outerFrac)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float cx = size * 0.5f, cy = size * 0.5f;
            float rOut = size * outerFrac * 0.5f;
            float rIn  = size * innerFrac * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float aOut = Mathf.Clamp01(rOut - d);
                    float aIn  = Mathf.Clamp01(d - rIn);
                    var c = col; c.a *= Mathf.Min(aOut, aIn);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
