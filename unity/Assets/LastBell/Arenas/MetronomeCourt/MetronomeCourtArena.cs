using System.Collections.Generic;
using LastBell.Run;
using UnityEngine;

namespace LastBell.Arenas.MetronomeCourt
{
    // The Metronome Court (docs/metronome-arena/tempo-tyrant.md): a circular raised stone platform, ~25 m playable
    // diameter (RunMaps bound 12.5 m), uninterrupted flat combat floor with thin low-contrast radial inlays and a clock-dial
    // border, a south entry bridge, perimeter parapet/posts outside the movement bound, and an independent giant
    // mechanical metronome (~10 m frame) on its own plinth beyond the north edge. Built from modular code parts at load
    // (floor, dial rim, parapet segment, post, bridge, plinth, metronome frame/pivot/rod/weight/escapement) with shared
    // cached materials, so the scene file stays small and nothing accumulates between attempts.
    //
    // Layout convention shared with every run map: centre origin, floor at y = 0, entry south at +z (camera side),
    // north = -z. The metronome is presentation only: its rod, escapement, light and cracks are derived every frame from
    // the active Tempo Tyrant's TempoClock (or a non-damaging Haste preview when no fight is running). One physical rod.
    public sealed class MetronomeCourtArena : MonoBehaviour
    {
        public bool quiet;   // HazardSystem dims decoration while hazards are up (shared arena convention)

        public const float FloorRadius = 13.1f, ParapetRadius = 13.5f, PostRadius = 14.4f, MetronomeZ = -19.5f, FrameHeight = 10f;
        static readonly Color Stone = new Color(0.60f, 0.58f, 0.54f), StoneDark = new Color(0.30f, 0.29f, 0.29f),
            Ivory = new Color(0.80f, 0.77f, 0.69f), Brass = new Color(0.66f, 0.49f, 0.24f), BrassInlay = new Color(0.56f, 0.50f, 0.40f),
            Iron = new Color(0.12f, 0.12f, 0.14f), Teal = new Color(0.10f, 0.35f, 0.36f), Abyss = new Color(0.07f, 0.08f, 0.11f);

        public Transform Rod { get; private set; }
        public Transform Weight { get; private set; }
        public Transform Escapement { get; private set; }
        public Transform Frame { get; private set; }
        public float RodAngle { get; private set; }
        public bool ShowingCracks { get; private set; }
        public bool Preview { get; private set; }
        public const float SwingDegrees = 26f;

        readonly List<Renderer> lamps = new List<Renderer>(), cracks = new List<Renderer>();
        Light metronomeLight; MaterialPropertyBlock mpb;
        double previewBeat; int lastBeat; float escapement, escShown;
        static readonly int PEmission = Shader.PropertyToID("_EmissionColor"), PBase = Shader.PropertyToID("_BaseColor");

        void Awake() { Build(); }

