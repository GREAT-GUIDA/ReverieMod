// FleshPulse.fx - Vein pulse, damage flash, and death dissolve for Dream Eye flesh
sampler uImage0 : register(s0);
float uTime;
float uIntensity;
float uFlashIntensity;
float uDissolveProgress;

float3 uVeinColor;
float uVeinSpeed;
float uVeinDensity;

float4 MainPS(float2 coords : TEXCOORD0, float4 vertColor : COLOR0) : COLOR0 {
    float4 texColor = tex2D(uImage0, coords);
    if (texColor.a <= 0.0)
        return float4(0, 0, 0, 0);

    // Vein pulse effect
    float2 centered = coords - 0.5;
    float dist = length(centered);
    float angle = atan2(centered.y, centered.x);

    float veinPattern = sin(angle * uVeinDensity + uTime * uVeinSpeed) *
                        sin(dist * 8.0 + uTime * uVeinSpeed * 0.7);
    veinPattern = smoothstep(0.3, 0.7, (veinPattern + 1.0) * 0.5);

    float veinMask = smoothstep(0.2, 0.8, dist) * (1.0 - smoothstep(0.85, 1.0, dist));
    float veinStrength = veinPattern * veinMask * uIntensity;

    // Apply vein color
    float3 withVeins = lerp(texColor.rgb, uVeinColor, veinStrength * 0.4);

    // Damage flash (additive red)
    float3 flashed = withVeins + float3(1.0, 0.3, 0.3) * uFlashIntensity;

    // Dissolve effect
    float noise = frac(sin(dot(coords, float2(12.9898, 78.233))) * 43758.5453);
    float dissolveEdge = smoothstep(uDissolveProgress - 0.1, uDissolveProgress, noise);
    float dissolveMask = step(noise, uDissolveProgress);

    float3 finalColor = flashed;
    float finalAlpha = texColor.a * (1.0 - dissolveMask);

    // Glowing dissolve edge
    float edgeGlow = dissolveEdge * (1.0 - dissolveMask) * 2.0;
    finalColor += float3(1.0, 0.6, 0.3) * edgeGlow;

    return float4(finalColor * vertColor.rgb, finalAlpha * vertColor.a);
}

technique Technique1 {
    pass P0 {
        PixelShader = compile ps_3_0 MainPS();
    }
}
