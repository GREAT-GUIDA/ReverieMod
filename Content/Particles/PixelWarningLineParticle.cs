using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace ReverieMod.Content.Particles;

// A single white pixel is stretched into a line. It has no glow or arrow texture.
public class PixelWarningLineParticle : Particle {
    public float lineLength;
    public float lineWidth;
    public float opacity;

    public override Texture2D Texture => ModAsset.WarningPixel.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeProjectiles;
        cutOffscreen = false;
        timeLeft = maxTimeLeft = 60;
    }

    public override void AI() {
        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        float progress = 1f - timeLeft / (float)maxTimeLeft;
        float opacity = GuidaUtils.Smoothstep(0f, 0.19f, progress) *
            GuidaUtils.Smoothstep(1f, 0.76f, progress);
        spriteBatch.Draw(Texture, position - Main.screenPosition, null,
            color * (this.opacity * opacity), rotation,
            new Vector2(0f, Texture.Height * 0.5f),
            new Vector2(lineLength / Texture.Width, lineWidth / Texture.Height),
            SpriteEffects.None, 0f);
        return false;
    }
}
