using System.Collections.Generic;
using UnityEngine;

namespace LastBell.Run
{
    // Striking Order presentation (stand-in, no art yet): the ivory Roman numeral over each owner's head (an identity
    // marker, never coral), a fractured clock-tick rim on each ring that ticks round and lengthens when it locks, and a
    // shard of the halo dropping into the ring for the last 0.4 s before it strikes. The ring itself is the shared coral
    // circle telegraph (the real hit shape); the strike is the shared chime column and crack-ring (BellkeeperFx).
    // Driven by StrikingOrderAbility on its own ticks; nothing here decides timing or hits.
    sealed class StrikingOrderFx
    {
        static readonly Color IvoryBase = new Color(0.95f, 0.92f, 0.82f), AmberBase = new Color(1.0f, 0.55f, 0.22f), CoralBase = new Color(1.0f, 0.42f, 0.32f);
        static Material ivory, amber, coral;
        const float HeadHeight = 2.55f, ShardTicks = 24;

        sealed class Slot
        {
            public PlayerController Owner; public Hazard Ring;
            public Transform Glyph, Rim, Shard;
            public bool Dropped, Rang;
        }
        readonly Slot[] slots;
        readonly Transform root;

        public StrikingOrderFx(IReadOnlyList<PlayerController> owners)
        {
            if (!ivory) ivory = Prim.Material(IvoryBase, 1.8f);
            if (!amber) amber = Prim.Material(AmberBase, 1.5f);
            if (!coral) coral = Prim.Material(CoralBase, 1.6f);
            root = new GameObject("Striking Order (numerals)").transform;
            slots = new Slot[owners.Count];
            for (int k = 0; k < owners.Count; k++)
                slots[k] = new Slot { Owner = owners[k], Glyph = Glyph(StrikingOrderAbility.Numerals[k], root) };
        }

        public int NumeralCount { get { int n = 0; foreach (var s in slots) if (s.Glyph) n++; return n; } }
        public int NumeralsOn(PlayerController p) { int n = 0; foreach (var s in slots) if (s.Glyph && s.Owner == p) n++; return n; }
        public bool Dropped(int k) => slots[k].Dropped;
        public bool HasRung(int k) => slots[k].Rang;
        public void Drop(int k) { slots[k].Dropped = true; Remove(slots[k]); }
        public void Rang(int k) { slots[k].Rang = true; Remove(slots[k]); }

        public void Spawned(int k, Hazard h)
        {
            var s = slots[k]; s.Ring = h;
            s.Rim = new GameObject("Clock rim " + StrikingOrderAbility.Numerals[k]).transform; s.Rim.SetParent(root, false);
            for (int i = 0; i < 12; i++)
            {
                if (i == 3 || i == 8) continue;   // fractured: two hours missing from the dial
                var q = Quaternion.Euler(0, i * 30, 0);
                Bar(s.Rim, q * new Vector3(0, 0.06f, h.Radius - 0.22f), new Vector3(0.09f, 0.04f, i % 3 == 0 ? 0.45f : 0.3f), amber, q);
            }
            s.Shard = Bar(root, Vector3.zero, new Vector3(0.38f, 0.95f, 0.38f), coral, Quaternion.Euler(180, 0, 0), PrimitiveKind.Cone);
            s.Shard.gameObject.SetActive(false);
        }

        public void Tick(Hazard[] rings)
        {
            var cam = Camera.main;
            var face = cam ? cam.transform.rotation : Quaternion.identity;
            var right = cam ? Sim.Flat(cam.transform.right).normalized : Vector3.right;
            // each owner's pending numerals in a row over the head: the next to ring full size, later ones smaller
            var seen = new Dictionary<PlayerController, int>(); var total = new Dictionary<PlayerController, int>();
            foreach (var s in slots) if (s.Glyph) { total.TryGetValue(s.Owner, out int c); total[s.Owner] = c + 1; }
            foreach (var s in slots)
            {
                if (s.Glyph && s.Owner)
                {
                    seen.TryGetValue(s.Owner, out int i); seen[s.Owner] = i + 1;
                    float x = (i - (total[s.Owner] - 1) * 0.5f) * 0.62f;
                    s.Glyph.position = s.Owner.transform.position + Vector3.up * HeadHeight + right * x;
                    s.Glyph.rotation = face;
                    s.Glyph.localScale = Vector3.one * (i == 0 ? 1f : 0.65f);
                }
                var h = s.Ring;
                if (h == null || !s.Rim) continue;
                s.Rim.position = new Vector3(h.Origin.x, 0, h.Origin.z);
                s.Rim.rotation = Quaternion.Euler(0, -6f * (h.Age / 15), 0);   // ticks round a minute-step every quarter second
                s.Rim.localScale = h.Locked ? new Vector3(1, 1.6f, 1) : Vector3.one;
                int left = h.ResolveAt - h.Age;
                bool falling = left <= ShardTicks && left >= 0;
                s.Shard.gameObject.SetActive(falling);
                if (falling) s.Shard.position = new Vector3(h.Origin.x, Mathf.Lerp(0.4f, 7.5f, left / ShardTicks), h.Origin.z);
            }
        }

        public void Clear() { if (root) Object.Destroy(root.gameObject); }

        static void Remove(Slot s)
        {
            if (s.Glyph) Object.Destroy(s.Glyph.gameObject);
            if (s.Rim) Object.Destroy(s.Rim.gameObject);
            if (s.Shard) Object.Destroy(s.Shard.gameObject);
            s.Glyph = s.Rim = s.Shard = null;
        }

        // A Roman numeral from bars: I = one stroke, V = two leaning strokes, with clock-face serif bars top and bottom.
        static Transform Glyph(string numeral, Transform parent)
        {
            var g = new GameObject("Numeral " + numeral).transform; g.SetParent(parent, false);
            const float h = 0.5f, stroke = 0.075f;
            float x = 0;
            var parts = new List<Transform>();
            foreach (char c in numeral)
            {
                if (c == 'I') { parts.Add(Bar(g, new Vector3(x + 0.07f, 0, 0), new Vector3(stroke, h, 0.03f), ivory)); x += 0.15f; }
                else
                {
                    parts.Add(Bar(g, new Vector3(x + 0.085f, 0, 0), new Vector3(stroke, h * 1.04f, 0.03f), ivory, Quaternion.Euler(0, 0, 15)));
                    parts.Add(Bar(g, new Vector3(x + 0.215f, 0, 0), new Vector3(stroke, h * 1.04f, 0.03f), ivory, Quaternion.Euler(0, 0, -15)));
                    x += 0.32f;
                }
            }
            x -= 0.01f;
            parts.Add(Bar(g, new Vector3(x / 2, h / 2 + 0.02f, 0), new Vector3(x + 0.06f, 0.045f, 0.03f), ivory));
            parts.Add(Bar(g, new Vector3(x / 2, -h / 2 - 0.02f, 0), new Vector3(x + 0.06f, 0.045f, 0.03f), ivory));
            foreach (var p in parts) p.localPosition -= new Vector3(x / 2, 0, 0);
            return g;
        }

        static Transform Bar(Transform parent, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null, PrimitiveKind kind = PrimitiveKind.Cube)
        {
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = Prim.MeshOf(kind);
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
