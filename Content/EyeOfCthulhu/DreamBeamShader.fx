// DreamBeamShader.fx - Sweeping deathray visual
sampler uImage0 : register(s0);
float uTime;
float uLength;
float uIntensity;
float3 uColor;

float4 MainPS(float2 coords : TEXCOORD0, float4 vertColor : COLOR0) : COLOR0 {
    // Beam flows from center to edges
    float distFromCenter = abs(coords.x - 0.5) * 2.0;
    float alongBeam = coords.y;

    // Core beam intensity
    float coreWidth = 0.3;
    float core = 1.0 - smoothstep(0.0, coreWidth, distFromCenter);

    // Outer glow
    float glowWidth = 0.7;
    float glow = 1.0 - smoothstep(coreWidth, glowWidth, distFromCenter);

    // Flowing energy pattern
    float flow = sin(alongBeam * 10.0 - uTime * 8.0) * 0.5 + 0.5;
    float flowPattern = pow(flow, 2.0);

    // Pulsing intensity
    float pulse = sin(uTime * 4.0) * 0.2 + 0.8;

    // Combine
    float intensity = (core * 1.5 + glow * 0.6) * flowPattern * pulse * uIntensity;

    // Color gradient (brighter at core)
    float3 coreColor = uColor * 1.5;
    float3 edgeColor = uColor * 0.6;
    float3 finalColor = lerp(edgeColor, coreColor, core);

    // Edge fade
    float edgeFade = smoothstep(0.0, 0.05, alongBeam) * smoothstep(1.0, 0.95, alongBeam);

    float alpha = intensity * edgeFade;

    return float4(finalColor, alpha) * vertColor;
}

technique Technique1 {
    pass P0 {
        PixelShader = compile ps_3_0 MainPS();
    }
}
