// RiftDistortion.fx - Eyelid rift portal distortion effect
sampler uImage0 : register(s0);
float uTime;
float uIntensity;
float2 uCenter;
float uRadius;

float4 MainPS(float2 coords : TEXCOORD0, float4 vertColor : COLOR0) : COLOR0 {
    float2 centered = coords - uCenter;
    float dist = length(centered);

    // Distortion mask (strongest at center, fades at edges)
    float distortMask = smoothstep(uRadius, uRadius * 0.3, dist) * uIntensity;

    // Spiral distortion
    float angle = atan2(centered.y, centered.x);
    float spiral = sin(angle * 3.0 + uTime * 2.0 - dist * 8.0) * distortMask * 0.15;

    // Radial pull
    float2 distortDir = normalize(centered);
    float2 distortedCoords = coords + distortDir * spiral - distortDir * distortMask * 0.08;

    float4 texColor = tex2D(uImage0, distortedCoords);

    // Color tint at rift center
    float tintStrength = smoothstep(uRadius * 0.5, 0.0, dist) * uIntensity;
    float3 riftTint = float3(0.6, 0.4, 0.8);
    texColor.rgb = lerp(texColor.rgb, riftTint, tintStrength * 0.4);

    return texColor * vertColor;
}

technique Technique1 {
    pass P0 {
        PixelShader = compile ps_3_0 MainPS();
    }
}
