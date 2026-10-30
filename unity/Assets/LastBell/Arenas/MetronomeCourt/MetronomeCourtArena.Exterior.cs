using System.Collections.Generic;
using LastBell.Run;
using UnityEngine;
using UnityEngine.Rendering;

namespace LastBell.Arenas.MetronomeCourt
{
    // Exterior refinement (October 27, 2026, Risxhb): the empty void around the court becomes a sea of clouds like the main
    // menu's, with masonry rising out of it close to the arena: buttress piers with ribs into the drum, a broken service
    // gallery, and belfry spires on the flanks. Presentation only: nothing here is traversable or casts on the floor.
    // Camera-side pieces stay below the play plane; the metronome's sky opening (north) is left clear; every cloud
    // pixel is faded out above y = -1.2 and beside the drum by its shader, so no cloud crosses the combat disc.
    public sealed partial class MetronomeCourtArena
    {
        static Material cloudMat; static Texture2D puffTex;
        public int CloudPuffCount { get; private set; }
        static readonly Color CloudLit = new Color(0.60f, 0.59f, 0.74f), CloudShade = new Color(0.19f, 0.19f, 0.31f),   // pass 4: brighter crowns, softer violet shade
            CloudFar = new Color(0.20f, 0.20f, 0.31f);

        static Material CloudMaterial()
        {
            if (cloudMat) return cloudMat;
            var sh = Resources.Load<Shader>("MetronomeCourtCloud");
            if (!sh) return null;
            cloudMat = new Material(sh) { name = "MetronomeCourt_CloudSea" };
            cloudMat.SetTexture("_MainTex", PuffTexture());
            cloudMat.SetColor("_Lit", CloudLit); cloudMat.SetColor("_Shade", CloudShade); cloudMat.SetColor("_Far", CloudFar);
            // pass 3: soft edges where a puff meets masonry, only when the pipeline provides a camera depth texture
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            cloudMat.SetFloat("_SoftDepth", urp && urp.supportsCameraDepthTexture ? 1f : 0f);
            return cloudMat;
        }

