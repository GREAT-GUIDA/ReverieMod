// ScreenEffects.fx - Chromatic aberration, vignette, color tint, warp for phase transitions
sampler uImage0 : register(s0);
float uTime;
float uChromaticIntensity;
float uVignetteIntensity;
float3 uTintColor;
float uTintStrength;
float uWarpIntensity;
float2 uWarpCenter;

float4 MainPS(float2 coords : TEXCOORD0, float4 vertColor : COLOR0) : COLOR0 {
    float2 centeredCoords = coords - 0.5;

    // Chromatic aberration (RGB split)
    float2 aberrationOffset = normalize(centeredCoords) * uChromaticIntensity * 0.01;
    float r = tex2D(uImage0, coords + aberrationOffset).r;
    float g = tex2D(uImage0, coords).g;
    float b = tex2D(uImage0, coords - aberrationOffset).b;
    float3 chromaticColor = float3(r, g, b);

    // Screen warp (radial distortion from warp center)
    float2 toWarpCenter = coords - uWarpCenter;
    float warpDist = length(toWarpCenter);
    float warpStrength = uWarpIntensity * smoothstep(0.8, 0.0, warpDist);
    float2 warpOffset = toWarpCenter * warpStrength * sin(uTime * 2.0 + warpDist * 5.0) * 0.03;
    float3 warpedColor = tex2D(uImage0, coords + warpOffset).rgb;

    // Blend chromatic and warped
    float3 finalColor = lerp(chromaticColor, warpedColor, min(uWarpIntensity, 1.0));

    // Vignette (darkening at edges)
    float vignette = 1.0 - smoothstep(0.4, 1.2, length(centeredCoords));
    vignette = lerp(1.0, vignette, uVignetteIntensity);
    finalColor *= vignette;

    // Color tint
    finalColor = lerp(finalColor, uTintColor, uTintStrength);

    return float4(finalColor, 1.0) * vertColor;
}

technique Technique1 {
    pass P0 {
        PixelShader = compile ps_3_0 MainPS();
    }
}
