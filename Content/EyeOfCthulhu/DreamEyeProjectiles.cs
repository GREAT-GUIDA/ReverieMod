using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using ReverieMod.Content.Particles;

namespace ReverieMod.Content.EyeOfCthulhu;

// DreamTear: Arcing tear projectile that falls and creates blood puddle on impact
public class DreamTear : ModProjectile {
    public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.WaterStream;

    private bool willSplit => Projectile.ai[0] == 1f;

    public override void SetDefaults() {
        Projectile.width = 14;
        Projectile.height = 14;
        Projectile.friendly = false;
        Projectile.hostile = true;
        Projectile.penetrate = 1;
        Projectile.timeLeft = 300;
        Projectile.tileCollide = true;
        Projectile.ignoreWater = false;
    }

    public override void AI() {
        Projectile.velocity.Y += 0.25f; // Gravity
        if (Projectile.velocity.Y > 16f) Projectile.velocity.Y = 16f;

        Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

        // Trail particles
        if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(3)) {
            SmokeParticle smoke = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center, Projectile.velocity * -0.3f, scale: 0.3f);
            if (smoke != null) {
                smoke.color = new Color(200, 80, 80);
                smoke.startOpacity = 0.5f;
            }
        }
    }

    public override void OnKill(int timeLeft) {
        if (Main.netMode == NetmodeID.Server) return;

        // Blood splash
        for (int i = 0; i < 6; i++) {
            Vector2 vel = Main.rand.NextVector2CircularEdge(3f, 3f);
            SmokeParticle splash = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center, vel, scale: Main.rand.NextFloat(0.4f, 0.7f));
            if (splash != null) {
                splash.color = new Color(180, 60, 60);
                splash.startOpacity = 0.7f;
            }
        }

        SoundEngine.PlaySound(SoundID.NPCHit9, Projectile.Center);

        // Split into smaller tears
        if (willSplit && Main.netMode != NetmodeID.MultiplayerClient) {
            for (int i = 0; i < 3; i++) {
                Vector2 splitVel = new Vector2(Main.rand.NextFloat(-4f, 4f), Main.rand.NextFloat(-3f, 1f));
                Projectile.NewProjectile(Projectile.GetSource_Death(), Projectile.Center, splitVel,
                    Type, Projectile.damage / 2, Projectile.knockBack * 0.5f, Projectile.owner);
            }
        }
    }

    public override bool PreDraw(ref Color lightColor) {
        Texture2D texture = ModAsset.SoftCircle.Value;
        Color drawColor = new Color(200, 80, 80) * 0.9f;

        Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null,
            drawColor, Projectile.rotation, texture.Size() * 0.5f,
            Projectile.scale * (willSplit ? 1.5f : 1f), SpriteEffects.None);

        return false;
    }
}

// CurvingShard: Rotating iris shard that curves in flight
public class CurvingShard : ModProjectile {
    public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.CrystalShard;

    private float spinDirection => Projectile.ai[0]; // 1 or -1

    public override void SetDefaults() {
        Projectile.width = 14;
        Projectile.height = 14;
        Projectile.friendly = false;
        Projectile.hostile = true;
        Projectile.penetrate = 1;
        Projectile.timeLeft = 180;
        Projectile.tileCollide = true;
        Projectile.alpha = 50;
    }

    public override void AI() {
        Projectile.rotation += 0.3f;

        // Curve trajectory
        if (spinDirection != 0) {
            float curveStrength = 0.15f;
            Vector2 perpendicular = Projectile.velocity.RotatedBy(MathHelper.PiOver2 * spinDirection);
            Projectile.velocity += Vector2.Normalize(perpendicular) * curveStrength;
        }

        // Speed decay
        Projectile.velocity *= 0.995f;

        // Sparkle trail
        if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(4)) {
            SmokeParticle sparkle = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center, Vector2.Zero, scale: 0.25f);
            if (sparkle != null) {
                sparkle.color = new Color(100, 180, 255);
                sparkle.startOpacity = 0.6f;
            }
        }
    }

    public override void OnKill(int timeLeft) {
        if (Main.netMode == NetmodeID.Server) return;

        for (int i = 0; i < 4; i++) {
            Vector2 vel = Main.rand.NextVector2CircularEdge(2f, 2f);
            SmokeParticle particle = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center, vel, scale: 0.3f);
            if (particle != null) {
                particle.color = new Color(120, 200, 255);
            }
        }

        SoundEngine.PlaySound(SoundID.Item27, Projectile.Center);
    }

    public override Color? GetAlpha(Color lightColor) {
        return new Color(150, 200, 255, 200);
    }
}

