using UnityEngine;

namespace LastBell.Run
{
    // Shared dressing for the P1-P3 stand-ins (proposal, ART-PLAN-v2.md batch 4; tools/bellkeeper-ultimate/models/p1_kit.py).
    // The Escapement Relay, the Broken Hour and the Open Hour all draw four hour plates at 7 m and a hand over the boss; these
    // put the brass models over them. Presentation only: nothing here changes what counts as on a plate.
    public static class UltArt
    {
        // an hour plate: the brass model on top; the stand-in disc stays as a thin rim round it, so its lit / dim material
        // (which plate is live) still reads
        public static void DressPlate(Transform disc)
        {
            if (!disc) return;
            var p = disc.position;
            var m = GearFloor.Model("CR_Plate", disc.parent, new Vector3(p.x, 0, p.z), Quaternion.identity);
            if (!m) return;
            var s = disc.localScale; disc.localScale = new Vector3(3.75f, s.y, 3.75f);   // r 1.875: a 0.27 m rim outside the plate's 1.6
        }

        // the hand: the brass model at the stand-in bar's height, pointing along the pivot's +Z (the model points along its
        // Blender +X, which lands on Unity -X); the stand-in bar and arrowhead above the floor are hidden, the floor line stays
        public static void DressHand(Transform pivot)
        {
            if (!pivot) return;
            var m = GearFloor.Model("CR_Hand", pivot, pivot.position, pivot.rotation);
            if (!m) return;
            m.localPosition = Vector3.up * 4.2f; m.localRotation = Quaternion.FromToRotation(Vector3.left, Vector3.forward);
            foreach (Transform c in pivot) if (c != m && c.localPosition.y > 1f) c.gameObject.SetActive(false);
        }

        // the hand's tick: a clockwork snap to its new hour that overshoots a little and settles (a spring, damping 0.5),
        // instead of a flat turn; within 0.3 s it rests on the hour, so the plate it shows never lags the rule
        public static void TickHand(Transform pivot, ref float shown, ref float vel, float want, float dt)
        {
            if (!pivot) return;
            float d = Mathf.DeltaAngle(shown, want);
            if (Mathf.Abs(d) > 135f) { shown = want; vel = 0; }   // a jump (a rewind across the dial): no spin the long way round
            else { vel += (d * 900f - vel * 30f) * dt; shown += vel * dt; }
            pivot.rotation = Quaternion.Euler(0, shown, 0);
        }

        // the lit plate's rim breathes while it waits for its beat (its own emission, brighter and dimmer)
        static MaterialPropertyBlock rimGlow;
        public static void BreatheRim(Renderer rim, Material lit, float t)
        {
            if (!rim || !lit || !lit.HasProperty("_EmissionColor")) return;
            if (rimGlow == null) rimGlow = new MaterialPropertyBlock();
            rimGlow.SetColor("_EmissionColor", lit.GetColor("_EmissionColor") * (0.75f + 0.45f * (0.5f + 0.5f * Mathf.Sin(t * 6f))));
            rim.SetPropertyBlock(rimGlow);
        }
        public static void StillRim(Renderer rim) { if (rim) rim.SetPropertyBlock(null); }
    }
}
