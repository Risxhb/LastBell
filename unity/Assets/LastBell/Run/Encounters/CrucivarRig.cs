using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Crucivar's mechanical details, driven from code on top of the body clips (presentation only: the encounter calls
    // these on its ticks and nothing here affects hits). It finds named nodes in the model (Crucivar.glb, built by
    // tools/crucivar/crucivar_build.py) or in the placeholder body, and leaves anything it can't find alone:
    //   Shutter_0..2      belly furnace shutters (front vents), hinged at the top
    //   FlankVent_L/R     side vent grilles (left/right vents)      BackVent   exhaust under the flywheel (back vent)
    //   Insulation_*      ceramic panels that shed at Casting Heat  VentFin_*  shoulder fins that unfold at Open Pressure
    //   FlywheelGuard     swings open at Open Pressure              Flywheel   always turning, faster with heat
    //   Gauge             pressure needle (Pressure Release cue)    CrucibleLip  where the molten pour leaves the crucible
    // None of these nodes are animated by the body clips, so writing them here never fights the PlayableGraph.
    public sealed class CrucivarRig : MonoBehaviour
    {
        public float Heat = 1;
        public bool Walking;

        sealed class Part { public Transform t; public Quaternion rot; public Vector3 pos, scale; public Vector3 vel, spin; public float life; }
        readonly List<Part> shutters = new List<Part>(), flank = new List<Part>(), back = new List<Part>(), insulation = new List<Part>(),
            fins = new List<Part>();
        Part guard, flywheel, gauge; Transform lip, stream; Renderer streamR;
        float frontOpen, sideOpen, backOpen, finOpen, guardOpen, gaugeK, gaugeShown, pour, fly, shed, breathe;
        bool fbTarget, ventsOn, pouring, shedding, dead;
        static readonly Color MoltenGlow = new Color(1f, 0.45f, 0.1f);

        public void Bind(Transform visual)
        {
            foreach (var t in visual.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n.StartsWith("Shutter_")) shutters.Add(P(t));
                else if (n.StartsWith("FlankVent_")) flank.Add(P(t));
                else if (n.StartsWith("BackVent")) back.Add(P(t));
                else if (n.StartsWith("Insulation_")) insulation.Add(P(t));
                else if (n.StartsWith("VentFin_")) fins.Add(P(t));
                else if (n.StartsWith("FlywheelGuard")) guard = P(t);
                else if (n.StartsWith("Flywheel")) flywheel = P(t);
                else if (n.StartsWith("Gauge")) gauge = P(t);
                else if (n.StartsWith("CrucibleLip")) lip = t;
            }
            // molten pour: a straight column from the crucible lip to the floor (gravity, not the crucible's tilt)
            stream = Prim.Make(PrimitiveKind.Cylinder, transform, Vector3.zero, Vector3.one, MoltenGlow, 6f);
            stream.name = "PourStream"; streamR = stream.GetComponent<Renderer>(); streamR.enabled = false;
            foreach (var c in stream.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var f in fins) f.t.localScale = Vector3.Scale(f.scale, new Vector3(1, 0.15f, 1));   // folded until phase 3
        }
        static Part P(Transform t) => new Part { t = t, rot = t.localRotation, pos = t.localPosition, scale = t.localScale };

        // ---------------- encounter hooks ----------------
        public void Ratchet(float k) { }                                   // the three hammer notches live in the Diebreaker clip
        public void HammerLand() { }
        public void Hiss(bool on) { gaugeK = on ? 0.6f : 0; }
        public void Pour(bool on) { pouring = on; }
        public void Gauge(float k) { gaugeK = k; }
        public void Vents(bool frontBack, bool on) { fbTarget = frontBack; ventsOn = on; }
        public void BeginPhase(int phase)
        {
            if (phase >= 2 && !shedding)
            {
                shedding = true;
                foreach (var p in insulation)
                {
                    // fall away from the body: outward from the torso centre, a little up, tumbling
                    var outward = p.t.position - transform.position; outward.y = 0;
                    p.vel = outward.normalized * Random.Range(1.2f, 2.2f) + Vector3.up * Random.Range(1f, 2.2f);
                    p.spin = Random.insideUnitSphere * 220; p.life = 0;
                    p.t.SetParent(transform.parent, true);   // leaves the rig: world space
                }
            }
            if (phase >= 3) { finOpen = 0.001f; guardOpen = 0.001f; }
        }
        public void Shutdown() { dead = true; pouring = false; ventsOn = false; gaugeK = 0; }

        // ---------------- per frame ----------------
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            breathe += dt;
            // vents: the selected pair opens wide, the idle shutters breathe slightly while the furnace is lit
            float idle = dead ? 0 : 0.08f + 0.06f * Mathf.Sin(breathe * 2.1f);
            frontOpen = Mathf.MoveTowards(frontOpen, ventsOn && fbTarget ? 1 : idle, dt / 0.18f);
            backOpen = Mathf.MoveTowards(backOpen, ventsOn && fbTarget ? 1 : 0, dt / 0.18f);
            sideOpen = Mathf.MoveTowards(sideOpen, ventsOn && !fbTarget ? 1 : 0, dt / 0.18f);
            foreach (var s in shutters) s.t.localRotation = s.rot * Quaternion.Euler(-75 * frontOpen, 0, 0);
            foreach (var s in back) s.t.localRotation = s.rot * Quaternion.Euler(70 * backOpen, 0, 0);
            for (int i = 0; i < flank.Count; i++) flank[i].t.localRotation = flank[i].rot * Quaternion.Euler(0, 0, (flank[i].t.name.EndsWith("L") ? -1 : 1) * 65 * sideOpen);

            // phase 3: fins unfold, the flywheel guard swings open
            if (finOpen > 0) finOpen = Mathf.MoveTowards(finOpen, 1, dt / 0.8f);
            foreach (var f in fins) f.t.localScale = Vector3.Scale(f.scale, new Vector3(1, Mathf.Lerp(0.15f, 1f, Smooth(finOpen)), 1));
            if (guardOpen > 0) guardOpen = Mathf.MoveTowards(guardOpen, 1, dt / 0.6f);
            if (guard != null) guard.t.localRotation = guard.rot * Quaternion.Euler(0, -115 * Smooth(guardOpen), 0);

            // flywheel and gauge
            fly += dt * (dead ? 0 : 50 + 140 * Mathf.Max(0, Heat - 1));
            if (flywheel != null) flywheel.t.localRotation = flywheel.rot * Quaternion.Euler(0, 0, fly);
            gaugeShown = Mathf.MoveTowards(gaugeShown, gaugeK, dt / 0.3f);
            if (gauge != null) gauge.t.localRotation = gauge.rot * Quaternion.Euler(0, 0, 100 - 200 * gaugeShown);

            // insulation panels tumble to the floor and shrink away (scenery, never a hazard)
            foreach (var p in insulation)
            {
                if (!shedding || !p.t) continue;
                p.life += dt;
                if (p.t.position.y > 0.05f) { p.vel += Physics.gravity * dt; p.t.position += p.vel * dt; p.t.rotation *= Quaternion.Euler(p.spin * dt); }
                else { var q = p.t.position; q.y = 0.05f; p.t.position = q; p.vel = Vector3.zero; }
                if (p.life > 2.2f) p.t.localScale = Vector3.MoveTowards(p.t.localScale, Vector3.zero, dt * 2f);
                if (p.life > 3.5f) p.t.gameObject.SetActive(false);
            }

            // the pour column
            pour = Mathf.MoveTowards(pour, pouring ? 1 : 0, dt / (pouring ? 0.35f : 0.2f));
            streamR.enabled = pour > 0.01f && lip != null;
            if (streamR.enabled)
            {
                var top = lip.position; float h = Mathf.Max(0.1f, top.y);
                stream.position = new Vector3(top.x, h / 2, top.z); stream.rotation = Quaternion.identity;
                stream.localScale = new Vector3(0.22f * pour, h / 2, 0.22f * pour);   // Unity's cylinder is 2 units tall
            }
        }
        static float Smooth(float a) { a = Mathf.Clamp01(a); return a * a * (3 - 2 * a); }

        void OnDestroy() { foreach (var p in insulation) if (p.t && p.t.parent != transform && shedding) Destroy(p.t.gameObject); }

        // ---------------- placeholder body (used only when Resources/Characters/Crucivar is missing) ----------------
        public static Transform BuildPlaceholder(Transform host)
        {
            Color iron = new Color(0.16f, 0.15f, 0.15f), brass = new Color(0.72f, 0.52f, 0.25f), molten = new Color(1f, 0.45f, 0.1f), ceramic = new Color(0.55f, 0.5f, 0.45f);
            Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(0, 2.0f, 0), new Vector3(2.8f, 1.0f, 2.4f), iron);
            Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(0, 2.7f, 0), new Vector3(2.9f, 0.08f, 2.5f), brass);
            Prim.Make(PrimitiveKind.Cube, host, new Vector3(0, 3.1f, 0.6f), new Vector3(1.2f, 0.9f, 0.4f), iron);           // furnace-door face
            for (int e = -1; e <= 1; e += 2) Prim.Make(PrimitiveKind.Sphere, host, new Vector3(0.28f * e, 3.25f, 0.82f), Vector3.one * 0.16f, molten, 4f);
            for (int i = 0; i < 3; i++)
            {
                var hinge = new GameObject("Shutter_" + i).transform; hinge.SetParent(host, false); hinge.localPosition = new Vector3(-0.6f + 0.6f * i, 2.25f, 1.22f);
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(-0.6f + 0.6f * i, 1.95f, 1.15f), new Vector3(0.42f, 0.55f, 0.1f), molten, 3f);
                Prim.Make(PrimitiveKind.Cube, hinge, new Vector3(0, -0.3f, 0.04f), new Vector3(0.48f, 0.6f, 0.06f), brass);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                var v = new GameObject(s < 0 ? "FlankVent_L" : "FlankVent_R").transform; v.SetParent(host, false); v.localPosition = new Vector3(1.42f * s, 1.9f, 0);
                Prim.Make(PrimitiveKind.Cube, v, new Vector3(0.05f * s, -0.3f, 0), new Vector3(0.06f, 0.6f, 0.7f), brass);
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(1.40f * s, 1.6f, 0), new Vector3(0.05f, 0.55f, 0.6f), molten, 2f);
                // legs, arms
                Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(0.75f * s, 0.6f, 0), new Vector3(0.7f, 0.6f, 0.7f), iron);
                Prim.Make(PrimitiveKind.Cube, host, new Vector3(0.75f * s, 0.12f, 0.15f), new Vector3(0.9f, 0.24f, 1.1f), brass);
                Prim.Make(PrimitiveKind.Sphere, host, new Vector3(1.7f * s, 2.8f, 0), Vector3.one * 0.9f, brass);
                Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(1.85f * s, 2.0f, 0.2f), new Vector3(0.5f, 0.7f, 0.5f), iron);
                var ins = Prim.Make(PrimitiveKind.Cube, host, new Vector3(1.1f * s, 2.3f, 0.95f), new Vector3(0.6f, 0.5f, 0.12f), ceramic); ins.name = "Insulation_F" + s;
                var ins2 = Prim.Make(PrimitiveKind.Cube, host, new Vector3(1.35f * s, 2.3f, -0.6f), new Vector3(0.12f, 0.6f, 0.7f), ceramic); ins2.name = "Insulation_S" + s;
                for (int k = 0; k < 3; k++)
                {
                    var fin = new GameObject($"VentFin_{s}_{k}").transform; fin.SetParent(host, false); fin.localPosition = new Vector3(1.6f * s, 3.2f, -0.3f + 0.3f * k);
                    Prim.Make(PrimitiveKind.Cube, fin, new Vector3(0, 0.45f, 0), new Vector3(0.12f, 0.9f, 0.18f), brass);
                }
            }
            // right hammer (+x: the character faces +z), left crucible (-x)
            Prim.Make(PrimitiveKind.Cube, host, new Vector3(1.95f, 0.9f, 0.4f), new Vector3(1.2f, 1.0f, 1.2f), iron);
            Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(-1.95f, 1.2f, 0.4f), new Vector3(0.9f, 0.35f, 0.9f), brass);
            Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(-1.95f, 1.5f, 0.4f), new Vector3(0.7f, 0.04f, 0.7f), molten, 4f);
            var lip = new GameObject("CrucibleLip").transform; lip.SetParent(host, false); lip.localPosition = new Vector3(-1.95f, 1.55f, 0.85f);
            // back: chimney, flywheel, guard, exhaust
            Prim.Make(PrimitiveKind.Cylinder, host, new Vector3(0.6f, 3.6f, -0.6f), new Vector3(0.45f, 0.8f, 0.45f), iron);
            var fw = new GameObject("Flywheel").transform; fw.SetParent(host, false); fw.localPosition = new Vector3(0, 2.3f, -1.3f);   // turns about its local Z (the axle points backward)
            Prim.Make(PrimitiveKind.Cylinder, fw, Vector3.zero, new Vector3(1.6f, 0.06f, 1.6f), brass, 0, Quaternion.Euler(90, 0, 0));
            for (int k = 0; k < 3; k++) Prim.Make(PrimitiveKind.Cube, fw, Vector3.zero, new Vector3(1.5f, 0.12f, 0.1f), iron, 0, Quaternion.Euler(0, 0, 60 * k));
            var guardT = new GameObject("FlywheelGuard").transform; guardT.SetParent(host, false); guardT.localPosition = new Vector3(0.85f, 2.3f, -1.4f);
            Prim.Make(PrimitiveKind.Cube, guardT, new Vector3(-0.85f, 0, -0.05f), new Vector3(1.75f, 1.75f, 0.06f), iron);
            var bv = new GameObject("BackVent").transform; bv.SetParent(host, false); bv.localPosition = new Vector3(0, 1.4f, -1.22f);
            Prim.Make(PrimitiveKind.Cube, bv, new Vector3(0, -0.2f, -0.04f), new Vector3(0.8f, 0.4f, 0.06f), brass);
            Prim.Make(PrimitiveKind.Cube, host, new Vector3(0, 1.2f, -1.18f), new Vector3(0.7f, 0.35f, 0.05f), molten, 2f);
            return host;
        }
    }
}
