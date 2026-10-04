using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReverieMod.Content.Particles;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using System.IO;
using Terraria.Graphics.CameraModifiers;
using Terraria.Audio;

namespace ReverieMod.Content.KingSlime;


public class KingSlimeFireball : ModProjectile {
    private TrailParticle afterimage;
    public override string Texture => ModAsset.KingSlimeFireball_Mod;

    public override void SetStaticDefaults() {
        Main.projFrames[Type] = 6;
    }

    public override void SetDefaults() {
        Projectile.width = 24;
        Projectile.height = 24;
        Projectile.hostile = true;
        Projectile.penetrate = 1;
        Projectile.timeLeft = 195;
        Projectile.tileCollide = false;
        Projectile.ignoreWater = true;
    }

    public override void AI() {
        Projectile.frameCounter++;
        if (Projectile.frameCounter >= 4) {
            Projectile.frameCounter = 0;
            Projectile.frame = (Projectile.frame + 1) % Main.projFrames[Type];
        }
        // The bright round end leads; the pointed flame trails behind it.
        Projectile.rotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
        float speed = Projectile.velocity.Length();
        if (speed > 0.01f && speed < 18f)
            Projectile.velocity *= Math.Min(1.055f, 18f / speed);
        Lighting.AddLight(Projectile.Center, 0.85f, 0.32f, 0.07f);
        UpdateAfterimage();

        if (Main.netMode == NetmodeID.Server) return;
        // Vanilla Wand of Sparking sparks use torch dust 6, no gravity,
        // enlarged particles, and velocity inherited from the projectile.
        for (int i = 0; i < 2; i++) {
            Vector2 point = Projectile.Center - Projectile.velocity * Main.rand.NextFloat(0.2f, 0.8f) +
                Main.rand.NextVector2Circular(5f, 5f);
            Dust dust = Dust.NewDustPerfect(point, DustID.Torch,
                Projectile.velocity * Main.rand.NextFloat(0.08f, 0.22f) +
                Main.rand.NextVector2Circular(1.6f, 1.6f), 150);
            dust.noGravity = true;
            dust.scale = Main.rand.NextFloat(1.05f, 1.55f);
        }
    }

    private void UpdateAfterimage() {
        if (Main.netMode == NetmodeID.Server) return;
        if (afterimage?.IsAlive != true) {
            afterimage = ParticleManager.Instance?.NewParticle<TrailParticle>(
                Projectile.Center, Vector2.Zero);
            if (afterimage == null) return;
            afterimage.SetUp(8, ModAsset.KingSlimeFireball.Value,
                new Rectangle(0, Projectile.frame * 64, 48, 64),
                BlendState.AlphaBlend, 0.34f, 2.05f);
            afterimage.drawLayer = ParticleLayer.BeforeProjectiles;
            afterimage.color = new Color(255, 160, 85);
            afterimage.externalSamplesOnly = true;
        }
        afterimage.sourceRectangle = new Rectangle(0, Projectile.frame * 64, 48, 64);
        afterimage.position = Projectile.Center;
        afterimage.PushTrailSample(Projectile.Center, Projectile.rotation,
            SpriteEffects.None);
        if (afterimage.trailEnd < 8) afterimage.trailEnd++;
        afterimage.trailStart = 0;
        // Stop sampling on death; the existing afterimages fade on their own.
        afterimage.timeLeft = 9;
    }

    public override void OnHitPlayer(Player target, Player.HurtInfo info) {
        target.AddBuff(BuffID.OnFire, 300);
        if (Main.netMode != NetmodeID.Server)
            SoundEngine.PlaySound(KingSlimeSound.Impact, Projectile.Center);
    }

    public override void OnKill(int timeLeft) {
        if (Main.netMode == NetmodeID.Server) return;
        for (int i = 0; i < 12; i++) {
            Dust dust = Dust.NewDustPerfect(Projectile.Center + Main.rand.NextVector2Circular(6f, 6f),
                DustID.Torch, Main.rand.NextVector2Circular(3.8f, 3.8f), 120);
            dust.noGravity = true;
            dust.scale = Main.rand.NextFloat(1f, 1.55f);
        }
    }

