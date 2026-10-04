using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.ID;

namespace ReverieMod.Content.EyeOfCthulhu;

// EyeServant: Orbiting eye minion summoned during ServantWeave attack
public class EyeServant : ModNPC {
    private ref float MasterIndex => ref NPC.ai[0];
    private ref float OrbitAngle => ref NPC.ai[1];
    private ref float Timer => ref NPC.ai[2];
    public override string Texture => ModAsset.NPC_5_Mod;

    private const float OrbitRadius = 180f;
    private const float OrbitSpeed = 0.03f;

    public override void SetStaticDefaults() {
        Main.npcFrameCount[Type] = 3;
    }

    public override void SetDefaults() {
        NPC.width = 32;
        NPC.height = 32;
        NPC.damage = 18;
        NPC.defense = 8;
        NPC.lifeMax = 80;
        NPC.knockBackResist = 0.4f;
        NPC.noGravity = true;
        NPC.noTileCollide = true;
        NPC.HitSound = SoundID.NPCHit1;
        NPC.DeathSound = SoundID.NPCDeath1;
        NPC.value = 50f;
        NPC.aiStyle = -1;
    }

    public override void AI() {
        Timer++;

        // Find master
        int masterIdx = (int)MasterIndex;
        if (masterIdx < 0 || masterIdx >= Main.maxNPCs || !Main.npc[masterIdx].active ||
            Main.npc[masterIdx].type != ModContent.NPCType<DreamEye>()) {
            // Master dead, become aggressive
            BecomeAggressive();
            return;
        }

        NPC master = Main.npc[masterIdx];

        // Orbit phase (first 120 ticks)
        if (Timer < 120) {
            OrbitAngle += OrbitSpeed;
            Vector2 orbitPos = master.Center + OrbitAngle.ToRotationVector2() * OrbitRadius;
            NPC.Center = Vector2.Lerp(NPC.Center, orbitPos, 0.08f);
            NPC.rotation = (master.Center - NPC.Center).ToRotation() + MathHelper.PiOver2;
        }
        // Dash phase (sequential dashes)
        else {
            DashAtTarget();
        }
    }

    private void BecomeAggressive() {
        NPC.TargetClosest(true);
        Player target = Main.player[NPC.target];

        if (!target.active || target.dead) {
            NPC.velocity.Y -= 0.4f;
            return;
        }

        Vector2 toTarget = target.Center - NPC.Center;
        float distance = toTarget.Length();

        if (distance > 40f) {
            Vector2 desiredVel = Vector2.Normalize(toTarget) * 6f;
            NPC.velocity = (NPC.velocity * 9f + desiredVel) / 10f;
        }

        NPC.rotation = NPC.velocity.ToRotation() + MathHelper.PiOver2;
    }

    private void DashAtTarget() {
        NPC.TargetClosest(true);
        Player target = Main.player[NPC.target];

        float dashTimer = Timer - 120f;

        if (dashTimer % 80f < 20f) {
            // Wind up
            Vector2 toTarget = target.Center - NPC.Center;
            NPC.velocity *= 0.9f;
            NPC.rotation = toTarget.ToRotation() + MathHelper.PiOver2;
        } else if (dashTimer % 80f < 50f) {
            // Dash
            if ((int)dashTimer % 80 == 20) {
                Vector2 dashDir = Vector2.Normalize(target.Center - NPC.Center);
                NPC.velocity = dashDir * 14f;
            }
            NPC.velocity *= 0.97f;
        } else {
            // Recovery
            NPC.velocity *= 0.94f;
        }
    }

    public override void FindFrame(int frameHeight) {
        NPC.frameCounter++;
        if (NPC.frameCounter >= 8) {
            NPC.frameCounter = 0;
            NPC.frame.Y += frameHeight;
            if (NPC.frame.Y >= frameHeight * 3) {
                NPC.frame.Y = 0;
            }
        }
    }

    public override void HitEffect(NPC.HitInfo hit) {
        if (NPC.life <= 0 && Main.netMode != NetmodeID.Server) {
            for (int i = 0; i < 8; i++) {
                Vector2 vel = Main.rand.NextVector2CircularEdge(3f, 3f);
                Dust.NewDust(NPC.position, NPC.width, NPC.height, DustID.Blood, vel.X, vel.Y);
            }
        }
    }
}

// RiftParticle: Eyelid rift visual effect
public class RiftParticle : Particle {
    public override Texture2D Texture => ModAsset.WarningRing.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        width = 80;
        height = 80;
        timeLeft = maxTimeLeft = 30;
        drawLayer = ParticleLayer.BeforeProjectiles;
        alpha = 0f;
    }

    public override void AI() {
        float progress = 1f - timeLeft / (float)maxTimeLeft;
        alpha = GuidaUtils.Smoothstep(0f, 0.3f, progress) * GuidaUtils.Smoothstep(1f, 0.7f, progress);
        scale += 0.05f;
        rotation += 0.03f;

        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        Color drawColor = new Color(140, 100, 200) * alpha;
        drawColor.A = 0;

        spriteBatch.Draw(Texture, position - Main.screenPosition, null, drawColor, rotation,
            Texture.Size() * 0.5f, scale, SpriteEffects.None, 0f);

        return false;
    }
}

