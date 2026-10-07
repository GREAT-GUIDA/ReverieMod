using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReverieMod.Content.Particles;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;

namespace ReverieMod.Content.EyeOfCthulhu;

internal static class EyeDraw {
    internal static Color Crimson => new(255, 55, 100);
    internal static Color Cyan => new(105, 225, 255);
    internal static Color Additive(Color color, float opacity) { color *= opacity; color.A = 0; return color; }
    internal static void Line(SpriteBatch sb, Vector2 start, Vector2 end, float width, Color color) {
        Texture2D pixel = ModAsset.WarningPixel.Value;
        Vector2 delta = end - start;
        if (delta.LengthSquared() < 0.01f || width <= 0f) return;
        sb.Draw(pixel, start - Main.screenPosition, null, color, delta.ToRotation(),
            new Vector2(0f, pixel.Height * 0.5f), new Vector2(delta.Length() / pixel.Width, width / pixel.Height), SpriteEffects.None, 0f);
    }
    internal static void Glow(SpriteBatch sb, Vector2 center, float diameter, Color tint, float opacity) {
        Texture2D glow = ModAsset.SoftCircle.Value;
        sb.Draw(glow, center - Main.screenPosition, null, Additive(tint, opacity), 0f,
            glow.Size() * 0.5f, diameter / glow.Width, SpriteEffects.None, 0f);
    }
    internal static void Ring(SpriteBatch sb, Vector2 center, float radius, Color tint, float width) {
        for (int i = 0; i < 80; i++) {
            Vector2 a = center + (i * MathHelper.TwoPi / 80f).ToRotationVector2() * radius;
            Vector2 b = center + ((i + 1) * MathHelper.TwoPi / 80f).ToRotationVector2() * radius;
            Line(sb, a, b, width, Additive(tint, 1f));
        }
    }
    internal static void Arrows(SpriteBatch sb, Vector2 start, Vector2 end, Color color, float size) {
        Vector2 delta = end - start;
        Vector2 axis = delta.SafeNormalize(Vector2.UnitY);
        Vector2 normal = new(-axis.Y, axis.X);
        float shift = (float)Main.timeForVisualEffects * 2f % 75f;
        for (float d = shift; d < delta.Length(); d += 75f) {
            Vector2 tip = start + axis * d;
            Line(sb, tip - axis * size + normal * size * 0.55f, tip, 2f, Additive(color, 0.75f));
            Line(sb, tip - axis * size - normal * size * 0.55f, tip, 2f, Additive(color, 0.75f));
        }
    }
    internal static void Warning(SpriteBatch sb, Vector2 start, Vector2 end, float opacity, float width) {
        Vector2 normal = (end - start).SafeNormalize(Vector2.UnitY).RotatedBy(MathHelper.PiOver2);
        Line(sb, start, end, width, Additive(Crimson, opacity * 0.1f));
        Line(sb, start + normal * width * 0.5f, end + normal * width * 0.5f, 2f, Additive(Crimson, opacity * 0.75f));
        Line(sb, start - normal * width * 0.5f, end - normal * width * 0.5f, 2f, Additive(Crimson, opacity * 0.75f));
        Line(sb, start, end, 2f, Additive(new Color(255, 190, 205), opacity));
        Arrows(sb, start, end, Crimson * opacity, Math.Min(24f, width * 0.3f));
    }
    internal static void Eye(SpriteBatch sb, Vector2 center, float rotation, int frame, Color color, Vector2 scale) {
        Texture2D body = ModAsset.EyeOfCthulhu_png.Value;
        Rectangle source = new(0, frame * (body.Height / 6), body.Width, body.Height / 6);
        // The visible eye occupies the bottom of the frame; flames extend behind it.
        sb.Draw(body, center - Main.screenPosition, source, color, rotation,
            new Vector2(body.Width * 0.5f, 118f), scale, SpriteEffects.None, 0f);
    }
}