    public override bool PreDraw(ref Color lightColor) {
        Texture2D texture = ModAsset.KingSlimeFireball.Value;
        Rectangle source = new(0, Projectile.frame * 64, 48, 64);
        Vector2 origin = new(24f, 32f);
        Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
        Main.spriteBatch.EndAndBegin(BlendState.Additive);
        Main.spriteBatch.Draw(glow, Projectile.Center - Main.screenPosition, null,
            new Color(255, 118, 32) * 0.55f, 0f, glow.Size() * 0.5f,
            new Vector2(1.15f, 0.72f), SpriteEffects.None, 0f);
        Main.spriteBatch.EndAndBeginDefault();
        Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, source,
            Color.White, Projectile.rotation, origin, 2.05f, SpriteEffects.None);
        return false;
    }
}


public class KingSlimeGrenade : ModProjectile {
    private int bounceCount;
    private int primingDuration = 45;
    private float tempoMultiplier = 1f;
    private TrailParticle afterimage;
    private static Texture2D whiteFlashTexture;
    public static float FlightGravity => 0.4f;
    public static int ExplosionDiameter => 220;
    private static float ExplosionVisualScale => 1.1f;
    public override string Texture => ModAsset.KingSlimeGrenade_Mod;

    public override void Unload() {
        whiteFlashTexture?.Dispose();
        whiteFlashTexture = null;
    }

    public override void SetDefaults() {
        Projectile.width = 18;
        Projectile.height = 26;
        Projectile.scale = 2f;
        Projectile.hostile = true;
        Projectile.penetrate = -1;
        Projectile.timeLeft = 600;
        Projectile.tileCollide = false;
    }

    public override bool? CanDamage() => Projectile.ai[0] == 2f && Projectile.timeLeft >= 5;

