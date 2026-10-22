// Area 3 shared atmosphere: one clock, one wind, one cloud sea. Every Area 3 shader includes this, so towers, water,
// mist, cloud cards and particles sink into the same clouds and move on the same wind (Windblown teardown §5–§6).
// All values are globals pushed by LookSettings.SendToGPU; _LB_Time is driven by VerdantChimesArena (deterministic for
// the batch motion strip, Time.time in Play mode).
#ifndef LB_AREA3_CLOUD_INCLUDED
#define LB_AREA3_CLOUD_INCLUDED

float _LB_Time;
float4 _LB_Wind;            // xy = direction in world XZ (normalised), z = strength (m/s), w = gust frequency
float _LB_FlowSpeed;        // waterfall speed near the lip, m/s
float _LB_CloudTop, _LB_CloudBottom, _LB_CloudPower, _LB_CloudNoiseScale, _LB_CloudWisp;
float4 _LB_CloudThin, _LB_CloudThick, _LB_HazeColor;
float _LB_HazeStart, _LB_HazeEnd, _LB_HazeAmount, _LB_FloorKeepRadius;
float4 _LB_Impacts[24];      // where waterfalls land: xz, impact height y (in z), sheet width (in w)
float _LB_ImpactCount;

// 0..1 closeness to the nearest waterfall impact (for foam rings, brighter mist and rising motes)
float lbcImpact(float2 xz, float radius)
{
    float best = 0;
    for (int k = 0; k < 24; k++) { if (k >= (int)_LB_ImpactCount) break; float d = distance(xz, _LB_Impacts[k].xy); best = max(best, 1 - saturate(d / (radius + _LB_Impacts[k].w * 0.5))); }
    return best;
}

float lbcHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float lbcNoise(float2 p)
{
    float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(lbcHash(i), lbcHash(i + float2(1, 0)), f.x), lerp(lbcHash(i + float2(0, 1)), lbcHash(i + float2(1, 1)), f.x), f.y);
}
float lbcFbm(float2 p) { float a = 0.5, s = 0; for (int k = 0; k < 5; k++) { s += lbcNoise(p) * a; p *= 2.03; a *= 0.5; } return s; }

// cloud drift follows the wind
float2 lbcDrift(float speedScale) { return _LB_Wind.xy * _LB_Time * 0.04 * speedScale * max(0.5, _LB_Wind.z); }

// cloud density at a point of the sea, 0..1 (the Mist shader and the height fog share it so wisps line up)
float lbcDensity(float2 xz, float layerSeed)
{
    float2 p = xz * _LB_CloudNoiseScale;
    float c = lbcFbm(p + lbcDrift(1) + layerSeed * 3.7) * 0.6 + lbcFbm(p * 2.1 - lbcDrift(1.7) + layerSeed * 1.3) * 0.4;
    return smoothstep(0.36, 0.78, c);
}

// gust 0..1 travelling along the wind direction
float lbcGust(float3 posWS) { return sin(_LB_Time * _LB_Wind.w + dot(posWS.xz, _LB_Wind.xy) * 0.06) * 0.5 + 0.5; }

// how far a point is swallowed by the cloud sea, 0..1: a power-shaped height ramp whose boundary is broken up by the
// same noise as the mist layers (±_LB_CloudWisp metres), so tower bases melt into wisps that drift over time
float lbcCloudFactor(float3 posWS)
{
    float n = lbcDensity(posWS.xz, 0);
    float y = posWS.y + (n - 0.5) * 2 * _LB_CloudWisp + sin(_LB_Time * 0.9 + dot(posWS.xz, float2(0.11, 0.07))) * 1.2;
    float c = saturate((_LB_CloudTop - y) / max(0.01, _LB_CloudTop - _LB_CloudBottom));
    return pow(c, _LB_CloudPower);
}

// apply cloud immersion and aerial haze; the combat floor and rim are excluded (floor readability is a settled rule)
float3 lbcApply(float3 col, float3 posWS)
{
    float keep = (posWS.y > -1.0 && length(posWS.xz) < _LB_FloorKeepRadius) ? 0 : 1;
    float c = lbcCloudFactor(posWS) * keep;
    float n = lbcDensity(posWS.xz, 0);
    float3 cloud = lerp(_LB_CloudThin.rgb, _LB_CloudThick.rgb, saturate(n * 0.8 + c * 0.4));
    col = lerp(col, cloud, c);
    float d = distance(posWS, _WorldSpaceCameraPos);
    float h = smoothstep(_LB_HazeStart, _LB_HazeEnd, d) * _LB_HazeAmount * keep * lerp(0.55, 1, c);
    col = lerp(col, _LB_HazeColor.rgb, h); // aerial perspective lifts and cools distant shapes toward the horizon
    return col;
}

// foliage sway: a wind push that swells with the travelling gust, plus a small per-leaf flutter (metres)
float3 lbcSway(float3 posWS)
{
    float g = lbcGust(posWS), f = sin(_LB_Time * 3.1 + dot(posWS, float3(1.7, 2.3, 1.1))) * 0.5;
    return float3(_LB_Wind.x, 0, _LB_Wind.y) * (_LB_Wind.z * 0.04) * (g + 0.3) + float3(f, f * 0.4, -f) * 0.03;
}

#endif
