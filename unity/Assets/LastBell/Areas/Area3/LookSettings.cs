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
        public Color fogColor = new Color(0.52f, 0.72f, 0.70f); // matches the aerial haze and horizon, so distance lightens instead of darkening
        public float fogStart = 60, fogEnd = 220;

        [Header("Cloud sea (towers sink into it)")]
        [Tooltip("Height where objects start sinking into the clouds (m).")] public float cloudTop = -6f;
        [Tooltip("Height where objects are fully swallowed (m).")] public float cloudBottom = -30f;
        [Tooltip("> 1: the fade starts gentle and thickens near the bottom.")] public float cloudPower = 1.6f;
        [Tooltip("Shared with the mist layers so wisps line up.")] public float cloudNoiseScale = 0.03f;
        [Tooltip("How far (m) the noise pushes the fade boundary up and down.")] public float cloudWisp = 6f;
        public Color cloudThin = new Color(0.22f, 0.48f, 0.50f), cloudThick = new Color(0.92f, 0.99f, 0.97f);
        [Tooltip("Combat floor and rim within this radius (above -1 m) are never fogged.")] public float floorKeepRadius = 17f;

        [Header("Aerial haze")]
        public Color hazeColor = new Color(0.52f, 0.72f, 0.70f);
        public float hazeStart = 35, hazeEnd = 120; [Range(0, 1)] public float hazeAmount = 0.62f;

        [Header("Wind and water")]
        [Tooltip("Direction in world XZ.")] public Vector2 windDirection = new Vector2(0.8f, 0.35f);
        [Tooltip("m/s; drives motes, petals, spray, foliage sway, cloud drift and waterfall bend.")] public float windStrength = 2.2f;
        public float gustFrequency = 0.6f;
        [Tooltip("Waterfall speed near the lip (m/s); waterfall glints ride at the same speed.")] public float flowSpeed = 6.5f;

        [Header("Outline")]
        public Color outlineColor = new Color(0.047f, 0.14f, 0.133f);
        [Range(0, 1)] public float outlineOpacity = 0.75f;
        public float outlineThickness = 1.5f;
        public float outlineFadeStart = 30, outlineFadeEnd = 75;

        static readonly int RemapMin = Shader.PropertyToID("_LB_DiffuseRemapMin"), RemapMax = Shader.PropertyToID("_LB_DiffuseRemapMax"),
            UnlitTint = Shader.PropertyToID("_LB_DiffuseUnlitTint"), BrushScale = Shader.PropertyToID("_LB_BrushNoiseScale"), BrushStr = Shader.PropertyToID("_LB_BrushNoiseStr"),
            TopAmb = Shader.PropertyToID("_LB_FakeTopAmbient"), TopStart = Shader.PropertyToID("_LB_FakeTopAmbientStart"), TopEnd = Shader.PropertyToID("_LB_FakeTopAmbientEnd"),
            BackTint = Shader.PropertyToID("_LB_BackLightTint"), BackStr = Shader.PropertyToID("_LB_BacklightStr"), BackTight = Shader.PropertyToID("_LB_BacklightTightness"),
            AmbSky = Shader.PropertyToID("_LB_AmbientSky"), AmbGround = Shader.PropertyToID("_LB_AmbientGround"), FillCol = Shader.PropertyToID("_LB_FillColor"), FillDir = Shader.PropertyToID("_LB_FillDirection"),
            OutCol = Shader.PropertyToID("_LB_OutlineColor"), OutOp = Shader.PropertyToID("_LB_OutlineOpacity"), OutThick = Shader.PropertyToID("_LB_OutlineThickness"),
            OutFadeA = Shader.PropertyToID("_LB_OutlineFadeStart"), OutFadeB = Shader.PropertyToID("_LB_OutlineFadeEnd"),
            CloudTop = Shader.PropertyToID("_LB_CloudTop"), CloudBottom = Shader.PropertyToID("_LB_CloudBottom"), CloudPower = Shader.PropertyToID("_LB_CloudPower"),
            CloudScale = Shader.PropertyToID("_LB_CloudNoiseScale"), CloudWisp = Shader.PropertyToID("_LB_CloudWisp"), CloudThin = Shader.PropertyToID("_LB_CloudThin"),
            CloudThick = Shader.PropertyToID("_LB_CloudThick"), FloorKeep = Shader.PropertyToID("_LB_FloorKeepRadius"), Haze = Shader.PropertyToID("_LB_HazeColor"),
            HazeStart = Shader.PropertyToID("_LB_HazeStart"), HazeEnd = Shader.PropertyToID("_LB_HazeEnd"), HazeAmt = Shader.PropertyToID("_LB_HazeAmount"),
            Wind = Shader.PropertyToID("_LB_Wind"), Flow = Shader.PropertyToID("_LB_FlowSpeed");

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
            Shader.SetGlobalFloat(CloudTop, cloudTop); Shader.SetGlobalFloat(CloudBottom, cloudBottom); Shader.SetGlobalFloat(CloudPower, cloudPower);
            Shader.SetGlobalFloat(CloudScale, cloudNoiseScale); Shader.SetGlobalFloat(CloudWisp, cloudWisp);
            Shader.SetGlobalColor(CloudThin, cloudThin.linear); Shader.SetGlobalColor(CloudThick, cloudThick.linear); Shader.SetGlobalFloat(FloorKeep, floorKeepRadius);
            var hz = hazeColor.linear; hz.a = 1; Shader.SetGlobalColor(Haze, hz); Shader.SetGlobalFloat(HazeStart, hazeStart); Shader.SetGlobalFloat(HazeEnd, hazeEnd); Shader.SetGlobalFloat(HazeAmt, hazeAmount);
            var wd = windDirection.sqrMagnitude > 0 ? windDirection.normalized : Vector2.right;
            Shader.SetGlobalVector(Wind, new Vector4(wd.x, wd.y, windStrength, gustFrequency)); Shader.SetGlobalFloat(Flow, flowSpeed);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart; RenderSettings.fogEndDistance = fogEnd;
            if (sun) { sun.color = sunColor; sun.intensity = sunIntensity; sun.transform.rotation = Quaternion.Euler(sunEuler); }
        }
    }
}
