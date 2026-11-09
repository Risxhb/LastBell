using UnityEngine;

namespace LastBell.VFX
{
    // Aurelion v4 presentation pass (8 Nov graphics audit, work/graphics-coverage-20261108/REPORT.md; done as recommended,
    // review later). Presentation only: callers pass the numbers the hazard resolved with, nothing here decides timing,
    // targets or hits. Not LAN-replicated yet (like DevourDust): clients still see the shared telegraphs.
    public sealed partial class BellkeeperFx
    {
        // a flat lane quad with U along its length (0 at the origin, 1 at the far end) and V across, for Band fronts
        static Mesh LaneMesh(float length, float width)
        {
            var m = new Mesh { name = "FX_MS_Beam" };
            m.vertices = new[] { new Vector3(-width / 2, 0, 0), new Vector3(width / 2, 0, 0), new Vector3(-width / 2, 0, length), new Vector3(width / 2, 0, length) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateBounds(); return m;
        }
        // a standing sheet along +Z, `height` tall: U along its length, V up (Pillar mode fades it upward)
        static Mesh SheetMesh(float length, float height)
        {
            var m = new Mesh { name = "FX_MS_BeamSheet" };
            m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, length), new Vector3(0, height, 0), new Vector3(0, height, length) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 };   // both faces
            m.RecalculateBounds(); return m;
        }

        // Meridian Lances, Meridian Recall and Hour Hands: a light beam strikes down one locked lane from its origin. A hot
        // front races out from him along the lane (0.12 s), a standing sheet of light flashes along it and fades upward,
        // and the far end, where the lane leaves the arena (`reach`), bursts in sparks and dust. Heavy = the Recall (gold).
        public void BeamResolve(Vector3 origin, float yaw, float reach, float width, bool heavy = false)
        {
            reach = Mathf.Max(1f, reach);
            var o = new Vector3(origin.x, 0.07f, origin.z);
            Color hi = heavy ? Gold : Coral;
            Play(LaneMesh(reach, width), true, addMat, o, yaw, 0.42f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(-0.05f, 1.1f, Ease(Mathf.Clamp01(u / 0.3f))));
                b.mpb.SetFloat(PWidth, 0.08f); b.mpb.SetFloat(PTail, 0.45f);
                b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.35f) / 0.65f)); b.mpb.SetFloat(PIntensity, 0.7f);
            }, 0, Ink, hi, Amber);
            Play(SheetMesh(reach, heavy ? 1.8f : 1.3f), true, addMat, o, yaw, 0.32f, (b, u) =>
            {
                b.go.transform.localScale = new Vector3(1, Mathf.Lerp(1f, 0.4f, u), 1);
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, Mathf.Lerp(0.4f, 0.05f, u));
            }, 2, Ink, hi, Amber);
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            for (float d = 3f; d < reach - 1f; d += 3f)
                Sparks(o + dir * d + Vector3.up * 0.2f, Vector3.up + dir * 0.3f, 2, 0.8f, 3.5f, 0.12f, 0.35f);
            var end = o + dir * reach;
            Sparks(end + Vector3.up * 0.4f, -dir * 0.6f + Vector3.up, heavy ? 22 : 14, 1.0f, 6, 0.16f, 0.5f);
            Dust(end, 0.8f, heavy ? 14 : 8, 0.9f);
        }

        // Final Noon (the enrage, and Last Light's last slice): the noon sun falls on the arena. A blinding gold column on
        // him, a white-gold wash racing out across the whole floor to the rim, and a long shake. Fires on the wipe tick.
        public void FinalNoon(Vector3 boss, float rim)
        {
            var c = new Vector3(boss.x, 0.06f, boss.z);
            Play(cylinder, false, addMat, c, 0, 1.6f, (b, u) =>
            {
                float r = Mathf.Lerp(1.2f, 4.5f, Ease(Mathf.Clamp01(u / 0.4f)));
                b.go.transform.localScale = new Vector3(r, Mathf.Lerp(18f, 10f, u), r);
                b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.3f) / 0.7f)); b.mpb.SetFloat(PIntensity, 1.4f);
            }, 2, Ink, Ivory, Gold);
            Play(FxMeshes.Annulus(0.2f, rim + 2f), true, addMat, new Vector3(0, 0.08f, 0), 0, 1.4f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(0f, 1.05f, Ease(Mathf.Clamp01(u / 0.55f))));
                b.mpb.SetFloat(PWidth, 0.12f); b.mpb.SetFloat(PTail, 1.2f);
                b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.5f) / 0.5f)); b.mpb.SetFloat(PIntensity, 1.3f);
            }, 0, Ink, Ivory, Gold);
            Play(cylinder, false, addMat, new Vector3(0, 0.06f, 0), 0, 1.2f, (b, u) =>
            {
                float r = Mathf.Lerp(2f, rim, Ease(Mathf.Clamp01(u / 0.55f)));
                b.go.transform.localScale = new Vector3(r, Mathf.Lerp(2.4f, 0.6f, u), r);
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.6f);
            }, 2, Ink, Gold, Ivory);
            Sparks(c + Vector3.up * 1.5f, Vector3.up, 60, 1.4f, 9, 0.2f, 0.9f);
            for (int i = 0; i < 16; i++)
            {
                var d = Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward;
                Sparks(d * rim * 0.75f + Vector3.up * 0.3f, Vector3.up + d * 0.4f, 4, 0.6f, 5, 0.15f, 0.6f);
            }
            CameraShake.Shake(0.55f, 0.9f);
        }

        // Meridian Towers: a cobalt light column grows inside each tower over its warning (`seconds`), full height on the
        // landing tick, then fades. Cobalt, not coral: a tower is "get in".
        public void TowerColumn(Vector3 at, float radius, float seconds)
        {
            var c = new Vector3(at.x, 0.05f, at.z);
            float life = seconds + 0.35f, land = seconds / life;
            var cobalt = new Color(0.45f, 0.75f, 2.0f);
            Play(cylinder, false, addMat, c, 0, life, (b, u) =>
            {
                float k = Mathf.Clamp01(u / land);
                float r = radius * 0.92f;
                b.go.transform.localScale = new Vector3(r, Mathf.Lerp(0.4f, 6.5f, k * k), r);
                b.mpb.SetFloat(PDissolve, u < land ? 0.55f * (1 - k) : Mathf.Clamp01((u - land) / (1 - land)));
                b.mpb.SetFloat(PIntensity, u < land ? Mathf.Lerp(0.06f, 0.25f, k) : 0.45f);
            }, 2, Ink, cobalt, cobalt);
        }
    }
}
