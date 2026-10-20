using System.Collections.Generic;
using LastBell.ClassTest;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace LastBell.Scenes.ClassTest
{
    // Self-building class-test arena. Drop on an empty GameObject in an empty scene and press Play;
    // it creates its own camera, light, floor, dummy, player and HUD from primitives.
    // All combat rules live in LastBell.ClassTest.TrainingSim; this only maps input and draws state.
    public sealed class ClassTestArena : MonoBehaviour
    {
        [SerializeField] int seed = 1;
        [SerializeField] int startingClass = 0;
        [SerializeField] bool startWithHazards = false;
        [Tooltip("World units per combat unit. Combat units match the legacy Godot reference.")]
        [SerializeField] float worldScale = 1f / 40f;

        TrainingSim sim;
        int classIndex;
        int supportTarget;
        Transform allyRoot;
        float accumulator;
        Vec2 previousPlayerPos;
        int lastEventSerial;
        bool showHelp = true, showRings = true;

        // Latched between ticks so short taps at high frame rates are never lost.
        bool latchBasic, latchTechnique, latchSignature, latchGuard;
        bool prevBasic, prevTechnique, prevSignature, prevGuard, prevDash, latchDash;

        Camera cam;
        Transform playerRoot, dummyRoot;
        Renderer playerBody, dummyBody;
        LineRenderer guardRing, castRing, tracer;
        readonly LineRenderer[] rangeRings = new LineRenderer[3];
        float tracerUntil, dummyFlashUntil;
        readonly Dictionary<int, HazardView> hazardViews = new Dictionary<int, HazardView>();
        readonly List<FloatingText> floaters = new List<FloatingText>();
        Material spriteMat;

        static readonly Color[] SlotColors =
        {
            new Color(0.92f, 0.92f, 0.86f), // Basic
            new Color(0.35f, 0.85f, 0.95f), // Technique
            new Color(1.00f, 0.78f, 0.25f), // Signature
            new Color(0.55f, 1.00f, 0.55f), // Guard
        };
        static readonly Color[] ClassColors = { new Color(0.30f, 0.50f, 0.85f), new Color(0.62f, 0.38f, 0.90f) };
        static readonly string[] SlotKeys = { "J / LMB", "K / RMB", "L / Q", "Space" };

        sealed class HazardView { public GameObject Go; public Material Fill; public LineRenderer Edge; public Hazard Hazard; }
        sealed class FloatingText { public Vector3 World; public string Text; public Color Color; public float Born; }

        void Start()
        {
            classIndex = Mathf.Clamp(startingClass, 0, ClassKits.All.Length - 1);
            sim = new TrainingSim(ClassKits.All[classIndex], seed) { HazardsEnabled = startWithHazards };
            previousPlayerPos = sim.Player.Position;
            lastEventSerial = sim.EventSerial;
            BuildScene();
            ApplyClassVisuals();
        }

        // ---------- Scene construction ----------

        void BuildScene()
        {
            spriteMat = new Material(FindShader("Sprites/Default", "Universal Render Pipeline/Unlit", "Unlit/Color"));

            cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Class Test Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            cam.transform.position = new Vector3(0f, 19f, -15.5f);
            cam.transform.LookAt(new Vector3(0f, 0f, -2.3f));
            cam.fieldOfView = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.07f, 0.09f);

            if (FindObjectOfType<Light>() == null)
            {
                var light = new GameObject("Class Test Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            }
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.4f);

            Vector3 min = ToWorld(TrainingSim.ArenaMin), max = ToWorld(TrainingSim.ArenaMax);
            var floor = Primitive(PrimitiveType.Cube, "Floor", transform);
            floor.transform.position = new Vector3((min.x + max.x) * 0.5f, -0.05f, (min.z + max.z) * 0.5f);
            floor.transform.localScale = new Vector3(Mathf.Abs(max.x - min.x), 0.1f, Mathf.Abs(max.z - min.z));
            floor.GetComponent<Renderer>().material = LitMaterial(new Color(0.20f, 0.21f, 0.24f));

            // 100-unit grid so distances can be read by eye.
            var gridColor = new Color(1f, 1f, 1f, 0.06f);
            for (float x = TrainingSim.ArenaMin.X + 10; x < TrainingSim.ArenaMax.X; x += 100)
                Line("Grid", gridColor, 0.02f, 0.004f, new Vec2(x, TrainingSim.ArenaMin.Y), new Vec2(x, TrainingSim.ArenaMax.Y));
            for (float y = TrainingSim.ArenaMin.Y + 20; y < TrainingSim.ArenaMax.Y; y += 100)
                Line("Grid", gridColor, 0.02f, 0.004f, new Vec2(TrainingSim.ArenaMin.X, y), new Vec2(TrainingSim.ArenaMax.X, y));
            var a = TrainingSim.ArenaMin; var b = TrainingSim.ArenaMax;
            var border = Line("Border", new Color(0.85f, 0.35f, 0.25f, 0.9f), 0.06f, 0.006f,
                a, new Vec2(b.X, a.Y), b, new Vec2(a.X, b.Y));
            border.loop = true;

            dummyRoot = new GameObject("Training Dummy").transform;
            dummyRoot.SetParent(transform, false);
            dummyRoot.position = ToWorld(TrainingSim.BossPosition);
            var body = Primitive(PrimitiveType.Cylinder, "Body", dummyRoot);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.transform.localScale = new Vector3(1.1f, 1f, 1.1f);
            dummyBody = body.GetComponent<Renderer>();
            dummyBody.material = LitMaterial(new Color(0.62f, 0.45f, 0.22f));
            var bell = Primitive(PrimitiveType.Sphere, "Bell", dummyRoot);
            bell.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            bell.transform.localScale = Vector3.one * 0.9f;
            bell.GetComponent<Renderer>().material = LitMaterial(new Color(0.78f, 0.62f, 0.30f));

            for (int i = 0; i < 3; i++)
            {
                rangeRings[i] = Circle("Range " + (ActionSlot)i, SlotColors[i], 0.05f, 0.02f, 96);
                rangeRings[i].transform.position = dummyRoot.position + Vector3.up * 0.02f;
            }

            playerRoot = new GameObject("Player").transform;
            playerRoot.SetParent(transform, false);
            var capsule = Primitive(PrimitiveType.Capsule, "Body", playerRoot);
            capsule.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            capsule.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
            playerBody = capsule.GetComponent<Renderer>();
            var nose = Primitive(PrimitiveType.Cube, "Facing", playerRoot);
            nose.transform.localScale = new Vector3(0.14f, 0.14f, 0.35f);
            nose.GetComponent<Renderer>().material = LitMaterial(new Color(0.9f, 0.9f, 0.9f));
            nose.transform.localPosition = new Vector3(0f, 0.7f, 0.32f);

            guardRing = Circle("Guard", SlotColors[3], 0.08f, 0.03f, 48);
            guardRing.transform.SetParent(playerRoot, false);
            castRing = Circle("Cast", SlotColors[2], 0.06f, 0.03f, 48);
            castRing.transform.SetParent(playerRoot, false);
            tracer = Line("Tracer", Color.white, 0.09f, 1.0f, TrainingSim.Spawn, TrainingSim.BossPosition);
            tracer.enabled = false;
            allyRoot = Primitive(PrimitiveType.Capsule, "Training Ally", transform).transform;
            allyRoot.localScale = new Vector3(0.55f, 0.9f, 0.55f);
        }

        void ApplyClassVisuals()
        {
            playerBody.material = LitMaterial(ClassColors[classIndex % ClassColors.Length]);
            for (int i = 0; i < 3; i++)
            {
                float r = sim.Kit.Offensive((ActionSlot)i).Range * worldScale;
                SetCircle(rangeRings[i], r);
            }
        }

        // ---------- Frame loop ----------

        void Update()
        {
            HandleToggles();

            bool basic = In.Basic, technique = In.Technique, signature = In.Signature, guard = In.Guard, dash = In.Dash;
            latchDash |= dash;
            latchBasic |= basic; latchTechnique |= technique; latchSignature |= signature; latchGuard |= guard;

            accumulator += Mathf.Min(Time.deltaTime, 0.1f);
            const float dt = 1f / TrainingSim.TickRate;
            while (accumulator >= dt)
            {
                accumulator -= dt;
                Vector2 m = In.Move;
                var cmd = new TrainingCommand
                {
                    SupportTarget = In.SelfCast ? 1 : supportTarget,
                    Move = new Vec2(m.x, -m.y), // screen up = combat -Y
                    Basic = basic || latchBasic,
                    Technique = technique || latchTechnique,
                    Signature = signature || latchSignature,
                    Guard = guard || latchGuard,
                };
                cmd.BasicPressed = cmd.Basic && !prevBasic;
                cmd.TechniquePressed = cmd.Technique && !prevTechnique;
                cmd.SignaturePressed = cmd.Signature && !prevSignature;
                cmd.GuardPressed = cmd.Guard && !prevGuard;
                cmd.Dash = dash || latchDash; cmd.DashPressed = cmd.Dash && !prevDash; prevDash = cmd.Dash; latchDash = false;
                prevBasic = cmd.Basic; prevTechnique = cmd.Technique; prevSignature = cmd.Signature; prevGuard = cmd.Guard;
                latchBasic = latchTechnique = latchSignature = latchGuard = false;

                previousPlayerPos = sim.Player.Position;
                sim.Step(cmd);
            }

            ConsumeEvents();
            UpdateVisuals(accumulator / dt);
        }

        void HandleToggles()
        {
            for (int i = 0; i < ClassKits.All.Length; i++)
                if (In.ClassKey(i)) SwitchClass(i);
            if (In.Down(KeyId.Tab)) SwitchClass((classIndex + 1) % ClassKits.All.Length);
            if (In.Down(KeyId.R)) { sim.Reset(seed); SnapPlayer(); }
            if (In.Down(KeyId.M)) sim.ResetMeter();
            if (In.Down(KeyId.H)) sim.HazardsEnabled = !sim.HazardsEnabled;
            if (In.Down(KeyId.I)) sim.Invulnerable = !sim.Invulnerable;
            if (In.Down(KeyId.U)) { sim.DummyInfinite = !sim.DummyInfinite; sim.Reset(seed); SnapPlayer(); }
            if (In.Down(KeyId.G)) showRings = !showRings;
            if (In.Down(KeyId.F1)) showHelp = !showHelp;
        }

        void SwitchClass(int index)
        {
            classIndex = index;
            sim.SetClass(ClassKits.All[index]);
            SnapPlayer();
            ApplyClassVisuals();
        }

        void SnapPlayer()
        {
            previousPlayerPos = sim.Player.Position;
            lastEventSerial = sim.EventSerial;
            foreach (var v in hazardViews.Values) Destroy(v.Go);
            hazardViews.Clear();
        }

        void ConsumeEvents()
        {
            int first = sim.EventSerial - sim.Events.Count + 1;
            for (int i = 0; i < sim.Events.Count; i++)
            {
                if (first + i <= lastEventSerial) continue;
                var e = sim.Events[i];
                if (e.Kind == TrainingEventKind.ActionUsed)
                {
                    tracer.enabled = true;
                    tracerUntil = Time.time + 0.12f;
                    tracer.material.color = SlotColors[(int)e.Slot];
                    float w = e.Slot == ActionSlot.Signature ? 0.22f : e.Slot == ActionSlot.Technique ? 0.14f : 0.07f;
                    tracer.startWidth = tracer.endWidth = w;
                    dummyFlashUntil = Time.time + 0.08f;
                    Float(dummyRoot.position + new Vector3(Random.Range(-0.4f, 0.4f), 2.9f, 0f), e.Damage.ToString(), SlotColors[(int)e.Slot]);
                }
                else if (e.Kind == TrainingEventKind.HazardResolved || e.Kind == TrainingEventKind.ActionBlocked)
                {
                    Color c = e.Text.EndsWith("hit") ? new Color(1f, 0.35f, 0.3f)
                        : e.Kind == TrainingEventKind.ActionBlocked ? new Color(1f, 0.7f, 0.5f) : SlotColors[3];
                    Float(playerRoot.position + Vector3.up * 1.6f, e.Text, c);
                }
                else if (e.Kind == TrainingEventKind.Healed || e.Kind == TrainingEventKind.ShieldApplied || e.Kind == TrainingEventKind.ShieldAbsorbed || e.Kind == TrainingEventKind.Cleansed)
                {
                    Float(ToWorld(e.TargetId == 2 ? sim.Ally.Position : sim.Player.Position) + Vector3.up * 1.6f, e.Text, SlotColors[3]);
                }
                else if (e.Kind == TrainingEventKind.CastCancelled || e.Kind == TrainingEventKind.DummyDefeated)
                {
                    Float(playerRoot.position + Vector3.up * 1.6f, e.Text, Color.white);
                }
            }
            lastEventSerial = sim.EventSerial;
        }

        void UpdateVisuals(float alpha)
        {
            var p = sim.Player;
            allyRoot.gameObject.SetActive(sim.IsHealer && sim.Ally.Present && sim.Ally.Alive);
            allyRoot.position = ToWorld(sim.Ally.Position) + Vector3.up * 0.9f;
            Vec2 lerped = previousPlayerPos + (p.Position - previousPlayerPos) * alpha;
            if (Vec2.Distance(previousPlayerPos, p.Position) > 50f) lerped = p.Position; // blink or respawn
            playerRoot.position = ToWorld(lerped);
            playerRoot.gameObject.SetActive(p.Alive);
            Vector3 toDummy = dummyRoot.position - playerRoot.position; toDummy.y = 0f;
            if (toDummy.sqrMagnitude > 0.0001f) playerRoot.rotation = Quaternion.LookRotation(toDummy);


            guardRing.enabled = sim.GuardActive || (p.Shield > 0 && sim.Tick < p.ShieldUntil);
            SetCircle(guardRing, 0.5f);
            castRing.enabled = p.Casting;
            if (p.Casting)
            {
                var def = sim.Kit.Offensive(p.CastSlot);
                float t = 1f - (p.CastUntil - sim.Tick) / (float)Mathf.Max(1, def.CastTicks);
                SetCircle(castRing, Mathf.Lerp(1.1f, 0.3f, t));
            }
            playerBody.material.color = ClassColors[classIndex % ClassColors.Length] *
                (sim.Tick < p.ImmuneUntil && (Time.frameCount / 4) % 2 == 0 ? 1.8f : 1f);

            float d = sim.DistanceToDummy;
            for (int i = 0; i < 3; i++)
            {
                rangeRings[i].enabled = showRings;
                rangeRings[i].transform.position = sim.IsHealer && i > 0 ? playerRoot.position : dummyRoot.position;
                bool inRange = d <= sim.Kit.Offensive((ActionSlot)i).Range;
                Color c = SlotColors[i]; c.a = inRange ? 0.9f : 0.25f;
                rangeRings[i].material.color = c;
            }

            if (tracer.enabled)
            {
                if (Time.time > tracerUntil) tracer.enabled = false;
                tracer.SetPosition(0, playerRoot.position + Vector3.up * 0.7f);
                tracer.SetPosition(1, dummyRoot.position + Vector3.up * 1.2f);
            }
            dummyBody.transform.localScale = Time.time < dummyFlashUntil ? new Vector3(1.2f, 1.05f, 1.2f) : new Vector3(1.1f, 1f, 1.1f);
            dummyRoot.gameObject.SetActive(sim.DummyAlive);

            SyncHazards();
        }

        void SyncHazards()
        {
            var live = new HashSet<int>();
            foreach (var h in sim.Hazards)
            {
                live.Add(h.Id);
                HazardView v;
                if (!hazardViews.TryGetValue(h.Id, out v))
                {
                    v = BuildHazard(h);
                    hazardViews.Add(h.Id, v);
                }
                float t = Mathf.Clamp01((sim.Tick - h.WarningTick) / (float)(h.ResolveTick - h.WarningTick));
                v.Fill.color = Color.Lerp(new Color(0.9f, 0.35f, 0.15f, 0.18f), new Color(1f, 0.15f, 0.1f, 0.55f), t * t);
            }
            var dead = new List<int>();
            foreach (var kv in hazardViews) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead) { Destroy(hazardViews[id].Go); hazardViews.Remove(id); }
        }

        HazardView BuildHazard(Hazard h)
        {
            var go = new GameObject(h.Name + " #" + h.Id);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            const int seg = 48;
            Vec2 dir;
            if (h.Kind == HazardKind.Cone)
            {
                verts.Add(ToWorld(h.Origin));
                for (int i = 0; i <= seg; i++)
                {
                    float ang = h.Direction - h.HalfAngle + 2f * h.HalfAngle * i / seg;
                    dir = new Vec2(Mathf.Cos(ang), Mathf.Sin(ang));
                    verts.Add(ToWorld(h.Origin + dir * h.Radius));
                    if (i > 0) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
                }
            }
            else
            {
                for (int i = 0; i <= seg * 2; i++)
                {
                    float ang = Mathf.PI * 2f * i / (seg * 2);
                    dir = new Vec2(Mathf.Cos(ang), Mathf.Sin(ang));
                    verts.Add(ToWorld(h.Origin + dir * h.InnerRadius));
                    verts.Add(ToWorld(h.Origin + dir * h.Radius));
                    if (i > 0)
                    {
                        int k = i * 2;
                        tris.Add(k - 2); tris.Add(k - 1); tris.Add(k);
                        tris.Add(k - 1); tris.Add(k + 1); tris.Add(k);
                    }
                }
            }
            // Emit both windings so the fill shows regardless of the shader's cull mode.
            int n = tris.Count;
            for (int i = 0; i < n; i += 3) { tris.Add(tris[i]); tris.Add(tris[i + 2]); tris.Add(tris[i + 1]); }
            for (int i = 0; i < verts.Count; i++) verts[i] += Vector3.up * 0.012f;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var fill = new Material(spriteMat);
            go.AddComponent<MeshRenderer>().sharedMaterial = fill;

            var edge = go.AddComponent<LineRenderer>();
            ConfigureLine(edge, new Color(1f, 0.3f, 0.2f, 1f), 0.06f);
            if (h.Kind == HazardKind.Cone)
            {
                var pts = new List<Vector3> { ToWorld(h.Origin) };
                for (int i = 1; i <= seg + 1; i++) pts.Add(verts[i]);
                edge.positionCount = pts.Count;
                edge.SetPositions(pts.ToArray());
                edge.loop = true;
            }
            else
            {
                var pts = new List<Vector3>();
                for (int i = 0; i <= seg * 2; i++) pts.Add(verts[i * 2 + 1]);
                edge.positionCount = pts.Count;
                edge.SetPositions(pts.ToArray());
                var inner = new GameObject("Inner edge").AddComponent<LineRenderer>();
                inner.transform.SetParent(go.transform, false);
                ConfigureLine(inner, new Color(1f, 0.3f, 0.2f, 1f), 0.06f);
                var ip = new List<Vector3>();
                for (int i = 0; i <= seg * 2; i++) ip.Add(verts[i * 2]);
                inner.positionCount = ip.Count;
                inner.SetPositions(ip.ToArray());
            }
            for (int i = 0; i < edge.positionCount; i++) edge.SetPosition(i, edge.GetPosition(i) + Vector3.up * 0.01f);
            return new HazardView { Go = go, Fill = fill, Edge = edge, Hazard = h };
        }

        void Float(Vector3 world, string text, Color color)
        {
            floaters.Add(new FloatingText { World = world, Text = text, Color = color, Born = Time.time });
            if (floaters.Count > 24) floaters.RemoveAt(0);
        }

        // ---------- HUD ----------

        GUIStyle label, small, big, boxStyle;

        void OnGUI()
        {
            if (sim == null) return;
            float s = Mathf.Max(0.75f, Screen.height / 1080f);
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { richText = true };
                small = new GUIStyle(label);
                big = new GUIStyle(label) { fontStyle = FontStyle.Bold };
                boxStyle = new GUIStyle(GUI.skin.box);
            }
            label.fontSize = Mathf.RoundToInt(17 * s);
            small.fontSize = Mathf.RoundToInt(14 * s);
            big.fontSize = Mathf.RoundToInt(26 * s);

            var kit = sim.Kit;
            var p = sim.Player;
            float pad = 14 * s, line = 24 * s;

            // Left: class kit.
            var left = new Rect(pad, pad, 470 * s, 390 * s);
            GUI.Box(left, GUIContent.none, boxStyle);
            float y = left.y + 8 * s; float x = left.x + 12 * s; float w = left.width - 24 * s;
            GUI.Label(new Rect(x, y, w, 34 * s), kit.Name.ToUpperInvariant(), big); y += 34 * s;
            GUI.Label(new Rect(x, y, w, line), "[1] Vanguard  [2] Arcanist  [3] Healer  [Tab] cycle", small); y += line;
            GUI.Label(new Rect(x, y, w, line * 2), "<color=#ccc>" + kit.Identity + "</color>", small); y += line * 1.6f;
            Pips(new Rect(x, y, w, line), "HP", p.Hp, TrainingSim.MaxHp, new Color(0.9f, 0.3f, 0.3f), s); y += line;
            Pips(new Rect(x, y, w, line), kit.ResourceName, p.Resource, kit.ResourceCap, SlotColors[1], s); y += line * 1.2f;
            for (int i = 0; i < 4; i++)
            {
                var slot = (ActionSlot)i;
                string name, detail;
                if (slot == ActionSlot.Guard) { name = kit.Guard.Name; detail = sim.IsHealer ? "1 Focus / shield + cleanse" : (kit.Guard.Window / 60f).ToString("0.0#") + " s window"; }
                else
                {
                    var def = kit.Offensive(slot);
                    name = def.Name;
                    detail = "range " + def.Range + "  dmg " + def.Damage + (def.ResourceDelta != 0 ? "  " + (def.ResourceDelta > 0 ? "+" : "") + def.ResourceDelta : "") + (def.CastTicks > 0 ? "  cast " + (def.CastTicks / 60f).ToString("0.0") + " s" : "");
                }
                if (sim.IsHealer && slot == ActionSlot.Technique) detail = "2 Focus / heal 1 + regen 2 / cast 0.5s";
                if (sim.IsHealer && slot == ActionSlot.Signature) detail = "5 Focus / heal 3 / radius 180";
                int cd = sim.CooldownRemaining(slot);
                string state = cd > 0 ? "<color=#f88>" + (cd / 60f).ToString("0.0") + " s</color>" : "<color=#8f8>ready</color>";
                if ((!sim.IsHealer || slot == ActionSlot.Basic) && slot != ActionSlot.Guard && cd == 0 && sim.DistanceToDummy > kit.Offensive(slot).Range) state = "<color=#fc6>out of range</color>";
                if (sim.IsHealer && cd == 0)
                {
                    int cost = slot == ActionSlot.Guard ? HealerRules.WardCost : -kit.Offensive(slot).ResourceDelta;
                    if (p.Resource < cost) state = "<color=#fc6>need Focus</color>";
                    else if ((slot == ActionSlot.Guard || slot == ActionSlot.Technique) && sim.SelectSupportTarget(In.SelfCast ? 1 : supportTarget, kit.Technique.Range) == null) state = "<color=#fc6>invalid / range</color>";
                    else if (p.Casting && slot != ActionSlot.Guard) state = "casting";
                }
                GUI.Label(new Rect(x, y, w, line), "<color=#" + ColorUtility.ToHtmlStringRGB(SlotColors[i]) + ">[" + SlotKeys[i] + "]</color> <b>" + name + "</b>  " + state, small);
                GUI.Label(new Rect(x, y + line * 0.8f, w, line), "<color=#999>" + detail + "</color>", small);
                y += line * 1.65f;
            }
            GUI.Label(new Rect(x, y + 4 * s, w, line), "Distance to dummy: <b>" + Mathf.RoundToInt(sim.DistanceToDummy) + "</b>", small);

            if (sim.IsHealer)
            {
                float sy = left.yMax + 8 * s;
                GUI.Box(new Rect(left.x, sy, left.width, 155 * s), GUIContent.none);
                string[] targets = { "Auto (lowest HP)", "Self", "Ally" };
                for (int i = 0; i < 3; i++)
                    if (GUI.Button(new Rect(x + i * 145 * s, sy + 5 * s, 140 * s, line), (supportTarget == i ? "> " : "") + targets[i])) supportTarget = i;
                sy += line + 8 * s;
                foreach (var a in sim.SupportActors)
                {
                    GUI.Label(new Rect(x, sy, w, line), (a.Id == 1 ? "Self" : "Ally") + " HP " + a.Hp + "/5  Shield " + a.Shield + "  Regen " + Mathf.Max(0, (a.HotUntil - sim.Tick) / 60) + "s  Chill " + Mathf.Max(0, (a.ChillUntil - sim.Tick) / 60) + "s", small);
                    sy += line;
                }
                GUI.Label(new Rect(x, sy, w, line * 2), "Mend: 1 + two regen ticks. Sanctuary: 3 HP, radius 180.\nShift / LB: self-cast. Auto ties prefer self. R resets ally.", small);
            }

            // Right: dummy and meter.
            var right = new Rect(Screen.width - 400 * s - pad, pad, 400 * s, 250 * s);
            GUI.Box(right, GUIContent.none, boxStyle);
            x = right.x + 12 * s; y = right.y + 8 * s; w = right.width - 24 * s;
            GUI.Label(new Rect(x, y, w, 34 * s), "TRAINING DUMMY", big); y += 34 * s;
            GUI.Label(new Rect(x, y, w, line), "HP: " + (sim.DummyInfinite ? "infinite" : sim.DummyHp + " / " + sim.DummyMaxHp) + (sim.LastKillTicks > 0 ? "   last kill " + (sim.LastKillTicks / 60f).ToString("0.0") + " s" : ""), label); y += line;
            GUI.Label(new Rect(x, y, w, line), "Damage <b>" + sim.TotalDamage + "</b>    DPS (10 s) <b>" + sim.Dps.ToString("0.0") + "</b>", label); y += line * 1.2f;
            for (int i = 0; i < 3; i++)
            {
                GUI.Label(new Rect(x, y, w, line), "<color=#" + ColorUtility.ToHtmlStringRGB(SlotColors[i]) + ">" + kit.Offensive((ActionSlot)i).Name + "</color>  " + sim.DamageBySlot[i] + " dmg  ×" + sim.UsesBySlot[i], small);
                y += line * 0.9f;
            }
            y += line * 0.3f;
            GUI.Label(new Rect(x, y, w, line), "[H] Hazards " + OnOff(sim.HazardsEnabled) + "   [I] Invulnerable " + OnOff(sim.Invulnerable), small); y += line * 0.9f;
            GUI.Label(new Rect(x, y, w, line), "[U] Dummy " + (sim.DummyInfinite ? "infinite" : sim.DummyMaxHp + " HP") + "   [G] Rings " + OnOff(showRings), small); y += line * 0.9f;
            GUI.Label(new Rect(x, y, w, line), "[M] Reset meter   [R] Reset all   [F1] Help", small);

            // Cooldowns next to the player so eyes can stay on the arena.
            if (p.Alive && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(playerRoot.position);
                float bw = 22 * s, bh = 6 * s;
                float bx = sp.x - (bw * 4 + 6 * s) * 0.5f, by = Screen.height - sp.y + 10 * s;
                for (int i = 0; i < 4; i++)
                {
                    var slot = (ActionSlot)i;
                    int total = slot == ActionSlot.Guard ? kit.Guard.Cooldown : kit.Offensive(slot).Cooldown;
                    float ready = 1f - sim.CooldownRemaining(slot) / (float)Mathf.Max(1, total);
                    var r = new Rect(bx + i * (bw + 2 * s), by, bw, bh);
                    Fill(r, new Color(0f, 0f, 0f, 0.6f));
                    Color c = SlotColors[i]; if (ready < 1f) c *= 0.55f;
                    Fill(new Rect(r.x, r.y, r.width * ready, r.height), c);
                }
            }

            // Floating text.
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                var f = floaters[i];
                float age = Time.time - f.Born;
                if (age > 0.9f) { floaters.RemoveAt(i); continue; }
                Vector3 sp = cam.WorldToScreenPoint(f.World + Vector3.up * age * 0.8f);
                if (sp.z < 0) continue;
                var c = f.Color; c.a = 1f - age / 0.9f;
                GUI.color = c;
                GUI.Label(new Rect(sp.x - 150 * s, Screen.height - sp.y - 14 * s, 300 * s, 28 * s), f.Text, Centered(label));
                GUI.color = Color.white;
            }

            // Recent log.
            var logRect = new Rect(pad, Screen.height - pad - 170 * s, 420 * s, 170 * s);
            GUI.Box(logRect, GUIContent.none, boxStyle);
            y = logRect.y + 6 * s;
            int start = Mathf.Max(0, sim.Events.Count - 7);
            for (int i = start; i < sim.Events.Count; i++)
            {
                var e = sim.Events[i];
                GUI.Label(new Rect(logRect.x + 10 * s, y, logRect.width - 20 * s, line), "<color=#888>" + (e.Tick / 60f).ToString("0.00") + "</color>  " + e.Text + (e.Damage > 0 ? "  <b>" + e.Damage + "</b>" : ""), small);
                y += line * 0.9f;
            }

            if (!p.Alive)
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 40 * s), "KNOCKED OUT — respawning", Centered(big));

            if (showHelp)
            {
                var help = new Rect(Screen.width - 400 * s - pad, Screen.height - pad - 170 * s, 400 * s, 170 * s);
                GUI.Box(help, GUIContent.none, boxStyle);
                GUI.Label(new Rect(help.x + 12 * s, help.y + 8 * s, help.width - 24 * s, help.height - 16 * s),
                    "<b>Controls</b>\nMove: WASD / arrows / left stick\nBasic: J / LMB / A (hold to repeat)\nTechnique: K / RMB / X\nSignature: L / Q / Y      Guard: Space / RB\nRings show where each action reaches the dummy.", small);
            }
        }

        static string OnOff(bool v) { return v ? "<color=#8f8>on</color>" : "<color=#888>off</color>"; }

        static GUIStyle Centered(GUIStyle s) { return new GUIStyle(s) { alignment = TextAnchor.MiddleCenter }; }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Pips(Rect r, string name, int value, int max, Color color, float s)
        {
            GUI.Label(new Rect(r.x, r.y, 90 * s, r.height), name, small);
            float size = 16 * s;
            for (int i = 0; i < max; i++)
            {
                var pip = new Rect(r.x + 90 * s + i * (size + 5 * s), r.y + (r.height - size) * 0.5f, size, size);
                Fill(pip, new Color(0f, 0f, 0f, 0.5f));
                if (i < value) Fill(new Rect(pip.x + 2 * s, pip.y + 2 * s, pip.width - 4 * s, pip.height - 4 * s), color);
            }
        }

        // ---------- Helpers ----------

        Vector3 ToWorld(Vec2 p)
        {
            return new Vector3((p.X - TrainingSim.BossPosition.X) * worldScale, 0f, (TrainingSim.BossPosition.Y - p.Y) * worldScale);
        }

        static Shader FindShader(params string[] names)
        {
            foreach (var n in names) { var sh = Shader.Find(n); if (sh != null) return sh; }
            return null;
        }

        static Material LitMaterial(Color c)
        {
            var m = new Material(FindShader("Universal Render Pipeline/Lit", "Standard", "Universal Render Pipeline/Unlit", "Unlit/Color"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            return go;
        }

        void ConfigureLine(LineRenderer lr, Color c, float width)
        {
            lr.material = new Material(spriteMat) { color = c };
            lr.startWidth = lr.endWidth = width;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
        }

        LineRenderer Line(string name, Color c, float width, float height, params Vec2[] points)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            ConfigureLine(lr, c, width);
            lr.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++) lr.SetPosition(i, ToWorld(points[i]) + Vector3.up * height);
            return lr;
        }

        LineRenderer Circle(string name, Color c, float width, float height, int segments)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            ConfigureLine(lr, c, width);
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = segments;
            lr.transform.localPosition = Vector3.up * height;
            return lr;
        }

        static void SetCircle(LineRenderer lr, float radius)
        {
            int n = lr.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
        }

        // ---------- Input (works with the legacy Input Manager or the Input System package) ----------

        enum KeyId { Tab, R, M, H, I, U, G, F1 }

        static class In
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            static Keyboard K { get { return Keyboard.current; } }
            static Gamepad P { get { return Gamepad.current; } }
            static bool Held(Key k) { return K != null && K[k].isPressed; }

            public static Vector2 Move
            {
                get
                {
                    Vector2 v = Vector2.zero;
                    if (Held(Key.A) || Held(Key.LeftArrow)) v.x -= 1;
                    if (Held(Key.D) || Held(Key.RightArrow)) v.x += 1;
                    if (Held(Key.S) || Held(Key.DownArrow)) v.y -= 1;
                    if (Held(Key.W) || Held(Key.UpArrow)) v.y += 1;
                    if (P != null && P.leftStick.ReadValue().sqrMagnitude > v.sqrMagnitude) v = P.leftStick.ReadValue();
                    return Vector2.ClampMagnitude(v, 1f);
                }
            }
            public static bool SelfCast { get { return Held(Key.LeftShift) || (P != null && P.leftShoulder.isPressed); } }
            public static bool Basic { get { return Held(Key.J) || (Mouse.current != null && Mouse.current.leftButton.isPressed) || (P != null && P.buttonSouth.isPressed); } }
            public static bool Technique { get { return Held(Key.K) || (Mouse.current != null && Mouse.current.rightButton.isPressed) || (P != null && P.buttonWest.isPressed); } }
            public static bool Signature { get { return Held(Key.L) || Held(Key.Q) || (P != null && P.buttonNorth.isPressed); } }
            public static bool Guard { get { return Held(Key.Space) || (P != null && P.rightShoulder.isPressed); } }
            public static bool Dash { get { return Held(Key.E) || Held(Key.Digit5) || (P != null && P.buttonEast.isPressed); } }
            public static bool ClassKey(int i)
            {
                if (K == null) return false;
                if (i == 0) return K.digit1Key.wasPressedThisFrame;
                if (i == 1) return K.digit2Key.wasPressedThisFrame;
                if (i == 2) return K.digit3Key.wasPressedThisFrame;
                return false;
            }
            public static bool Down(KeyId k)
            {
                if (K == null) return false;
                switch (k)
                {
                    case KeyId.Tab: return K.tabKey.wasPressedThisFrame;
                    case KeyId.R: return K.rKey.wasPressedThisFrame;
                    case KeyId.M: return K.mKey.wasPressedThisFrame;
                    case KeyId.H: return K.hKey.wasPressedThisFrame;
                    case KeyId.I: return K.iKey.wasPressedThisFrame;
                    case KeyId.U: return K.uKey.wasPressedThisFrame;
                    case KeyId.G: return K.gKey.wasPressedThisFrame;
                    default: return K.f1Key.wasPressedThisFrame;
                }
            }
