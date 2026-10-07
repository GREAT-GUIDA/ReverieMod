using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReverieMod.Content;
using Terraria;

namespace ReverieMod.Content.Particles;

// These particles are updated by ParticleManager, then drawn by the screen
// presentation between its title/flash and the cinematic bars.
public abstract class TitleScreenEffectParticle : EffectParticle {
    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) =>
        ScreenPresentationSystem.DrawingTitleEffects &&
        base.PreDraw(spriteBatch, lightColor);

    protected override Vector2 GetDrawPosition() =>
        Vector2.Transform(position,
            Matrix.Invert(Main.GameViewMatrix.TransformationMatrix));
}

// A screen-space burst over the title as it dissolves. The separated colored
// rings are intentionally slightly different in radius and phase: their edges
// split into a spectrum while the overlapping center stays pale.
public class TitleFadeEffectParticle : TitleScreenEffectParticle {
    public Color tint = Color.White;

    protected override void SetupParticleDefaults() {
        timeLeft = maxTimeLeft = 48;
        drawLayer = ParticleLayer.BeforeInterface;
    }

    protected override void SetupEffectLayers() {
        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionLight.Value,
            BlendState.Additive) {
            EndFrame = 17,
            BaseColor = new Color(205, 225, 255),
            BaseScaleVector2 = new Vector2(1.65f, 0.19f),
            CustomScaleCurve = t => 0.38f + 1.28f * MathF.Sin(t * MathHelper.Pi),
            CustomOpacityCurve = t => 0.62f * MathF.Sin(t * MathHelper.Pi)
        });

        AddEffectLayer(new EffectParticleLayer(ModAsset.TitleEffect2.Value,
            BlendState.Additive) {
            EndFrame = 27,
            CustomColorCurve = _ => Color.Lerp(tint, Color.White, 0.55f),
            CustomScaleCurve = t => MathHelper.Lerp(0.52f, 1.72f, t),
            CustomRotationCurve = t => t * 0.18f,
            CustomOpacityCurve = t => 0.34f * MathF.Sin(t * MathHelper.Pi)
        });

        AddPrismRing(new Color(255, 83, 111), 0.94f, -0.025f, 0, 0.18f);
        AddPrismRing(new Color(255, 207, 105), 0.97f, -0.010f, 1, 0.13f);
        AddPrismRing(new Color(115, 250, 221), 1.015f, 0.012f, 2, 0.17f);
        AddPrismRing(new Color(142, 139, 255), 1.045f, 0.027f, 3, 0.19f);
        AddPrismRing(Color.White, 0.99f, 0f, 0, 0.09f);
    }

    private void AddPrismRing(Color ringColor, float radius, float phase,
        int delay, float opacity) {
        AddEffectLayer(new EffectParticleLayer(ModAsset.TitleFadeCircle.Value,
            BlendState.Additive) {
            StartFrame = delay,
            EndFrame = 40,
            BaseColor = ringColor,
            // XNA's positive angle turns clockwise on screen; -90 degrees
            // puts the source ring's notch at the bottom.
            BaseRotation = -MathHelper.PiOver2 + phase,
            CustomScaleCurve = t => radius * MathHelper.Lerp(0.30f, 1.04f,
                GuidaUtils.Smoothstep(0f, 1f, t)),
            CustomOpacityCurve = t => opacity *
                GuidaUtils.Smoothstep(0f, 0.16f, t) *
                GuidaUtils.Smoothstep(1f, 0.72f, t)
        });
    }

}

// Short moving glints, based on InventoryVisualTweaks' world-item star pulse.
public class TitleBlinkParticle : TitleScreenEffectParticle {
    public Color tint = Color.White;
    public float size = 1f;

    protected override void SetupParticleDefaults() {
        timeLeft = maxTimeLeft = 18;
        drawLayer = ParticleLayer.BeforeInterface;
    }

    protected override void SetupEffectLayers() {
        AddEffectLayer(new EffectParticleLayer(ModAsset.TitleBlink.Value,
            BlendState.Additive) {
            CustomColorCurve = _ => tint,
            CustomScaleCurve = t => size * MathHelper.Lerp(0.22f, 0.49f,
                MathF.Sin(t * MathHelper.Pi)),
            CustomOpacityCurve = t => 0.75f * MathF.Sin(t * MathHelper.Pi)
        });
    }

    public override void AI() {
        base.AI();
        velocity *= 0.94f;
    }

}
