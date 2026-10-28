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
            var pierStone = StoneM(StoneDark * 1.08f); var capStone = StoneM(new Color(0.40f, 0.40f, 0.43f)); var recess = StoneM(StoneDark * 0.4f);
            var ribStone = StoneM(StoneDark * 1.25f);
            var pierSlate = SlateM(new Color(0.25f, 0.26f, 0.34f)); var finialIron = M(new Color(0.17f, 0.18f, 0.26f), 0.35f, 0.6f);
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
                foreach (var (cx, cz) in new[] { (-1f, -1f), (-1f, 1f), (1f, -1f), (1f, 1f) })
                {
                    P(PrimitiveKind.Cube, stat, head + Vector3.up * 0.95f + rot * new Vector3(cx * 1.32f, 0, cz * 1.32f), new Vector3(0.32f, 1.9f, 0.32f), capStone, new Vector3(0, a, 0), "PierHeadPilaster");
                    var diag = rot * new Vector3(cx, 0, cz).normalized;
                    P(PrimitiveKind.Cube, stat, head + Vector3.up * 1.95f + rot * new Vector3(cx * 1.58f, 0, cz * 1.58f), new Vector3(0.3f, 0.28f, 0.55f), capStone, Quaternion.LookRotation(diag).eulerAngles + new Vector3(14f, 0, 0), "PierHeadGargoyle");
                    var corner = head + Vector3.up * 2.15f + rot * new Vector3(cx * 1.3f, 0, cz * 1.3f);
                    P(PrimitiveKind.Cube, stat, corner + Vector3.up * 0.35f, new Vector3(0.36f, 0.7f, 0.36f), capStone, new Vector3(0, a, 0), "PierPinnacleShaft");
                    Mesh(stat, Frustum(0.2f, 0.2f, 0.02f, 0.02f, 1.1f), capStone, "RisingPierPinnacle", corner + Vector3.up * 0.7f).localRotation = rot;
                }
                Mesh(stat, Frustum(1.47f, 1.47f, 1.47f, 1.47f, 0.1f), capStone, "PierHeadString", head + Vector3.up * 1.72f).localRotation = rot;
                foreach (var (lx, lz, yawOff) in new[] { (0f, 1.38f, 0f), (0f, -1.38f, 0f), (1.38f, 0f, 90f), (-1.38f, 0f, 90f) })
                    P(PrimitiveKind.Cube, stat, head + Vector3.up * 2.35f + rot * new Vector3(lx, 0, lz), new Vector3(2.25f, 0.4f, 0.2f), capStone, new Vector3(0, a + yawOff, 0), "PierParapet");
                Mesh(stat, Frustum(1.05f, 1.05f, 0.04f, 0.04f, 1.7f), pierSlate, "RisingPierRoof", head + Vector3.up * 2.15f).localRotation = rot;
                Mesh(stat, Frustum(0.1f, 0.1f, 0.02f, 0.02f, 0.45f), finialIron, "RisingPierFinial", head + Vector3.up * 3.8f).localRotation = rot;
                P(PrimitiveKind.Sphere, stat, head + Vector3.up * 3.92f, Vector3.one * 0.18f, M(Brass * 0.6f, 0.4f, 0.8f), null, "RisingPierBall");
                // a pointed opening with a sill on each chamber face (the court face keeps its original opening)
                foreach (var (lx, lz, yawOff) in new[] { (0f, -1.40f, 0f), (0f, 1.40f, 0f), (1.40f, 0f, 90f), (-1.40f, 0f, 90f) })
                {
                    var at = head + Vector3.up * 1.0f + rot * new Vector3(lx, 0, lz);
                    if (lz > -1f)
                    {
                        P(PrimitiveKind.Cube, stat, at, new Vector3(0.7f, 1.1f, 0.06f), recess, new Vector3(0, a + yawOff, 0), "PierHeadOpening");
                        P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.55f, new Vector3(0.5f, 0.5f, 0.06f), recess, new Vector3(0, a + yawOff, 45), "PierHeadOpeningPoint");
                    }
                    P(PrimitiveKind.Cube, stat, at + Vector3.up * -0.62f + rot * new Vector3(lx, 0, lz).normalized * 0.06f, new Vector3(0.95f, 0.12f, 0.22f), capStone, new Vector3(0, a + yawOff, 0), "PierHeadSill");
                }
                var slot = dir * (r - 1.42f) + Vector3.up * (top + 1.0f);
                P(PrimitiveKind.Cube, stat, slot, new Vector3(0.7f, 1.1f, 0.06f), recess, new Vector3(0, a, 0), "PierHeadOpening");
                P(PrimitiveKind.Cube, stat, slot + Vector3.up * 0.55f, new Vector3(0.5f, 0.5f, 0.06f), recess, new Vector3(0, a, 45), "PierHeadOpeningPoint");
                // stepped weathering courses down the shaft (pass 5: a belt course under the corbel and one more set-off),
                // and a lancet slit in each side face
                float W(float y) => 1.9f - 0.65f * y / h;   // the shaft's half-width at height y above its foot
                foreach (var (cy, ch) in new[] { (h - 2.4f, 0.2f), (h - 5.5f, 0.35f), (h - 10.5f, 0.35f) })
                    Mesh(stat, Frustum(W(cy) + 0.16f, W(cy) + 0.16f, W(cy + ch) + 0.08f, W(cy + ch) + 0.08f, ch), capStone, "RisingPierCourse", foot + Vector3.up * cy).localRotation = rot;
                foreach (var sx in new[] { -1f, 1f })
                {
                    var slit = foot + Vector3.up * (h - 4.0f) + rot * new Vector3(sx * (W(h - 4.0f) + 0.02f), 0, 0);
                    P(PrimitiveKind.Cube, stat, slit, new Vector3(0.28f, 1.3f, 0.06f), recess, new Vector3(0, a + 90f, 0), "PierSlit");
                    P(PrimitiveKind.Cube, stat, slit + Vector3.up * 0.65f, new Vector3(0.2f, 0.2f, 0.06f), recess, new Vector3(0, a + 90f, 45), "PierSlitPoint");
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
                for (int k = 0; k < 5; k++)
                {
                    Vector3 p0 = Curve(k / 5f), p1 = Curve((k + 1) / 5f); var ribDir = (p1 - p0).normalized;
                    var stone = P(PrimitiveKind.Cube, stat, (p0 + p1) / 2, new Vector3(0.85f, (p1 - p0).magnitude + 0.08f, 1.0f + 0.12f * (k % 2)), ribStone, null, "PierRibStone");
                    stone.localRotation = Quaternion.LookRotation(Vector3.Cross(tangentP, ribDir), ribDir);
                }
                var cope = P(PrimitiveKind.Cube, stat, (from + to) / 2 + Vector3.up * 0.55f, new Vector3(0.5f, (to - from).magnitude * 0.98f, 0.5f), capStone, null, "PierRibCoping");
                var copeDir = (to - from).normalized; cope.localRotation = Quaternion.LookRotation(Vector3.Cross(tangentP, copeDir), copeDir);
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
                for (float a = a0 + 6; a < a1 - 3; a += 11)
                {
                    var d = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    P(PrimitiveKind.Cube, stat, d * 16.85f + Vector3.up * -6.75f, new Vector3(0.07f, 0.9f, 0.07f), rail, new Vector3(0, a, 0), "GalleryRailPost");
                }
                float mid = (a0 + a1) / 2; var md = Quaternion.Euler(0, mid, 0) * Vector3.forward;
                P(PrimitiveKind.Cube, stat, md * 16.85f + Vector3.up * -6.35f, new Vector3((a1 - a0) * Mathf.Deg2Rad * 16.85f * 0.8f, 0.06f, 0.06f), rail, new Vector3(0, mid, 0), "GalleryRail");
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