#else
            public static Vector2 Move
            {
                get
                {
                    Vector2 v = Vector2.zero;
                    if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) v.x -= 1;
                    if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) v.x += 1;
                    if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v.y -= 1;
                    if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v.y += 1;
                    var stick = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                    if (stick.sqrMagnitude > v.sqrMagnitude) v = stick;
                    return Vector2.ClampMagnitude(v, 1f);
                }
            }
            public static bool SelfCast { get { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.JoystickButton4); } }
            public static bool Basic { get { return Input.GetKey(KeyCode.J) || Input.GetMouseButton(0) || Input.GetKey(KeyCode.JoystickButton0); } }
            public static bool Technique { get { return Input.GetKey(KeyCode.K) || Input.GetMouseButton(1) || Input.GetKey(KeyCode.JoystickButton2); } }
            public static bool Signature { get { return Input.GetKey(KeyCode.L) || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.JoystickButton3); } }
            public static bool Guard { get { return Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.JoystickButton5); } }
            public static bool Dash { get { return Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Alpha5) || Input.GetKey(KeyCode.JoystickButton1); } }
            public static bool ClassKey(int i) { return Input.GetKeyDown(KeyCode.Alpha1 + i); }
            public static bool Down(KeyId k)
            {
                switch (k)
                {
                    case KeyId.Tab: return Input.GetKeyDown(KeyCode.Tab);
                    case KeyId.R: return Input.GetKeyDown(KeyCode.R);
                    case KeyId.M: return Input.GetKeyDown(KeyCode.M);
                    case KeyId.H: return Input.GetKeyDown(KeyCode.H);
                    case KeyId.I: return Input.GetKeyDown(KeyCode.I);
                    case KeyId.U: return Input.GetKeyDown(KeyCode.U);
                    case KeyId.G: return Input.GetKeyDown(KeyCode.G);
                    default: return Input.GetKeyDown(KeyCode.F1);
                }
            }
#endif
        }
    }
}
