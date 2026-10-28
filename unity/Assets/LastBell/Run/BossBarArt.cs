using UnityEngine;

namespace LastBell.Run
{
    // Boss health bar art for the IMGUI HUD: ornamental end pieces and an animated "HP pool" fill.
    // Painted assets are optional: drop transparent PNGs into Resources/UI/BossBar/ (ornament_left, ornament_right,
    // fill_liquid) and they replace the procedural fallbacks below without code changes.
    public static class BossBarArt
    {
        static Texture2D ornL, ornR, liquid, ripple, smoke;
        static bool loaded, painted;

        static void Load()
        {
            if (loaded && ornL && ornR && liquid && ripple && smoke) return;
            loaded = true;
            ornL = Resources.Load<Texture2D>("UI/BossBar/ornament_left");
            ornR = Resources.Load<Texture2D>("UI/BossBar/ornament_right");
            liquid = Resources.Load<Texture2D>("UI/BossBar/fill_liquid");
            painted = ornL && ornR;
            if (!ornL) ornL = Ornament(false);
            if (!ornR) ornR = ornL == null ? null : Ornament(true);
            if (!liquid) liquid = Liquid();
            if (!ripple) ripple = Ripple();
            if (!smoke) smoke = Smoke();
        }

        // Ornament size relative to the bar frame height (art is 2:1, vertically centred on the bar ends).
        // Inner = fraction of the art width (from its outer end) where the crest ends and the spear begins: the crest sits
        // outside the bar end and the spear tucks under the bar, so the HP fill is never covered.
        public const float OrnamentAspect = 2f;
        static float OrnamentHeight => painted ? 3.4f : 3.0f;
        static float Inner => painted ? 0.53f : 0.56f;

        // bar: the inner fill rect in screen px. frac = live HP, trail = damage chip, flash 0..1 after a hit.
        public static void Draw(Rect bar, float frac, float trail, float alpha, float flash, bool lowHp)
        {
            Load();
            float t = Time.unscaledTime;
            var fill = new Rect(bar.x, bar.y, bar.width * frac, bar.height);
            if (fill.width > 0.5f)
            {
                // The whole bar is a liquid: the crimson pool drifts, and two layers of smoke waves roll through it in
                // opposite directions at different scales, so the body of the bar is always moving. Below the low-HP
                // threshold the pool throbs darker.
                // Risxhb, October 26, 2026: the flow must read at a glance, so it drifts about three times faster than the
                // first version, a second counter-flowing pool churns through the first, and the smoke waves are stronger.
                float pulse = lowHp ? 0.82f + 0.18f * Mathf.Sin(t * 6f) : 1f;
                float uvW = fill.width / Mathf.Max(1, bar.height * 8f);
                Draw(fill, liquid, new Rect(t * 0.22f, 0, uvW, 1), new Color(pulse, pulse, pulse, alpha));
                Draw(fill, liquid, new Rect(-t * 0.14f + 0.43f, 0, uvW * 0.6f, 1), new Color(pulse, pulse * 0.9f, pulse * 0.9f, 0.45f * alpha));
                float sw = fill.width / Mathf.Max(1, bar.height * 6f);
                // smoke is darker red than the pool (Risxhb, October 26, 2026), so the currents read against the bright
                // crimson as they roll through; the pale highlight layers were washing the bar towards pink
                Draw(fill, smoke, new Rect(t * 0.24f, Mathf.Sin(t * 0.9f) * 0.3f, sw, 0.9f), new Color(0.36f, 0.02f, 0.03f, 0.62f * alpha));
                Draw(fill, smoke, new Rect(-t * 0.15f + 0.31f, t * 0.12f, sw * 1.7f, 1.3f), new Color(0.24f, 0.0f, 0.02f, 0.6f * alpha));
                Draw(fill, smoke, new Rect(t * 0.38f + 0.67f, -t * 0.08f, sw * 0.8f, 0.7f), new Color(0.48f, 0.05f, 0.05f, 0.45f * alpha));
                // surface ripple along the top of the pool
                float rh = Mathf.Max(2, bar.height * 0.32f);
                Draw(new Rect(fill.x, fill.y, fill.width, rh), ripple, new Rect(t * 0.4f, 0, fill.width / Mathf.Max(1, bar.height * 4f), 1), new Color(1, 1, 1, 0.7f * alpha));
                // depth: darker floor so the pool reads rounded
                UiTheme.Fill(new Rect(fill.x, fill.yMax - bar.height * 0.3f, fill.width, bar.height * 0.3f), new Color(0.16f, 0.01f, 0.01f, 0.35f * alpha));
            }
            // hit flash over the whole live fill
            if (flash > 0.01f && fill.width > 0.5f) UiTheme.Fill(fill, new Color(1f, 0.95f, 0.9f, 0.45f * flash * alpha));
        }

