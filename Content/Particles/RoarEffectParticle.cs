using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ReverieMod.Content.Particles;

// Two expanding pressure rings and a short flash for a large creature's roar.
public class RoarEffectParticle : EffectParticle {
    protected override void SetupParticleDefaults() {
        timeLeft = maxTimeLeft = 34;
        drawLayer = ParticleLayer.BeforeProjectiles;
        cutOffscreen = true;
    }

    protected override void SetupEffectLayers() {
        AddEffectLayer(new EffectParticleLayer(ModAsset.SoftCircle.Value,
            BlendState.NonPremultiplied) {
            EndFrame = 15,
            BaseColor = new Color(85, 155, 255),
            CustomScaleCurve = t => MathHelper.Lerp(0.23f, 0.82f, t),
            CustomOpacityCurve = t => 0.18f * GuidaUtils.Smoothstep(1f, 0.1f, t)
        });

        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionSpread.Value,
            BlendState.Additive) {
            EndFrame = 28,
            BaseColor = new Color(110, 195, 255),
            CustomScaleCurve = t => MathHelper.Lerp(0.34f, 1.68f,
                GuidaUtils.Smoothstep(0f, 1f, t)),
            CustomOpacityCurve = t => 0.62f * GuidaUtils.Smoothstep(1f, 0.18f, t)
        });

        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionSpread.Value,
            BlendState.Additive) {
            StartFrame = 7,
            EndFrame = 34,
            BaseColor = new Color(70, 145, 255),
            CustomScaleCurve = t => MathHelper.Lerp(0.38f, 2.05f,
                GuidaUtils.Smoothstep(0f, 1f, t)),
            CustomOpacityCurve = t => 0.32f * GuidaUtils.Smoothstep(1f, 0.15f, t)
        });

        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionLight.Value,
            BlendState.Additive) {
            EndFrame = 13,
            BaseColor = new Color(190, 225, 255),
            CustomScaleCurve = t => MathHelper.Lerp(0.28f, 0.68f, t),
            CustomOpacityCurve = t => 0.38f * GuidaUtils.Smoothstep(1f, 0.2f, t)
        });
    }
}

// A bright ring collapses into its center as energy is absorbed.
public class AbsorptionEffectParticle : EffectParticle {
    protected override void SetupParticleDefaults() {
        timeLeft = maxTimeLeft = 26;
        drawLayer = ParticleLayer.BeforeNPCs;
        cutOffscreen = true;
    }

    protected override void SetupEffectLayers() {
        AddEffectLayer(new EffectParticleLayer(ModAsset.SoftCircle.Value,
            BlendState.NonPremultiplied) {
            BaseColor = new Color(85, 160, 255),
            CustomScaleCurve = t => MathHelper.Lerp(2.2f, 0.18f,
                GuidaUtils.Smoothstep(0f, 1f, t)),
            CustomOpacityCurve = t =>
                (0.03f + 0.20f * GuidaUtils.Smoothstep(0f, 0.82f, t)) *
                GuidaUtils.Smoothstep(1f, 0.9f, t)
        });

        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionSpread.Value,
            BlendState.Additive) {
            BaseColor = new Color(125, 205, 255),
            CustomScaleCurve = t => MathHelper.Lerp(2.85f, 0.16f,
                GuidaUtils.Smoothstep(0f, 1f, t)),
            CustomOpacityCurve = t =>
                (0.05f + 0.44f * GuidaUtils.Smoothstep(0f, 0.82f, t)) *
                GuidaUtils.Smoothstep(1f, 0.9f, t),
            CustomRotationCurve = t => t * 0.32f
        });

        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionLight.Value,
            BlendState.Additive) {
            BaseColor = new Color(205, 235, 255),
            CustomScaleCurve = t => MathHelper.Lerp(0.9f, 0.13f, t),
            CustomOpacityCurve = t => 0.52f *
                GuidaUtils.Smoothstep(0.35f, 0.92f, t) *
                GuidaUtils.Smoothstep(1f, 0.9f, t)
        });
    }
}
