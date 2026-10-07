using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace ReverieMod.Content.Particles;

// A single white pixel is stretched into a line. It has no glow or arrow texture.
public class PixelWarningLineParticle : Particle<PixelWarningLineParticle> {
    public float lineLength;
    public float lineWidth;
    public float opacity;

    public new static PixelWarningLineParticle Spawn(Vector2 position, float rotation,
        float length, float width, int duration, float opacity, Color? color = null) {
        PixelWarningLineParticle line = Particle.Spawn<PixelWarningLineParticle>(position);
        line.rotation = rotation;
        line.lineLength = length;
        line.lineWidth = width;
        line.timeLeft = line.maxTimeLeft = Math.Max(1, duration);
        line.opacity = opacity;
        if (color.HasValue) line.color = color.Value;
        return line;
    }

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