// WarningLineParticle: Telegraph warning line for GazeDash
public class WarningLineParticle : Particle {
    public float lineRotation;
    public float lineLength;
    public float lineWidth = 60f;
    public float time = 30f;
    public new Color color = Color.Red;

    private float elapsed;

    public override Texture2D Texture => ModAsset.WarningPixel.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        width = 1;
        height = 1;
        drawLayer = ParticleLayer.BeforeProjectiles;
        alpha = 0.7f;
        timeLeft = maxTimeLeft = (int)time;
    }

    public override void AI() {
        elapsed++;
        float progress = elapsed / time;
        alpha = 0.7f * GuidaUtils.Smoothstep(0f, 0.2f, progress) *
            GuidaUtils.Smoothstep(1f, 0.8f, progress);

        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        Vector2 start = position;
        Vector2 end = start + lineRotation.ToRotationVector2() * lineLength;
        Vector2 perpendicular = (lineRotation + MathHelper.PiOver2).ToRotationVector2();

        // Draw as quad
        Vector2 widthOffset = perpendicular * lineWidth * 0.5f;
        Color drawColor = color * alpha;

        spriteBatch.End();
        spriteBatch.Begin(default, BlendState.Additive, SamplerState.PointClamp,
            default, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

        Rectangle rect = new Rectangle(
            (int)(start.X - Main.screenPosition.X - lineWidth * 0.5f),
            (int)(start.Y - Main.screenPosition.Y - lineWidth * 0.5f),
            (int)lineLength,
            (int)lineWidth
        );

        spriteBatch.Draw(Texture, rect, null, drawColor, lineRotation,
            Vector2.Zero, SpriteEffects.None, 0f);

        spriteBatch.End();
        spriteBatch.Begin(default, BlendState.AlphaBlend, Main.DefaultSamplerState,
            default, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

        return false;
    }
}

// SmokeParticle: Generic smoke/mist particle (enhanced version)
public class SmokeParticle : Particle {
    public new Color color = Color.White;
    public float startOpacity = 0.8f;
    public float fadeInTime = 3f;
    public float fadeOutStart = 0.7f;

    public override Texture2D Texture => ModAsset.SmokeDust.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        width = 20;
        height = 20;
        timeLeft = maxTimeLeft = 40;
        drawLayer = ParticleLayer.BeforeProjectiles;
        scale = 1f;
    }

    public override void AI() {
        float progress = 1f - timeLeft / (float)maxTimeLeft;

        alpha = startOpacity *
            GuidaUtils.Smoothstep(0f, fadeInTime / maxTimeLeft, progress) *
            GuidaUtils.Smoothstep(1f, fadeOutStart, progress);

        scale += 0.02f;
        velocity *= 0.96f;
        position += velocity;

        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        Color drawColor = color * alpha;

        spriteBatch.Draw(Texture, position - Main.screenPosition, null, drawColor, rotation,
            Texture.Size() * 0.5f, scale, SpriteEffects.None, 0f);

        return false;
    }
}

// RoarEffectParticle: Expanding ring effect for transitions
public class RoarEffectParticle : Particle {
    public override Texture2D Texture => ModAsset.ExplosionSpread.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        width = 256;
        height = 256;
        timeLeft = maxTimeLeft = 30;
        drawLayer = ParticleLayer.BeforeProjectiles;
        scale = 0.5f;
        alpha = 0f;
    }

    public override void AI() {
        float progress = 1f - timeLeft / (float)maxTimeLeft;

        alpha = 0.8f * GuidaUtils.Smoothstep(0f, 0.2f, progress) *
            GuidaUtils.Smoothstep(1f, 0.6f, progress);

        scale = 0.5f + progress * 2.5f;
        rotation += 0.08f;

        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        spriteBatch.End();
        spriteBatch.Begin(default, BlendState.Additive, SamplerState.PointClamp,
            default, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

        Color drawColor = new Color(180, 140, 255) * alpha;
        drawColor.A = 0;

        spriteBatch.Draw(Texture, position - Main.screenPosition, null, drawColor, rotation,
            Texture.Size() * 0.5f, scale, SpriteEffects.None, 0f);

        spriteBatch.End();
        spriteBatch.Begin(default, BlendState.AlphaBlend, Main.DefaultSamplerState,
            default, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

        return false;
    }
}

// TwistCircleParticle: Screen distortion circle
public class TwistCircleParticle : Particle {
    public float size = 8f;
    public int time = 36;
    public float strength = 0.2f;

    public override Texture2D Texture => ModAsset.TexTwistCircle.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        width = 512;
        height = 512;
        timeLeft = maxTimeLeft = time;
        drawLayer = ParticleLayer.BeforeProjectiles;
        scale = size;
        alpha = strength;
    }

    public override void AI() {
        scale += 0.3f;
        alpha *= 0.92f;

        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        // This would typically apply a screen distortion shader
        // Simplified here as a visual indicator
        return false;
    }
}
