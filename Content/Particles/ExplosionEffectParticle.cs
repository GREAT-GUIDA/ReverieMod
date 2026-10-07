using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ReverieMod.Content.Particles;

// A reusable burst. The warning ring is deliberately absent from these layers.
public class ExplosionEffectParticle : EffectParticle<ExplosionEffectParticle> {
    protected override void SetupParticleDefaults() {
        timeLeft = maxTimeLeft = 25;
        drawLayer = ParticleLayer.BeforeProjectiles;
        cutOffscreen = true;
    }

    protected override void SetupEffectLayers() {
        AddEffectLayer(new EffectParticleLayer(ModAsset.SoftCircle.Value,
            BlendState.NonPremultiplied) {
            EndFrame = 8,
            BaseColor = Color.White,
            CustomScaleCurve = t => MathHelper.Lerp(0.22f, 0.35f, t),
            CustomOpacityCurve = t =>
                0.68f * GuidaUtils.Smoothstep(1f, 0.18f, t)
        });

        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionSpread.Value,
            BlendState.Additive) {
            EndFrame = 25,
            BaseColor = new Color(255, 155, 72),
            CustomScaleCurve = t => MathHelper.Lerp(0.36f, 1.26f,
                GuidaUtils.Smoothstep(0f, 0.85f, t)),
            CustomOpacityCurve = t =>
                0.64f * GuidaUtils.Smoothstep(1f, 0.08f, t),
            CustomColorCurve = t => Color.Lerp(
                new Color(255, 215, 112), new Color(255, 105, 47), t)
        });

        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionLight.Value,
            BlendState.Additive) {
            EndFrame = 12,
            BaseColor = new Color(255, 235, 178),
            CustomScaleCurve = t => MathHelper.Lerp(0.42f, 0.75f, t),
            CustomOpacityCurve = t =>
                0.72f * GuidaUtils.Smoothstep(1f, 0.25f, t)
        });
    }
}
