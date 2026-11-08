using UnityEngine;

namespace LastBell.VFX
{
    // The Ultimate's own effects (proposal, ART-PLAN-v2.md: animation and VFX polish). Presentation only: the mechanics call
    // these on their own ticks and nothing here decides timing or hits. Same palette and particle pools as the base fight.
    public sealed partial class BellkeeperFx
    {
        // a floor stone breaking loose: a puff of grey dust round it and a few brass sparks from the crack
        public void UltStoneDrop(Vector3 pos)
        {
            Dust(pos, 0.5f, 3, 0.5f);
            Sparks(pos + Vector3.up * 0.1f, Vector3.up, 2, 1f, 3, 0.1f, 0.35f);
        }

        // the clapper striking through its lane: sparks thrown off both sides along the whole lane
        public void UltClapperStrike(Vector3 centre, Vector3 dir, float length)
        {
            dir = new Vector3(dir.x, 0, dir.z).normalized; var side = Vector3.Cross(Vector3.up, dir);
            for (int i = -5; i <= 5; i++)
            {
                var p = centre + dir * (length * i / 10f) + Vector3.up * 0.5f;
                Sparks(p, side + Vector3.up * 0.4f, 3, 0.5f, 6, 0.14f, 0.4f);
                Sparks(p, -side + Vector3.up * 0.4f, 3, 0.5f, 6, 0.14f, 0.4f);
            }
        }

        // the great bell shattering at XIII: a ring of gold sparks off its whole wall and a slow lift of teal motes
        public void UltBellShatter(float radius)
        {
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2 / 24; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                Sparks(d * radius + Vector3.up * Random.Range(2f, 8f), d + Vector3.up * 0.3f, 5, 0.6f, 9, 0.2f, 0.8f);
            }
            for (int i = 0; i < 40; i++)
            {
                var p = Random.insideUnitSphere * radius; p.y = Random.Range(0.3f, 3f);
                motes.Emit(p, Vector3.up * Random.Range(0.6f, 1.6f), Random.Range(0.12f, 0.25f), Random.Range(1.2f, 2f));
            }
            CameraShake.Shake(0.4f, 0.6f);
        }

        // the Echo Bellkeeper moving after a toll: it comes apart in teal motes where it was and gathers where it goes
        public void UltEchoShift(Vector3 from, Vector3 to)
        {
            for (int i = 0; i < 18; i++)
            {
                var o = Random.insideUnitSphere * 0.9f; o.y = Mathf.Abs(o.y) * 3f;
                motes.Emit(from + o, Vector3.up * Random.Range(0.5f, 1.4f) + o * 0.5f, Random.Range(0.12f, 0.22f), Random.Range(0.5f, 0.9f));
                motes.Emit(to + o * 1.6f, -o * 0.9f, Random.Range(0.12f, 0.22f), Random.Range(0.4f, 0.6f));
            }
        }
    }
}