    public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
        if (Projectile.ai[0] != 2f) return false;
        Vector2 closest = new(
            MathHelper.Clamp(Projectile.Center.X, targetHitbox.Left, targetHitbox.Right),
            MathHelper.Clamp(Projectile.Center.Y, targetHitbox.Top, targetHitbox.Bottom));
        float radius = ExplosionDiameter * 0.5f;
        return Vector2.DistanceSquared(Projectile.Center, closest) <= radius * radius;
    }

    public override bool TileCollideStyle(ref int width, ref int height, ref bool fallThrough,
        ref Vector2 hitboxCenterFrac) {
        int targetIndex = (int)Projectile.ai[1];
        if (targetIndex >= 0 && targetIndex < Main.maxPlayers) {
            Player target = Main.player[targetIndex];
            // Stop falling through before this step reaches the platform under
            // the player's feet; comparing centers reacts one frame too late.
            fallThrough = target.active && !target.dead &&
                target.Bottom.Y > Projectile.Bottom.Y + Math.Max(Projectile.velocity.Y, 0f) + 2f;
        }
        return true;
    }

    public void SetTempo(float tempo) {
        tempoMultiplier = tempo;
        Projectile.netUpdate = true;
    }

    public override void SendExtraAI(BinaryWriter writer) {
        writer.Write((byte)bounceCount);
        writer.Write(tempoMultiplier);
    }

    public override void ReceiveExtraAI(BinaryReader reader) {
        bounceCount = reader.ReadByte();
        tempoMultiplier = reader.ReadSingle();
    }

    public override void AI() {
        if (Projectile.ai[0] == 2f) {
            Projectile.velocity = Vector2.Zero;
            if (Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                ExplosionVisuals();
            }
            return;
        }

        if (Projectile.ai[0] == 1f) {
            if (Main.netMode != NetmodeID.Server && Projectile.localAI[1] == 0f) {
                Projectile.localAI[1] = 1f;
                SoundEngine.PlaySound(KingSlimeSound.Mechanism, Projectile.Center);
            }
            // The fuse catches in midair: motion fades without any further gravity.
            Projectile.ai[2] += tempoMultiplier;
            Projectile.velocity *= (float)Math.Pow(0.75f, tempoMultiplier);
            if (Projectile.velocity.LengthSquared() < 0.0025f)
                Projectile.velocity = Vector2.Zero;
            Projectile.rotation += Projectile.velocity.X * 0.025f;
            if (Projectile.ai[2] >= primingDuration) Explode();
            return;
        }

        Projectile.ai[2] += tempoMultiplier;
        Projectile.tileCollide = Projectile.ai[2] >= 8f && Projectile.velocity.Y >= 0f;
        Projectile.velocity.X *= (float)Math.Pow(0.999f, tempoMultiplier);
        float previousVerticalSpeed = Projectile.velocity.Y;
        Projectile.velocity.Y = Math.Min(Projectile.velocity.Y +
            FlightGravity * tempoMultiplier * tempoMultiplier, 26f * tempoMultiplier);
        Projectile.rotation += Projectile.velocity.X * 0.036f + 0.045f * tempoMultiplier;
        UpdateAfterimage();
        if (Main.netMode != NetmodeID.Server) {
            Dust smoke = Dust.NewDustPerfect(
                Projectile.Center - Projectile.velocity * 0.65f + Main.rand.NextVector2Circular(6f, 6f),
                DustID.Smoke, -Projectile.velocity * 0.07f + Main.rand.NextVector2Circular(0.4f, 0.4f),
                110, new Color(145, 145, 145), Main.rand.NextFloat(0.7f, 0.95f));
            smoke.noGravity = true;
        }
        if (Main.netMode != NetmodeID.Server && (int)Projectile.ai[2] % 2 == 0) {
            SmokeParticle smoke = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center - Projectile.velocity * 0.8f +
                    Main.rand.NextVector2Circular(5f, 5f),
                -Projectile.velocity * 0.035f + Main.rand.NextVector2Circular(0.45f, 0.45f),
                alpha: 0f, scale: Main.rand.NextFloat(0.4f, 0.65f));
            if (smoke != null) {
                smoke.drawLayer = ParticleLayer.BeforeProjectiles;
                smoke.startOpacity = Main.rand.NextFloat(0.28f, 0.38f);
                smoke.timeLeft = smoke.maxTimeLeft = Main.rand.Next(25, 34);
            }
        }
        if (bounceCount > 0 && previousVerticalSpeed < 0f && Projectile.velocity.Y >= -0.15f)
            StartPriming();
    }

    private void UpdateAfterimage() {
        if (Main.netMode == NetmodeID.Server) return;
        if (afterimage == null || !afterimage.IsAlive) {
            afterimage = ParticleManager.Instance?.NewParticle<TrailParticle>(Projectile.Center,
                Vector2.Zero);
            if (afterimage == null) return;
            Texture2D texture = ModAsset.KingSlimeGrenade.Value;
            afterimage.SetUp(9, texture, texture.Bounds, BlendState.AlphaBlend, 0.22f,
                Projectile.scale);
            afterimage.drawLayer = ParticleLayer.BeforeProjectiles;
            afterimage.color = new Color(210, 219, 215);
            afterimage.externalSamplesOnly = true;
        }
        afterimage.position = Projectile.Center;
        afterimage.PushTrailSample(afterimage.position, Projectile.rotation,
            SpriteEffects.None);
        if (afterimage.trailEnd < 9) afterimage.trailEnd++;
        afterimage.trailStart = 0;
        // Stop sampling after the flight phase; the remaining images fade out themselves.
        afterimage.timeLeft = 10;
    }

    public override bool OnTileCollide(Vector2 oldVelocity) {
        if (Math.Abs(Projectile.velocity.X - oldVelocity.X) > 0.01f)
            Projectile.velocity.X = -oldVelocity.X * 0.55f;
        if (oldVelocity.Y > 0f && Projectile.velocity.Y < oldVelocity.Y - 0.01f) {
            bounceCount = 1;
            if (Main.netMode != NetmodeID.Server)
                SoundEngine.PlaySound(KingSlimeSound.Mechanism, Projectile.Center);
            // Each landing varies the height of the rebound; the fuse is fixed.
            Projectile.velocity.Y = -Main.rand.NextFloat(5.4f, 9.6f) * tempoMultiplier;
            Projectile.velocity.X = MathHelper.Lerp(Projectile.velocity.X * 0.55f,
                Math.Sign(Projectile.velocity.X) * 2.2f * tempoMultiplier, 0.7f);
        }
        Projectile.netUpdate = true;
        return false;
    }

    private void StartPriming() {
        if (Projectile.ai[0] != 0f) return;
        Projectile.ai[0] = 1f;
        Projectile.ai[2] = 0f;
        Projectile.tileCollide = false;
        Projectile.timeLeft = (int)Math.Ceiling(primingDuration / tempoMultiplier) + 8;
        Projectile.netUpdate = true;
    }

    private void Explode() {
        if (Projectile.ai[0] == 2f) return;
        Vector2 center = Projectile.Center;
        KingSlimeMinionDamage.HitNearby(center, ExplosionDiameter * 0.5f,
            Projectile.damage);
        Projectile.ai[0] = 2f;
        Projectile.width = Projectile.height = ExplosionDiameter;
        Projectile.Center = center;
        Projectile.velocity = Vector2.Zero;
        Projectile.tileCollide = false;
        Projectile.timeLeft = 8;
        Projectile.netUpdate = true;
    }

    private void ExplosionVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        Terraria.Audio.SoundEngine.PlaySound(KingSlimeSound.Impact, Projectile.Center);
        Main.instance.CameraModifiers.Add(new PunchCameraModifier(
            Projectile.Center, Main.rand.NextVector2Unit(), 7f, 6f, 13));
        TwistCircleParticle twist = ParticleManager.Instance?.NewParticle<TwistCircleParticle>(
            Projectile.Center, Vector2.Zero);
        if (twist != null) {
            twist.size = 6.5f;
            twist.time = 26;
            twist.strength = 0.16f;
        }
        ParticleManager.Instance?.NewParticle<ExplosionEffectParticle>(
            Projectile.Center, Vector2.Zero,
            scale: ExplosionDiameter / (float)ModAsset.ExplosionSpread.Value.Width *
                0.9f * ExplosionVisualScale);
        for (int i = 0; i < 50; i++) {
            Vector2 direction = Main.rand.NextVector2Unit();
            Dust spark = Dust.NewDustPerfect(
                Projectile.Center + direction * Main.rand.NextFloat(2f, 25f) * ExplosionVisualScale,
                DustID.Firework_Red, direction * Main.rand.NextFloat(2.5f, 5f) * ExplosionVisualScale,
                70, new Color(255, 205, 110),
                Main.rand.NextFloat(0.95f, 1.35f) * ExplosionVisualScale);
            spark.noGravity = Main.rand.NextBool(3);
        }
        int count = 14;
        for (int i = 0; i < count; i++) {
            Vector2 direction = (MathHelper.TwoPi * i / count + Main.rand.NextFloat(-0.2f, 0.2f)).ToRotationVector2();
            SmokeParticle smoke = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center + direction * Main.rand.NextFloat(6f, 18f) * ExplosionVisualScale,
                direction * Main.rand.NextFloat(2.8f, 4.6f) * ExplosionVisualScale,
                alpha: 0f,
                scale: Main.rand.NextFloat(1.2f, 1.65f) * ExplosionVisualScale);
            if (smoke != null) {
                smoke.color = new Color(205, 190, 170);
                smoke.startOpacity = 0.68f;
            }
        }
    }

    public override bool PreDraw(ref Color lightColor) {
        if (Projectile.ai[0] == 2f) return false;
        if (Projectile.ai[0] == 1f) DrawWarningCircle();
        Texture2D texture = ModAsset.KingSlimeGrenade.Value;
        float charge = Projectile.ai[0] == 1f ? MathHelper.Clamp(Projectile.ai[2] / primingDuration, 0f, 1f) : 0f;
        float flash = charge * (0.3f + 0.7f * Math.Abs((float)Math.Sin(Projectile.ai[2] * 0.85f)));
        Vector2 shake = charge > 0f ? Main.rand.NextVector2Circular(3.2f, 3.2f) * charge : Vector2.Zero;
        Vector2 drawPosition = Projectile.Center - Main.screenPosition + shake;
        Main.EntitySpriteDraw(texture, drawPosition, null,
            lightColor, Projectile.rotation, texture.Size() * 0.5f,
            Projectile.scale, SpriteEffects.None);
        if (flash > 0f) {
            if (whiteFlashTexture == null || whiteFlashTexture.IsDisposed) {
                Color[] pixels = new Color[texture.Width * texture.Height];
                texture.GetData(pixels);
                for (int i = 0; i < pixels.Length; i++) {
                    byte opacity = pixels[i].A;
                    pixels[i] = new Color(opacity, opacity, opacity, opacity);
                }
                whiteFlashTexture = new Texture2D(Main.graphics.GraphicsDevice,
                    texture.Width, texture.Height);
                whiteFlashTexture.SetData(pixels);
            }
            Main.EntitySpriteDraw(whiteFlashTexture, drawPosition, null,
                Color.White * flash, Projectile.rotation, texture.Size() * 0.5f,
                Projectile.scale, SpriteEffects.None);
        }
        return false;
    }

    private void DrawWarningCircle() {
        float charge = MathHelper.Clamp(Projectile.ai[2] / primingDuration, 0f, 1f);
        float fade = GuidaUtils.Smoothstep(0f, 0.3f, charge);
        float pulse = 1f + (float)Math.Sin(Projectile.ai[2] * 0.65f) * 0.15f * charge;
        Vector2 center = Projectile.Center - Main.screenPosition;
        Texture2D circle = ModAsset.SoftCircle.Value;
        Texture2D ring = ModAsset.WarningRing.Value;
        float radiusScale = ExplosionDiameter / (float)circle.Width * ExplosionVisualScale;

        Main.spriteBatch.EndAndBegin(BlendState.AlphaBlend);
        Main.spriteBatch.Draw(circle, center, null,
            new Color(255, 72, 48) * (0.20f * fade * pulse), 0f,
            circle.Size() * 0.5f, radiusScale, SpriteEffects.None, 0f);
        Main.spriteBatch.EndAndBegin(BlendState.Additive);
        Main.spriteBatch.Draw(ring, center, null,
            new Color(255, 88, 58) * (0.68f * fade * pulse),
            Projectile.ai[2] * 0.025f, ring.Size() * 0.5f,
            radiusScale, SpriteEffects.None, 0f);
        Main.spriteBatch.EndAndBeginDefault();
    }
}


