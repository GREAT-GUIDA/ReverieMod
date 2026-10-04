using System;
using GuidaSharedCode;
using ReverieMod.Content.Particles;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;

namespace ReverieMod.Content.KingSlime;

public partial class KingSlime {
    private void HammerSlam(Player target) {
        if (Timer < 12f || PassedTime(12f)) {
            NPC.velocity.X *= grounded ? 0.74f : 0.94f;
            if (!grounded) Timer = Math.Min(Timer, 4f);
            if (PassedTime(12f)) {
                float dx = target.Center.X + target.velocity.X * 5f - NPC.Center.X;
                float horizontal = MathHelper.Clamp(dx / 36f, -11.2f, 11.2f);
                LaunchJump(horizontal, HammerJumpSpeed(target), heavy: true);
            }
            return;
        }

        if (Timer < 60f || (MoveValue == 0f && NPC.localAI[0] == 1f && !grounded)) {
            NPC.velocity.X *= 0.992f;
            if (grounded && Timer > 16f) {
                Timer = 109f;
                NPC.localAI[0] = 0f;
                NPC.velocity = Vector2.Zero;
                return;
            }
            if (Timer > 16f && NPC.velocity.Y >= -0.6f) {
                MoveValue = target.Center.X >= NPC.Center.X ? 1f : -1f;
                Timer = 60f;
                NPC.velocity = Vector2.Zero;
                motionMode = MotionMode.Controlled;
                NPC.netUpdate = true;
            }
            else if (Timer >= 59f) Timer = 59f;
            return;
        }

        if (Timer < 81f) {
            motionMode = MotionMode.Controlled;
            NPC.velocity = Vector2.Zero;
            if (PassedTime(63f))
                SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
            return;
        }

        // A distant fall can hold the timer at 108 while airborne. Test the
        // landing before the next tick advances it past the dive branch.
        if (Timer > 85f && LandedFromAir()) {
            NPC.localAI[0] = 0f;
            NPC.velocity = Vector2.Zero;
            Timer = 109f;
            NPC.netUpdate = true;
            Land(true);
            SpawnHammerImpact();
        }
        if (Timer >= 109f && !grounded && NPC.localAI[0] == 1f) Timer = 108f;
        if (Timer < 109f) {
            motionMode = MotionMode.Dive;
            if (PassedTime(81f)) {
                NPC.velocity = new Vector2(0f, 5f);
                NPC.netUpdate = true;
                SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
            }
            NPC.velocity.X *= 0.82f;
            NPC.velocity.Y = Math.Min(NPC.velocity.Y + 2.6f, 30f);
            if (Timer >= 108f && !grounded) Timer = 108f;
            return;
        }

        NPC.velocity.X *= grounded ? 0.72f : 0.95f;
        if (PassedTime(120f)) ReleaseHammer();
        if (Timer >= 109f && SlotFinished(135f) && grounded)
            AdvancePattern(target);
    }

    private float HammerJumpSpeed(Player target) => 18.1f + MathHelper.Clamp(
        (NPC.Center.Y - target.Top.Y - 70f) * 0.015f, 0f, 3.5f);

    private float PredictHammerRise(Player target, Vector2? origin = null) {
        float startY = (origin ?? NPC.Center).Y;
        float speed = 18.1f + MathHelper.Clamp(
            (startY - target.Top.Y - 70f) * 0.015f, 0f, 3.5f);
        float vertical = JumpImpulse(speed);
        float rise = 0f;
        for (int i = 0; i < 90 && vertical < 0f; i++) {
            float nearApex = 1f - MathHelper.Clamp(Math.Abs(vertical) / 4.4f, 0f, 1f);
            vertical += 0.45f * (1f - 0.78f * nearApex);
            if (vertical < 0f) rise -= vertical;
        }
        return rise;
    }

