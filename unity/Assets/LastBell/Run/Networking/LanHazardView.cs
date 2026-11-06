using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run.Networking
{
    // Reconstructs the host's shared-FX telegraphs; timing, hit tests and damage remain on the host.
    public sealed class LanHazardView
    {
        sealed class Entry { public LanWorldFrame.TelegraphState Shape; public BellkeeperFx.Telegraph Telegraph; public bool Spent; }
        readonly BellkeeperFx fx;
        readonly Dictionary<int, Entry> live = new Dictionary<int, Entry>();
        readonly HashSet<int> seen = new HashSet<int>();
        static readonly int Memory = Shader.PropertyToID("_Memory");
        public LanHazardView(BellkeeperFx effects) { fx = effects; }
        static bool SameShape(LanWorldFrame.TelegraphState a, LanWorldFrame.TelegraphState b) => a.Shape == b.Shape && a.Radius == b.Radius && a.Inner == b.Inner && a.HalfAngle == b.HalfAngle;
        BellkeeperFx.Telegraph Create(LanWorldFrame.TelegraphState h)
        {
            switch (h.Shape)
            {
                case 0: return fx.Circle(h.Radius);
                case 1: return fx.Ring(h.Inner, h.Radius);
                case 2: return fx.Cone(h.Radius, h.HalfAngle);
                case 3: return fx.Lane(h.Radius, h.Inner);
                default: return null;
            }
        }
        public void Apply(LanWorldFrame frame)
        {
            seen.Clear();
            foreach (var h in frame.Telegraphs)
            {
                seen.Add(h.Id);
                if (!live.TryGetValue(h.Id, out var entry) || !SameShape(entry.Shape, h))
                {
                    if (entry != null) entry.Telegraph?.Dismiss();
                    entry = new Entry { Shape = h, Telegraph = Create(h) }; live[h.Id] = entry;
                }
                var t = entry.Telegraph; if (t == null || !t.go) continue;
                t.Place(new Vector3(h.X, 0, h.Z), h.Yaw); t.Progress(h.Progress); t.Shade(h.Shade); t.Motif(h.Motif);
                t.mpb.SetFloat(Memory, h.Memory); t.maxFade = h.MaxFade; t.Order = h.Order;
                t.Clip(new Vector3(h.ClipX, 0, h.ClipZ), h.ClipRadius);
                if (h.Tinted) t.Tint(new Color(h.FillR,h.FillG,h.FillB,h.FillA), new Color(h.BorderR,h.BorderG,h.BorderB,h.BorderA));
                if (h.HoleCount > 0)
                {
                    var holes = new Vector4[h.HoleCount];
                    for (int i=0;i<holes.Length;i++) holes[i]=new Vector4(h.Holes[i*4],h.Holes[i*4+1],h.Holes[i*4+2],h.Holes[i*4+3]);
                    t.Holes(holes,holes.Length);
                }
                else if (t.holeBuf != null) t.Holes(new Vector4[0],0);
                t.FlashScale = h.FlashScale;
                var scale = t.go.transform.localScale; if (scale.z != h.ScaleZ) t.go.transform.localScale = new Vector3(scale.x, scale.y, h.ScaleZ);
                if (h.Locked) t.Lock();
                if (h.Spent && !entry.Spent) { t.SpentFast = h.SpentFast; t.Resolve(); entry.Spent = true; }
            }
            var remove = new List<int>();
            foreach (var pair in live) if (!seen.Contains(pair.Key)) { pair.Value.Telegraph?.Dismiss(); remove.Add(pair.Key); }
            foreach (int id in remove) live.Remove(id);
        }
        public void Clear() { foreach (var pair in live) pair.Value.Telegraph?.Dismiss(); live.Clear(); }
    }
}