public class KingSlimeShuriken : ModProjectile {
    private TrailParticle afterimage;
    private float tempoMultiplier = 1f;
    public static int WarningDuration => 70;

    public override string Texture => ModAsset.KingSlimeShuriken_Mod;

    public override void SetDefaults() {
        Projectile.width = 16;
        Projectile.height = 16;
        Projectile.scale = 2.5f;
        Projectile.hostile = true;
        Projectile.penetrate = 1;
        Projectile.timeLeft = 230;
        Projectile.tileCollide = false;
    }

    public override bool? CanDamage() => Projectile.ai[0] == 2f;

    public void SetTempo(float tempo) {
        tempoMultiplier = tempo;
        Projectile.timeLeft = (int)Math.Ceiling(230f / tempo);
        Projectile.velocity *= 1.08f;
        Projectile.netUpdate = true;
    }

    public override void SendExtraAI(BinaryWriter writer) => writer.Write(tempoMultiplier);

    public override void ReceiveExtraAI(BinaryReader reader) => tempoMultiplier = reader.ReadSingle();

    public override void AI() {
        Vector2 direction = Projectile.ai[1].ToRotationVector2();
        if (Projectile.ai[0] == 0f) {
            // A short decelerating throw places each blade around the hovering boss.
            Projectile.ai[2] += tempoMultiplier;
            Projectile.velocity *= (float)Math.Pow(0.82f, tempoMultiplier);
            Projectile.rotation += 0.37f * tempoMultiplier;
            if (Projectile.ai[2] >= 12f) {
                Projectile.ai[0] = 1f;
                Projectile.ai[2] = 0f;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
            }
            return;
        }

        if (Projectile.ai[0] == 1f) {
            Projectile.ai[2] += tempoMultiplier;
            Projectile.velocity = Vector2.Zero;
            Projectile.rotation += 0.16f * tempoMultiplier *
                (1f - Projectile.ai[2] / WarningDuration);
            if (Main.netMode != NetmodeID.Server && Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                PixelWarningLineParticle line = ParticleManager.Instance?.NewParticle<PixelWarningLineParticle>(
                    Projectile.Center, Vector2.Zero);
                if (line != null) {
                    line.rotation = Projectile.ai[1];
                    line.lineLength = 1500f;
                    line.lineWidth = 4.2f;
                    line.opacity = 0.37f;
                    line.timeLeft = line.maxTimeLeft =
                        (int)Math.Ceiling(WarningDuration / tempoMultiplier);
                }
            }
            if (Projectile.ai[2] >= WarningDuration) {
                Projectile.ai[0] = 2f;
                Projectile.ai[2] = 0f;
                if (Main.netMode != NetmodeID.Server)
                    SoundEngine.PlaySound(KingSlimeSound.ItemUse, Projectile.Center);
                Projectile.netUpdate = true;
            }
            return;
        }

        Projectile.ai[2] += tempoMultiplier;
        // The warned flight path stays valid through walls and platforms.
        Projectile.velocity = direction * MathHelper.Lerp(5.95f, 25.4f,
            GuidaUtils.Smoothstep(0f, 9f, Projectile.ai[2])) * tempoMultiplier;
        Projectile.rotation += 0.52f * tempoMultiplier;
        UpdateAfterimage();
        if (Projectile.ai[2] >= 104f) Projectile.Kill();
    }

