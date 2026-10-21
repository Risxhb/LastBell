using UnityEngine;

namespace LastBell.Areas.Area3
{
    // Per-area art direction in one asset, after Windblown's LightingDecoSettings (research/windblown-artstyle.html §2):
    // sun, CustomLit globals, ambient, fill, fog and outline. SendToGPU pushes everything as _LB_* shader globals and to
    // the sun Light and RenderSettings, so one inspector tunes the whole look. Defaults are the Verdant Chimes values
    // proven in prototype/arena-3d/areas/area-3/verdant-arena.js (LOOK).
    [CreateAssetMenu(menuName = "Last Bell/Area 3/Look Settings", fileName = "Area3_VerdantChimes_Look")]
    public sealed class LookSettings : ScriptableObject
    {
        [Header("Sun")]
        public Color sunColor = new Color(1f, 0.91f, 0.78f);
        public float sunIntensity = 1.0f;
        public Vector3 sunEuler = new Vector3(44, 129, 0);   // low enough for real shadows from the belfries and rim, not so low they sweep the floor

        [Header("CustomLit")]
        [Range(0, 1)] public float diffuseRemapMin = 0.38f;
        [Range(0, 1)] public float diffuseRemapMax = 0.66f;
        [Tooltip("Shadows take this colour instead of going dark.")]
        public Color unlitTint = new Color(0.25f, 0.43f, 0.46f);
        public float brushNoiseScale = 1f;
        [Range(0, 0.4f)] public float brushNoiseStr = 0.1f;
        public Color fakeTopAmbient = new Color(0.373f, 0.62f, 0.525f);
        [Range(-1, 1)] public float topStart = 0.2f;
        [Range(-1, 1)] public float topEnd = 0.95f;
        public float topStrength = 0.32f;
        public Color backlightTint = new Color(0.75f, 0.96f, 0.91f);
        public float backlightStr = 0.42f;
        public float backlightTightness = 3.2f;

        [Header("Ambient and fill")]
        public Color ambientSky = new Color(0.25f, 0.36f, 0.37f);
        public Color ambientGround = new Color(0.07f, 0.10f, 0.10f);
        public Color fillColor = new Color(0.19f, 0.42f, 0.41f);
        public Vector3 fillDirection = new Vector3(0.3f, 0.45f, -0.84f);

        [Header("Fog")]
        public Color fogColor = new Color(0.07f, 0.235f, 0.25f);
        public float fogStart = 32, fogEnd = 140;

        [Header("Outline")]
        public Color outlineColor = new Color(0.047f, 0.14f, 0.133f);
        [Range(0, 1)] public float outlineOpacity = 0.75f;
        public float outlineThickness = 1.5f;
        public float outlineFadeStart = 40, outlineFadeEnd = 110;

        static readonly int RemapMin = Shader.PropertyToID("_LB_DiffuseRemapMin"), RemapMax = Shader.PropertyToID("_LB_DiffuseRemapMax"),
            UnlitTint = Shader.PropertyToID("_LB_DiffuseUnlitTint"), BrushScale = Shader.PropertyToID("_LB_BrushNoiseScale"), BrushStr = Shader.PropertyToID("_LB_BrushNoiseStr"),
            TopAmb = Shader.PropertyToID("_LB_FakeTopAmbient"), TopStart = Shader.PropertyToID("_LB_FakeTopAmbientStart"), TopEnd = Shader.PropertyToID("_LB_FakeTopAmbientEnd"),
            BackTint = Shader.PropertyToID("_LB_BackLightTint"), BackStr = Shader.PropertyToID("_LB_BacklightStr"), BackTight = Shader.PropertyToID("_LB_BacklightTightness"),
            AmbSky = Shader.PropertyToID("_LB_AmbientSky"), AmbGround = Shader.PropertyToID("_LB_AmbientGround"), FillCol = Shader.PropertyToID("_LB_FillColor"), FillDir = Shader.PropertyToID("_LB_FillDirection"),
            OutCol = Shader.PropertyToID("_LB_OutlineColor"), OutOp = Shader.PropertyToID("_LB_OutlineOpacity"), OutThick = Shader.PropertyToID("_LB_OutlineThickness"),
            OutFadeA = Shader.PropertyToID("_LB_OutlineFadeStart"), OutFadeB = Shader.PropertyToID("_LB_OutlineFadeEnd");

        public void SendToGPU(Light sun)
        {
            Shader.SetGlobalFloat(RemapMin, diffuseRemapMin); Shader.SetGlobalFloat(RemapMax, diffuseRemapMax);
            Shader.SetGlobalColor(UnlitTint, unlitTint.linear); Shader.SetGlobalFloat(BrushScale, brushNoiseScale); Shader.SetGlobalFloat(BrushStr, brushNoiseStr);
            Shader.SetGlobalColor(TopAmb, fakeTopAmbient.linear * topStrength); Shader.SetGlobalFloat(TopStart, topStart); Shader.SetGlobalFloat(TopEnd, topEnd);
            Shader.SetGlobalColor(BackTint, backlightTint.linear); Shader.SetGlobalFloat(BackStr, backlightStr); Shader.SetGlobalFloat(BackTight, backlightTightness);
            Shader.SetGlobalColor(AmbSky, ambientSky.linear); Shader.SetGlobalColor(AmbGround, ambientGround.linear);
            Shader.SetGlobalColor(FillCol, fillColor.linear); Shader.SetGlobalVector(FillDir, fillDirection.normalized);
            Shader.SetGlobalColor(OutCol, outlineColor.linear); Shader.SetGlobalFloat(OutOp, outlineOpacity); Shader.SetGlobalFloat(OutThick, outlineThickness);
            Shader.SetGlobalFloat(OutFadeA, outlineFadeStart); Shader.SetGlobalFloat(OutFadeB, outlineFadeEnd);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
            if (sun) { sun.color = sunColor; sun.intensity = sunIntensity; sun.transform.rotation = Quaternion.Euler(sunEuler); }
        }
    }
}