    private void SpawnHammerImpact(bool damageMinions = true) {
        if (damageMinions)
            KingSlimeMinionDamage.HitNearby(NPC.Bottom, 175f, ContactDamage(50));
        BounceStandingPlayers();
        if (Main.netMode == NetmodeID.Server) return;
        ParticleManager.Instance?.NewParticle<GroundCrackParticle>(
            NPC.Bottom + Vector2.UnitY * 10f, Vector2.Zero);
        Main.instance.CameraModifiers.Add(new PunchCameraModifier(
            NPC.Bottom, -Vector2.UnitY, 11f, 8f, 16));
        TwistCircleParticle twist = ParticleManager.Instance?.NewParticle<TwistCircleParticle>(
            NPC.Bottom, Vector2.Zero);
        if (twist != null) {
            twist.size = 10f;
            twist.time = 35;
            twist.strength = 0.24f;
        }
        for (int i = 0; i < 18; i++) {
            float sideways = Main.rand.NextFloat(-NPC.width * 1.25f, NPC.width * 1.25f);
            SmokeParticle smoke = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                NPC.Bottom + new Vector2(sideways, Main.rand.NextFloat(-6f, 3f)),
                new Vector2(Math.Sign(sideways) * Main.rand.NextFloat(1.4f, 5.2f),
                    -Main.rand.NextFloat(1.3f, 4.2f)),
                alpha: 0f, scale: Main.rand.NextFloat(1.15f, 2.2f) * NPC.scale);
            if (smoke == null) continue;
            smoke.drawLayer = ParticleLayer.BeforeProjectiles;
            smoke.startOpacity = Main.rand.NextFloat(0.52f, 0.76f);
            smoke.timeLeft = smoke.maxTimeLeft = Main.rand.Next(35, 52);
            smoke.rotation = Main.rand.NextFloat(-0.4f, 0.4f);
        }
    }

    private void BounceStandingPlayers() {
        if (Main.netMode == NetmodeID.MultiplayerClient) {
            Player localPlayer = Main.LocalPlayer;
            if (localPlayer.active && !localPlayer.dead && localPlayer.velocity.Y == 0f)
                localPlayer.velocity.Y = -6.5f;
            return;
        }
        for (int i = 0; i < Main.maxPlayers; i++) {
            Player player = Main.player[i];
            if (player.active && !player.dead && player.velocity.Y == 0f)
                player.velocity.Y = -6.5f;
        }
    }

    private void ReleaseHammer() {
        if (hammerParticle?.IsAlive == true && hammerParticle.held)
            hammerParticle.Release(new Vector2((MoveValue == 0f ? 1f : MoveValue) * 5f, -4f));
        hammerParticle = null;
    }

    private void UpdateHammerVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        if (CurrentMove == Move.Ultimate) {
            UpdateUltimateHammerVisuals();
            return;
        }
        UpdateHammerWarning();
        bool teleport = CurrentMove == Move.TeleportHammerSlam;
        if ((CurrentMove != Move.HammerSlam && !teleport) || Timer >= 120f) {
            ReleaseHammer();
            return;
        }
        float visualTime = Timer;
        if (visualTime < 60f) return;
        if (hammerParticle?.IsAlive != true) {
            hammerParticle = ParticleManager.Instance?.NewParticle<KingSlimeHammerParticle>(
                NPC.Center, Vector2.Zero, alpha: 0f);
            if (hammerParticle == null) return;
            hammerParticle.SetHolder(NPC);
        }

        float lift = GuidaUtils.Smoothstep(60f, 76f, visualTime);
        float swing = GuidaUtils.Smoothstep(81f, 99f, visualTime);
        if (teleport && teleportOccurred && grounded) swing = 1f;
        Vector2 grip = NPC.Center + new Vector2(0f, -NPC.height * 0.12f);
        Vector2 raised = grip + new Vector2(0f, -80f - 60f * lift);
        Vector2 striking = NPC.Bottom + new Vector2(0f, -12f);
        hammerParticle.grip = grip;
        hammerParticle.position = Vector2.Lerp(raised, striking, Easing.CubicIn(swing));
        hammerParticle.headRotation = 0f;
        hammerParticle.rotation = 0f;
        hammerParticle.scale = 1.25f + 1.1f * swing;
        hammerParticle.alpha = GuidaUtils.Smoothstep(60f, 65f, visualTime);
        hammerParticle.starOpacity = GuidaUtils.Smoothstep(65f, 79f, visualTime) *
            GuidaUtils.Smoothstep(88f, 81f, visualTime);
        hammerParticle.starRotation = MathHelper.TwoPi * 1.4f *
            GuidaUtils.Smoothstep(65f, 83f, visualTime);
        hammerParticle.timeLeft = 2;

        if (visualTime >= 81f && !grounded) {
            UpdateBodyMotionTrail();
            SpawnHammerSpeedLines();
        }
    }

    private void UpdateHammerWarning() {
        if (CurrentMove != Move.HammerSlam || Timer < 22f || Timer >= 81f ||
            hammerWarningCreated) return;
        // Preview the apex once from the launched velocity. The warning stays
        // fixed while the slime rises, then expires on its own at the slam.
        Vector2 apex = NPC.Bottom;
        float horizontal = NPC.velocity.X;
        float vertical = NPC.velocity.Y;
        for (int i = 0; i < 90 && vertical < 0f; i++) {
            horizontal *= 0.992f;
            apex.X += horizontal;
            float nearApex = 1f - MathHelper.Clamp(
                Math.Abs(vertical) / (4.4f * appliedTempo), 0f, 1f);
            vertical += 0.45f * appliedTempo * appliedTempo * (1f - 0.78f * nearApex);
            if (vertical < 0f) apex.Y += vertical;
        }
        WarningLineParticle warning = ParticleManager.Instance?.NewParticle<WarningLineParticle>(
            apex, Vector2.Zero);
        if (warning == null) return;
        hammerWarningCreated = true;
        StyleVerticalBodyWarning(warning);
        warning.lineRotation = MathHelper.Pi;
        warning.time = Math.Max(1f, (81f - Timer) / appliedTempo);
        warning.arrowSpeed = appliedTempo;
        Player target = Main.player[NPC.target];
        warning.lineLength = MathHelper.Clamp(
            target.Bottom.Y - apex.Y + 112f, 180f, 980f);
    }

    private void SpawnHammerSpeedLines() {
        if ((int)Timer % 2 == 0 || NPC.velocity.Y < 8f) return;
        for (int i = 0; i < 2; i++) {
            SpeedLineParticle line = ParticleManager.Instance?.NewParticle<SpeedLineParticle>(
                NPC.Center + new Vector2(Main.rand.NextFloat(-NPC.width * 0.65f,
                    NPC.width * 0.65f), Main.rand.NextFloat(-NPC.height * 0.6f,
                    NPC.height * 0.3f)),
                new Vector2(0f, -Main.rand.NextFloat(2f, 5f)),
                alpha: Main.rand.NextFloat(0.28f, 0.45f));
            if (line == null) continue;
            line.color = new Color(185, 220, 255);
            line.rotation = 0f;
            line.drawScale = new Vector2(Main.rand.NextFloat(0.12f, 0.19f),
                Main.rand.NextFloat(0.9f, 1.6f));
            line.velocityDrag = 0.93f;
            line.fadeInEnd = 0.12f;
            line.fadeOutStart = 0.48f;
            line.timeLeft = line.maxTimeLeft = Main.rand.Next(12, 18);
        }
    }

    private float UmbrellaClosure() => GuidaUtils.Smoothstep(76f, 90f, Timer);

    private void UmbrellaRush(Player target) {
        if (Timer < 12f || PassedTime(12f)) {
            NPC.velocity.X *= grounded ? 0.75f : 0.94f;
            if (!grounded) Timer = Math.Min(Timer, 4f);
            if (PassedTime(12f)) {
                MoveValue = target.Center.X >= NPC.Center.X ? 1f : -1f;
                umbrellaRushDistanceScale = MathHelper.Clamp(
                    Math.Abs(target.Center.X - NPC.Center.X) / 520f, 0.85f, 1.12f);
                LaunchJump(0f, 16f, heavy: false);
            }
            return;
        }

        if (Timer < 60f || (MoveValue == 0f && NPC.localAI[0] == 1f && !grounded)) {
            NPC.velocity.X *= 0.96f;
            if (grounded && Timer > 16f) {
                // A ceiling may cancel the rush, but its musical slot still ends on time.
                Timer = 106f;
                NPC.localAI[0] = 0f;
                NPC.velocity = Vector2.Zero;
                ReleaseUmbrella();
                return;
            }
            if (PassedTime(18f)) {
                SoundEngine.PlaySound(KingSlimeSound.Mechanism, NPC.Center);
            }
            // The turn begins at the actual apex, even if a low ceiling shortened the jump.
            if (Timer > 16f && NPC.velocity.Y >= -0.6f) {
                Timer = 60f;
                NPC.velocity = Vector2.Zero;
                motionMode = MotionMode.Controlled;
                NPC.netUpdate = true;
            }
            else if (Timer >= 59f) Timer = 59f;
            return;
        }

        if (Timer < 76f) {
            motionMode = MotionMode.Controlled;
            NPC.velocity = Vector2.Zero;
            return;
        }

        if (Timer < 106f) {
            motionMode = MotionMode.Rush;
            if (PassedTime(76f)) {
                SoundEngine.PlaySound(KingSlimeSound.Mechanism, NPC.Center);
                NPC.netUpdate = true;
            }
            if (PassedTime(80f))
                SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
            if (Timer > 80f && (NPC.collideX || NPC.collideY)) {
                Timer = 106f;
                motionMode = MotionMode.Natural;
                NPC.velocity *= 0.16f;
                ReleaseUmbrella();
                NPC.netUpdate = true;
                return;
            }

            // Closing the canopy supplies the acceleration. Its momentum carries
            // the slime briefly after the umbrella is fully shut, then tapers.
            float progress = MathHelper.Clamp((Timer - 76f) / 30f, 0f, 1f);
            float speed = 33f * UmbrellaClosure();
            speed *= 1f - 0.68f * GuidaUtils.Smoothstep(0.72f, 1f, progress);
            NPC.velocity = new Vector2(MoveValue * speed * umbrellaRushDistanceScale, 0f);
            if (Timer >= 80f) SpawnUmbrellaWind();
            return;
        }

        if (PassedTime(106f)) {
            ReleaseUmbrella();
            NPC.velocity.X *= 0.32f;
            NPC.velocity.Y = 1.5f;
            NPC.netUpdate = true;
        }
        NPC.velocity.X *= grounded ? 0.75f : 0.97f;
        if (LandedFromAir()) {
            NPC.localAI[0] = 0f;
            NPC.velocity.X *= 0.35f;
            Land(false);
            NPC.netUpdate = true;
        }
        if (Timer >= 106f && SlotFinished(135f) && grounded)
            AdvancePattern(target);
    }

    private void SpawnUmbrellaWind() {
        if (Main.netMode == NetmodeID.Server || (int)Timer % 2 == 0) return;
        for (int i = 0; i < 2; i++) {
            SpeedLineParticle line = ParticleManager.Instance?.NewParticle<SpeedLineParticle>(
                NPC.Center + new Vector2(MoveValue * Main.rand.NextFloat(-NPC.width * 0.25f,
                    NPC.width * 0.48f), Main.rand.NextFloat(-NPC.height * 0.38f, NPC.height * 0.26f)),
                new Vector2(-MoveValue * Main.rand.NextFloat(2f, 4f), 0f),
                alpha: Main.rand.NextFloat(0.22f, 0.34f));
            if (line == null) continue;
            line.color = new Color(235, 207, 220);
            line.rotation = MoveValue > 0f ? -MathHelper.PiOver2 : MathHelper.PiOver2;
            line.drawScale = new Vector2(Main.rand.NextFloat(0.1f, 0.18f),
                Main.rand.NextFloat(0.65f, 1.15f));
            line.velocityDrag = 0.93f;
            line.fadeInEnd = 0.12f;
            line.fadeOutStart = 0.5f;
            line.timeLeft = line.maxTimeLeft = Main.rand.Next(10, 16);
        }
    }

    private void ReleaseUmbrella() {
        if (umbrellaParticle?.IsAlive == true && umbrellaParticle.held)
            umbrellaParticle.Release(new Vector2((MoveValue == 0f ? 1f : MoveValue) * 6f, -3.8f));
        umbrellaParticle = null;
    }

    private void UpdateUmbrellaVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        if (CurrentMove != Move.UmbrellaRush || Timer >= 106f) {
            ReleaseUmbrella();
            return;
        }
        UpdateUmbrellaWarning();
        if (Timer < 12f) return;

        if (umbrellaParticle?.IsAlive != true) {
            umbrellaParticle = ParticleManager.Instance?.NewParticle<KingSlimeUmbrellaParticle>(
                NPC.Center, Vector2.Zero, alpha: 0f, scale: 2f);
            if (umbrellaParticle == null) return;
            umbrellaParticle.SetHolder(NPC);
        }

        float direction = MoveValue == 0f ?
            (Main.player[NPC.target].Center.X >= NPC.Center.X ? 1f : -1f) : MoveValue;
        float turn = GuidaUtils.Smoothstep(60f, 72f, Timer);
        float close = UmbrellaClosure();
        umbrellaParticle.frame = Timer < 18f ? 2 : Timer < 23f ? 1 :
            close < 0.30f ? 0 : close < 0.76f ? 1 : 2;

        Vector2 upright = NPC.Center + new Vector2(0f, -NPC.height * 0.62f);
        Vector2 forward = NPC.Center + new Vector2(direction * (NPC.width * 0.42f + 45f),
            -NPC.height * 0.06f);
        float pullBack = 24f * GuidaUtils.Smoothstep(67f, 75f, Timer) *
            GuidaUtils.Smoothstep(84f, 77f, Timer);
        float lead = 24f * close;
        umbrellaParticle.position = Vector2.Lerp(upright, forward, turn) +
            new Vector2(direction * (lead - pullBack),
                (float)Math.Sin(Timer * 0.24f) * 2f * (1f - turn));
        umbrellaParticle.rotation = direction * MathHelper.PiOver2 * turn +
            (float)Math.Sin(Timer * 0.27f) * 0.045f * (1f - turn);
        umbrellaParticle.flutter = Timer >= 23f
            ? (float)Math.Sin(Timer * 0.37f) * 0.035f * (1f - close) -
                0.12f * close * (1f - close) : 0f;
        umbrellaParticle.alpha = GuidaUtils.Smoothstep(12f, 21f, Timer);
        umbrellaParticle.timeLeft = 2;
    }

    private void UpdateUmbrellaWarning() {
        if (Timer < 30f || Timer >= 76f || umbrellaWarningCreated) return;
        float direction = MoveValue == 0f
            ? (Main.player[NPC.target].Center.X >= NPC.Center.X ? 1f : -1f) : MoveValue;
        Vector2 rushStart = new(NPC.Center.X, PredictUmbrellaApexY());
        WarningLineParticle warning = ParticleManager.Instance?.NewParticle<WarningLineParticle>(
            rushStart + Vector2.UnitX * direction * NPC.width * 0.25f, Vector2.Zero);
        if (warning == null) return;
        umbrellaWarningCreated = true;
        StyleBodyWarning(warning);
        warning.lineLength = 680f * umbrellaRushDistanceScale;
        warning.time = Math.Max(1f, (76f - Timer) / appliedTempo);
        warning.arrowSpeed = appliedTempo;
        warning.lineRotation = direction > 0f
            ? MathHelper.PiOver2 : -MathHelper.PiOver2;
    }

    private float PredictUmbrellaApexY() {
        float y = NPC.Center.Y;
        float velocity = NPC.velocity.Y;
        GetTileCollisionBody(out Vector2 offset, out int width, out int height);
        Vector2 probe = NPC.position + offset;
        for (int i = 0; i < 80 && velocity < 0f; i++) {
            float nearApex = 1f - MathHelper.Clamp(
                Math.Abs(velocity) / (4.4f * appliedTempo), 0f, 1f);
            velocity += 0.45f * (1f - 0.78f * nearApex) * appliedTempo * appliedTempo;
            float nextY = y + Math.Min(velocity, 0f);
            if (Collision.SolidCollision(probe + Vector2.UnitY * (nextY - NPC.Center.Y),
                    width, height)) break;
            y = nextY;
        }
        return y;
    }


    private Vector2 StaffGrip(float aim) => NPC.Center + aim.ToRotationVector2() *
        (NPC.width * 0.27f) - Vector2.UnitY * NPC.height * 0.10f;

    private void CreateBoomerangWarning(Vector2 origin, float angle, float musicalTicks) {
        if (Main.netMode == NetmodeID.Server) return;
        WarningLineParticle line = ParticleManager.Instance?
            .NewParticle<WarningLineParticle>(origin, Vector2.Zero);
        if (line == null) return;
        line.lineRotation = angle + MathHelper.PiOver2;
        line.lineLength = KingSlimeBoomerang.TravelDistance;
        line.lineWidth = 42f;
        line.alpha = 0.48f;
        line.smaller = true;
        line.color = Color.White;
        line.time = Math.Max(1f, musicalTicks / appliedTempo);
    }

    private void LaunchBoomerang(Vector2 origin, float angle) {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        int index = Projectile.NewProjectile(NPC.GetSource_FromAI(), origin,
            angle.ToRotationVector2() * 8f, ModContent.ProjectileType<KingSlimeBoomerang>(),
            20, 2f, Main.myPlayer, ai0: origin.X, ai1: origin.Y, ai2: angle);
        if (index >= 0 && index < Main.maxProjectiles &&
            Main.projectile[index].ModProjectile is KingSlimeBoomerang boomerang)
            boomerang.SetTempo(appliedTempo, NPC.whoAmI);
    }

    private void Boomerang(Player target) {
        if (Timer < 29f) {
            NPC.velocity = Vector2.Zero;
            motionMode = MotionMode.Controlled;
        }
        if (PassedTime(1f)) {
            MoveValue = target.Center.X + target.velocity.X * 5f >= NPC.Center.X
                ? 0f : MathHelper.Pi;
            NPC.netUpdate = true;
            CreateBoomerangWarning(NPC.Center + MoveValue.ToRotationVector2() *
                (NPC.width * 0.32f), MoveValue, 96f);
        }
        float throwPose = GuidaUtils.Smoothstep(5f, 17f, Timer) *
            GuidaUtils.Smoothstep(38f, 28f, Timer);
        NPC.rotation += (MoveValue == 0f ? -1f : 1f) *
            0.12f * throwPose;
        landingCompression = Math.Max(landingCompression, 0.16f * throwPose);
        if (PassedTime(29f)) {
            SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
            LaunchBoomerang(NPC.Center + MoveValue.ToRotationVector2() *
                (NPC.width * 0.32f), MoveValue);
        }
        if (SlotFinished(45f)) AdvancePattern(target);
    }

    private void UpdateBoomerangVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        if (CurrentMove != Move.Boomerang || Timer < 2f || Timer >= 29f) {
            boomerangParticle = null;
            return;
        }
        if (boomerangParticle?.IsAlive != true) {
            boomerangParticle = ParticleManager.Instance?.NewParticle<KingSlimePropParticle>(
                NPC.Center, Vector2.Zero, alpha: 0f, scale: 2.8f);
            if (boomerangParticle != null) {
                boomerangParticle.SetHolder(NPC);
                boomerangParticle.propTexture = ModAsset.KingSlimeBoomerang.Value;
            }
        }
        if (boomerangParticle == null) return;
        Vector2 direction = MoveValue == 0f ? Vector2.UnitX : -Vector2.UnitX;
        float windup = GuidaUtils.Smoothstep(5f, 19f, Timer);
        boomerangParticle.position = NPC.Center + direction * (NPC.width * 0.30f - 18f * windup) -
            Vector2.UnitY * NPC.height * 0.10f;
        boomerangParticle.rotation = MoveValue + MathHelper.PiOver2 -
            (direction.X >= 0f ? 1f : -1f) * 0.55f * windup;
        boomerangParticle.spriteDirection = direction.X >= 0f ? 1 : -1;
        boomerangParticle.alpha = GuidaUtils.Smoothstep(2f, 9f, Timer);
        boomerangParticle.timeLeft = 2;
    }

    private void FireWand(Player target) {
        NPC.velocity.X *= grounded ? 0.72f : 0.96f;
        if (PassedTime(2f)) {
            Vector2 aim = target.Center + target.velocity * 6f - NPC.Center;
            MoveValue = aim.ToRotation();
            NPC.netUpdate = true;
            if (Main.netMode != NetmodeID.Server) {
                Vector2 direction = MoveValue.ToRotationVector2();
                PixelWarningLineParticle line = ParticleManager.Instance?.NewParticle<PixelWarningLineParticle>(
                    StaffGrip(MoveValue) + direction * 30f, Vector2.Zero);
                if (line != null) {
                    line.rotation = MoveValue;
                    line.lineLength = 2000f;
                    line.lineWidth = 7f;
                    line.opacity = 0.38f;
                    line.color = new Color(255, 176, 102);
                    line.timeLeft = line.maxTimeLeft =
                        (int)Math.Ceiling(30f / appliedTempo);
                }
            }
        }
        if (PassedTime(21f)) {
            SoundEngine.PlaySound(KingSlimeSound.Cast, NPC.Center);
            landingCompression = Math.Max(landingCompression, 0.18f);
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                Vector2 direction = MoveValue.ToRotationVector2();
                Vector2 muzzle = StaffGrip(MoveValue) + direction * 30f;
                Projectile.NewProjectile(NPC.GetSource_FromAI(), muzzle,
                    direction * 3.2f * appliedTempo,
                    ModContent.ProjectileType<KingSlimeFireball>(), 20, 1f,
                    Main.myPlayer);
            }
        }
        if (PassedTime(37f)) ReleaseFireWand();
        if (SlotFinished(45f)) AdvancePattern(target);
    }

    private void SlimeStaffRain(Player target) {
        NPC.velocity.X *= grounded ? 0.70f : 0.94f;
        for (int cast = 28; cast <= 148; cast += 15) {
            float warningStart = Math.Max(1f, cast - 30f);
            if (PassedTime(warningStart)) {
                if (Main.netMode != NetmodeID.MultiplayerClient) {
                    float x = target.Center.X + target.velocity.X * 27f +
                        Main.rand.NextFloat(-520f, 520f);
                    Vector2 landing = new(x, target.Bottom.Y);
                    int slimeType = Main.rand.Next(3) switch {
                        0 => NPCID.GreenSlime,
                        1 => NPCID.BlueSlime,
                        _ => NPCID.RedSlime
                    };
                    Projectile dropSpawner = Projectile.NewProjectileDirect(NPC.GetSource_FromAI(), landing,
                        Vector2.Zero, ModContent.ProjectileType<KingSlimeDropSpawner>(),
                        0, 0f, Main.myPlayer, ai0: target.whoAmI, ai1: 1600f,
                        ai2: slimeType);
                    // Release the slime shortly after the cast, with the same
                    // early warning lead-in.
                    dropSpawner.timeLeft = Math.Max(1,
                        (int)Math.Ceiling((cast + 24f - warningStart) / appliedTempo));
                    dropSpawner.netUpdate = true;
                }
            }
            if (!PassedTime(cast)) continue;
            landingCompression = Math.Max(landingCompression, 0.15f);
            SoundEngine.PlaySound(KingSlimeSound.Cast, NPC.Center);
        }
        if (PassedTime(158f)) ReleaseSlimeStaff();
        if (SlotFinished(180f) && grounded) AdvancePattern(target);
    }

    private void ReleaseFireWand() {
        if (fireWandParticle?.IsAlive == true && fireWandParticle.held)
            fireWandParticle.Release(MoveValue.ToRotationVector2() * 3f - Vector2.UnitY * 3f);
        fireWandParticle = null;
    }

    private void ReleaseSlimeStaff() {
        if (slimeStaffParticle?.IsAlive == true && slimeStaffParticle.held)
            slimeStaffParticle.Release(new Vector2(NPC.direction * 3f, -4f));
        slimeStaffParticle = null;
    }

    private void UpdateStaffVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        if (CurrentMove != Move.FireWand || Timer >= 37f) ReleaseFireWand();
        else if (Timer >= 3f) {
            if (fireWandParticle?.IsAlive != true) {
                fireWandParticle = ParticleManager.Instance?.NewParticle<KingSlimeStaffParticle>(
                    NPC.Center, Vector2.Zero, alpha: 0f, scale: 2.5f);
                if (fireWandParticle != null) fireWandParticle.SetHolder(NPC);
            }
            if (fireWandParticle != null) {
                float swing = -0.62f * GuidaUtils.Smoothstep(8f, 17f, Timer) +
                    1.05f * GuidaUtils.Smoothstep(17f, 23f, Timer) -
                    0.43f * GuidaUtils.Smoothstep(24f, 36f, Timer);
                fireWandParticle.position = StaffGrip(MoveValue);
                fireWandParticle.rotation = MoveValue + MathHelper.PiOver4 + swing;
                fireWandParticle.alpha = GuidaUtils.Smoothstep(3f, 10f, Timer);
                fireWandParticle.timeLeft = 2;
            }
        }

        if (CurrentMove != Move.SlimeStaffRain || Timer >= 158f) ReleaseSlimeStaff();
        else if (Timer >= 4f) {
            if (slimeStaffParticle?.IsAlive != true) {
                slimeStaffParticle = ParticleManager.Instance?.NewParticle<KingSlimeStaffParticle>(
                    NPC.Center, Vector2.Zero, alpha: 0f, scale: 2.5f);
                if (slimeStaffParticle != null) {
                    slimeStaffParticle.summoning = true;
                    slimeStaffParticle.SetHolder(NPC);
                }
            }
            if (slimeStaffParticle != null) {
                float direction = NPC.direction == 0 ? 1f : NPC.direction;
                float windup = -0.85f * GuidaUtils.Smoothstep(10f, 25f, Timer);
                float sweep = 1.05f * (float)Math.Sin((Timer - 25f) * MathHelper.TwoPi / 24f);
                float swing = MathHelper.Lerp(windup, sweep,
                    GuidaUtils.Smoothstep(25f, 34f, Timer));
                slimeStaffParticle.position = NPC.Center + new Vector2(direction * NPC.width * 0.30f,
                    -NPC.height * 0.17f - 5f * (float)Math.Sin(Timer * 0.18f));
                slimeStaffParticle.spriteDirection = direction > 0f ? 1 : -1;
                slimeStaffParticle.rotation = direction * swing;
                slimeStaffParticle.alpha = GuidaUtils.Smoothstep(4f, 12f, Timer);
                slimeStaffParticle.timeLeft = 2;
            }
        }
    }

    private Vector2 ShortswordDirection() => MoveValue.ToRotationVector2();

    private Vector2 ShortswordHeldPosition() {
        float pullBack = 17f * GuidaUtils.Smoothstep(4f, 11f, Timer) *
            GuidaUtils.Smoothstep(16f, 12f, Timer);
        float thrust = 58f * GuidaUtils.Smoothstep(12f, 22f, Timer) *
            GuidaUtils.Smoothstep(39f, 32f, Timer);
        return NPC.Center + ShortswordDirection() *
            (NPC.width * 0.28f - pullBack + thrust) - Vector2.UnitY * (NPC.height * 0.09f);
    }

    private Vector2 ShortswordTipPosition() =>
        ShortswordHeldPosition() + ShortswordDirection() * 34f;

    private void ShortswordThrust(Player target) {
        if (Timer < 12f) {
            NPC.velocity.X *= grounded ? 0.70f : 0.95f;
            if (!grounded) Timer = Math.Min(Timer, 5f);
            if (PassedTime(3f)) {
                Vector2 aim = target.Center + target.velocity * 3f -
                    (NPC.Bottom - Vector2.UnitY * target.height * 0.5f);
                MoveValue = aim.ToRotation();
                NPC.netUpdate = true;
            }
            return;
        }

        if (Timer < 32f) {
            motionMode = MotionMode.Rush;
            if (PassedTime(12f)) {
                SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
                NPC.netUpdate = true;
            }
            float progress = MathHelper.Clamp((Timer - 12f) / 20f, 0f, 1f);
            NPC.velocity = ShortswordDirection() * (8.2f * (float)Math.Sin(MathHelper.Pi * progress));
            return;
        }

        NPC.velocity.X *= grounded ? 0.72f : 0.94f;
        if (PassedTime(39f)) ReleaseShortsword();
        if (SlotFinished(45f) && grounded) AdvancePattern(target);
    }

    private void ReleaseShortsword() {
        if (shortswordParticle?.IsAlive == true && shortswordParticle.held) {
            Vector2 direction = ShortswordDirection();
            shortswordParticle.Release(direction * 4f + new Vector2(direction.X * 2f, -4f));
        }
        shortswordParticle = null;
    }

    private void UpdateShortswordVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        if (CurrentMove != Move.ShortswordThrust || Timer >= 39f) {
            ReleaseShortsword();
            return;
        }
        if (Timer < 4f) return;
        if (shortswordParticle?.IsAlive != true) {
            shortswordParticle = ParticleManager.Instance?.NewParticle<KingSlimeCopperShortswordParticle>(
                NPC.Center, Vector2.Zero, alpha: 0f, scale: 2.5f);
            if (shortswordParticle == null) return;
            shortswordParticle.SetHolder(NPC);
        }
        shortswordParticle.position = ShortswordHeldPosition();
        shortswordParticle.rotation = MoveValue + MathHelper.PiOver4;
        shortswordParticle.alpha = GuidaUtils.Smoothstep(4f, 9f, Timer);
        shortswordParticle.timeLeft = 2;
    }
}