    private void UpdateAfterimage() {
        if (Main.netMode == NetmodeID.Server) return;
        if (afterimage == null || !afterimage.IsAlive) {
            afterimage = ParticleManager.Instance?.NewParticle<TrailParticle>(Projectile.Center,
                Vector2.Zero);
            if (afterimage == null) return;
            Texture2D texture = ModAsset.KingSlimeShuriken.Value;
            afterimage.SetUp(9, texture, texture.Bounds, BlendState.AlphaBlend, 0.42f,
                Projectile.scale);
            afterimage.drawLayer = ParticleLayer.BeforeProjectiles;
            afterimage.color = Color.White;
            afterimage.trailAfterImage = 1f;
            afterimage.externalSamplesOnly = true;
        }
        afterimage.position = Projectile.Center;
        afterimage.PushTrailSample(afterimage.position, Projectile.rotation,
            SpriteEffects.None);
        if (afterimage.trailEnd < 9) afterimage.trailEnd++;
        afterimage.trailStart = 0;
        // Stop feeding samples when the blade dies; the trail expires on its own.
        afterimage.timeLeft = 10;
    }

    public override bool PreDraw(ref Color lightColor) {
        Texture2D texture = ModAsset.KingSlimeShuriken.Value;
        Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null,
            lightColor, Projectile.rotation, texture.Size() * 0.5f,
            Projectile.scale, SpriteEffects.None);
        return false;
    }
}


