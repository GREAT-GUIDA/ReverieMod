using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace ReverieMod.Content.Particles;

public class GlowStreakParticle : Particle {
    public Vector2 drawSize;

    public override Texture2D Texture => ModAsset.TeleportSolidBloom.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeProjectiles;
        useLighting = false;
        cutOffscreen = true;
        width = 12;
        height = 60;
    }

    public override void AI() {
        base.AI();
        velocity.X *= 0.97f;
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        float progress = 1f - timeLeft / (float)maxTimeLeft;
        float fade = GuidaUtils.Smoothstep(0f, 0.14f, progress) *
            GuidaUtils.Smoothstep(1f, 0.56f, progress);
        if (fade <= 0f) return false;

        Vector2 center = GetDrawPosition();
        Texture2D halo = Texture;
        Texture2D core = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
        Color haloColor = color * (alpha * fade * 0.23f);
        haloColor.A = 0;
        Color coreColor = Color.Lerp(color, Color.White, 0.78f) * (alpha * fade * 0.68f);
        coreColor.A = 0;
        spriteBatch.Draw(halo, center, null, haloColor, rotation, halo.Size() * 0.5f,
            new Vector2(drawSize.X * 2.25f / halo.Width, drawSize.Y * 1.12f / halo.Height),
            SpriteEffects.None, 0f);
        spriteBatch.Draw(core, center, null, coreColor, rotation, core.Size() * 0.5f,
            new Vector2(drawSize.X / core.Width, drawSize.Y / core.Height),
            SpriteEffects.None, 0f);
        return false;
    }
}
