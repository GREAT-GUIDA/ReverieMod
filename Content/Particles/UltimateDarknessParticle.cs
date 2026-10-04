using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace ReverieMod.Content.Particles;

// Screen shade lives behind actors, so every warning and dash remains legible.
public class UltimateDarknessParticle : Particle {
    private bool sustained;

    public void Sustain() => sustained = true;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeNPCs;
        cutOffscreen = false;
        alpha = 0f;
        timeLeft = maxTimeLeft = 2;
    }

    public override void AI() {
        alpha = MathHelper.Lerp(alpha, sustained ? 0.62f : 0f,
            sustained ? 0.075f : 0.08f);
        if (!sustained && alpha < 0.01f) Kill();
        else timeLeft = 2;
        sustained = false;
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        Texture2D pixel = ModAsset.WarningPixel.Value;
        spriteBatch.EndAndBegin(BlendState.AlphaBlend, SamplerState.PointClamp,
            null, Matrix.Identity);
        spriteBatch.Draw(pixel, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight),
            Color.Black * alpha);
        spriteBatch.EndAndBeginDefault();
        return false;
    }
}