// The same launched projectile handles slowing, hovering, and returning.
public class KingSlimeBoomerang : ModProjectile {
    private TrailParticle trail;
    private float tempoMultiplier = 1f;
    private int throwerNpcIndex = -1;
    public static float TravelDistance => 900f;

    public override string Texture => ModAsset.KingSlimeBoomerang_Mod;

    public override void SetDefaults() {
        Projectile.width = 26;
        Projectile.height = 26;
        Projectile.scale = 2.8f;
        Projectile.hostile = true;
        Projectile.penetrate = -1;
        Projectile.timeLeft = 176;
        Projectile.tileCollide = false;
        Projectile.ignoreWater = true;
        Projectile.netImportant = true;
    }

    public void SetTempo(float tempo, int throwerIndex) {
        tempoMultiplier = tempo;
        throwerNpcIndex = throwerIndex;
        Projectile.timeLeft = (int)Math.Ceiling(Projectile.timeLeft / tempo);
        Projectile.netUpdate = true;
    }

    public override void SendExtraAI(BinaryWriter writer) {
        writer.Write(tempoMultiplier);
        writer.Write((short)throwerNpcIndex);
    }

    public override void ReceiveExtraAI(BinaryReader reader) {
        tempoMultiplier = reader.ReadSingle();
        throwerNpcIndex = reader.ReadInt16();
    }

