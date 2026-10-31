using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LastBell.Run
{
    // Gear Chain presentation (stand-in geometry, presentation only): one per chain. A segmented chain between the two ends,
    // brass with a soft ivory pulse while its length is inside the band, crisp coral with the seconds left on its middle link
    // while it's outside; thin dotted ivory guide rings at the band's min and max around each partner (or around the post);
    // a brass gear-tooth post rising from the rim for a post chain. Removes itself the frame its hazard leaves
    // HazardSystem.Live (resolve, boss death, ClearTransient) or the chain ends (down, dash).
    public sealed class GearChainFx : MonoBehaviour
    {
        public static int Count { get; private set; }   // live chain presentations, for tests

        static readonly Color Brass = new Color(0.86f, 0.63f, 0.30f), Ivory = new Color(1f, 0.95f, 0.82f),
            Coral = new Color(1f, 0.42f, 0.32f), Amber = new Color(1f, 0.70f, 0.30f);
        const float Lift = 1.0f, GuideY = 0.06f;
        const int RingPts = 72;

        CombatWorld w; GearChain ability; GearChain.Link link;
        readonly List<Material> mats = new List<Material>();
        LineRenderer chain; LineRenderer[] guides; TextMesh count; Transform post; float postRise;

        public static void Show(CombatWorld world, GearChain ability, GearChain.Link link)
        {
            var go = new GameObject("Gear Chain");
            go.transform.SetParent(world.Root, false);
            var fx = go.AddComponent<GearChainFx>(); fx.w = world; fx.ability = ability; fx.link = link; fx.Build();
        }

        void OnEnable() { Count++; }
        void OnDisable() { Count--; }

        void Build()
        {
            chain = Line("Chain", LinkTex(), 0.11f, 2.5f);
            chain.textureMode = LineTextureMode.Tile; chain.positionCount = 2;
            guides = new LineRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                var g = Line("Guide", DotTex(), 0.05f, 2f);
                g.textureMode = LineTextureMode.Tile; g.loop = true; g.positionCount = RingPts;
                g.startColor = g.endColor = new Color(Ivory.r, Ivory.g, Ivory.b, 0.55f);
                guides[i] = g;
            }
            var t = new GameObject("Countdown"); t.transform.SetParent(transform, false);
            count = t.AddComponent<TextMesh>();
            count.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.GetComponent<MeshRenderer>().sharedMaterial = count.font.material;
            count.anchor = TextAnchor.MiddleCenter; count.characterSize = 0.12f; count.fontSize = 48; count.fontStyle = FontStyle.Bold;
            Update();
        }

        void Update()
        {
            if (link.Ended != null || !w.Hazards.Live.Contains(link.Hazard) || !link.A) { Destroy(gameObject); return; }
            var a = Sim.Flat(link.A.transform.position); var b = Sim.Flat(link.Other);
            bool ok = ability.InBand(link);
            float left = (link.Hazard.ResolveAt - link.Hazard.Age) * Sim.Dt;

            // chain: brass with an ivory pulse inside the band, crisp coral and a little wider outside
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            var c = ok ? Color.Lerp(Brass, Ivory, 0.35f * pulse) : Coral;
            chain.startColor = chain.endColor = c;
            chain.widthMultiplier = ok ? 0.11f : 0.15f;
            chain.SetPosition(0, a + Vector3.up * Lift);
            chain.SetPosition(1, b + Vector3.up * (link.ToPost ? 1.3f : Lift));

            count.gameObject.SetActive(!ok);
            if (!ok)
            {
                count.text = left.ToString("0.0");
                count.color = left < 1f ? Amber : Coral;
                var ct = count.transform; ct.position = (a + b) * 0.5f + Vector3.up * (Lift + 0.45f);
                var cam = Camera.main; if (cam) ct.rotation = Quaternion.LookRotation(ct.position - cam.transform.position);
            }

            // guide rings around each partner, or around the post for a post chain
            Ring(guides[0], link.ToPost ? link.Post : a, ability.Min); Ring(guides[1], link.ToPost ? link.Post : a, ability.Max);
            bool two = !link.ToPost;
            guides[2].enabled = guides[3].enabled = two;
            if (two) { Ring(guides[2], b, ability.Min); Ring(guides[3], b, ability.Max); }

            // gear-tooth post rising out of the rim wall
            if (link.ToPost)
            {
                if (!post) { post = Post(); postRise = 0; }
                postRise = Mathf.MoveTowards(postRise, 1, Time.deltaTime / 0.3f);
                post.position = new Vector3(link.Post.x, -1.6f * (1 - postRise), link.Post.z);
            }
            else if (post) { Destroy(post.gameObject); post = null; }
        }

        static void Ring(LineRenderer g, Vector3 at, float r)
        {
            for (int i = 0; i < RingPts; i++)
            {
                float t = i * Mathf.PI * 2 / RingPts;
                g.SetPosition(i, new Vector3(at.x + Mathf.Cos(t) * r, GuideY, at.z + Mathf.Sin(t) * r));
            }
        }

        // tiles = texture repeats per metre (Tile mode)
        LineRenderer Line(string name, Texture2D tex, float width, float tiles)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            l.useWorldSpace = true; l.widthMultiplier = width; l.numCapVertices = 0;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; l.receiveShadows = false;
            var m = new Material(Mat()) { mainTexture = tex, mainTextureScale = new Vector2(tiles, 1) };
            l.sharedMaterial = m; mats.Add(m);
            return l;
        }

        Transform Post()
        {
            var root = new GameObject("Gear Post").transform; root.SetParent(transform, false);
            var body = Part(PrimitiveType.Cylinder, root, new Vector3(0, 0.8f, 0), new Vector3(0.55f, 0.8f, 0.55f), Brass * 0.8f);
            for (int i = 0; i < 8; i++)
            {
                var tooth = Part(PrimitiveType.Cube, root, Quaternion.Euler(0, i * 45, 0) * new Vector3(0, 1.45f, 0.32f), new Vector3(0.16f, 0.22f, 0.18f), Brass);
                tooth.localRotation = Quaternion.Euler(0, i * 45, 0);
            }
            Part(PrimitiveType.Cylinder, root, new Vector3(0, 1.3f, 0), new Vector3(0.75f, 0.05f, 0.75f), Ivory * 0.9f);
            return root;
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = Mat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock(); mpb.SetColor("_Color", c); r.SetPropertyBlock(mpb);
            return go.transform;
        }

        void OnDestroy()
        {
            foreach (var m in mats) if (m) Destroy(m);
        }

        // ---------------- shared stand-in assets (built once) ----------------
        static Material mat; static Texture2D linkTex, dotTex;
        static Material Mat() { if (!mat) mat = new Material(Shader.Find("Sprites/Default")); return mat; }
        // chain links: solid plate, dark gap, solid plate (tiled along the chain)
        static Texture2D LinkTex()
        {
            if (linkTex) return linkTex;
            linkTex = new Texture2D(8, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var a = new[] { 1f, 1f, 1f, 0.35f, 1f, 1f, 1f, 0f };
            for (int i = 0; i < 8; i++) linkTex.SetPixel(i, 0, new Color(a[i] > 0.5f ? 1 : 0.55f, a[i] > 0.5f ? 1 : 0.55f, a[i] > 0.5f ? 1 : 0.55f, a[i]));
            linkTex.Apply(); return linkTex;
        }
        static Texture2D DotTex()
        {
            if (dotTex) return dotTex;
            dotTex = new Texture2D(4, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            dotTex.SetPixels(new[] { Color.white, Color.white, Color.clear, Color.clear }); dotTex.Apply(); return dotTex;
        }
    }
}