// BloodGlob: Spinning blood projectile
public class BloodGlob : ModProjectile {
    public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.WaterStream;

    public override void SetDefaults() {
        Projectile.width = 16;
        Projectile.height = 16;
        Projectile.friendly = false;
        Projectile.hostile = true;
        Projectile.penetrate = 1;
        Projectile.timeLeft = 240;
        Projectile.tileCollide = true;
    }

    public override void AI() {
        Projectile.rotation += 0.2f;

        Projectile.velocity *= 0.99f;

        if (Projectile.velocity.Y < 10f) {
            Projectile.velocity.Y += 0.15f;
        }

        if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(3)) {
            SmokeParticle drip = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center, Projectile.velocity * -0.2f, scale: 0.25f);
            if (drip != null) {
                drip.color = new Color(180, 50, 50);
                drip.startOpacity = 0.6f;
            }
        }
    }

    public override void OnKill(int timeLeft) {
        if (Main.netMode == NetmodeID.Server) return;

        for (int i = 0; i < 5; i++) {
            Vector2 vel = Main.rand.NextVector2CircularEdge(2.5f, 2.5f);
            SmokeParticle splash = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center, vel, scale: Main.rand.NextFloat(0.3f, 0.6f));
            if (splash != null) {
                splash.color = new Color(160, 40, 40);
            }
        }

        SoundEngine.PlaySound(SoundID.NPCHit9, Projectile.Center);
    }

    public override bool PreDraw(ref Color lightColor) {
        Texture2D texture = ModAsset.SoftCircle.Value;
        Color drawColor = new Color(180, 60, 60) * 0.85f;

        Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null,
            drawColor, Projectile.rotation, texture.Size() * 0.5f,
            Projectile.scale * 1.2f, SpriteEffects.None);

        return false;
    }
}

// ToothProjectile: Sharp tooth spit from maw
public class ToothProjectile : ModProjectile {
    public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.Bone;

    public override void SetDefaults() {
        Projectile.width = 12;
        Projectile.height = 12;
        Projectile.friendly = false;
        Projectile.hostile = true;
        Projectile.penetrate = 1;
        Projectile.timeLeft = 180;
        Projectile.tileCollide = true;
    }

    public override void AI() {
        Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

        Projectile.velocity.Y += 0.2f;
        if (Projectile.velocity.Y > 12f) Projectile.velocity.Y = 12f;
    }

    public override void OnKill(int timeLeft) {
        if (Main.netMode == NetmodeID.Server) return;

        for (int i = 0; i < 3; i++) {
            Vector2 vel = Main.rand.NextVector2CircularEdge(2f, 2f);
            SmokeParticle particle = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center, vel, scale: 0.2f);
            if (particle != null) {
                particle.color = new Color(220, 200, 180);
            }
        }

        SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
    }

    public override Color? GetAlpha(Color lightColor) {
        return new Color(255, 230, 200, 200);
    }
}

// AmbushRift: Eyelid rift that either shoots needles or bursts out with boss
public class AmbushRift : ModProjectile {
    public override string Texture => ModAsset.WarningRing_Mod;

    private bool isRealRift => Projectile.ai[0] == 1f;
    private ref float Timer => ref Projectile.ai[1];

    public override void SetDefaults() {
        Projectile.width = 80;
        Projectile.height = 80;
        Projectile.friendly = false;
        Projectile.hostile = false;
        Projectile.penetrate = -1;
        Projectile.timeLeft = 150;
        Projectile.tileCollide = false;
        Projectile.ignoreWater = true;
        Projectile.alpha = 255;
    }

