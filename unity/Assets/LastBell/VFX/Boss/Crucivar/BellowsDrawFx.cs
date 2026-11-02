using UnityEngine;

namespace LastBell.VFX
{
    // Crucivar v2 Bellows Draw (abilities-v2/03-crucivar.bellows_draw.md). Presentation only: the encounter calls these on
    // its own ticks and the hazards' telegraphs are the hit shapes. The inhale chevrons are amber (forced movement, never
    // damage); the rim pour and the blast use molten slag and the danger coral.
    public sealed partial class BellkeeperFx
    {
        // The inhale warning: one wave of amber chevrons on the floor, pointing at the boss and sliding `slide` metres
        // toward it over `life` seconds. Call it every ~0.35 s while the inhale is telegraphed and live.
        public void InhaleChevrons(Vector3 centre, float slide, float life)
        {
            const int Spokes = 10; float[] radii = { 6.2f, 8.4f, 11.2f };
            float turn = Random.Range(0f, 360f / Spokes);
            for (int s = 0; s < Spokes; s++)
            {
                float yaw = turn + s * 360f / Spokes;
                var outward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                foreach (var r in radii)
                {
                    var start = new Vector3(centre.x, 0.07f, centre.z) + outward * r;
                    Play(Chevron(0.55f, 0.45f, 0.16f), true, alphaMat, start, yaw + 180, life, (b, u) =>
                    {
                        b.go.transform.position = start - outward * slide * Ease(u);
                        b.mpb.SetFloat(PProgress, 1.05f); b.mpb.SetFloat(PWidth, 0.25f); b.mpb.SetFloat(PTail, 0.9f);
                        b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.55f) / 0.45f));
                        b.mpb.SetFloat(PIntensity, Mathf.Clamp01(u / 0.15f));
                    }, 0, Ink, Amber, Gold);
                }
            }
        }

        // Slag Rim: the troughs tip and a molten front runs inward across the annulus; the standing pool is the edge hazard.
        public void SlagRimPour(Vector3 centre, float inner, float outer)
        {
            var c = new Vector3(centre.x, 0.06f, centre.z);
            Play(FxMeshes.Annulus(inner, outer), true, alphaMat, c, 0, 0.5f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(1.1f, -0.1f, Ease(Mathf.Clamp01(u / 0.35f))));   // U runs outward: front moves in
                b.mpb.SetFloat(PWidth, 0.12f); b.mpb.SetFloat(PTail, 0.3f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.35f) / 0.65f));
            }, 0, Slag, Molten, Amber);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2 / 16; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                Sparks(c + d * (inner + 0.4f) + Vector3.up * 0.3f, Vector3.up - d * 0.4f, 4, 0.5f, 4.5f, 0.15f, 0.45f);
            }
            CameraShake.Shake(0.12f, 0.2f);
        }

        // Exhale: a point-blank blast through all four vents, a coral shock ring out to the circle's edge plus steam.
        public void BellowsBlast(Vector3 centre, float radius)
        {
            var c = new Vector3(centre.x, 0.07f, centre.z);
            Play(FxMeshes.Annulus(0.8f, radius), true, alphaMat, c, 0, 0.45f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(0f, 1.1f, Ease(Mathf.Clamp01(u / 0.3f))));
                b.mpb.SetFloat(PWidth, 0.2f); b.mpb.SetFloat(PTail, 0.4f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.3f) / 0.7f));
            }, 0, Ink, Coral, Amber);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2 / 12; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                dust.Emit(c + d * 1.4f + Vector3.up * 1.0f, d * 7f + Vector3.up * 0.6f, 1.8f, 0.5f, 0.4f);
            }
            Sparks(c + Vector3.up * 1.2f, Vector3.up, 26, 1.4f, 6, 0.16f, 0.5f);
            CameraShake.Shake(0.3f, 0.3f);
        }

        // a flat V pointing along +Z: two arms of `thick` width, `half` wide and `depth` deep; U runs from the back to the tip
        static Mesh Chevron(float half, float depth, float thick)
        {
            var v = new[]
            {
                new Vector3(-half, 0, 0), new Vector3(-half, 0, thick), new Vector3(0, 0, depth), new Vector3(0, 0, depth + thick),
                new Vector3(half, 0, 0), new Vector3(half, 0, thick),
            };
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++) uv[i] = new Vector2(v[i].z / (depth + thick), 0.5f);
            var m = new Mesh { name = "FX_MS_Chevron", vertices = v, uv = uv, triangles = new[] { 0, 1, 2, 2, 1, 3, 2, 3, 4, 4, 3, 5 } };
            m.RecalculateBounds(); return m;
        }
    }
}
