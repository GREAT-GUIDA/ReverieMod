using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ReverieMod.Content.Particles;

public class SmokeParticle : Particle<SmokeParticle> {
    public float startOpacity = 0.5f;

    public override Texture2D Texture => ModAsset.SmokeDust.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        width = 34;
        height = 36;
        timeLeft = 28;
        maxTimeLeft = 28;
        drawLayer = ParticleLayer.BeforeNPCs;
        cutOffscreen = true;
    }

    public override void AI() {
        velocity.X *= 0.94f;
        velocity.Y *= 0.96f;
        base.AI();
        scale *= 1.006f;
        float fadeIn = MathHelper.Clamp((maxTimeLeft - timeLeft) / 3f, 0f, 1f);
        alpha = startOpacity * fadeIn * MathHelper.Clamp(timeLeft / 16f, 0f, 1f);
    }
}