        // feathered fbm puff (as the menu's cloud sea): lumpy alpha inside a soft round falloff; r = local lightness
        static Texture2D PuffTexture()
        {
            if (puffTex) return puffTex;
            const int S = 128;
            float Hash(int x, int y) { uint n = (uint)(x * 374761393 + y * 668265263 + 1013904223); n = (n ^ (n >> 13)) * 1274126177u; return ((n ^ (n >> 16)) & 0xffff) / 65535f; }
            float Noise(float x, float y)
            {
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx), fy);
            }
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (S - 1f) * 2 - 1, v = y / (S - 1f) * 2 - 1, r = Mathf.Sqrt(u * u + v * v * 1.2f);
                    float n = 0, amp = 0.5f, sum = 0, f = 2.2f;
                    for (int o = 0; o < 4; o++) { n += Noise(u * f + 7, v * f + 3) * amp; sum += amp; amp *= 0.5f; f *= 2; }
                    n /= sum;
                    float fall = Mathf.Clamp01(1 - r * r * 1.1f);
                    float a = Mathf.Clamp01((n - 0.30f) * 3.2f) * Mathf.Pow(fall, 1.5f);   // a defined billow body, feathered edge
                    float lit = Mathf.Clamp01(0.55f + 0.45f * (n - 0.3f) * 2f);
                    px[y * S + x] = new Color32((byte)(lit * 255), (byte)(lit * 255), (byte)(lit * 255), (byte)(a * 255));
                }
            puffTex = new Texture2D(S, S, TextureFormat.RGBA32, true, false) { name = "MetronomeCourt_CloudPuff", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            puffTex.SetPixels32(px); puffTex.Apply(true, Application.isPlaying);   // readable in the editor, for the menu's baked copy
            return puffTex;
        }

        // One mesh of camera-facing puffs: each vertex holds its puff centre, corner (uv0) and half-size (uv1).
        // Sorted back to front from the usual south/elevated cameras, since the puffs blend over each other.
        static Mesh PuffMesh(string name, List<(Vector3 c, Vector2 half, float light, float alpha)> puffs, Vector3 viewFrom)
        {
            puffs.Sort((a, b) => (b.c - viewFrom).sqrMagnitude.CompareTo((a.c - viewFrom).sqrMagnitude));
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var size = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
            foreach (var p in puffs)
            {
                int i = v.Count;
                foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(-1, 1), new Vector2(1, 1), new Vector2(1, -1) })
                { v.Add(p.c); uv.Add(corner); size.Add(p.half); col.Add(new Color(p.light, p.light, p.light, p.alpha)); }
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, size); m.SetColors(col); m.SetTriangles(tri, 0);
            m.bounds = new Bounds(Vector3.down * 12f, new Vector3(420, 80, 420));   // the vertices sit at puff centres; cards expand on the GPU
            return m;
        }

        // keep each puff's soft top below the play plane: half-height limited by its centre depth
        static Vector2 PuffHalf(float y, float width)
        {
            float hh = Mathf.Min(width * 0.55f, (-2.6f - y) / 0.8f);
            return new Vector2(width, Mathf.Max(0.8f, hh));
        }

        void CloudLayer(Transform root, string name, List<(Vector3, Vector2, float, float)> puffs, int queue)
        {
            var mat = CloudMaterial(); if (!mat) return;
            var go = new GameObject(name); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = PuffMesh(name, puffs, new Vector3(0, 28, 40));
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingOrder = queue;   // deep layer first, the near billows last
            CloudPuffCount += puffs.Count;
        }

        // The menu-like sea: a deep dark layer far below, a broad undulating sea, and close billows that wrap the drum,
        // the keel and the bases of the piers so the court and its masonry rise out of cloud.
        void BuildCloudSea(Transform root)
        {
            var rng = new System.Random(311); float R() => (float)rng.NextDouble();
            Vector3 At(float aDeg, float r, float y) => Quaternion.Euler(0, aDeg, 0) * Vector3.forward * r + Vector3.up * y;
            var deep = new List<(Vector3, Vector2, float, float)>();
            for (int i = 0; i < 46; i++)
            {
                float a = R() * 360, r = 26 + Mathf.Pow(R(), 0.7f) * 140, y = -27 - R() * 8, w = 26 + R() * 26;
                deep.Add((At(a, r, y), PuffHalf(y, w), 0.55f + R() * 0.15f, 0.8f));
            }
            // pass 2 (critic: "one smoky layer"): a lower, thinner sea, and above it sculpted cloud banks, each a domed
            // mass with a shadowed underside row, a middle row and small lit crowns, separated by dark gaps
            var sea = new List<(Vector3, Vector2, float, float)>();
            for (int i = 0; i < 110; i++)
            {
                float a = R() * 360, r = 16 + Mathf.Pow(R(), 0.85f) * 125, w = Mathf.Lerp(8, 24, Mathf.InverseLerp(16, 140, r)) + R() * 9;
                float y = -16.5f - R() * 4f - Mathf.InverseLerp(40, 140, r) * 5 + Mathf.Sin(a * Mathf.Deg2Rad * 3 + r * 0.05f) * 1.5f;
                sea.Add((At(a, r, y), PuffHalf(y, w), 0.7f + R() * 0.2f, 0.85f));
            }
            var banks = new List<(Vector3, Vector2, float, float)>();
            int bankCount = 0;
            for (float a0 = 8; a0 < 360; a0 += 22 + R() * 12)
            {
                float r0 = 17.5f + R() * 9 + (bankCount % 3 == 2 ? 16 : 0), baseY = -9.5f - R() * 2.5f - Mathf.InverseLerp(30, 60, r0) * 2;   // pass 3: nearer the rim
                float span = 10 + R() * 9, scale = Mathf.Lerp(1f, 1.6f, Mathf.InverseLerp(20, 55, r0));
                var tangent = Quaternion.Euler(0, a0 + 90, 0) * Vector3.forward; var centre = At(a0, r0, 0);
                void Row(int n, float y, float wMin, float wMax, float light, float spread)
                {
                    for (int k = 0; k < n; k++)
                    {
                        float t = n == 1 ? 0 : (k / (n - 1f) - 0.5f) * spread + (R() - 0.5f) * 1.5f;
                        var c = centre + tangent * (t * span) + Quaternion.Euler(0, a0, 0) * Vector3.forward * ((R() - 0.5f) * 4f) + Vector3.up * (y + (R() - 0.5f) * 0.8f);
                        banks.Add((c, PuffHalf(c.y, (wMin + R() * (wMax - wMin)) * scale), light, 0.97f));
                    }
                }
                Row(7, baseY - 0.4f, 4.5f, 7f, 0.4f, 1.0f);       // shadowed underside
                Row(6, baseY + 2.0f, 4f, 6.2f, 0.85f, 0.8f);      // body
                Row(4, baseY + 4.1f, 2.8f, 4.4f, 1.35f, 0.55f);   // lit crowns
                bankCount++;
            }
            var near = new List<(Vector3, Vector2, float, float)>();
            for (int i = 0; i < 30; i++)
            {
                float a = R() * 360, r = 14.8f + R() * 2.5f, y = -9.4f - R() * 2.5f, w = 3.5f + R() * 3.5f;   // pass 3: a collar at the drum only
                if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 10 && r < 22) y -= 2.5f;   // the bridge piers stay legible down to their feet
                near.Add((At(a, r, y), PuffHalf(y, w), 0.9f + R() * 0.25f, 0.95f));
            }
            CloudLayer(root, "Cloud sea (deep)", deep, -3);
            CloudLayer(root, "Cloud sea", sea, -2);
            CloudLayer(root, "Cloud banks", banks, -1);
            CloudLayer(root, "Cloud billows (near)", near, 0);
        }

        // a drifting cloud bank: a small cluster of puffs (replaces the old flat sheet; still orbits in UpdateAmbience)
        static Mesh BankMesh(float size, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            var puffs = new List<(Vector3, Vector2, float, float)>();
            int n = 5 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                var c = new Vector3((R() - 0.5f) * size * 1.2f, (R() - 0.5f) * 1.2f, (R() - 0.5f) * size * 0.5f);
                puffs.Add((c, new Vector2(size * (0.28f + R() * 0.22f), size * 0.16f), 0.95f + R() * 0.2f, 0.85f));
            }
            return PuffMesh("CloudBankPuffs", puffs, new Vector3(0, 28, 40));
        }

        // ---- masonry rising out of the cloud sea, close to the court ----
        void BuildRisingStructures(Transform stat)
        {
            var srng = new System.Random(523); float R() => (float)srng.NextDouble();
            var drng = new System.Random(911); float D() => (float)drng.NextDouble();   // r3 decoration (moss, ivy, streaks): its own sequence, so the piers keep their places
            // r3 (October 28 critic, F5): the piers use the weathered masonry (rain streaks, stronger block steps)
            var pierStone = SurfM(StoneDark * 1.08f, Surface.Weathered); var capStone = StoneM(new Color(0.40f, 0.40f, 0.43f)); var recess = StoneM(StoneDark * 0.4f);
            // r3 (F8): the rib stones carry the soft paving mottle; the coursed masonry smeared into diagonal streaks on them
            var ribStone = PavingM(StoneDark * 1.25f); var ribStoneB = PavingM(StoneDark * 1.15f); var mortar = StoneM(StoneDark * 0.45f);
            // r3 (F2): a lighter, greener weathered slate, so the roof no longer matches the fog and vanishes from above
            var pierSlate = SurfM(new Color(0.30f, 0.31f, 0.33f), Surface.Slate, 0.1f);   // round 3: matte and neutral (round 2 read as a glass pyramid)
            var finialIron = M(new Color(0.17f, 0.18f, 0.26f), 0.35f, 0.6f);
            var lead = M(new Color(0.45f, 0.47f, 0.50f), 0.2f); var walk = PavingM(new Color(0.29f, 0.31f, 0.36f)); var coping = StoneM(new Color(0.32f, 0.32f, 0.35f));
            // r3 (F1): cream candle light behind iron louvres in the belfry openings (cream, not orange: the warnings stay hottest)
            var belfryLight = M(new Color(0.5f, 0.41f, 0.27f), 0.2f, 0, true); var louvre = M(new Color(0.17f, 0.18f, 0.23f), 0.35f, 0.6f);   // round 2: warmer and dimmer light (round 1 read near-white)
            var moss = M(new Color(0.33f, 0.38f, 0.25f)); var ivy = SurfM(new Color(0.20f, 0.26f, 0.16f), Surface.Cloth); var ivyLeaf = SurfM(new Color(0.33f, 0.40f, 0.23f), Surface.Cloth);
            var quoin = StoneM(new Color(0.5f, 0.5f, 0.54f)); var agedBrassP = M(Brass * 0.6f, 0.4f, 0.8f);   // round 3: light corner quoins, aged brass finials
            var stain = StoneM(StoneDark * 0.75f); var stainLight = StoneM(new Color(0.40f, 0.40f, 0.43f) * 0.62f);
            var pierAt = new Dictionary<float, Vector3>();   // pass 4: pier centres, for the arches below
            // buttress piers: every one carries a rib up into the rim ledge (a load path), skipping the bridge and the metronome
            foreach (float a in new[] { 38f, 72f, 108f, 143f, 217f, 252f, 288f, 322f })
            {
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward; float r = 19.5f + R() * 2.5f;
                bool camSide = CameraSide(dir * r);
                float top = camSide ? -5.4f - R() * 0.6f : -4.8f - R() * 0.6f, bottom = -30f, h = top - bottom;   // roof tips stay below the play plane   // heads under the rim ledge: supports, not chimneys
                var foot = dir * r + Vector3.up * bottom; pierAt[a] = dir * r;
                var rot = Quaternion.Euler(0, a, 0);
                Mesh(stat, Frustum(1.9f, 1.9f, 1.25f, 1.25f, h), pierStone, "RisingPier", foot).localRotation = rot;
                // pass 4 (final critic: "pyramid-capped boxes"): a corbelled course flares out under the belfry chamber, and the
                // big plain pyramid becomes four corner pinnacles round a low slate cap with an iron finial (tips lower than before)
                Mesh(stat, Frustum(1.25f, 1.25f, 1.5f, 1.5f, 0.35f), capStone, "RisingPierCorbel", foot + Vector3.up * (h - 0.35f)).localRotation = rot;
                Mesh(stat, Frustum(1.4f, 1.4f, 1.3f, 1.3f, 1.9f), pierStone, "RisingPierChamber", foot + Vector3.up * h).localRotation = rot;   // pass 3: a small belfry head
                Mesh(stat, Frustum(1.55f, 1.55f, 1.5f, 1.5f, 0.25f), capStone, "RisingPierCornice", foot + Vector3.up * (h + 1.9f)).localRotation = rot;
                // pass 5 (Risxhb: the pier heads "look basic"): a finished belfry head. Corner pilasters and a pointed opening
                // with a sill on every face of the chamber, a string course and the cornice, corner gargoyles, a low parapet
                // between four gothic pinnacles (square shaft and spirelet), and a steep slate spire with an iron finial and a
                // brass ball. The parapet closes the open top that read as a hollow box from the combat camera.
                var head = foot + Vector3.up * h;
                Vector3 L(float x, float y, float z) => head + Vector3.up * y + rot * new Vector3(x, 0, z);   // head-local point
                foreach (var (cx, cz) in new[] { (-1f, -1f), (-1f, 1f), (1f, -1f), (1f, 1f) })
                {
                    P(PrimitiveKind.Cube, stat, L(cx * 1.32f, 0.95f, cz * 1.32f), new Vector3(0.32f, 1.9f, 0.32f), capStone, new Vector3(0, a, 0), "PierHeadPilaster");
                    // r3 (F7): a carved spout gargoyle (bracket, a body pitched 25 degrees down, a wedge head with ears and an
                    // open spout) instead of a tilted box, and the water stain it leaves down both outer faces of the pilaster
                    var diag = rot * new Vector3(cx, 0, cz).normalized; var look = Quaternion.LookRotation(diag); var pitch = look * Quaternion.Euler(25f, 0, 0);
                    var g0 = L(cx * 1.5f, 1.95f, cz * 1.5f);
                    P(PrimitiveKind.Cube, stat, g0 + Vector3.down * 0.19f - diag * 0.08f, new Vector3(0.16f, 0.2f, 0.16f), capStone, look.eulerAngles, "PierGargoyleBracket");
                    P(PrimitiveKind.Cube, stat, g0, new Vector3(0.26f, 0.24f, 0.5f), capStone, pitch.eulerAngles, "PierHeadGargoyle");
                    var gHead = g0 + pitch * Vector3.forward * 0.22f;
                    Mesh(stat, Frustum(0.13f, 0.12f, 0.07f, 0.06f, 0.22f), capStone, "PierGargoyleHead", gHead).localRotation = pitch * Quaternion.Euler(90f, 0, 0);
                    foreach (var ex in new[] { -1f, 1f })
                        P(PrimitiveKind.Cube, stat, gHead + pitch * new Vector3(ex * 0.07f, 0.13f, 0.04f), new Vector3(0.05f, 0.07f, 0.05f), capStone, pitch.eulerAngles, "PierGargoyleEar");
                    P(PrimitiveKind.Cube, stat, gHead + pitch * Vector3.forward * 0.25f, new Vector3(0.07f, 0.07f, 0.12f), recess, pitch.eulerAngles, "PierGargoyleSpout");
                    foreach (var (fx, fz) in new[] { (cx, 0f), (0f, cz) })
                    {
                        var fn = rot * new Vector3(fx, 0, fz);
                        Mesh(stat, Trapezoid(0.035f, 0.1f, 1.35f, 0.01f), stainLight, "PierGargoyleStain", L(cx * 1.32f, 0.25f, cz * 1.32f) + fn * 0.165f).localRotation = Quaternion.LookRotation(fn);
                    }
                    var corner = L(cx * 1.3f, 2.15f, cz * 1.3f);
                    P(PrimitiveKind.Cube, stat, corner + Vector3.up * 0.35f, new Vector3(0.36f, 0.7f, 0.36f), capStone, new Vector3(0, a, 0), "PierPinnacleShaft");
                    P(PrimitiveKind.Cube, stat, corner + Vector3.up * 0.73f, new Vector3(0.42f, 0.06f, 0.42f), quoin, new Vector3(0, a, 0), "PierPinnacleBand");   // r3: a capping band and a finial (critic: "bare spikes")
                    Mesh(stat, Frustum(0.2f, 0.2f, 0.02f, 0.02f, 1.1f), capStone, "RisingPierPinnacle", corner + Vector3.up * 0.76f).localRotation = rot;
                    // round 3 (N1): two rings of crockets pitched 30 degrees out, so the spikes read as Gothic pinnacles
                    foreach (float ct in new[] { 0.33f, 0.66f })
                        foreach (var fd in new[] { rot * Vector3.forward, rot * Vector3.back, rot * Vector3.right, rot * Vector3.left })
                            P(PrimitiveKind.Cube, stat, corner + Vector3.up * (0.76f + 1.1f * ct) + fd * (0.2f - 0.18f * ct + 0.03f), new Vector3(0.08f, 0.08f, 0.12f), capStone, (Quaternion.LookRotation(fd) * Quaternion.Euler(-30f, 0, 0)).eulerAngles, "PierPinnacleCrocket");
                    P(PrimitiveKind.Sphere, stat, corner + Vector3.up * 1.9f, Vector3.one * 0.12f, agedBrassP, null, "PierPinnacleFinial");
                }
                Mesh(stat, Frustum(1.47f, 1.47f, 1.47f, 1.47f, 0.1f), capStone, "PierHeadString", L(0, 1.72f, 0)).localRotation = rot;
                // r3 (F2): a crenellated parapet (a base course with three merlons and darker coping per side) round a lead-grey
                // walk that closes the gap to the roof, and a lighter slate roof with pale lead hip flashings that draw the
                // pyramid from straight above. Merlons top out at head + 2.71 m, the finial at head + 4.0 m: under the play plane.
                Mesh(stat, Frustum(1.3f, 1.3f, 1.3f, 1.3f, 0.06f), walk, "PierParapetWalk", L(0, 2.15f, 0)).localRotation = rot;
                foreach (var (lx, lz, yawOff) in new[] { (0f, 1.38f, 0f), (0f, -1.38f, 0f), (1.38f, 0f, 90f), (-1.38f, 0f, 90f) })
                {
                    var c0 = L(lx, 0, lz); var along = rot * (yawOff == 0 ? Vector3.right : Vector3.forward);
                    P(PrimitiveKind.Cube, stat, c0 + Vector3.up * 2.25f, new Vector3(2.25f, 0.2f, 0.2f), capStone, new Vector3(0, a + yawOff, 0), "PierParapetBase");
                    foreach (float m in new[] { -0.75f, 0f, 0.75f })
                    {
                        P(PrimitiveKind.Cube, stat, c0 + along * m + Vector3.up * 2.5f, new Vector3(0.5f, 0.3f, 0.2f), capStone, new Vector3(0, a + yawOff, 0), "PierMerlon");
                        P(PrimitiveKind.Cube, stat, c0 + along * m + Vector3.up * 2.67f, new Vector3(0.54f, 0.04f, 0.24f), coping, new Vector3(0, a + yawOff, 0), "PierMerlonCoping");
                    }
                }
                Mesh(stat, Frustum(1.05f, 1.05f, 0.04f, 0.04f, 1.7f), pierSlate, "RisingPierRoof", L(0, 2.15f, 0)).localRotation = rot;
                foreach (var (cx, cz) in new[] { (-1f, -1f), (-1f, 1f), (1f, -1f), (1f, 1f) })
                {
                    Vector3 c = L(cx * 1.05f, 2.15f, cz * 1.05f), apexPt = L(0, 3.85f, 0), ax = apexPt - c;
                    P(PrimitiveKind.Cube, stat, (c + apexPt) / 2, new Vector3(0.09f, ax.magnitude, 0.05f), lead, Quaternion.FromToRotation(Vector3.up, ax).eulerAngles, "PierRoofHip");
                }
                Mesh(stat, Frustum(0.1f, 0.1f, 0.02f, 0.02f, 0.45f), finialIron, "RisingPierFinial", L(0, 3.8f, 0)).localRotation = rot;
                P(PrimitiveKind.Sphere, stat, L(0, 4.27f, 0), Vector3.one * 0.14f, agedBrassP, null, "RisingPierBall");
                // r3 (F1): every chamber face has a lit belfry opening: a cream light panel (and its pointed head) set back
                // behind three iron louvres, framed by cap-stone jambs and a pointed hood that stand proud, so it reads as a
                // recess with light in it (the old 0.06 m black boxes read as stickers). Sill with a moss cap below.
                foreach (var (lx, lz, yawOff) in new[] { (0f, -1.40f, 0f), (0f, 1.40f, 0f), (1.40f, 0f, 90f), (-1.40f, 0f, 90f) })
                {
                    var at = L(lx, 1.0f, lz); var n = rot * new Vector3(lx, 0, lz).normalized; var fy = new Vector3(0, a + yawOff, 0);
                    var sideV = Quaternion.Euler(fy) * Vector3.right;
                    P(PrimitiveKind.Cube, stat, at, new Vector3(0.66f, 1.05f, 0.04f), belfryLight, fy, "PierHeadOpeningLight");
                    P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.52f, new Vector3(0.47f, 0.47f, 0.04f), belfryLight, new Vector3(0, a + yawOff, 45), "PierHeadOpeningPoint");
                    for (int k = -1; k <= 1; k++)
                        P(PrimitiveKind.Cube, stat, at + Vector3.up * (k * 0.3f) + n * 0.06f, new Vector3(0.62f, 0.05f, 0.14f), louvre, new Vector3(30f, a + yawOff, 0), "PierHeadLouvre");
                    foreach (var s in new[] { -1f, 1f })
                    {
                        P(PrimitiveKind.Cube, stat, at + sideV * (s * 0.39f) + n * 0.06f, new Vector3(0.12f, 1.1f, 0.18f), capStone, fy, "PierHeadJamb");
                        P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.72f + sideV * (s * 0.18f) + n * 0.06f, new Vector3(0.52f, 0.1f, 0.18f), capStone, new Vector3(0, a + yawOff, -s * 45f), "PierHeadHood");
                    }
                    var sill = at + Vector3.up * -0.62f + n * 0.06f;
                    P(PrimitiveKind.Cube, stat, sill, new Vector3(0.95f, 0.12f, 0.22f), capStone, fy, "PierHeadSill");
                }
                // stepped weathering courses down the shaft (pass 5: a belt course under the corbel and one more set-off),
                // and a lancet slit in each side face
                float W(float y) => 1.9f - 0.65f * y / h;   // the shaft's half-width at height y above its foot
                foreach (var (cy, ch) in new[] { (h - 2.4f, 0.2f), (h - 5.5f, 0.35f), (h - 10.5f, 0.35f) })
                    Mesh(stat, Frustum(W(cy) + 0.16f, W(cy) + 0.16f, W(cy + ch) + 0.08f, W(cy + ch) + 0.08f, ch), capStone, "RisingPierCourse", foot + Vector3.up * cy).localRotation = rot;
                foreach (var sx in new[] { -1f, 1f })
                {
                    var slit = foot + Vector3.up * (h - 4.0f) + rot * new Vector3(sx * (W(h - 4.0f) + 0.02f), 0, 0);
                    // round 3 (N4): a lit slit in a stone surround, which breaks up the dark column under the head
                    P(PrimitiveKind.Cube, stat, slit - rot * new Vector3(sx * 0.012f, 0, 0), new Vector3(0.44f, 1.5f, 0.05f), capStone, new Vector3(0, a + 90f, 0), "PierSlitSurround");
                    P(PrimitiveKind.Cube, stat, slit, new Vector3(0.24f, 1.25f, 0.06f), belfryLight, new Vector3(0, a + 90f, 0), "PierSlit");
                    P(PrimitiveKind.Cube, stat, slit + Vector3.up * 0.63f, new Vector3(0.17f, 0.17f, 0.06f), belfryLight, new Vector3(0, a + 90f, 45), "PierSlitPoint");
                }
                // r3 (F5, F9): grime streaks running down the shaft from under the corbel, and two to four ivy curtains with
                // leaf clumps hanging from the cornice (chamber faces, beside the openings) or from under the corbel (shaft)
                for (int k = 0; k < 3; k++)
                {
                    int f = drng.Next(4); float fx = f == 0 ? 1 : f == 1 ? -1 : 0, fz = f == 2 ? 1 : f == 3 ? -1 : 0;
                    var fn = rot * new Vector3(fx, 0, fz); float len = 2.0f + D() * 1.2f, y0 = h - 0.4f, w = W(y0 - len * 0.5f);
                    var along = rot * new Vector3(fz, 0, fx) * ((D() - 0.5f) * 1.4f);
                    Mesh(stat, Trapezoid(0.2f + D() * 0.06f, 0.3f + D() * 0.06f, len, 0.01f), stain, "PierGrimeStreak", foot + Vector3.up * (y0 - len) + fn * (w + 0.015f) + along).localRotation = Quaternion.LookRotation(fn);
                }
                // round 3 (N4): light quoins up the four shaft corners under the corbel (alternating long and short blocks,
                // 3 cm proud), the strongest masonry cue at range, and ivy as cascades of overlapping flattened clumps that
                // narrow as they fall, in two greens, from a parapet corner and from beside a window hood
                for (int j = 0; j < 12; j++)
                {
                    float qy = h - 0.55f - j * 0.35f, qw = W(qy), ql = j % 2 == 0 ? 0.55f : 0.3f;
                    foreach (var (cx, cz) in new[] { (-1f, -1f), (-1f, 1f), (1f, -1f), (1f, 1f) })
                    {
                        var cn = foot + Vector3.up * qy + rot * new Vector3(cx * qw, 0, cz * qw);
                        if (j % 2 == 0) P(PrimitiveKind.Cube, stat, cn + rot * new Vector3(-cx * (ql / 2 - 0.03f), 0, cz * 0.02f), new Vector3(ql, 0.32f, 0.1f), quoin, new Vector3(0, a, 0), "PierQuoin");
                        else P(PrimitiveKind.Cube, stat, cn + rot * new Vector3(cx * 0.02f, 0, -cz * (ql / 2 - 0.03f)), new Vector3(0.1f, 0.32f, ql), quoin, new Vector3(0, a, 0), "PierQuoin");
                    }
                }
                int ivyCount = 2 + drng.Next(2);
                for (int k = 0; k < ivyCount; k++)
                {
                    float cx = drng.Next(2) == 0 ? -1 : 1, cz = drng.Next(2) == 0 ? -1 : 1;
                    Vector3 fn, startPt, sideAxis;
                    if (k == 0)
                    {   // beside a window hood, on a random chamber face
                        int f = drng.Next(4); float fx = f == 0 ? 1 : f == 1 ? -1 : 0, fz = f == 2 ? 1 : f == 3 ? -1 : 0;
                        fn = rot * new Vector3(fx, 0, fz); sideAxis = rot * new Vector3(fz, 0, fx);
                        startPt = L(0, 1.8f, 0) + fn * 1.42f + sideAxis * (cx * 0.62f);
                    }
                    else
                    {   // over a parapet corner, beside the pinnacle
                        fn = rot * new Vector3(cx, 0, cz).normalized; sideAxis = Vector3.Cross(Vector3.up, fn);
                        startPt = L(cx * 1.44f, 2.3f, cz * 1.44f);
                    }
                    var fq = Quaternion.LookRotation(fn); int clumps = 4 + drng.Next(4); float size = 0.42f + D() * 0.06f; var pos = startPt;
                    for (int c = 0; c < clumps; c++)
                    {
                        P(PrimitiveKind.Sphere, stat, pos + fn * 0.04f, new Vector3(size, size * 0.72f, size * 0.32f), c % 3 == 1 ? ivyLeaf : ivy, (fq * Quaternion.Euler(0, 0, D() * 180f)).eulerAngles, "PierIvy");
                        pos += Vector3.down * (size * 0.62f) + sideAxis * ((D() - 0.5f) * 0.16f); size *= 0.86f;
                    }
                }
                // a dark pointed maintenance opening facing the court
                var face = dir * (r - 1.33f) + Vector3.up * (top - 3.2f);
                P(PrimitiveKind.Cube, stat, face, new Vector3(1.2f, 3.0f, 0.06f), recess, new Vector3(0, a, 0), "PierDoor");
                P(PrimitiveKind.Cube, stat, face + Vector3.up * 1.5f, new Vector3(0.85f, 0.85f, 0.06f), recess, new Vector3(0, a, 45), "PierDoorPoint");
                P(PrimitiveKind.Cube, stat, face + Vector3.up * -1.6f - dir * 0.08f, new Vector3(1.7f, 0.22f, 0.25f), capStone, new Vector3(0, a, 0), "PierDoorSill");
                // the rib: from the pier head up and in to the underside of the rim ledge
                // pass 5: the rib springs from a corbel on the shaft's court face, below the belfry head, instead of running
                // through the chamber (the crossed junction read as a mistake), and lands on a springer block at the drum
                var from = dir * (r - 1.15f) + Vector3.up * (top - 1.5f); var to = dir * 14.6f + Vector3.up * -2.3f;
                P(PrimitiveKind.Cube, stat, dir * (r - 1.45f) + Vector3.up * (top - 1.95f), new Vector3(1.1f, 0.5f, 0.6f), capStone, new Vector3(0, a, 0), "PierRibCorbel");
                P(PrimitiveKind.Cube, stat, dir * 14.85f + Vector3.up * -2.75f, new Vector3(1.0f, 0.6f, 0.7f), capStone, new Vector3(0, a, 0), "PierRibSpringer");
                // pass 2: an arched flying buttress (five stones on a curve that sags below the chord) instead of a strut
                var sag = Vector3.down * 1.6f; var tangentP = Vector3.Cross(Vector3.up, dir);
                Vector3 Curve(float t) => Vector3.Lerp(from, to, t) + sag * (4 * t * (1 - t));
                // r3 (F8): eight voussoirs with 3 cm joints over a dark mortar core, alternating in value, so the rib reads as
                // jointed stones instead of one bent plank (and the soft paving mottle no longer smears into diagonal streaks)
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p0 = Curve(k / 8f), p1 = Curve((k + 1) / 8f); var ribDir = (p1 - p0).normalized; float segLen = (p1 - p0).magnitude;
                    var q = Quaternion.LookRotation(Vector3.Cross(tangentP, ribDir), ribDir);
                    P(PrimitiveKind.Cube, stat, (p0 + p1) / 2, new Vector3(0.7f, segLen + 0.06f, 0.86f), mortar, q.eulerAngles, "PierRibCore");
                    P(PrimitiveKind.Cube, stat, (p0 + p1) / 2, new Vector3(0.85f, segLen - 0.03f, 1.0f + 0.12f * (k % 2)), k % 2 == 0 ? ribStone : ribStoneB, q.eulerAngles, "PierRibStone");
                    // round 3 (N5): a jointed coping block on each voussoir (the straight slab still smeared and read as a plank)
                    var up2 = Vector3.Cross(tangentP, ribDir).normalized; if (up2.y < 0) up2 = -up2;
                    P(PrimitiveKind.Cube, stat, (p0 + p1) / 2 + up2 * 0.57f, new Vector3(0.6f, segLen - 0.04f, 0.14f), k % 2 == 0 ? capStone : coping, q.eulerAngles, "PierRibCoping");
                    D();   // keeps the decoration sequence (the moss patch here read as a green tile at range and was removed)
                }
            }
            // pass 4 (final critic: "no pointed arches, triangular gaps"): a pointed arch between each flank pier pair, its
            // voussoirs on two curves that meet at a keystone, leaving triangular gaps under the gallery and the ribs
            foreach (var (aA, aB) in new[] { (72f, 108f), (252f, 288f) })
            {
                Vector3 pA = pierAt[aA], pB = pierAt[aB]; var span = (pB - pA).normalized; var perp = Vector3.Cross(Vector3.up, span);
                const float spring = -12.6f, apex = -8.0f;
                var s0 = pA + span * 1.35f + Vector3.up * spring; var s1 = pB - span * 1.35f + Vector3.up * spring;
                var crown = (s0 + s1) / 2 + Vector3.up * (apex - spring);
                foreach (var s in new[] { s0, s1 })
                {
                    var ctrl = s + Vector3.up * (apex - spring) * 0.85f;
                    Vector3 B(float t) => (1 - t) * (1 - t) * s + 2 * (1 - t) * t * ctrl + t * t * crown;
                    for (int k = 0; k < 6; k++)
                    {
                        Vector3 q0 = B(k / 6f), q1 = B((k + 1) / 6f); var segDir = (q1 - q0).normalized;
                        var v = P(PrimitiveKind.Cube, stat, (q0 + q1) / 2, new Vector3(0.9f, (q1 - q0).magnitude + 0.06f, 0.75f + 0.1f * (k % 2)), ribStone, null, "ArchVoussoir");
                        v.localRotation = Quaternion.LookRotation(Vector3.Cross(perp, segDir), segDir);
                    }
                    P(PrimitiveKind.Cube, stat, s + Vector3.down * 0.2f, new Vector3(1.0f, 0.4f, 1.1f), capStone, new Vector3(0, Mathf.Atan2(span.x, span.z) * Mathf.Rad2Deg, 0), "ArchImpost");
                }
                P(PrimitiveKind.Cube, stat, crown + Vector3.down * 0.15f, new Vector3(1.0f, 0.95f, 0.85f), capStone, new Vector3(0, Mathf.Atan2(span.x, span.z) * Mathf.Rad2Deg + 90f, 0), "ArchKeystone");
            }
            // a broken service gallery round the keel, several metres under the rim, with brass rail stubs
            var gallery = StoneM(StoneDark * 0.95f); var rail = M(Brass * 0.55f, 0.45f, 0.8f);
            foreach (var (a0, a1) in new[] { (28f, 96f), (118f, 160f), (205f, 262f), (282f, 334f) })
            {
                Mesh(stat, Sector(14.9f, 17.1f, a0, a1, 0.55f, 16, 0, 0.06f), gallery, "ServiceGallery", Vector3.up * -7.2f);
                var railPosts = new List<Vector3>();
                for (float a = a0 + 6; a < a1 - 3; a += 11)
                {
                    // final fix (critic: "a loose rail post at the rib foot"): no post within 4 degrees of a pier's rib, and
                    // the rail breaks there instead of running through the rib
                    if (System.Array.Exists(new[] { 38f, 72f, 108f, 143f, 217f, 252f, 288f, 322f }, pa => Mathf.Abs(Mathf.DeltaAngle(pa, a)) < 4f)) { railPosts.Add(new Vector3(float.NaN, 0, 0)); continue; }
                    var d = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    P(PrimitiveKind.Cube, stat, d * 16.85f + Vector3.up * -6.75f, new Vector3(0.07f, 0.9f, 0.07f), rail, new Vector3(0, a, 0), "GalleryRailPost");
                    railPosts.Add(d * 16.85f + Vector3.up * -6.35f);
                }
                // r3 (critic: "stray brown rods under the buttress"): one straight rail across the whole arc stuck out past the
                // curved gallery at both ends; the rail is now a chord between each pair of neighbouring posts
                for (int k = 0; k + 1 < railPosts.Count; k++)
                {
                    if (float.IsNaN(railPosts[k].x) || float.IsNaN(railPosts[k + 1].x)) continue;
                    var span = railPosts[k + 1] - railPosts[k];
                    P(PrimitiveKind.Cube, stat, (railPosts[k] + railPosts[k + 1]) / 2, new Vector3(0.06f, 0.06f, span.magnitude + 0.06f), rail, Quaternion.LookRotation(span).eulerAngles, "GalleryRail");
                }
            }
            // belfry spires on the flanks, rising from the sea to well above the rim (outside every gameplay frame's floor)
            var mid1 = StoneM(new Color(0.27f, 0.27f, 0.37f)); var roof = SlateM(new Color(0.19f, 0.20f, 0.29f));   // middle layer: dusty indigo; pass 4: slate roofs
            var bellM = M(new Color(0.45f, 0.33f, 0.18f), 0.4f, 0.8f); var glow = M(WindowGlow * 0.9f, 0.2f, 0, true);
            foreach (var (a, r, topY) in new[] { (76f, 37f, 5f), (104f, 42f, 8f), (256f, 39f, 6.5f), (284f, 35f, 3.5f) })
            {
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward; var basePos = dir * r + Vector3.up * -26f; float w = 2.9f + R() * 0.6f;
                float bodyH = topY + 26f - w * 2.2f;
                float yaw = a + 180f;
                Mesh(stat, Frustum(w * 0.62f, w * 0.62f, w * 0.5f, w * 0.5f, bodyH), mid1, "BelfryBody", basePos).localRotation = Quaternion.Euler(0, yaw, 0);
                var chamber = basePos + Vector3.up * bodyH;
                Mesh(stat, Frustum(w * 0.56f, w * 0.56f, w * 0.52f, w * 0.52f, w * 1.1f), mid1, "BelfryChamber", chamber).localRotation = Quaternion.Euler(0, yaw, 0);
                Mesh(stat, Frustum(w * 0.66f, w * 0.66f, w * 0.6f, w * 0.6f, 0.4f), capStone, "BelfryCornice", chamber + Vector3.up * w * 1.1f).localRotation = Quaternion.Euler(0, yaw, 0);
                var opening = chamber + Vector3.up * w * 0.5f - dir * (w * 0.53f);
                P(PrimitiveKind.Cube, stat, opening, new Vector3(w * 0.42f, w * 0.62f, 0.06f), recess, new Vector3(0, a, 0), "BelfryOpening");
                P(PrimitiveKind.Cube, stat, opening + Vector3.up * w * 0.31f, new Vector3(w * 0.3f, w * 0.3f, 0.06f), recess, new Vector3(0, a, 45), "BelfryOpeningPoint");
                P(PrimitiveKind.Cone, stat, opening + Vector3.up * -0.05f - dir * 0.05f, new Vector3(w * 0.26f, w * 0.26f, w * 0.26f), bellM, null, "BelfryBell");
                Mesh(stat, Frustum(w * 0.64f, w * 0.64f, 0.05f, 0.05f, w * 2.1f), roof, "BelfryRoof", chamber + Vector3.up * (w * 1.1f + 0.4f)).localRotation = Quaternion.Euler(0, yaw, 0);
                P(PrimitiveKind.Cube, stat, basePos + Vector3.up * (bodyH * 0.72f) - dir * (w * 0.63f), new Vector3(0.22f, 0.7f, 0.05f), glow, new Vector3(0, a, 0), "BelfryWindow");
            }
        }
    }
}