        Transform P(PrimitiveKind k, Transform parent, Vector3 pos, Vector3 scale, Color c, Vector3? euler = null, string name = null, bool glow = false)
        {
            var go = new GameObject(name ?? k.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(k);
            go.AddComponent<MeshRenderer>().sharedMaterial = TempoTyrantRig.Mat(c, glow);
            return go.transform;
        }

        void Build()
        {
            if (transform.Find("Generated")) return;
            mpb = new MaterialPropertyBlock();
            var root = new GameObject("Generated").transform; root.SetParent(transform, false);
            var stat = new GameObject("Static").transform; stat.SetParent(root, false);

            // ---- combat floor: one quiet flat disc, thin radial inlays, inner ring, clock-dial border ----
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -0.35f, 0), new Vector3(FloorRadius * 2, 0.35f, FloorRadius * 2), Stone, name: "Floor");
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -3.6f, 0), new Vector3(FloorRadius * 2 + 1.6f, 3.0f, FloorRadius * 2 + 1.6f), StoneDark, name: "PlatformDrum");
            P(PrimitiveKind.Cone, stat, new Vector3(0, -10.5f, 0), new Vector3(FloorRadius * 2 + 1.6f, 7f, FloorRadius * 2 + 1.6f), StoneDark, new Vector3(180, 0, 0), "PlatformKeel");
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                P(PrimitiveKind.Cube, stat, dir * 7.6f + Vector3.up * 0.004f, new Vector3(0.05f, 0.008f, 10.6f), BrassInlay, new Vector3(0, a, 0), "RadialInlay");
            }
            Ring(stat, 2.6f, 0.05f, 48, BrassInlay, "InnerRingInlay");
            Ring(stat, 12.45f, 0.07f, 120, BrassInlay, "DialInnerLine");
            // dial border band (outside the inlays, still walkable floor) with minute / hour ticks
            for (int i = 0; i < 60; i++)
            {
                float a = i * 6f; bool hour = i % 5 == 0; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                P(PrimitiveKind.Cube, stat, dir * (hour ? 12.78f : 12.85f) + Vector3.up * 0.006f, new Vector3(hour ? 0.14f : 0.05f, 0.01f, hour ? 0.55f : 0.32f), hour ? Brass : BrassInlay, new Vector3(0, a, 0), "DialTick");
            }
            Ring(stat, 13.15f, 0.12f, 120, Brass, "DialOuterLine");

            // ---- perimeter: low parapet segments + posts with lamps and teal banners, all outside the bound ----
            for (int i = 0; i < 48; i++)
            {
                float a = i * 7.5f + 3.75f; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 9f) continue;   // south bridge gap (+z)
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                float ph = CameraSide(dir * ParapetRadius) ? 0.4f : 0.84f;   // camera-side parapet stays low: never hides a player
                P(PrimitiveKind.Cube, stat, dir * ParapetRadius + Vector3.up * ph / 2, new Vector3(1.72f, ph, 0.42f), Ivory, new Vector3(0, a + 90, 0), "ParapetSegment");
                P(PrimitiveKind.Cube, stat, dir * ParapetRadius + Vector3.up * (ph + 0.03f), new Vector3(1.76f, 0.07f, 0.5f), Brass, new Vector3(0, a + 90, 0), "ParapetCap");
            }
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 5f) continue;
                Post(stat, root, Quaternion.Euler(0, a, 0) * Vector3.forward * PostRadius, a);
            }
            foreach (var s in new[] { -1, 1 }) Post(stat, root, new Vector3(2.7f * s, 0, PostRadius), 0);

            // ---- south entry bridge (decorative approach; not part of the combat bound) ----
            P(PrimitiveKind.Cube, stat, new Vector3(0, -0.35f, 21f), new Vector3(4.6f, 0.7f, 15f), Stone, name: "BridgeDeck");
            for (int s = -1; s <= 1; s += 2)
            {
                P(PrimitiveKind.Cube, stat, new Vector3(2.45f * s, 0.45f, 21f), new Vector3(0.3f, 0.9f, 15f), Ivory, name: "BridgeRail");
                for (int k = 0; k < 4; k++) P(PrimitiveKind.Cube, stat, new Vector3(2.45f * s, -3f, 15.5f + k * 4f), new Vector3(0.7f, 5f, 0.9f), StoneDark, name: "BridgePier");
            }

            // ---- north plinth and the independent giant metronome ----
            var m = new GameObject("Metronome (arena object)").transform; m.SetParent(root, false); m.localPosition = new Vector3(0, 0, MetronomeZ);
            P(PrimitiveKind.Cube, stat, new Vector3(0, -2.2f, MetronomeZ), new Vector3(9f, 4.4f, 7f), StoneDark, name: "Plinth");
            P(PrimitiveKind.Cube, stat, new Vector3(0, 0.05f, MetronomeZ), new Vector3(8.4f, 0.15f, 6.4f), Brass, name: "PlinthTrim");
            for (int k = 0; k < 3; k++) P(PrimitiveKind.Cube, stat, new Vector3(0, -0.6f - k * 0.6f, MetronomeZ + 3.6f + k * 0.6f), new Vector3(5f, 0.6f, 0.6f), Stone, name: "PlinthStep");
            for (int s = -1; s <= 1; s += 2) P(PrimitiveKind.Cube, stat, new Vector3(4.2f * s, 2.6f, MetronomeZ), new Vector3(0.9f, 5.2f, 0.9f), Ivory, name: "PlinthPylon");
            BuildMetronome(m);

            // ---- distant gothic clocktower scenery (never inside the camera's combat framing) ----
            var rng = new System.Random(11);
            for (int i = 0; i < 22; i++)
            {
                float a = -150 + i * 300f / 21; if (Mathf.Abs(Mathf.DeltaAngle(a, 0)) < 25) continue;
                float r = 38 + (float)rng.NextDouble() * 26, h = 14 + (float)rng.NextDouble() * 24;
                var pos = Quaternion.Euler(0, a + 180, 0) * Vector3.forward * r; pos.y = -14;
                var t = P(PrimitiveKind.Cube, stat, pos + Vector3.up * h / 2, new Vector3(4 + (float)rng.NextDouble() * 3, h, 4 + (float)rng.NextDouble() * 3), StoneDark, name: "Tower");
                P(PrimitiveKind.Cone, stat, pos + Vector3.up * (h + 3.5f), new Vector3(5.5f, 7f, 5.5f), Iron, name: "Spire");
                if (i % 4 == 0) P(PrimitiveKind.Cube, stat, pos + Vector3.up * (h - 3), new Vector3(1.6f, 2.2f, 1.6f) + Vector3.forward * 4, Brass, name: "TowerWindow", glow: true);
            }
            P(PrimitiveKind.Cylinder, stat, new Vector3(0, -30, 0), new Vector3(400, 0.1f, 400), Abyss, name: "MistFloor");

            StaticBatchingUtility.Combine(stat.gameObject);
        }

        void Ring(Transform parent, float r, float width, int segs, Color c, string name)
        {
            float seg = 2 * Mathf.PI * r / segs * 1.02f;
            for (int i = 0; i < segs; i++)
            {
                float a = (i + 0.5f) * 360f / segs; var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                P(PrimitiveKind.Cube, parent, dir * r + Vector3.up * 0.005f, new Vector3(seg, 0.01f, width), c, new Vector3(0, a + 90, 0), name);
            }
        }

        // The fixed gameplay camera sits south (+z) of the player looking north and down: anything tall between them
        // could hide a player at the wall, so camera-side perimeter pieces are low bollards (lamp on top, no spire).
        static bool CameraSide(Vector3 at) => at.z > -4f;

        void Post(Transform stat, Transform root, Vector3 at, float yaw)
        {
            if (CameraSide(at))
            {
                P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.4f, new Vector3(0.7f, 0.8f, 0.7f), Ivory, new Vector3(0, yaw, 0), "Bollard");
                P(PrimitiveKind.Cube, stat, at + Vector3.up * 0.85f, new Vector3(0.82f, 0.12f, 0.82f), Brass, new Vector3(0, yaw, 0), "BollardCap");
                var low = P(PrimitiveKind.Sphere, root, at + Vector3.up * 1.08f, new Vector3(0.3f, 0.3f, 0.3f), new Color(1f, 0.7f, 0.3f), name: "Lamp", glow: true);
                lamps.Add(low.GetComponent<Renderer>());
                return;
            }
            P(PrimitiveKind.Cube, stat, at + Vector3.up * 1.6f, new Vector3(0.85f, 3.2f, 0.85f), Ivory, new Vector3(0, yaw, 0), "Post");
            P(PrimitiveKind.Cube, stat, at + Vector3.up * 3.25f, new Vector3(1.0f, 0.18f, 1.0f), Brass, new Vector3(0, yaw, 0), "PostCap");
            P(PrimitiveKind.Cone, stat, at + Vector3.up * 4.0f, new Vector3(0.7f, 1.3f, 0.7f), Iron, name: "PostSpire");
            var outward = Sim.Flat(at).normalized;
            P(PrimitiveKind.Cube, stat, at + outward * 0.47f + Vector3.up * 1.7f, new Vector3(0.62f, 2.0f, 0.04f), Teal, new Vector3(0, yaw, 0), "Banner");
            var lamp = P(PrimitiveKind.Sphere, root, at + Vector3.up * 3.55f, new Vector3(0.32f, 0.32f, 0.32f), new Color(1f, 0.7f, 0.3f), name: "Lamp", glow: true);
            lamps.Add(lamp.GetComponent<Renderer>());
        }

        void BuildMetronome(Transform m)
        {
            // tapered open frame: two slanted brass/iron stiles, cross braces, apex cap, rear spine
            Frame = new GameObject("Frame").transform; Frame.SetParent(m, false);
            float baseHalf = 2.6f, topHalf = 0.55f, h = FrameHeight;
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(baseHalf * s, 0, 0); var b = new Vector3(topHalf * s, h, 0);
                var stile = P(PrimitiveKind.Cube, Frame, (a + b) / 2, new Vector3(0.55f, (b - a).magnitude, 0.7f), Iron, name: "Stile");
                stile.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                var trim = P(PrimitiveKind.Cube, Frame, (a + b) / 2 + new Vector3(0, 0, 0.37f), new Vector3(0.16f, (b - a).magnitude, 0.06f), Brass, name: "StileTrim");
                trim.localRotation = stile.localRotation;
                var crack = P(PrimitiveKind.Cube, Frame, Vector3.Lerp(a, b, 0.55f) + new Vector3(-0.05f * s, 0, 0.41f), new Vector3(0.05f, 1.6f, 0.02f), new Color(1f, 0.35f, 0.2f), new Vector3(0, 0, 25 * s), "FrenzyCrack", true);
                cracks.Add(crack.GetComponent<Renderer>());
            }
            for (int k = 1; k <= 3; k++)
            {
                float y = h * k / 4f, half = Mathf.Lerp(baseHalf, topHalf, y / h);
                P(PrimitiveKind.Cube, Frame, new Vector3(0, y, -0.25f), new Vector3(half * 2, 0.18f, 0.18f), Brass, name: "Brace");
            }
            P(PrimitiveKind.Cube, Frame, new Vector3(0, h / 2, -0.4f), new Vector3(0.5f, h, 0.15f), Iron, name: "Spine");
            P(PrimitiveKind.Cube, Frame, new Vector3(0, h + 0.3f, 0), new Vector3(1.6f, 0.6f, 1.0f), Brass, name: "ApexCap");
            P(PrimitiveKind.Cube, Frame, new Vector3(0, 0.25f, 0), new Vector3(baseHalf * 2 + 0.6f, 0.5f, 1.6f), Iron, name: "FrameBase");
            var mid = P(PrimitiveKind.Cube, Frame, new Vector3(0, 3.6f, 0.38f), new Vector3(0.08f, 1.8f, 0.02f), new Color(1f, 0.35f, 0.2f), null, "FrenzyCrack", true);
            cracks.Add(mid.GetComponent<Renderer>());
            // tempo scale behind the rod
            for (int k = 0; k < 9; k++) P(PrimitiveKind.Cube, Frame, new Vector3(0, 3.0f + k * 0.65f, -0.12f), new Vector3(k % 2 == 0 ? 0.7f : 0.4f, 0.05f, 0.02f), Brass, name: "ScaleMark");
            // escapement wheel at the lower pivot (separate, steps on each tick)
            Escapement = P(PrimitiveKind.Cylinder, m, new Vector3(0, 1.5f, 0.1f), new Vector3(1.9f, 0.08f, 1.9f), Brass, new Vector3(90, 0, 0), "EscapementWheel");
            for (int k = 0; k < 12; k++)
                P(PrimitiveKind.Cube, Escapement, Quaternion.Euler(0, k * 30, 0) * Vector3.forward * 0.52f, new Vector3(0.09f, 1.5f, 0.12f), Brass, new Vector3(0, k * 30, 0), "Tooth");
            // pivot, ONE rod and its sliding weight (separate objects for animation)
            var pivot = new GameObject("Pivot").transform; pivot.SetParent(m, false); pivot.localPosition = new Vector3(0, 1.5f, 0.55f);
            P(PrimitiveKind.Cylinder, pivot, Vector3.zero, new Vector3(0.55f, 0.25f, 0.55f), Brass, new Vector3(90, 0, 0), "Axle");
            Rod = new GameObject("Rod").transform; Rod.SetParent(pivot, false);
            P(PrimitiveKind.Cube, Rod, new Vector3(0, 4.1f, 0), new Vector3(0.16f, 8.2f, 0.12f), Ivory, name: "RodBar");
            P(PrimitiveKind.Cone, Rod, new Vector3(0, 8.4f, 0), new Vector3(0.25f, 0.5f, 0.25f), Brass, name: "RodTip");
            Weight = new GameObject("SlidingWeight").transform; Weight.SetParent(Rod, false); Weight.localPosition = new Vector3(0, 5.6f, 0);
            P(PrimitiveKind.Cube, Weight, Vector3.zero, new Vector3(0.75f, 1.0f, 0.4f), Brass, name: "WeightBody");
            P(PrimitiveKind.Cube, Weight, new Vector3(0, 0, 0.21f), new Vector3(0.35f, 0.55f, 0.02f), Iron, name: "WeightFace");
            var go = new GameObject("Metronome light"); go.transform.SetParent(m, false); go.transform.localPosition = new Vector3(0, 6, 4);
            metronomeLight = go.AddComponent<Light>(); metronomeLight.type = LightType.Point; metronomeLight.range = 18; metronomeLight.intensity = 6;
        }

        void Update()
        {
            var d = RunDirector.Instance;
            var boss = d != null ? d.Boss as TempoTyrantEncounter : null;
            bool fight = boss && !boss.Dead && d.State == RunState.Combat;
            float phase; bool haste; bool frenzy = boss && boss.Frenzy && !boss.Dead;
            if (fight)
            {
                Preview = false;
                phase = boss.Clock.RodPhase; haste = boss.Clock.State == Tempo.Haste;
                int beat = (int)System.Math.Floor(boss.Clock.Beat);
                if (beat != lastBeat) { lastBeat = beat; escapement += 30; }
            }
            else
            {
                // readiness / menu preview: non-damaging Haste ticking, frozen while paused
                Preview = true; haste = true;
                if (d == null || !d.Paused) previewBeat += Time.deltaTime * 160 / 60.0;
                phase = Mathf.Cos((float)(previewBeat % 2.0) * Mathf.PI);
                int beat = (int)System.Math.Floor(previewBeat);
                if (beat != lastBeat) { lastBeat = beat; escapement += 30; }
                if (boss && boss.Dead) { phase = 0; }
            }
            RodAngle = phase * SwingDegrees;
            if (Rod) Rod.localRotation = Quaternion.Euler(0, 0, RodAngle);
            escShown = Mathf.MoveTowards(escShown, escapement, Time.deltaTime * 900);
            if (Escapement) Escapement.localRotation = Quaternion.Euler(90, 0, 0) * Quaternion.Euler(0, escShown, 0);
            var tint = haste ? new Color(1f, 0.62f, 0.25f) : new Color(0.3f, 0.95f, 0.85f);
            if (metronomeLight) { metronomeLight.color = tint; metronomeLight.intensity = (haste ? 7f : 4f) * (frenzy ? 1.6f : 1f); }
            if (mpb == null) mpb = new MaterialPropertyBlock();   // survives a script reload
            float lampK = (quiet ? 0.6f : 1f) * (haste ? 2.4f : 1.4f);
            mpb.SetColor(PBase, tint); mpb.SetColor(PEmission, tint * lampK);
            foreach (var r in lamps) if (r) r.SetPropertyBlock(mpb);
            ShowingCracks = frenzy;
            mpb.SetColor(PBase, new Color(1f, 0.35f, 0.2f)); mpb.SetColor(PEmission, new Color(1f, 0.35f, 0.2f) * (frenzy ? 3f : 0f));
            foreach (var r in cracks) if (r) { r.enabled = frenzy; r.SetPropertyBlock(mpb); }
        }
    }
}
