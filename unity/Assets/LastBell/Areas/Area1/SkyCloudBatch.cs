using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LastBell.Areas.Area1
{
    // All of Area 1's SkyCloud puffs as one mesh and one draw call (perf report 2026-10-28: ~840 puff renderers in 64
    // materials cost several hundred draw calls a frame). Each puff keeps its own quad; its centre, size, atlas cell,
    // colours and opacity ride on the vertices (SkyCloud's SKYCLOUD_BATCHED variant), and the drift, breathing and
    // opacity pulse SkywardSpireArena used to apply per transform and per material run in the shader from the same
    // numbers. Unity sorts transparent renderers back to front by their centres; Sort() does the same for the quads
    // before each camera renders, so overlapping puffs blend in the same order as before. The merged draw goes at
    // queue Transparent-1 (2999): before every other transparent, which in this arena are all nearer the camera.
    public sealed class SkyCloudBatch
    {
        const float BreatheAmount = 0.03f;   // SkywardSpireArena: ±3% breathing

        readonly HashSet<Transform> covered = new HashSet<Transform>();
        readonly List<Renderer> hidden = new List<Renderer>();
        GameObject go; Mesh mesh; Material material;
        Vector3[] rest; Vector3[] drift; bool[] drifts;   // per puff: rest centre; start angle, radius, speed
        int[] triStart, triCount; int[] srcTris;          // per puff: its triangle indices inside srcTris
        int[] order, indices; float[] keys;
        public float StartTime { get => startTime; set => startTime = value; }   // SkyCloudBatchCheck lines the drift up with a hand-posed frame
        float startTime;

        public static int LastPuffCount { get; private set; }
        public bool Covers(Transform t) => covered.Contains(t);

        public static SkyCloudBatch Build(List<(Renderer r, float opacityPhase)> puffs, Dictionary<Transform, (float angle, float radius, float speed)> driftOf,
            Dictionary<Transform, float> breathePhase, Scene scene, float now)
        {
            var first = puffs[0].r.sharedMaterial;
            var tex = first.GetTexture("_MainTex"); var sun = first.GetVector("_SunDir");
            foreach (var (r, _) in puffs)                 // one draw needs one atlas and one sun; anything else stays as it is
            {
                var m = r.sharedMaterial;
                if (m.shader != first.shader || m.GetTexture("_MainTex") != tex || m.GetVector("_SunDir") != sun || !(r is MeshRenderer) || !r.TryGetComponent(out MeshFilter f) || !f.sharedMesh) return null;
            }
            var b = new SkyCloudBatch { startTime = now };
            int n = puffs.Count;
            b.rest = new Vector3[n]; b.drift = new Vector3[n]; b.drifts = new bool[n]; b.triStart = new int[n]; b.triCount = new int[n];
            var pos = new List<Vector3>(); var uv0 = new List<Vector2>(); var tris = new List<int>();
            var ch = new List<Vector4>[7]; for (int c = 0; c < 7; c++) ch[c] = new List<Vector4>();
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Vector4 Col(Material m, string p) { var c = m.GetColor(p); return linear ? (Vector4)c.linear : (Vector4)c; }
            for (int i = 0; i < n; i++)
            {
                var (r, opPhase) = puffs[i]; var t = r.transform; var m = r.sharedMaterial; var src = r.GetComponent<MeshFilter>().sharedMesh;
                var centre = t.position; var w2o = t.localToWorldMatrix;
                var size = new Vector2(((Vector3)w2o.GetColumn(0)).magnitude, ((Vector3)w2o.GetColumn(1)).magnitude);   // what SkyCloud reads from the object matrix
                bool hasDrift = driftOf.TryGetValue(t, out var d); bool breathes = breathePhase.TryGetValue(t, out var bp);
                b.rest[i] = centre; b.drift[i] = new Vector3(d.angle, d.radius, d.speed); b.drifts[i] = hasDrift;
                var centreCh = new Vector4(centre.x, centre.y, centre.z, breathes ? bp : 0);
                var sizeCh = new Vector4(size.x, size.y, m.GetFloat("_Cell"), m.GetFloat("_FlipX"));
                var driftCh = new Vector4(d.angle, d.radius, d.speed, hasDrift ? 1 : 0);
                var lookCh = new Vector4(m.GetFloat("_Opacity"), opPhase, m.GetFloat("_SoftDistance"), breathes ? BreatheAmount : 0);
                Vector4 lit = Col(m, "_Lit"), shade = Col(m, "_Shade"), rim = Col(m, "_Rim");
                int v0 = pos.Count; var sv = src.vertices; var su = src.uv;
                for (int k = 0; k < sv.Length; k++)
                {
                    pos.Add(sv[k]); uv0.Add(su[k]);
                    ch[0].Add(centreCh); ch[1].Add(sizeCh); ch[2].Add(driftCh); ch[3].Add(lookCh); ch[4].Add(lit); ch[5].Add(shade); ch[6].Add(rim);
                }
                b.triStart[i] = tris.Count;
                foreach (var k in src.triangles) tris.Add(v0 + k);
                b.triCount[i] = tris.Count - b.triStart[i];
                b.covered.Add(t); b.hidden.Add(r);
            }
            b.srcTris = tris.ToArray(); b.indices = new int[b.srcTris.Length]; b.order = new int[n]; b.keys = new float[n];
            b.mesh = new Mesh { name = "SkyCloudBatch", indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            b.mesh.MarkDynamic();
            b.mesh.SetVertices(pos); b.mesh.SetUVs(0, uv0);
            for (int c = 0; c < 7; c++) b.mesh.SetUVs(c + 1, ch[c]);
            b.mesh.SetIndices(b.srcTris, MeshTopology.Triangles, 0, false);
            b.mesh.bounds = Reach(b, puffs);
            b.material = new Material(first) { name = "SkyCloudBatch", renderQueue = (int)RenderQueue.Transparent - 1, enableInstancing = false };
            b.material.EnableKeyword("SKYCLOUD_BATCHED");
            b.go = new GameObject("SkyCloudBatch");
            SceneManager.MoveGameObjectToScene(b.go, scene);   // unloads with the arena
            b.go.AddComponent<MeshFilter>().sharedMesh = b.mesh;
            var mr = b.go.AddComponent<MeshRenderer>(); mr.sharedMaterial = b.material;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            foreach (var r in b.hidden) r.enabled = false;
            LastPuffCount = n;
            Debug.Log($"Skyward Spire clouds: {n} puffs drawn as one mesh");
            b.Animate(now);
            return b;
        }

        // Everywhere a puff can be: a drifting puff circles its radius, a breathing one grows by 3%.
        static Bounds Reach(SkyCloudBatch b, List<(Renderer r, float)> puffs)
        {
            var box = new Bounds(b.rest[0], Vector3.zero);
            for (int i = 0; i < b.rest.Length; i++)
            {
                var s = puffs[i].r.transform.lossyScale; float half = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)) * 0.5f * (1 + BreatheAmount) + 1;
                if (b.drifts[i]) { float rad = b.drift[i].y + half; box.Encapsulate(new Bounds(new Vector3(0, b.rest[i].y, 0), new Vector3(rad * 2, half * 2, rad * 2))); }
                else box.Encapsulate(new Bounds(b.rest[i], Vector3.one * half * 2));
            }
            return box;
        }

        public void Animate(float now)
        {
            if (!material) return;
            material.SetFloat("_CloudClock", now);
            material.SetFloat("_CloudDrift", now - startTime);
        }

        // Back to front by distance from the camera to each puff's (drifted) centre, as Unity sorts transparent renderers.
        public void Sort(Vector3 cam, float now)
        {
            if (!mesh) return;
            float dt = now - startTime;
            for (int i = 0; i < order.Length; i++)
            {
                var c = rest[i];
                if (drifts[i]) { float a = drift[i].x + drift[i].z * dt; c = new Vector3(Mathf.Cos(a) * drift[i].y, c.y, Mathf.Sin(a) * drift[i].y); }
                order[i] = i; keys[i] = -(c - cam).sqrMagnitude;
            }
            System.Array.Sort(keys, order);
            int w = 0;
            foreach (var i in order) { System.Array.Copy(srcTris, triStart[i], indices, w, triCount[i]); w += triCount[i]; }
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
        }

        public void Dispose()
        {
            foreach (var r in hidden) if (r) r.enabled = true;
            hidden.Clear(); covered.Clear();
            void Drop(Object o) { if (!o) return; if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
            Drop(go); Drop(mesh); Drop(material);
        }
    }
}