public partial class EyeOfCthulhu {
    public override void FindFrame(int frameHeight) {
        NPC.frameCounter++;
        int phase = Phase > 0 && (State != Attack.Transform || Timer >= 84f) ? 3 : 0;
        NPC.frame.Y = (phase + (int)NPC.frameCounter / 7 % 3) * frameHeight;
    }

    private void UpdateVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        flash *= 0.9f;
        Lighting.AddLight(NPC.Center, Phase > 0 ? 0.8f : 0.35f, 0.06f, Phase > 0 ? 0.12f : 0.3f);
        if (NPC.velocity.LengthSquared() > 200f && Main.rand.NextBool(2)) {
            GlowStreakParticle streak = ParticleManager.Instance.NewParticle<GlowStreakParticle>(
                NPC.Center + Main.rand.NextVector2Circular(35f, 35f), -NPC.velocity * 0.1f);
            streak.color = EyeDraw.Crimson;
            streak.drawSize = new Vector2(5f, 36f);
            streak.rotation = NPC.velocity.ToRotation() + MathHelper.PiOver2;
            streak.timeLeft = streak.maxTimeLeft = 18;
        }
        if (State == Attack.Transform && Timer < 84f && (int)Timer % 4 == 0) {
            Vector2 offset = Main.rand.NextVector2CircularEdge(100f, 100f);
            Dust dust = Dust.NewDustPerfect(NPC.Center + offset, DustID.Blood, -offset * 0.055f, 0, EyeDraw.Crimson, 1.3f);
            dust.noGravity = true;
        }
    }

    internal static void Pulse(Vector2 center, float size) {
        if (Main.netMode == NetmodeID.Server) return;
        CthulhuPulseParticle pulse = ParticleManager.Instance.NewParticle<CthulhuPulseParticle>(center, Vector2.Zero, scale: size);
        pulse.color = EyeDraw.Crimson;
    }

    internal static void BloodBurst(Vector2 center, int count, float speed) {
        if (Main.netMode == NetmodeID.Server) return;
        for (int i = 0; i < count; i++) {
            Dust dust = Dust.NewDustPerfect(center + Main.rand.NextVector2Circular(24f, 24f), DustID.Blood,
                Main.rand.NextVector2Circular(speed, speed), 30, default, Main.rand.NextFloat(1f, 1.8f));
            dust.noGravity = true;
        }
    }

    private void Roar(float strength) {
        if (Main.netMode == NetmodeID.Server) return;
        SoundEngine.PlaySound(TombwardSound.EyeRoar, NPC.Center);
        Pulse(NPC.Center, strength);
        if (Vector2.DistanceSquared(Main.LocalPlayer.Center, NPC.Center) < 1600f * 1600f)
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(NPC.Center, Vector2.UnitY,
                7f * strength, 5f, 20, 1600f, "ReverieEyeRoar"));
    }

    private void RushBurst() {
        if (Main.netMode == NetmodeID.Server) return;
        SoundEngine.PlaySound(TombwardSound.EyeRush, NPC.Center);
        Pulse(NPC.Center, 0.5f);
        BloodBurst(NPC.Center, 12, 3f);
        flash = 0.65f;
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
        int frame = NPC.frame.Y / (ModAsset.EyeOfCthulhu_png.Value.Height / 6);
        float breath = (float)Math.Sin(Main.timeForVisualEffects * 0.07f) * 0.025f;
        Vector2 scale = new(1f + breath, 1f - breath);
        float opacity = State == Attack.Arrival ? GuidaUtils.Smoothstep(0f, 30f, Timer) : 1f;
        if (despawning) opacity = NPC.timeLeft / 60f;
        if (State == Attack.Death) {
            float death = GuidaUtils.Smoothstep(0f, 90f, Timer);
            scale *= 1f + death * 0.24f;
            opacity *= 1f - death;
            EyeDraw.Ring(spriteBatch, NPC.Center, 40f + death * 190f, EyeDraw.Crimson * (1f - death), 5f);
            EyeDraw.Glow(spriteBatch, NPC.Center, 160f + death * 200f, EyeDraw.Crimson, death * 0.6f);
        }
        if (State == Attack.Transform) {
            float charge = GuidaUtils.Smoothstep(0f, 84f, Timer);
            if (Timer < 84f) {
                EyeDraw.Ring(spriteBatch, NPC.Center, 180f * (1f - charge) + 45f, EyeDraw.Crimson * charge, 4f);
                scale *= 1f + (float)Math.Sin(Timer * 0.8f) * 0.05f * charge;
            }
        }
        EyeDraw.Glow(spriteBatch, NPC.Center, Phase > 0 ? 220f : 160f, Phase > 0 ? EyeDraw.Crimson : EyeDraw.Cyan, 0.16f * opacity);
        if (NPC.velocity.LengthSquared() > 70f) {
            scale *= new Vector2(0.93f, 1.1f);
            for (int i = NPC.oldPos.Length - 1; i > 0; i--) {
                if (NPC.oldPos[i] == Vector2.Zero) continue;
                EyeDraw.Eye(spriteBatch, NPC.oldPos[i] + NPC.Size * 0.5f, NPC.oldRot[i], frame,
                    EyeDraw.Additive(EyeDraw.Crimson, opacity * (1f - i / (float)NPC.oldPos.Length) * 0.28f), scale);
            }
        }
        if (State == Attack.Dash) {
            int tick = (int)Timer % (Phase > 0 ? 105 : 120);
            int leg = (int)Timer / (Phase > 0 ? 105 : 120);
            if (tick >= 36 && tick < 66 && leg < (Phase > 0 ? 3 : 2) && dashDirection != Vector2.Zero) {
                EyeDraw.Warning(spriteBatch, dashStart, dashStart + dashDirection * (Phase > 0 ? 675f : 575f),
                    GuidaUtils.Smoothstep(36f, 43f, tick), 92f);
                EyeDraw.Ring(spriteBatch, NPC.Center, MathHelper.Lerp(85f, 48f, (tick - 36f) / 30f), EyeDraw.Crimson, 3f);
                scale *= new Vector2(1.12f, 0.87f);
            }
        }
        Color bodyColor = Color.Lerp(drawColor, Color.White, 0.65f) * opacity;
        EyeDraw.Eye(spriteBatch, NPC.Center + Vector2.UnitY * NPC.gfxOffY, NPC.rotation, frame, bodyColor, scale);
        if (flash > 0.01f)
            EyeDraw.Eye(spriteBatch, NPC.Center, NPC.rotation, frame,
                EyeDraw.Additive(new Color(255, 140, 175), flash * 0.7f * opacity), scale);
        return false;
    }
}

public class CthulhuPulseParticle : EffectParticle {
    protected override void SetupParticleDefaults() {
        timeLeft = maxTimeLeft = 30;
        drawLayer = ParticleLayer.BeforeNPCs;
        cutOffscreen = true;
    }
    protected override void SetupEffectLayers() {
        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionSpread.Value, BlendState.Additive) {
            BaseColor = new Color(255, 45, 95),
            CustomScaleCurve = t => MathHelper.Lerp(0.12f, 1.65f, GuidaUtils.Smoothstep(0f, 1f, t)),
            CustomOpacityCurve = t => 0.65f * GuidaUtils.Smoothstep(1f, 0.08f, t)
        });
        AddEffectLayer(new EffectParticleLayer(ModAsset.ExplosionLight.Value, BlendState.Additive) {
            EndFrame = 14,
            BaseColor = new Color(255, 205, 215),
            CustomScaleCurve = t => MathHelper.Lerp(0.15f, 0.9f, t),
            CustomOpacityCurve = t => 0.45f * GuidaUtils.Smoothstep(1f, 0f, t)
        });
    }
}
