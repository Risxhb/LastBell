using System.Collections.Generic;
using LastBell.VFX;
using UnityEngine;

namespace LastBell.Run
{
    // Presentation only (telegraph audit A12 / F06): Total Eclipse's Umbra hits anyone outside every light well, so the floor
    // outside the wells gets a subdued coral footprint with the wells cut out of it. Each well's edge is then a true
    // hostile boundary (hole radius = the InWell test's), drawn as a continuous contour; the wells themselves stay empty.
    // It holds steady (no countdown: the Umbra is a state, not a timed hit) and draws under every timed warning.
    // Kept out of TotalEclipse.cs (owned by the solo-reference work); the encounter wraps Eclipse.Run() in During().
    public sealed class TotalEclipseFx : MonoBehaviour
    {
        static readonly Color Fill = new Color(0.62f, 0.22f, 0.17f), Border = new Color(1.35f, 0.42f, 0.32f);
        TotalEclipse eclipse; BellkeeperFx.Telegraph umbra;
        readonly Vector4[] holes = new Vector4[6];

        public static IEnumerable<int> During(CombatWorld world, TotalEclipse eclipse, IEnumerable<int> run)
        {
            var go = new GameObject("Total Eclipse umbra fx"); go.transform.SetParent(world.Root, false);
            var fx = go.AddComponent<TotalEclipseFx>(); fx.eclipse = eclipse;
            fx.umbra = world.BossFx.Circle(AurelionAbility.Rim);
            fx.umbra.Tint(Fill, Border); fx.umbra.Motif(2); fx.umbra.Progress(0); fx.umbra.Order = -1;
            fx.umbra.Place(Vector3.zero, 0); fx.umbra.Holes(fx.holes, 0);
            try { foreach (var w in run) yield return w; }
            finally { fx.umbra.Expire(); Destroy(go); }
        }

        void LateUpdate()
        {
            if (eclipse == null || umbra == null) return;
            var wells = eclipse.Wells;
            int n = Mathf.Min(wells.Count, holes.Length);
            float r = eclipse.WellRadius - 0.1f;   // TotalEclipse.InWell: inside within WellRadius - 0.1
            for (int k = 0; k < n; k++) holes[k] = new Vector4(wells[k].x, wells[k].z, r, 0);
            umbra.Holes(holes, eclipse.Active ? n : 0);
        }
    }
}
