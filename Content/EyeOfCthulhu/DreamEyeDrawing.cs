using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using ReverieMod.Content.Particles;
using Terraria.ID;

namespace ReverieMod.Content.EyeOfCthulhu;

public partial class DreamEye : ModNPC {
    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
        Texture2D texture = ModContent.Request<Texture2D>(Texture).Value;
        Vector2 drawPos = NPC.Center - screenPos;
        Rectangle frame = NPC.frame;
        Vector2 origin = frame.Size() * 0.5f;

        // Draw trail
        DrawTrail(spriteBatch, screenPos);

        // Glow layer (additive behind main sprite)
        if (glowPulse > 0.1f) {
            spriteBatch.End();
            spriteBatch.Begin(default, BlendState.Additive, SamplerState.PointClamp,
                default, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            Color glowColor = new Color(255, 120, 80) * (glowPulse * 0.4f);
            glowColor.A = 0;

            for (int i = 0; i < 4; i++) {
                Vector2 offset = new Vector2(
                    (float)Math.Cos(visualTicks * 0.08f + i * MathHelper.PiOver2),
                    (float)Math.Sin(visualTicks * 0.08f + i * MathHelper.PiOver2)
                ) * 6f * glowPulse;

                spriteBatch.Draw(texture, drawPos + offset, frame, glowColor, NPC.rotation,
                    origin, NPC.scale, SpriteEffects.None, 0f);
            }

            spriteBatch.End();
            spriteBatch.Begin(default, BlendState.AlphaBlend, SamplerState.PointClamp,
                default, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);
        }

        // Main body
        Color baseColor = drawColor;
        if (isEnraged) {
            baseColor = Color.Lerp(drawColor, new Color(255, 100, 100),
                0.3f + 0.2f * (float)Math.Sin(enragedTimer * 0.15f));
        }

        spriteBatch.Draw(texture, drawPos, frame, baseColor, NPC.rotation,
            origin, NPC.scale, SpriteEffects.None, 0f);

        // Pupil highlight (Gaze phase only)
        if (CurrentPhase == Phase.Gaze || CurrentPhase == Phase.Intro) {
            DrawPupilHighlight(spriteBatch, screenPos);
        }

        // Energy rings (Maw/Nightmare phases)
        if (CurrentPhase == Phase.Maw || CurrentPhase == Phase.Nightmare) {
            DrawEnergyRings(spriteBatch, drawPos);
        }

        return false;
    }

    private void DrawTrail(SpriteBatch spriteBatch, Vector2 screenPos) {
        if (NPC.velocity.Length() < 3f) return;

        Texture2D trailTexture = ModAsset.SoftCircle.Value;
        Color trailColor = CurrentPhase switch {
            Phase.Gaze => new Color(100, 180, 255),
            Phase.Maw => new Color(200, 100, 120),
            Phase.Nightmare => new Color(120, 80, 200),
            _ => Color.White
        };

        for (int i = 0; i < trailPositions.Length; i++) {
            int readIndex = (trailHead + i) % trailPositions.Length;
            Vector2 pos = trailPositions[readIndex];
            if (pos == Vector2.Zero) continue;

            float progress = i / (float)trailPositions.Length;
            float alpha = (1f - progress) * 0.5f;
            float scale = (1f - progress) * NPC.scale * 0.6f;

            Color color = trailColor * alpha;
            color.A = 0;

            spriteBatch.Draw(trailTexture, pos - screenPos, null, color, 0f,
                trailTexture.Size() * 0.5f, scale, SpriteEffects.None, 0f);
        }
    }

    private void DrawPupilHighlight(SpriteBatch spriteBatch, Vector2 screenPos) {
        Texture2D glowTexture = ModAsset.SoftCircle.Value;

        // Pupil position (frame 0–2: white sclera is centered around y=120, pupil around y=153)
        // Offset in sprite space, then transform to world
        Vector2 pupilLocalOffset = new Vector2(0, 33f) * NPC.scale; // 153 - 120
        Vector2 pupilWorld = NPC.Center + pupilLocalOffset.RotatedBy(NPC.rotation) + pupilOffset;
        Vector2 pupilScreen = pupilWorld - screenPos;

        // Pupil glow
        float pupilGlow = 0.4f + glowPulse * 0.6f;
        Color pupilColor = new Color(255, 200, 150) * pupilGlow;
        pupilColor.A = 0;

        float pupilDrawScale = pupilScale * NPC.scale * 0.22f;
        spriteBatch.Draw(glowTexture, pupilScreen, null, pupilColor, 0f,
            glowTexture.Size() * 0.5f, pupilDrawScale, SpriteEffects.None, 0f);

        // Highlight dot
        Color highlightColor = Color.White * (0.7f * pupilGlow);
        highlightColor.A = 0;
        Vector2 highlightOffset = new Vector2(-3f, -3f) * pupilScale;
        spriteBatch.Draw(glowTexture, pupilScreen + highlightOffset, null, highlightColor,
            0f, glowTexture.Size() * 0.5f, pupilDrawScale * 0.4f, SpriteEffects.None, 0f);
    }

    private void DrawEnergyRings(SpriteBatch spriteBatch, Vector2 drawPos) {
        Texture2D ringTexture = ModAsset.WarningRing.Value;

        int ringCount = CurrentPhase == Phase.Nightmare ? 3 : 2;
        for (int i = 0; i < ringCount; i++) {
            float offset = i * MathHelper.TwoPi / ringCount;
            float rotation = irisRotation + offset;
            float pulse = 1f + 0.08f * (float)Math.Sin(visualTicks * 0.04f + offset);
            float scale = (1.1f + i * 0.25f) * pulse * NPC.scale;

            Color ringColor = CurrentPhase == Phase.Nightmare
                ? new Color(180, 80, 200, 0) * 0.35f
                : new Color(200, 100, 120, 0) * 0.3f;

            spriteBatch.Draw(ringTexture, drawPos, null, ringColor, rotation,
                ringTexture.Size() * 0.5f, scale, SpriteEffects.None, 0f);
        }
    }

    public override void PostDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
        // Screen glow
        if (Main.netMode != NetmodeID.Server && glowPulse > 0.3f) {
            ScreenEffectSystem.AddGlow(NPC.Center, new Color(255, 180, 120),
                NPC.scale * 1.8f, glowPulse * 0.5f);
        }

        // Shatter transition distortion
        if (CurrentPhase == Phase.ShatterTransition && Main.netMode != NetmodeID.Server) {
            float distort = GuidaUtils.Smoothstep(30f, 70f, Timer) *
                            GuidaUtils.Smoothstep(90f, 75f, Timer);
            ScreenTwistSystem.URadialBlurIntensity = distort * 0.25f;
            ScreenTwistSystem.URadialBlurPosition =
                (NPC.Center - Main.screenPosition) / Main.ScreenSize.ToVector2();
        }
    }
}