    public override void AI() {
        float age = Projectile.localAI[0] += tempoMultiplier;
        Vector2 launch = new(Projectile.ai[0], Projectile.ai[1]);
        Vector2 direction = Projectile.ai[2].ToRotationVector2();
        float outward = MathHelper.Clamp(age / 76f, 0f, 1f);
        float returnProgress = GuidaUtils.Smoothstep(84f, 156f, age);
        float remaining = 1f - outward;
        float distance = TravelDistance * (1f - remaining * remaining) *
            (1f - returnProgress);
        if (returnProgress >= 1f) {
            Projectile.Center = launch;
            Projectile.Kill();
            return;
        }
        Vector2 next = launch + direction * distance;
        if (Main.netMode != NetmodeID.MultiplayerClient && returnProgress > 0f &&
            throwerNpcIndex >= 0 && throwerNpcIndex < Main.maxNPCs) {
            NPC thrower = Main.npc[throwerNpcIndex];
            if (thrower.active && thrower.ModNPC is KingSlime) {
                Rectangle nextHitbox = new((int)(next.X - Projectile.width * 0.5f),
                    (int)(next.Y - Projectile.height * 0.5f), Projectile.width,
                    Projectile.height);
                if (thrower.Hitbox.Intersects(Rectangle.Union(Projectile.Hitbox, nextHitbox))) {
                    Projectile.Kill();
                    return;
                }
            }
        }
        Projectile.velocity = next - Projectile.Center;
        Projectile.rotation += 0.36f * tempoMultiplier;
        UpdateTrail();
    }

    private void UpdateTrail() {
        if (Main.netMode == NetmodeID.Server) return;
        if (trail?.IsAlive != true) {
            trail = ParticleManager.Instance?.NewParticle<TrailParticle>(Projectile.Center,
                Vector2.Zero);
            if (trail == null) return;
            Texture2D texture = ModAsset.KingSlimeBoomerang.Value;
            trail.SetUp(9, texture, texture.Bounds, BlendState.AlphaBlend,
                0.42f, Projectile.scale);
            trail.drawLayer = ParticleLayer.BeforeProjectiles;
            trail.color = Color.White;
            trail.externalSamplesOnly = true;
        }
        trail.position = Projectile.Center;
        trail.PushTrailSample(Projectile.Center, Projectile.rotation, SpriteEffects.None);
        if (trail.trailEnd < 9) trail.trailEnd++;
        trail.trailStart = 0;
        // Stop sampling at the launch point; the existing trail fades on its own.
        trail.timeLeft = 10;
    }

    public override bool PreDraw(ref Color lightColor) {
        Texture2D texture = ModAsset.KingSlimeBoomerang.Value;
        Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null,
            lightColor, Projectile.rotation, texture.Size() * 0.5f,
            Projectile.scale, SpriteEffects.None);
        return false;
    }
}


// Invisible synchronized timer; its client draws a particle warning, then the server spawns a slime.
public class KingSlimeDropSpawner : ModProjectile {
    public override string Texture => ModAsset.WarningPixel_Mod;

    public override void SetDefaults() {
        Projectile.width = Projectile.height = 2;
        Projectile.timeLeft = 52;
        Projectile.tileCollide = false;
        Projectile.netImportant = true;
    }

    public override bool? CanDamage() => false;

