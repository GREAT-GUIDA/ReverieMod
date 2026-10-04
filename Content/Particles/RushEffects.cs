using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace ReverieMod.Content.Particles;

public class SpeedLineParticle : Particle {
    public Vector2 drawScale;
    public float velocityDrag;
    public float fadeInEnd;
    public float fadeOutStart;

    public override Texture2D Texture => TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeNPCs;
    }

    public override void AI() {
        position += velocity;
        velocity *= velocityDrag;
        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        float progress = 1f - timeLeft / (float)maxTimeLeft;
        float fade = GuidaUtils.Smoothstep(0f, fadeInEnd, progress) *
            GuidaUtils.Smoothstep(1f, fadeOutStart, progress);
        // ThePerfectGlow is drawn like the spear-tip glint: zero alpha retains its bright RGB.
        Color tint = color * (alpha * fade);
        tint.A = 0;
        spriteBatch.Draw(Texture, position - Main.screenPosition, null, tint, rotation,
            Texture.Size() * 0.5f, drawScale, SpriteEffects.None, 0f);
        return false;
    }
}

public class RushCircleParticle : Particle {
    public Vector2 drawScale;
    public Vector2 growthPerTick;
    public float velocityDrag;
    public float fadeInEnd;
    public float fadeOutStart;

    public override Texture2D Texture => ModAsset.RushCircle.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeProjectiles;
    }

    public override void AI() {
        position += velocity;
        velocity *= velocityDrag;
        drawScale += growthPerTick;
        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        float progress = 1f - timeLeft / (float)maxTimeLeft;
        float fade = GuidaUtils.Smoothstep(0f, fadeInEnd, progress) *
            GuidaUtils.Smoothstep(1f, fadeOutStart, progress);
        spriteBatch.EndAndBegin(BlendState.Additive);
        spriteBatch.Draw(Texture, position - Main.screenPosition, null, color * (alpha * fade),
            rotation, Texture.Size() * 0.5f, drawScale, SpriteEffects.None, 0f);
        spriteBatch.EndAndBeginDefault();
        return false;
    }
}
