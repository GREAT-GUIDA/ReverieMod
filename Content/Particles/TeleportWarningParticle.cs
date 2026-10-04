using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace ReverieMod.Content.Particles;

// Preview the arrival with the same blue slime dust used by vanilla King Slime's
// teleport, spread over a faint body silhouette so the landing spot stays clear.
public class TeleportWarningParticle : Particle {
    public float beatPhase;
    public float beatSpeed = 1f;
    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeNPCs;
        cutOffscreen = true;
        timeLeft = maxTimeLeft = 54;
    }

    public override void AI() {
        if (Main.netMode != NetmodeID.Server) {
            float progress = 1f - timeLeft / (float)maxTimeLeft;
            int count = progress < 0.55f ? 5 : 8;
            Vector2 topLeft = position - new Vector2(width * 0.65f, height * 1.2f);
            for (int i = 0; i < count; i++) {
                int index = Dust.NewDust(topLeft, (int)(width * 1.3f),
                    (int)(height * 1.2f), DustID.t_Slime, 0f, -0.3f,
                    150, new Color(78, 136, 255, 80),
                    Main.rand.NextFloat(1.2f, 1.8f));
                Dust dust = Main.dust[index];
                dust.noGravity = true;
                dust.velocity *= 0.5f;
            }
        }
        base.AI();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        float progress = 1f - timeLeft / (float)maxTimeLeft;
        float fade = GuidaUtils.Smoothstep(0f, 0.22f, progress) *
            GuidaUtils.Smoothstep(1f, 0.87f, progress);
        Texture2D body = ModAsset.KingSlimeBody.Value;
        spriteBatch.Draw(body, position - Main.screenPosition - Vector2.UnitY * height * 0.58f,
            new Rectangle(0, 0, 174, 120), new Color(100, 172, 255) * (0.12f * fade),
            0f, new Vector2(87f, 60f),
            new Vector2(width * 1.4f / 174f, height * 1.45f / 120f),
            SpriteEffects.None, 0f);

        // The flare and shader-driven ripple use the world-item glow treatment.
        // The beat phase controls each outward ripple; the whole mark grows in
        // and contracts away at the ends of the teleport warning.
        Vector2 center = position - Main.screenPosition - Vector2.UnitY * height * 0.58f;
        Texture2D glow = ModAsset.TeleportGlow.Value;
        Texture2D flare = ModAsset.TeleportFlare.Value;
        float age = maxTimeLeft - timeLeft;
        float phase = ((age * beatSpeed + beatPhase) % 22.5f) / 22.5f;
        float enter = GuidaUtils.Smoothstep(0f, 0.18f, progress);
        float exit = GuidaUtils.Smoothstep(0.80f, 1f, progress);
        float sizeEnvelope = MathHelper.Lerp(0.5f, 1f, enter) *
            MathHelper.Lerp(1f, 0.48f, exit);
        float ringFade = GuidaUtils.Smoothstep(0f, 0.14f, phase) *
            GuidaUtils.Smoothstep(1f, 0.64f, phase);
        float rotation = age * 0.075f;

        Texture2D bloom = ModAsset.TeleportSolidBloom.Value;
        Effect ripple = ModAsset.TeleportRippleBloom.Value;
        float rippleScale = width * (3f + phase * 1.4f) * sizeEnvelope / bloom.Width;
        DrawRipple(spriteBatch, ripple, bloom, center, phase, rippleScale,
            BlendState.NonPremultiplied, 0.78f * fade * ringFade);
        DrawRipple(spriteBatch, ripple, bloom, center, phase, rippleScale,
            BlendState.Additive, 1.1f * fade * ringFade);
        spriteBatch.EndAndBeginDefault();

        spriteBatch.EndAndBegin(BlendState.Additive);
        spriteBatch.Draw(glow, center, null, new Color(45, 108, 255) * (0.52f * fade),
            0f, glow.Size() * 0.5f, width * 2.2f * sizeEnvelope / glow.Width,
            SpriteEffects.None, 0f);
        spriteBatch.Draw(flare, center, null, new Color(145, 207, 255) * (0.8f * fade),
            rotation, flare.Size() * 0.5f, width * 1.18f * sizeEnvelope / flare.Width,
            SpriteEffects.None, 0f);
        spriteBatch.Draw(flare, center, null, new Color(190, 225, 255) * (0.64f * fade),
            -rotation, flare.Size() * 0.5f, width * 1f * sizeEnvelope / flare.Width,
            SpriteEffects.None, 0f);
        spriteBatch.EndAndBeginDefault();
        return false;
    }

    private static void DrawRipple(SpriteBatch spriteBatch, Effect effect, Texture2D bloom,
        Vector2 center, float phase, float scale, BlendState blend, float opacity) {
        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Immediate, blend, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone, effect,
            Main.GameViewMatrix.TransformationMatrix);
        effect.SetTime(phase)
            .Set("RingSpeed", 0.8f)
            .Set("RingCount", 1.15f)
            .Set("RingWidth", 0.42f)
            .SetColor(new Color(87, 166, 255))
            .Set("ColorMix", 0.92f)
            .SetOpacity(opacity)
            .Apply();
        spriteBatch.Draw(bloom, center, null, Color.White, 0f,
            bloom.Size() * 0.5f, scale, SpriteEffects.None, 0f);
    }
}
