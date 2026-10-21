using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Arcanist effects (#41): teal time magic. Seal dials must read without colour (handoff): each seal is its own dial
    // above the target with a hand showing time left; RIPE adds an outer ring of ticks (shape), INSCRIBED adds a centre
    // gem (shape). Normal vs empowered Lance and early vs perfect Convergence differ in size and ring count.
    public sealed partial class PlayerFx
    {
        public static readonly Color TimeTeal = new Color(0.45f, 1.9f, 1.75f), TimeIvory = new Color(1.8f, 1.75f, 1.55f), TimeBrass = new Color(1.6f, 1.15f, 0.5f);

        sealed class Dial { public Transform root, hand, ripe, gem; public Renderer face; }
        readonly List<Dial> dials = new List<Dial>();
        Transform anchorMarker, shieldRing; PlayerController shieldOwner;
        Burst lanceCharge; int lanceChargeId;

        void ResetArcanist()
        {
            foreach (var d in dials) if (d.root) d.root.gameObject.SetActive(false);
            if (anchorMarker) anchorMarker.gameObject.SetActive(false);
            if (shieldRing) shieldRing.gameObject.SetActive(false);
            shieldOwner = null; lanceCharge = null;
        }

        Dial NewDial()
        {
            var d = new Dial { root = new GameObject("Seal dial").transform };
            d.root.SetParent(transform, false);
            var faceT = Prim.Make(PrimitiveKind.Cylinder, d.root, Vector3.zero, new Vector3(0.9f, 0.03f, 0.9f), new Color(0.08f, 0.2f, 0.25f), 0.6f, Quaternion.Euler(90, 0, 0));
            d.face = faceT.GetComponent<Renderer>();
            Prim.Make(PrimitiveKind.Cylinder, d.root, new Vector3(0, 0, 0.02f), new Vector3(1.0f, 0.015f, 1.0f), new Color(0.75f, 0.55f, 0.25f), 0.8f, Quaternion.Euler(90, 0, 0));
            d.hand = new GameObject("hand").transform; d.hand.SetParent(d.root, false); d.hand.localPosition = new Vector3(0, 0, -0.03f);
            Prim.Make(PrimitiveKind.Cube, d.hand, new Vector3(0, 0.2f, 0), new Vector3(0.06f, 0.4f, 0.03f), new Color(0.9f, 0.95f, 0.9f), 2f);
            d.ripe = new GameObject("ripe ticks").transform; d.ripe.SetParent(d.root, false);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30 * Mathf.Deg2Rad;
                Prim.Make(PrimitiveKind.Cube, d.ripe, new Vector3(Mathf.Sin(a) * 0.62f, Mathf.Cos(a) * 0.62f, 0), new Vector3(0.07f, 0.16f, 0.04f), new Color(1f, 0.85f, 0.45f), 2.5f, Quaternion.Euler(0, 0, -i * 30));
            }
            d.gem = Prim.Make(PrimitiveKind.Sphere, d.root, new Vector3(0, 0, -0.06f), Vector3.one * 0.22f, new Color(0.4f, 1f, 0.95f), 3f);
            return d;
        }

        // Called every combat tick while seals exist (null clears). Dials sit in a row above the boss, oldest on the left.
        public void Seals(BossEncounter boss, IReadOnlyList<ArcanistKit.Seal> seals)
        {
            int n = seals == null || boss == null || !boss.Solid ? 0 : seals.Count;
            while (dials.Count < n) dials.Add(NewDial());
            var cam = Camera.main;
            for (int i = 0; i < dials.Count; i++)
            {
                var d = dials[i]; bool on = i < n; d.root.gameObject.SetActive(on);
                if (!on) continue;
                var s = seals[i];
                var right = cam ? cam.transform.right : Vector3.right;
                // in front of the target at chest height (toward the camera), so the follow camera never clips them
                var toCam = cam ? Sim.Flat(-cam.transform.forward).normalized : Vector3.back;
                d.root.position = boss.transform.position + toCam * (boss.BodyRadius + 0.6f) + Vector3.up * (boss.HitHeight + 0.6f) + right * (i - (n - 1) / 2f) * 1.4f;
                d.root.localScale = Vector3.one * 1.15f;
                if (cam) d.root.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
                d.hand.localRotation = Quaternion.Euler(0, 0, 360f * s.Left / 360f);   // sweeps back to twelve as the seal runs out
                d.ripe.gameObject.SetActive(s.Ripe);
                if (s.Ripe) d.ripe.localScale = Vector3.one * (1 + 0.06f * Mathf.Sin(Time.time * 14));
                d.gem.gameObject.SetActive(s.Inscribed);
                Prim.SetGlow(d.face, TimeTeal * (s.Ripe ? 0.5f : 0.2f));
            }
        }

        public void SealCast(Vector3 from, Vector3 to, bool hit)
        {
            var d = Sim.Flat(to - from); float len = d.magnitude; if (len < 0.1f) return;
            Play(FxMeshes.Quad(-0.08f, 0.08f, 0, len), true, add, from, Quaternion.LookRotation(d), 0.15f, (b, u) => { b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.1f); }, 2, Ink, TimeTeal, TimeIvory);
            if (!hit) return;
            Play(FxMeshes.Annulus(0.8f, 1f), true, add, to, Quaternion.identity, 0.35f, (b, u) =>
            {
                float r = Mathf.Lerp(1.8f, 0.6f, Ease(u)); b.go.transform.localScale = new Vector3(r, 1, r);   // a seal closes in (opposite of a bell's ring-out)
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.2f);
            }, 2, Ink, TimeTeal, TimeIvory);
            Spray(to, Vector3.up, 10, 0.8f, 3, 0.12f, 0.35f, motes);
        }

        public void Inscribe(BossEncounter boss)
        {
            if (boss == null) return;
            var cam2 = Camera.main; var at = boss.transform.position + (cam2 ? Sim.Flat(-cam2.transform.forward).normalized : Vector3.back) * (boss.BodyRadius + 0.6f) + Vector3.up * (boss.HitHeight + 0.6f);
            Spray(at, Vector3.up, 24, 1.0f, 4, 0.16f, 0.45f, sparks);
        }

        // Arc Lance: a tightening teal ring at the caster's feet during the brief rooted cast; the beam on release
        public void LanceCharge(PlayerController p, int ticks, bool empowered)
        {
            Kill(lanceCharge, lanceChargeId);
            float life = ticks / (float)Sim.Hz;
            lanceCharge = Play(FxMeshes.Annulus(0.85f, 1f), true, addFloor, new Vector3(p.transform.position.x, 0.05f, p.transform.position.z), Quaternion.identity, life, (b, u) =>
            {
                float r = Mathf.Lerp(1.6f, 0.7f, u); b.go.transform.localScale = new Vector3(r, 1, r);
                b.mpb.SetFloat(PIntensity, (empowered ? 1.2f : 0.6f) + u); b.mpb.SetFloat(PDissolve, 0);
            }, 2, Ink, TimeTeal, TimeIvory, p.transform);
            lanceChargeId = lanceCharge.id;
        }
        public void LanceCancel(PlayerController p) { Kill(lanceCharge, lanceChargeId); lanceCharge = null; }

        public void Lance(Vector3 from, Vector3 dir, float range, bool empowered, bool hit)
        {
            lanceCharge = null;
            float w = empowered ? 0.45f : 0.22f;
            Play(FxMeshes.Quad(-w, w, 0, range), true, add, from, Quaternion.LookRotation(dir), empowered ? 0.3f : 0.2f, (b, u) =>
            {
                b.go.transform.localScale = new Vector3(1 - 0.7f * u, 1, 1);
                b.mpb.SetFloat(PDissolve, u * 0.8f); b.mpb.SetFloat(PIntensity, empowered ? 2f : 1.2f);
            }, 2, Ink, TimeTeal, empowered ? TimeIvory : TimeTeal);
            if (empowered)
                for (int i = 0; i < 6; i++) Spray(from + dir * range * i / 6f, Vector3.up, 4, 0.6f, 3, 0.12f, 0.3f, sparks);
        }

        // Convergence: the dials collapse into the target; a perfect pair adds a second ring and a heavier flash
        public void SealBurst(Vector3 at, bool perfect)
        {
            var c = new Vector3(at.x, 0.05f, at.z);
            Play(cyl, false, add, c, Quaternion.identity, perfect ? 0.5f : 0.35f, (b, u) =>
            {
                float r = Mathf.Lerp(perfect ? 2.6f : 1.6f, 0.3f, Ease(u)); b.go.transform.localScale = new Vector3(r, Mathf.Lerp(1f, 5f, u), r);
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, perfect ? 1.8f : 1f);
            }, 2, Ink, TimeTeal, TimeIvory);
            for (int k = 0; k < (perfect ? 2 : 1); k++)
            {
                float delay = k * 0.12f;
                Play(FxMeshes.Annulus(0.2f, 1f), true, alphaFloor, c, Quaternion.identity, 0.45f + delay, (b, u) =>
                {
                    float tt = Mathf.Clamp01((u * (0.45f + delay) - delay) / 0.45f); float r = Mathf.Lerp(0.4f, perfect ? 4f : 2.5f, Ease(tt));
                    b.go.transform.localScale = new Vector3(r, 1, r); b.mpb.SetFloat(PProgress, 1.1f * tt); b.mpb.SetFloat(PDissolve, tt <= 0 ? 1 : Mathf.Clamp01((tt - 0.4f) / 0.6f));
                }, 0, Ink, TimeTeal, TimeIvory);
            }
            Spray(c + Vector3.up * 1.5f, Vector3.up, perfect ? 40 : 16, 1.2f, 6, 0.16f, 0.5f, sparks);
            if (perfect) CameraShake.Shake(0.3f, 0.3f);
        }

        // Temporal Anchor: a small upright clock-hand marker at the recorded spot until the return or expiry
        public void Anchor(PlayerController p, Vector3 at, bool on)
        {
            if (anchorMarker == null)
            {
                anchorMarker = new GameObject("Anchor marker").transform; anchorMarker.SetParent(transform, false);
                Prim.Make(PrimitiveKind.Cylinder, anchorMarker, new Vector3(0, 0.02f, 0), new Vector3(1.1f, 0.02f, 1.1f), new Color(0.2f, 0.6f, 0.6f), 0.8f);
                Prim.Make(PrimitiveKind.Cube, anchorMarker, new Vector3(0, 0.6f, 0), new Vector3(0.08f, 1.2f, 0.08f), new Color(0.9f, 0.7f, 0.35f), 1.2f);
                Prim.Make(PrimitiveKind.Sphere, anchorMarker, new Vector3(0, 1.25f, 0), Vector3.one * 0.25f, new Color(0.4f, 1f, 0.95f), 2.5f);
            }
            anchorMarker.gameObject.SetActive(on);
            if (on) { anchorMarker.position = new Vector3(at.x, 0, at.z); Spray(at + Vector3.up, Vector3.up, 12, 0.7f, 3, 0.12f, 0.4f, motes); }
        }
        public void AnchorReturn(Vector3 from, Vector3 to)
        {
            var d = Sim.Flat(to - from);
            if (d.magnitude > 0.2f)
                Play(FxMeshes.Quad(-0.3f, 0.3f, 0, d.magnitude), true, add, from + Vector3.up, Quaternion.LookRotation(d), 0.25f, (b, u) => { b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 1.3f); }, 2, Ink, TimeTeal, TimeIvory);
            for (int i = 0; i < 8; i++) motes.Emit(from + Random.insideUnitSphere * 0.5f + Vector3.up, Random.insideUnitSphere, 0.25f, 0.5f);
            Spray(to + Vector3.up, Vector3.up, 18, 1f, 4, 0.14f, 0.4f, sparks);
        }
        public void TimeShield(PlayerController p, bool on)
        {
            if (shieldRing == null)
            {
                shieldRing = new GameObject("Time shield").transform; shieldRing.SetParent(transform, false);
                for (int i = 0; i < 3; i++) Prim.Make(PrimitiveKind.Cylinder, shieldRing, new Vector3(0, 0.4f + 0.5f * i, 0), new Vector3(1.3f, 0.015f, 1.3f), new Color(0.4f, 1f, 0.95f), 1.6f);
            }
            shieldOwner = on ? p : null; shieldRing.gameObject.SetActive(on);
        }
        void LateUpdate()
        {
            if (shieldRing && shieldOwner) { shieldRing.position = shieldOwner.transform.position; shieldRing.rotation = Quaternion.Euler(0, Time.time * 120, 0); }
        }

        // Clockstep: fractured clock-face afterimages along the dash
        public void ClockstepTrail(PlayerController p)
        {
            var at = p.transform.position;
            Play(FxMeshes.Annulus(0.75f, 0.85f, 24), true, add, at + Vector3.up * 1.1f, Quaternion.LookRotation(Vector3.up, p.transform.forward), 0.35f, (b, u) =>
            {
                b.mpb.SetFloat(PDissolve, u); b.mpb.SetFloat(PIntensity, 0.9f);
            }, 2, Ink, TimeTeal, TimeIvory);
            Spray(at + Vector3.up, -p.transform.forward, 10, 0.5f, 3, 0.12f, 0.35f, motes);
        }
    }
}