    public override void AI() {
        Timer++;

        // Fade in
        if (Timer < 30) {
            Projectile.alpha = (int)(255 * (1f - Timer / 30f));
        }

        Projectile.rotation += 0.02f;

        // Fake rifts shoot needles at Timer=60
        if (!isRealRift && Timer == 60f) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                Player target = Main.player[Main.myPlayer]; // TODO: proper targeting
                for (int i = 0; i < 8; i++) {
                    float angle = MathHelper.TwoPi * i / 8f;
                    Vector2 vel = angle.ToRotationVector2() * 8f;

                    Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, vel,
                        ProjectileID.PinkLaser, 20, 1f, Projectile.owner);
                }
            }

            if (Main.netMode != NetmodeID.Server) {
                SoundEngine.PlaySound(SoundID.Item12, Projectile.Center);
            }
        }

        // Real rift bursts at Timer=60 (boss teleports here externally)
        if (isRealRift && Timer == 60f) {
            if (Main.netMode != NetmodeID.Server) {
                // Burst visual
                for (int i = 0; i < 12; i++) {
                    Vector2 vel = Main.rand.NextVector2CircularEdge(4f, 4f);
                    SmokeParticle particle = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                        Projectile.Center, vel, scale: Main.rand.NextFloat(0.6f, 1.2f));
                    if (particle != null) {
                        particle.color = new Color(120, 80, 180);
                    }
                }

                SoundEngine.PlaySound(SoundID.Roar, Projectile.Center);
            }
        }

        if (Timer >= 90) {
            Projectile.Kill();
        }
    }

    public override bool PreDraw(ref Color lightColor) {
        Texture2D texture = ModContent.Request<Texture2D>(Texture).Value;
        Color drawColor = isRealRift
            ? new Color(180, 100, 200, 255 - Projectile.alpha) * 0.7f
            : new Color(200, 100, 100, 255 - Projectile.alpha) * 0.6f;

        float pulse = 1f + 0.1f * (float)Math.Sin(Timer * 0.15f);

        Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null,
            drawColor, Projectile.rotation, texture.Size() * 0.5f,
            Projectile.scale * pulse, SpriteEffects.None);

        return false;
    }
}

// WatcherEye: Stationary eye that fires gaze beam after delay
public class WatcherEye : ModProjectile {
    public override string Texture => "Terraria/Images/NPC_2"; // Demon Eye texture as placeholder

    private float orbitAngle => Projectile.ai[0];
    private ref float FireDelay => ref Projectile.ai[1];
    private ref float Timer => ref Projectile.localAI[0];

    public override void SetDefaults() {
        Projectile.width = 32;
        Projectile.height = 32;
        Projectile.friendly = false;
        Projectile.hostile = false;
        Projectile.penetrate = -1;
        Projectile.timeLeft = 300;
        Projectile.tileCollide = false;
        Projectile.alpha = 255;
    }

    public override void AI() {
        Timer++;

        // Fade in
        if (Timer < 20) {
            Projectile.alpha = (int)(255 * (1f - Timer / 20f));
        }

        // Rotate to face center
        Projectile.rotation = orbitAngle + MathHelper.Pi;

        // Fire gaze beam after delay
        if (Timer == FireDelay && Main.netMode != NetmodeID.MultiplayerClient) {
            Vector2 beamDir = (-orbitAngle).ToRotationVector2();
            Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, beamDir * 0.1f,
                ProjectileID.EyeBeam, 24, 2f, Projectile.owner);

            if (Main.netMode != NetmodeID.Server) {
                SoundEngine.PlaySound(SoundID.Item12, Projectile.Center);
            }
        }

        if (Timer >= 180) {
            Projectile.alpha += 10;
            if (Projectile.alpha >= 255) Projectile.Kill();
        }
    }

    public override Color? GetAlpha(Color lightColor) {
        return new Color(255, 255, 255, 255 - Projectile.alpha);
    }
}

// DreamBeam: Sweeping deathray from boss mouth
public class DreamBeam : ModProjectile {
    public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.DeathLaser;

    private ref float Timer => ref Projectile.ai[0];

    public override void SetDefaults() {
        Projectile.width = 20;
        Projectile.height = 20;
        Projectile.friendly = false;
        Projectile.hostile = true;
        Projectile.penetrate = -1;
        Projectile.timeLeft = 120;
        Projectile.tileCollide = false;
        Projectile.alpha = 255;
    }

    public override void AI() {
        Timer++;

        // Fade in
        if (Timer < 10) {
            Projectile.alpha = (int)(255 * (1f - Timer / 10f));
        }

        // Sweep rotation
        Projectile.velocity = Projectile.velocity.RotatedBy(0.015f * Math.Sign(Projectile.velocity.X));

        // Extend length
        Projectile.scale = Math.Min(Projectile.scale + 0.1f, 3f);

        if (Timer >= 100) {
            Projectile.alpha += 20;
            if (Projectile.alpha >= 255) Projectile.Kill();
        }
    }

    public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
        // Laser beam collision (simplified)
        float point = 0f;
        return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
            Projectile.Center, Projectile.Center + Projectile.velocity * 600f, 22f, ref point);
    }

    public override bool PreDraw(ref Color lightColor) {
        // Beam rendering would use custom shader/primitive drawing
        // Simplified here with line segments
        return true;
    }
}
