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

        // LAN: on a host, each call is recorded as a boss-part effect ("CrucivarFx.<Method>") that CrucivarEncounter.
        // ReplayNetEffect plays again on the client's replica. Record is null outside a host session (one null check).
        // Ember (per bullet) is not recorded; FlameJet records every second call and the client plays it twice.
        static bool NetCrucivarOn => LastBell.Run.Networking.LanFx.Record != null;
        static void NetCrucivar(string method, params object[] args)
        {
            var r = LastBell.Run.Networking.LanFx.Record;
            if (r != null) r(LastBell.Run.Networking.LanFxTarget.BossPart, "CrucivarFx." + method, args);
        }
        static int flameJetCalls;

        // hammer landing (Anvil Quake's circle, Hard Second Press): A flat brass flash and clang; no extra shockwave.
        public void DiebreakerResolve(Vector3 centre, float radius)
        {
            if (NetCrucivarOn) NetCrucivar("DiebreakerResolve", centre, radius);
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

        // molten splash (brand landing, tower landing, castings sinking); the pool itself is the standing hazard's telegraph.
        public void SlagSplash(Vector3 pos, float radius)
        {
            if (NetCrucivarOn) NetCrucivar("SlagSplash", pos, radius);
            SlagSplashFx(pos, radius);
        }
        void SlagSplashFx(Vector3 pos, float radius)
        {
            var c = new Vector3(pos.x, 0.06f, pos.z);
            Play(cylinder, false, addMat, c, 0, 0.3f, (b, u) =>
            {
                float r = radius * Mathf.Lerp(0.5f, 1f, Ease(u));   // stops at the circle, never past it (8 Nov playtest)
                b.go.transform.localScale = new Vector3(r, 0.9f * (1 - u), r);
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.8f);
            }, 2, Slag, Molten, Amber);
            Sparks(c + Vector3.up * 0.3f, Vector3.up, 22, 1.4f, 5, 0.15f, 0.55f);
            CameraShake.Shake(0.1f, 0.15f);
        }

        // a die-stamp strip presses down (the Casting Order pours), then the stamped metal cools (the strip is safe right after).
        public void StripStamp(Vector3 origin, float yaw, float length, float width)
        {
            if (NetCrucivarOn) NetCrucivar("StripStamp", origin, yaw, length, width);
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

        // ---------------- v4 fire (look pass, 31 Oct) ----------------
        // Furnace Spin: flame licks along a live jet. Called every few ticks while it sweeps; the jet's telegraph stays the
        // hit shape, these only rise out of it (short-lived, inside the lane).
        public void FlameJet(Vector3 origin, float yaw, float length, float width)
        {
            if (NetCrucivarOn && (flameJetCalls++ & 1) == 0) NetCrucivar("FlameJet", origin, yaw, length, width);
            var rot = Quaternion.Euler(0, yaw, 0);
            for (float z = 2.2f; z < length; z += 1.6f)
            {
                var p = origin + rot * new Vector3(Random.Range(-0.35f, 0.35f) * width, 0.25f, z + Random.Range(-0.5f, 0.5f));
                Sparks(p, Vector3.up * 1.2f + rot * Vector3.forward * 0.6f, 2, 0.5f, 3.2f, 0.2f, 0.35f);
            }
        }

        // a turret's molten shot: a few embers trail behind the bullet (the moving circle is the hit shape)
        public void Ember(Vector3 pos, Vector3 vel)
        {
            Sparks(new Vector3(pos.x, 0.35f, pos.z), -vel.normalized * 0.4f + Vector3.up * 0.6f, 1, 0.4f, 1.6f, 0.16f, 0.3f);
        }

        // Pour Flood: the rim ring lands as molten metal all round the arena
        public void RimFlood(float inner, float outer)
        {
            if (NetCrucivarOn) NetCrucivar("RimFlood", inner, outer);
            for (int k = 0; k < 24; k++)
            {
                var d = Quaternion.Euler(0, k * 15f, 0) * Vector3.forward;
                Sparks(d * ((inner + outer) * 0.5f) + Vector3.up * 0.3f, Vector3.up, 6, 1.0f, 5.5f, 0.2f, 0.6f);
            }
            CameraShake.Shake(0.5f, 0.5f);
        }

        // Furnace Eruption: the whole floor erupts from the furnace outward
        public void Eruption(float radius)
        {
            if (NetCrucivarOn) NetCrucivar("Eruption", radius);   // one call: the splash and blast inside are not recorded again
            SlagSplashFx(Vector3.zero, radius * 0.5f);
            BellowsBlastFx(Vector3.zero, radius);
            for (int k = 0; k < 16; k++)
            {
                var d = Quaternion.Euler(0, k * 22.5f, 0) * Vector3.forward;
                for (float r = 2f; r < radius; r += 3f) Sparks(d * r + Vector3.up * 0.3f, Vector3.up, 3, 0.8f, 7f, 0.22f, 0.7f);
            }
            CameraShake.Shake(0.7f, 0.8f);
        }

    }
}
