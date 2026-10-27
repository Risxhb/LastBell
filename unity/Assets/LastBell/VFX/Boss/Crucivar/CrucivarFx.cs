using UnityEngine;

namespace LastBell.VFX
{
    // Resolve effects for Crucivar, The Furnace Heart (bosses/2-brass-foundry-crucivar/BOSS.md). Same rules as the
    // Bellkeeper's: the encounter calls these on its own ticks, they take their geometry from the hazard that resolved,
    // and nothing here decides timing or hits. Strikes use the danger palette; slag and the furnace use molten orange;
    // steam is ivory. Decorative light never reads as damage, so none of this outlives the hit by more than ~0.5 s.
    public sealed partial class BellkeeperFx
    {
        static readonly Color Molten = new Color(2.4f, 0.9f, 0.25f), Slag = new Color(0.55f, 0.16f, 0.05f),
            Steam = new Color(1.6f, 1.55f, 1.45f), SteamLow = new Color(0.32f, 0.30f, 0.30f);

        // Diebreaker: the hammer lands in its toothed circle. A flat brass flash and clang; no extra shockwave.
        public void DiebreakerResolve(Vector3 centre, float radius)
        {
            var c = new Vector3(centre.x, 0.06f, centre.z);
            Play(FxMeshes.Annulus(0.2f, radius), true, alphaMat, c, 0, 0.4f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(0f, 1.1f, Ease(Mathf.Clamp01(u / 0.3f))));
                b.mpb.SetFloat(PWidth, 0.18f); b.mpb.SetFloat(PTail, 0.35f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.3f) / 0.7f));
            }, 0, Ink, Coral, Gold);
            Sparks(c + Vector3.up * 0.3f, Vector3.up, 34, 1.3f, 7, 0.17f, 0.5f);
            Dust(c, radius * 0.8f, 30, 1.0f);
            CameraShake.Shake(0.34f, 0.3f);
        }

        // Slag Cast: the arc of molten metal lands and splashes; the pool itself is the standing hazard's telegraph.
        public void SlagSplash(Vector3 pos, float radius)
        {
            var c = new Vector3(pos.x, 0.06f, pos.z);
            Play(cylinder, false, addMat, c, 0, 0.3f, (b, u) =>
            {
                float r = radius * Mathf.Lerp(0.5f, 1.05f, Ease(u)); b.go.transform.localScale = new Vector3(r, 0.9f * (1 - u), r);
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.8f);
            }, 2, Slag, Molten, Amber);
            Sparks(c + Vector3.up * 0.3f, Vector3.up, 22, 1.4f, 5, 0.15f, 0.55f);
            CameraShake.Shake(0.1f, 0.15f);
        }

        // Pressure Release: a brief steam jet runs out through each locked sector, then clears fast.
        public void SteamRelease(Vector3 origin, float yaw, float radius, float halfAngle)
        {
            Play(FxMeshes.Sector(1.2f, radius, halfAngle), true, alphaMat, origin + Vector3.up * 0.1f, yaw, 0.45f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(-0.1f, 1.15f, Ease(Mathf.Clamp01(u / 0.3f))));
                b.mpb.SetFloat(PWidth, 0.25f); b.mpb.SetFloat(PTail, 0.6f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.25f) / 0.75f));
            }, 0, SteamLow, Steam, Ivory);
            var rot = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.Lerp(halfAngle, -halfAngle, i / 4f) * Mathf.Deg2Rad;
                var d = rot * new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                dust.Emit(origin + d * 1.6f + Vector3.up * 1.2f, d * 6f + Vector3.up * 0.8f, 1.6f, 0.45f, 0.4f);
            }
            CameraShake.Shake(0.12f, 0.2f);
        }

        // Casting Cycle: one die-stamp strip presses down, then the stamped metal cools (the strip is safe right after).
        public void StripStamp(Vector3 origin, float yaw, float length, float width)
        {
            Play(FxMeshes.Quad(-width / 2, width / 2, 0, length), true, alphaMat, origin + Vector3.up * 0.07f, yaw, 0.5f, (b, u) =>
            {
                b.mpb.SetFloat(PProgress, Mathf.Lerp(-0.1f, 1.15f, Ease(Mathf.Clamp01(u / 0.2f))));
                b.mpb.SetFloat(PWidth, 0.3f); b.mpb.SetFloat(PTail, 0.5f); b.mpb.SetFloat(PDissolve, Mathf.Clamp01((u - 0.2f) / 0.8f));
            }, 0, Slag, Molten, Amber);
            var rot = Quaternion.Euler(0, yaw, 0);
            for (float z = 1; z < length; z += 2.2f)
                Sparks(origin + rot * new Vector3(0, 0.3f, z), Vector3.up, 5, 1.2f, 4.5f, 0.14f, 0.4f);
            CameraShake.Shake(0.2f, 0.22f);
        }

        // Rail Ram braking at the lane end: reuses the charge trail and landing.
        public void RamBrake(Vector3 pos) => ChargeLand(pos);
    }
}
