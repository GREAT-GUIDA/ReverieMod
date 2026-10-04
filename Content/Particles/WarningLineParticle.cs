using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace ReverieMod.Content.Particles;

// An ArrowLine telegraph that can be aimed and timed by any attack.
public class WarningLineParticle : Particle {
    public float lineLength = 400f;
    public float lineWidth = 100f;
    public float lineRotation = 10f;
    public float timer;
    public float time = 100f;
    public float curve;
    public bool smaller;
    public float arrowSpeed = 1f;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeNPCs;
        cutOffscreen = false;
        color = new Color(105, 185, 255);
    }

    public override void AI() {
        timer += 1f;
        if (timer >= time) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        Texture2D arrow = ModAsset.Arrow.Value;
        Texture2D line = ModAsset.ArrowLine.Value;
        float progress = timer / time;
        float fade = GuidaUtils.Smoothstep(0f, 0.15f, progress) *
            GuidaUtils.Smoothstep(1f, 0.85f, progress);
        float arrowLength = lineWidth / arrow.Width * arrow.Height;
        float radius = curve != 0f ? lineLength / curve : 0f;

        DrawLineBands(spriteBatch, line, fade, color * 0.5f, BlendState.AlphaBlend);

        for (float distance = 0f; distance < lineLength; distance += arrowLength) {
            float along = distance + (float)(Main.timeForVisualEffects * 6f * arrowSpeed) % arrowLength;
            float curveNow = curve * along / lineLength;
            Vector2 offset = new Vector2(0f, -along);
            if (curveNow != 0f)
                offset = new Vector2(radius - (float)Math.Cos(curveNow) * radius,
                    -(float)Math.Sin(curveNow) * radius);
            Vector2 drawPosition = position + offset.RotatedBy(lineRotation);
            Vector2 drawScale = new Vector2(lineWidth / arrow.Width * fade, lineWidth / arrow.Width);
            if (!smaller)
                spriteBatch.Draw(arrow, drawPosition - Main.screenPosition, null,
                    color * (1f - along / lineLength) * Math.Min(along / lineLength * 10f, 1f) * alpha,
                    lineRotation + curveNow, arrow.Size() * 0.5f, drawScale);
        }

        DrawLineBands(spriteBatch, line, fade, color, BlendState.Additive);
        return false;
    }

    private void DrawLineBands(SpriteBatch spriteBatch, Texture2D line, float fade,
        Color tint, BlendState blend) {
        spriteBatch.EndAndBegin(SpriteSortMode.Deferred, blend);
        for (int i = 1; i <= 2; i++) {
            float phase = i - (float)(Main.timeForVisualEffects * 0.03f) % 1f;
            spriteBatch.DrawEntity(line, position - Main.screenPosition,
                tint * MathHelper.Lerp(0f, 1f, phase) * MathHelper.Lerp(2f, 1f, phase) * fade * alpha,
                lineRotation, new Vector2(line.Width / 2f, line.Height),
                new Vector2(lineWidth / line.Width *
                    (GuidaUtils.Smoothstep(0f, 1f, 2f - phase) * 0.95f + (2f - phase) * 0.1f),
                    lineLength / line.Height));
        }
        spriteBatch.EndAndBeginDefault();
    }
}