    public override void AI() {
        if (Main.netMode == NetmodeID.Server || Projectile.localAI[0] != 0f) return;
        Projectile.localAI[0] = 1f;
        float warningLength = Projectile.ai[1] + 400f;
        WarningLineParticle line = ParticleManager.Instance?.NewParticle<WarningLineParticle>(
            Projectile.Center - Vector2.UnitY * warningLength + Vector2.UnitY * 36f,
            Vector2.Zero);
        if (line == null) return;
        line.lineRotation = MathHelper.Pi;
        line.lineLength = warningLength;
        line.lineWidth = 28f;
        line.alpha = 0.88f;
        line.smaller = true;
        line.color = (int)Projectile.ai[2] switch {
            NPCID.GreenSlime => new Color(114, 226, 135),
            NPCID.RedSlime => new Color(255, 115, 120),
            _ => new Color(132, 213, 255)
        };
        // Keep the warned column briefly visible as the slime begins its fall.
        line.time = Projectile.timeLeft + 18f;
    }

    public override void OnKill(int timeLeft) {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        Vector2 spawn = Projectile.Center - Vector2.UnitY * Projectile.ai[1];
        int index = NPC.NewNPC(Projectile.GetSource_Death(), (int)spawn.X, (int)spawn.Y,
            (int)Projectile.ai[2]);
        if (index < 0 || index >= Main.maxNPCs) return;
        NPC slime = Main.npc[index];
        slime.Center = spawn;
        slime.velocity = Vector2.UnitY * 6f;
        slime.target = (int)Projectile.ai[0];
        slime.GetGlobalNPC<KingSlimeFallingSlimeGlobalNPC>().BeginDrop(
            Projectile.Center.Y, spawn.X);
        slime.netUpdate = true;
    }

    public override bool PreDraw(ref Color lightColor) => false;
}


// Invisible network carrier for the ultimate's harmless impact effects.
public class KingSlimeUltimateImpactEffect : ModProjectile {
    public override string Texture => ModAsset.WarningPixel_Mod;

    public override void SetDefaults() {
        Projectile.width = Projectile.height = 2;
        Projectile.hostile = false;
        Projectile.friendly = false;
        Projectile.tileCollide = false;
        Projectile.ignoreWater = true;
        Projectile.penetrate = -1;
        Projectile.timeLeft = 8;
    }

    public override bool? CanDamage() => false;

    public override void AI() {
        Projectile.velocity = Vector2.Zero;
        if (Projectile.localAI[0] != 0f) return;
        Projectile.localAI[0] = 1f;
        if (Main.netMode == NetmodeID.Server) return;
        SoundEngine.PlaySound(KingSlimeSound.Impact, Projectile.Center);
        Main.instance.CameraModifiers.Add(new PunchCameraModifier(
            Projectile.Center, -Vector2.UnitY, 19f, 12f, 26));
        ParticleManager.Instance?.NewParticle<ExplosionEffectParticle>(
            Projectile.Center, Vector2.Zero,
            scale: 720f / ModAsset.ExplosionSpread.Value.Width * 1.15f);
        TwistCircleParticle twist = ParticleManager.Instance?.NewParticle<TwistCircleParticle>(
            Projectile.Center, Vector2.Zero);
        if (twist != null) {
            twist.size = 18f;
            twist.time = 46;
            twist.strength = 0.38f;
        }
        for (int i = 0; i < 90; i++) {
            Vector2 direction = Main.rand.NextVector2Unit();
            Dust spark = Dust.NewDustPerfect(Projectile.Center + direction *
                Main.rand.NextFloat(5f, 80f), DustID.Firework_Red,
                direction * Main.rand.NextFloat(5f, 14f), 70,
                new Color(255, 205, 110), Main.rand.NextFloat(1.1f, 1.8f));
            spark.noGravity = Main.rand.NextBool(2);
        }
        for (int i = 0; i < 32; i++) {
            Vector2 direction = Main.rand.NextVector2Unit();
            SmokeParticle smoke = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                Projectile.Center + direction * Main.rand.NextFloat(10f, 95f),
                direction * Main.rand.NextFloat(3f, 9f), alpha: 0f,
                scale: Main.rand.NextFloat(2f, 3.4f));
            if (smoke == null) continue;
            smoke.color = new Color(205, 190, 170);
            smoke.startOpacity = 0.7f;
        }
    }

    public override bool PreDraw(ref Color lightColor) => false;
}