        // Ornaments sit on the bar ends; call BEFORE the frame and fill so the spear lies under the bar.
        public static void DrawOrnaments(Rect frame, float alpha)
        {
            Load();
            float h = frame.height * OrnamentHeight, w = h * OrnamentAspect, cy = frame.center.y;
            var o = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
            float tuck = frame.height * 0.25f;   // crest overlaps the frame end slightly
            GUI.DrawTexture(new Rect(frame.x - w * Inner + tuck, cy - h / 2, w, h), ornL, ScaleMode.StretchToFill, true);
            GUI.DrawTexture(new Rect(frame.xMax - w * (1 - Inner) - tuck, cy - h / 2, w, h), ornR, ScaleMode.StretchToFill, true);
            GUI.color = o;
        }

        static void Draw(Rect r, Texture tex, Rect uv, Color c)
        { var o = GUI.color; GUI.color = c; GUI.DrawTextureWithTexCoords(r, tex, uv, true); GUI.color = o; }

        // ---------------- procedural fallbacks ----------------
        static Texture2D New(int w, int h, TextureWrapMode wrap)
            => new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = wrap, filterMode = FilterMode.Bilinear };

        // Deep crimson pool with slow value-noise swirls; tiles horizontally.
        static Texture2D Liquid()
        {
            const int W = 256, H = 32; var tex = New(W, H, TextureWrapMode.Repeat);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)(H - 1);
                    float n = TileNoise(u * 6, v * 1.5f, 6) * 0.6f + TileNoise(u * 14 + 3, v * 3 + 1, 14) * 0.4f;
                    float depth = Mathf.Lerp(0.55f, 1.08f, v);               // brighter towards the top
                    float band = 0.6f + 0.8f * n;                            // strong swirls so the drift is visible
                    float k = depth * band;
                    px[y * W + x] = new Color(0.74f * k, 0.13f * k + 0.05f * n, 0.11f * k, 1);
                }
            tex.SetPixels(px); tex.Apply(); return tex;
        }
        static float TileNoise(float x, float y, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            float a = Hash(xi % period, yi), b = Hash((xi + 1) % period, yi), c = Hash(xi % period, yi + 1), d = Hash((xi + 1) % period, yi + 1);
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }
        static float Hash(int x, int y) { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return (h & 0xffff) / 65535f; }

        // Smoke waves: soft wisps from fractal value noise that tiles in both directions (so layers can drift freely).
        static Texture2D Smoke()
        {
            const int W = 256, H = 64; var tex = New(W, H, TextureWrapMode.Repeat);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H, n = 0, amp = 0.55f, tot = 0;
                    for (int o = 0; o < 4; o++)
                    {
                        int px0 = 4 << o, py0 = 2 << o;
                        // domain-warped octave: stretched horizontally so the wisps read as rolling waves
                        float wu = u * px0 + 0.35f * Mathf.Sin((v * py0 + o) * Mathf.PI * 2);
                        n += amp * Tile2(wu, v * py0, px0, py0); tot += amp; amp *= 0.5f;
                    }
                    n /= tot;
                    float a = Mathf.SmoothStep(0.42f, 0.78f, n);
                    px[y * W + x] = new Color(1, 1, 1, a);
                }
            tex.SetPixels(px); tex.Apply(); return tex;
        }
        static float Tile2(float x, float y, int px, int py)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            int x0 = ((xi % px) + px) % px, x1 = (x0 + 1) % px, y0 = ((yi % py) + py) % py, y1 = (y0 + 1) % py;
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0 + 97 * px), Hash(x1, y0 + 97 * px), sx), Mathf.Lerp(Hash(x0, y1 + 97 * px), Hash(x1, y1 + 97 * px), sx), sy);
        }

        // Tiling surface-ripple strip: a soft crimson crest that rises and falls along x (alpha only near the crest).
        static Texture2D Ripple()
        {
            const int W = 128, H = 16; var tex = New(W, H, TextureWrapMode.Repeat);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W;
                    float crest = 0.55f + 0.25f * Mathf.Sin(u * Mathf.PI * 2 * 2) + 0.12f * Mathf.Sin(u * Mathf.PI * 2 * 5 + 1.3f);
                    float v = 1 - y / (float)(H - 1);                       // 0 at the top edge
                    float d = Mathf.Abs(v - (1 - crest) * 0.9f);
                    float a = Mathf.Clamp01(1 - d * 5f);
                    tex.SetPixel(x, y, new Color(1f, 0.45f, 0.38f, a * 0.7f));
                }
            tex.Apply(); return tex;
        }

        // Gold filigree end piece (2:1), drawn with signed-distance shapes: a bell-crest diamond, a tapering spear that
        // meets the bar, two scroll curls and small finials. mirror = right-hand piece.
        static Texture2D Ornament(bool mirror)
        {
            const int W = 192, H = 96; var tex = New(W, H, TextureWrapMode.Clamp);
            var gold = new Color(0.90f, 0.74f, 0.44f); var hi = new Color(1f, 0.90f, 0.66f); var dark = new Color(0.30f, 0.20f, 0.08f);
            var px = new Color[W * H];
            float aa = 1.2f;
            for (int y = 0; y < H; y++)
                for (int x0 = 0; x0 < W; x0++)
                {
                    int x = mirror ? W - 1 - x0 : x0;
                    // local coords: bar end sits at the right edge (x=W) of the left piece, vertical centre y=H/2
                    float X = x + 0.5f, Y = y + 0.5f - H / 2f;
                    float dist = 1e9f;
                    // spear: horizontal blade from x 70 to the bar, tapering towards the bar
                    dist = Mathf.Min(dist, Capsule(X, Y, 64, 0, W + 4, 0, Mathf.Lerp(2.6f, 1.4f, Mathf.InverseLerp(64, W, X))));
                    // crest diamond
                    dist = Mathf.Min(dist, (Mathf.Abs(X - 56) / 18f + Mathf.Abs(Y) / 22f - 1) * 14f);
                    // scroll curls above and below (ring arcs)
                    dist = Mathf.Min(dist, Ring(X, Y, 96, -15, 12, 2.2f, Y < -15 || X > 100));
                    dist = Mathf.Min(dist, Ring(X, Y, 96, 15, 12, 2.2f, Y > 15 || X > 100));
                    // sweeping tendrils from the crest to the curls
                    dist = Mathf.Min(dist, Capsule(X, Y, 62, -10, 92, -27, 1.6f));
                    dist = Mathf.Min(dist, Capsule(X, Y, 62, 10, 92, 27, 1.6f));
                    // finial: small diamond and spike to the outer side
                    dist = Mathf.Min(dist, (Mathf.Abs(X - 24) / 7f + Mathf.Abs(Y) / 7f - 1) * 5f);
                    dist = Mathf.Min(dist, Capsule(X, Y, 6, 0, 38, 0, 1.2f));
                    float a = Mathf.Clamp01(0.5f - dist / aa);
                    // inner hollow in the crest diamond (dark inlay)
                    float inner = (Mathf.Abs(X - 56) / 9f + Mathf.Abs(Y) / 11f - 1) * 7f;
                    float inlay = Mathf.Clamp01(0.5f - inner / aa);
                    // simple bevel: lighter towards the top
                    var c = Color.Lerp(gold, hi, Mathf.Clamp01(-Y / (H * 0.5f)) * 0.8f);
                    c = Color.Lerp(c, dark, inlay * 0.85f);
                    // dark outline for separation on bright floors
                    float outline = Mathf.Clamp01(0.5f - (dist - 1.6f) / aa) * (1 - a);
                    var col = new Color(c.r, c.g, c.b, a);
                    if (outline > 0) col = new Color(Mathf.Lerp(0.04f, c.r, a), Mathf.Lerp(0.03f, c.g, a), Mathf.Lerp(0.02f, c.b, a), Mathf.Max(a, outline * 0.8f));
                    px[y * W + x0] = col;
                }
            // texture rows run bottom-up; the shapes are symmetric vertically, so no flip is needed
            tex.SetPixels(px); tex.Apply(); return tex;
        }
        static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
        {
            float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
            float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
            float dx = pax - bax * h, dy = pay - bay * h; return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }
        static float Ring(float x, float y, float cx, float cy, float rad, float thick, bool cut)
        {
            if (cut) return 1e9f;
            float d = Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - rad) - thick; return d;
        }
    }
}
