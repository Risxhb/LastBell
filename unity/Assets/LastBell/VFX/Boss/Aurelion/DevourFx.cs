using UnityEngine;

namespace LastBell.VFX
{
    // Aurelion v4 A17 Devour (abilities-v4/DEVOUR.md, accepted 2 Nov). Presentation only: while the pull runs the encounter
    // calls DevourDust every few ticks, and gold dust streams in from the rim to his chest along 16 spokes, at the pull's own
    // speed, so the floor shows how hard it drags. Gold, not coral: the pull never hurts.
    public sealed partial class BellkeeperFx
    {
        const int DevourSpokes = 16;
        const float DevourChest = 1.7f;
        FxParticles devourDust;

        // made with the other particle systems in Init, so the first Devour creates nothing mid-fight; no drag, so the dust
        // keeps the pull speed
        void InitDevour()
        {
            var m = new Material(addMat); m.SetFloat(PMode, 1); m.SetColor(PLow, Brass); m.SetColor(PHigh, Gold); m.SetFloat(PDissolve, 0);
            devourDust = FxParticles.Create(transform, "Devour Dust", m, 1200, 0f, 0f, 0.08f);
        }

        // one wave: a mote on every spoke at a random distance out, flying inward at `speed` m/s and rising to his chest
        public void DevourDust(Vector3 centre, float speed, float rim, float body)
        {
            if (devourDust == null) InitDevour();
            float turn = UnityEngine.Random.Range(0f, 360f / DevourSpokes);
            for (int s = 0; s < DevourSpokes; s++)
            {
                var dir = Quaternion.Euler(0, turn + s * 360f / DevourSpokes, 0) * Vector3.forward;
                float r = UnityEngine.Random.Range(body + 2f, rim), life = (r - body) / Mathf.Max(0.5f, speed);
                float y = UnityEngine.Random.Range(0.1f, 0.6f);
                var vel = -dir * speed + Vector3.up * (DevourChest - y) / life;
                devourDust.Emit(new Vector3(centre.x, y, centre.z) + dir * r, vel, UnityEngine.Random.Range(0.08f, 0.16f), life);
            }
        }
    }
}
