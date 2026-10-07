using System;
using System.IO;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReverieMod.Content;
using ReverieMod.Content.Particles;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;
using Terraria.Graphics.CameraModifiers;

namespace ReverieMod.Content.KingSlime;

public partial class KingSlime {
    internal bool UltimateActive => CurrentMove == Move.Ultimate && !dying;
    private bool ultimateUsed;
    private readonly Vector2[] ultimateFocus = new Vector2[7];
    private byte ultimateFocusMask;
    private Vector2 ultimateImpact;
    private int ultimateWarningMask;
    private int ultimateLeg = -1;
    private int ultimateShurikens;
    private TrailParticle ultimateTrail;
    private ScreenMaskParticle ultimateDarkness;
    private float ultimateFlareBoost;
    private Vector2 cinematicCameraPoint;
    private float introCameraY;
    private bool cinematicCameraInitialized;
    private bool ultimateCameraInitialized;
    private bool deathCameraInitialized;
    private Vector2 deathCameraStart;
    private Vector2 deathCameraEnd;

    private void WriteUltimateState(BinaryWriter writer) {
        writer.Write(ultimateFocusMask);
        for (int i = 0; i < ultimateFocus.Length; i++) {
            if ((ultimateFocusMask & (1 << i)) == 0) continue;
            writer.Write(ultimateFocus[i].X);
            writer.Write(ultimateFocus[i].Y);
        }
        if ((ultimateFocusMask & (1 << 6)) != 0) {
            writer.Write(ultimateImpact.X);
            writer.Write(ultimateImpact.Y);
        }
    }

    private void ReadUltimateState(BinaryReader reader, bool active) {
        if (!active) {
            ultimateFocusMask = 0;
            ultimateImpact = Vector2.Zero;
            return;
        }
        ultimateFocusMask = reader.ReadByte();
        for (int i = 0; i < ultimateFocus.Length; i++)
            ultimateFocus[i] = (ultimateFocusMask & (1 << i)) != 0
                ? new Vector2(reader.ReadSingle(), reader.ReadSingle()) : Vector2.Zero;
        ultimateImpact = (ultimateFocusMask & (1 << 6)) != 0
            ? new Vector2(reader.ReadSingle(), reader.ReadSingle()) : Vector2.Zero;
    }

    private void UpdateCinematicCamera() {
        if (Main.netMode == NetmodeID.Server || IsEcho || NPC.IsABestiaryIconDummy) return;
        if (!IntroActive && CurrentMove != Move.Ultimate && !dying) return;
        if (!cinematicCameraInitialized) {
            cinematicCameraPoint = Main.screenPosition +
                new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f;
            introCameraY = cinematicCameraPoint.Y;
            cinematicCameraInitialized = true;
        }
        if (CurrentMove == Move.Ultimate && !ultimateCameraInitialized) {
            cinematicCameraPoint = Main.screenPosition +
                new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f;
            ultimateCameraInitialized = true;
        }
        CameraModifier modifier = ModContent.GetInstance<CameraModifySystem>()
            .AddOrGetModifier(NPC, 5000f, 0.075f, 0.035f);
        if (dying) {
            if (!deathCameraInitialized) {
                deathCameraStart = Main.screenPosition +
                    new Vector2(Main.screenWidth, Main.screenHeight) * 0.5f;
                deathCameraEnd = NPC.Center;
                deathCameraInitialized = true;
                // Begin the fixed path at the camera's present position.
                modifier.CurrentMultiplier = 0.995f;
            }
            cinematicCameraPoint = Vector2.Lerp(deathCameraStart, deathCameraEnd,
                GuidaUtils.Smoothstep(0f, 85f, deathTimer));
            modifier.EndpointCenter = cinematicCameraPoint;
            modifier.TargetMultiplier = 0.995f;
            return;
        }
        Vector2 destination = new(NPC.Center.X, introCameraY);
        if (CurrentMove == Move.Ultimate)
            destination = Timer < 160f ? NPC.Center :
                Main.LocalPlayer.Center - Vector2.UnitY * 120f;
        cinematicCameraPoint = Vector2.Lerp(cinematicCameraPoint, destination, 0.06f);
        modifier.EndpointCenter = cinematicCameraPoint;
        modifier.TargetMultiplier = 0.65f;
    }

    private void Ultimate(Player target) {
        // Musical ticks: charge 0-180, four dashes 180-360, blades 360-450,
        // nine dashes 450-855, hammer 855-945. Each dash moves for 39/45 ticks.
        motionMode = MotionMode.Scripted;
        NPC.velocity = Vector2.Zero;
        NPC.damage = 0;
        grounded = false;
        NPC.localAI[0] = 1f;

        CaptureUltimateFocus(target);

        if (Main.netMode != NetmodeID.Server) {
            SpawnUltimateDarkness();
            CreateUltimateWarnings();
            SpawnUltimateChargeParticles();
        }

        if (Timer < 180f) {
            float gather = GuidaUtils.Smoothstep(0f, 180f, Timer);
            NPC.rotation = (float)Math.Sin(Timer * 0.13f) * 0.09f * gather;
            landingCompression = 0.10f * gather;
            if (PassedTime(1f)) SoundEngine.PlaySound(KingSlimeSound.Roar, NPC.Center);
            if (Main.netMode != NetmodeID.Server &&
                (PassedTime(60f) || PassedTime(120f))) {
                SoundEngine.PlaySound(KingSlimeSound.Cast, NPC.Center);
                AbsorptionEffectParticle.Spawn(NPC.Center, scale: 1.18f);
                TwistCircleParticle.Spawn(NPC.Center, 9f, 36, 0.24f, inverse: true);
            }
            return;
        }
        if (Timer < 360f) {
            RunUltimateDash((int)((Timer - 180f) / 45f));
            return;
        }
        if (Timer < 450f) {
            NPC.velocity = Vector2.Zero;
            landingCompression = 0.12f * GuidaUtils.Smoothstep(405f, 444f, Timer);
            for (int sound = 0; sound < 8; sound++)
                if (PassedTime(360f + sound * 5f))
                    SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                int wanted = Math.Min(16, (int)((Timer - 360f) / 2.5f) + 1);
                while (ultimateShurikens < wanted) {
                    float angle = -MathHelper.PiOver2 + ultimateShurikens * MathHelper.TwoPi / 16f;
                    Vector2 direction = angle.ToRotationVector2();
                    int index = Projectile.NewProjectile(NPC.GetSource_FromAI(),
                        NPC.Center + direction * 24f, direction * 12.5f * appliedTempo,
                        ModContent.ProjectileType<KingSlimeShuriken>(), 20, 2f,
                        Main.myPlayer, ai1: angle);
                    if (index >= 0 && index < Main.maxProjectiles &&
                        Main.projectile[index].ModProjectile is KingSlimeShuriken blade)
                        blade.SetTempo(appliedTempo);
                    ultimateShurikens++;
                }
            }
            NPC.rotation = (float)Math.Sin((Timer - 360f) * 0.11f) * 0.045f *
                GuidaUtils.Smoothstep(365f, 382f, Timer);
            return;
        }
        if (Timer < 855f) {
            RunUltimateDash(4 + (int)((Timer - 450f) / 45f));
            return;
        }

        NPC.damage = 0;
        NPC.rotation = MathHelper.Lerp(NPC.rotation, 0f, 0.16f);
        if (Timer < 890f) {
            NPC.velocity = Vector2.Zero;
            if (PassedTime(860f)) SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
            return;
        }
        if (Timer < 925f) {
            float t = MathHelper.Clamp((Timer - 890f) / 35f, 0f, 1f);
            float eased = t * t * t;
            if (!TryUltimateRoute(12, out _, out Vector2 lastDashEnd))
                lastDashEnd = NPC.Center;
            Vector2 desired = Vector2.Lerp(lastDashEnd, ultimateImpact, eased);
            NPC.velocity = desired - NPC.Center;
            NPC.damage = ContactDamage(50);
            if (Main.netMode != NetmodeID.Server) {
                UpdateBodyMotionTrail(ref ultimateTrail, false, true);
                SpawnHammerSpeedLines();
            }
            if (PassedTime(890f)) SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
            return;
        }
        if (PassedTime(925f)) UltimateHammerImpact();
        NPC.velocity = ultimateImpact - NPC.Center;
        NPC.damage = 0;
        if (Timer >= 945f && Main.netMode != NetmodeID.MultiplayerClient) {
            patternKind = 0;
            patternStep = 0;
            grounded = true;
            NPC.localAI[0] = 0f;
            StartPatternStep(target);
        }
    }

    private void RunUltimateDash(int leg) {
        if (!TryUltimateRoute(leg, out Vector2 start, out Vector2 end)) return;
        float begin = UltimateDashBegin(leg);
        if (ultimateLeg != leg) {
            ultimateLeg = leg;
            ultimateTrail = null;
            spearTipTrail = null;
            SpawnTeleportDust(NPC.Center, false);
            NPC.Center = start;
            SpawnTeleportDust(NPC.Center, true);
            SoundEngine.PlaySound(KingSlimeSound.Teleport, NPC.Center);
            KingSlimeSound.PlayRush(Vector2.Lerp(start, end, 0.5f));
            if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
            if (Main.netMode != NetmodeID.Server)
                TwistCircleParticle.Spawn(NPC.Center);
        }
        float t = MathHelper.Clamp((Timer - begin) / 39f, 0f, 1f);
        float progress = GuidaUtils.Smoothstep(0f, 1f, t);
        Vector2 desired = Vector2.Lerp(start, end, progress);
        NPC.velocity = desired - NPC.Center;
        NPC.damage = t < 1f ? ContactDamage(50) : 0;
        NPC.rotation = MathHelper.Lerp(NPC.rotation,
            MathHelper.Clamp(NPC.velocity.X * 0.0023f, -0.17f, 0.17f), 0.18f);
        landingCompression = 0.12f * GuidaUtils.Smoothstep(27f, 39f, Timer - begin);
        if (Main.netMode != NetmodeID.Server && t < 1f) {
            UpdateBodyMotionTrail(ref ultimateTrail, false, true);
            Vector2 direction = Vector2.Normalize(end - start);
            SpawnSpearRushEffects(direction, t < 0.04f, false);
        }
    }

    private float UltimateDashBegin(int leg) => leg < 4
        ? 180f + leg * 45f : 450f + (leg - 4) * 45f;

    private static int UltimateFocusGroup(int leg) => leg < 4 ? leg :
        leg < 10 ? 4 : leg < 12 ? 5 : 6;

    private void CaptureUltimateFocus(Player target) {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        for (int group = 0; group < ultimateFocus.Length; group++) {
            int bit = 1 << group;
            if ((ultimateFocusMask & bit) != 0) continue;
            int firstLeg = group < 4 ? group : group == 4 ? 4 : group == 5 ? 10 : 12;
            float captureTime = group == 0 ? 78f : UltimateDashBegin(firstLeg) - 102f;
            if (Timer < captureTime) continue;
            Vector2 focus = target.Center;
            ultimateFocus[group] = focus;
            ultimateFocusMask |= (byte)bit;
            if (group == 6) {
                float surface = target.Bottom.Y;
                Vector2 probe = new(focus.X - NPC.width * 0.5f, focus.Y);
                if (TryGroundSurface(probe, focus.Y, focus.Y + 1100f,
                        0f, false, false, out float found)) surface = found;
                ultimateImpact = new Vector2(focus.X, surface - NPC.height * 0.5f);
            }
            NPC.netUpdate = true;
        }
    }

    private void UpdateUltimateSpearVisuals() {
        bool firstPreparation = Timer >= 78f && Timer < 180f;
        bool secondPreparation = Timer >= 405f && Timer < 450f;
        bool firstDashes = Timer >= 180f && Timer < 360f;
        bool secondDashes = Timer >= 450f && Timer < 855f;
        if (!firstPreparation && !secondPreparation && !firstDashes && !secondDashes) {
            ReleaseSpear();
            return;
        }
        int leg = firstDashes ? (int)((Timer - 180f) / 45f) :
            secondDashes ? 4 + (int)((Timer - 450f) / 45f) :
            firstPreparation ? 0 : 4;
        if (!TryUltimateRoute(leg, out Vector2 start, out Vector2 end)) return;
        if (spearParticle?.IsAlive != true) {
            spearParticle = KingSlimeSpearParticle.Spawn(
                NPC.Center, alpha: 0f, scale: 2.5f);
            spearParticle.SetHolder(NPC);
        }

        float aimAngle = (end - start).ToRotation();
        bool rushing = firstDashes || secondDashes;
        float legBegin = UltimateDashBegin(leg);
        float legTime = Timer - legBegin;
        if (rushing && legTime >= 35f && leg != 3 && leg != 12 &&
            TryUltimateRoute(leg + 1, out Vector2 nextStart, out Vector2 nextEnd)) {
            float nextAngle = (nextEnd - nextStart).ToRotation();
            aimAngle += MathHelper.WrapAngle(nextAngle - aimAngle) *
                GuidaUtils.Smoothstep(35f, 44f, legTime);
        }
        if (!rushing) {
            float prepare = firstPreparation ? Timer : Timer - 390f;
            float spinStart = firstPreparation ? 78f : 15f;
            float spinEnd = firstPreparation ? 147f : 48f;
            aimAngle += MathHelper.TwoPi * GuidaUtils.Smoothstep(spinStart, spinEnd, prepare);
        }
        Vector2 direction = aimAngle.ToRotationVector2();
        float pullBack = rushing ? 22f * GuidaUtils.Smoothstep(31f, 44f, legTime) :
            28f * GuidaUtils.Smoothstep(firstPreparation ? 151f : 48f,
                firstPreparation ? 179f : 59f, firstPreparation ? Timer : Timer - 390f);
        spearParticle.position = NPC.Center + direction *
            (NPC.width * 0.42f + 8f - pullBack) - Vector2.UnitY * (NPC.height * 0.09f);
        spearParticle.rotation = aimAngle + MathHelper.PiOver4;
        spearParticle.alpha = rushing ? 1f : GuidaUtils.Smoothstep(
            firstPreparation ? 78f : 405f, firstPreparation ? 98f : 420f, Timer);
        spearParticle.tipStarOpacity = rushing
            ? GuidaUtils.Smoothstep(39f, 32f, legTime)
            : GuidaUtils.Smoothstep(firstPreparation ? 131f : 420f,
                firstPreparation ? 169f : 441f, Timer);
        spearParticle.tipStarRotation = Timer * 0.14f;
        spearParticle.tipRushStreak = rushing && legTime < 39f;
        if (spearParticle.tipRushStreak) UpdateSpearTipTrail();
    }

    private bool TryUltimateRoute(int leg, out Vector2 start, out Vector2 end) {
        start = end = Vector2.Zero;
        if (leg < 0 || leg >= 13) return false;
        int group = UltimateFocusGroup(leg);
        if ((ultimateFocusMask & (1 << group)) == 0) return false;
        Vector2 c = ultimateFocus[group];
        float reach = 820f;
        float diagonal = reach * 1.42f;
        float corner = reach * 0.71f;
        (Vector2 a, Vector2 b) = leg switch {
            0 => (new Vector2(reach, 0), new Vector2(-reach, 0)),
            1 => (new Vector2(-320, -reach), new Vector2(-320, reach)),
            2 => (new Vector2(320, -reach), new Vector2(320, reach)),
            3 => (new Vector2(0, reach), new Vector2(0, -reach * 0.26f)),
            4 => (new Vector2(300f, -diagonal + 300f),
                new Vector2(-diagonal + 300f, 300f)),
            5 => (new Vector2(-diagonal + 300f, -300f),
                new Vector2(300f, diagonal - 300f)),
            6 => (new Vector2(-300f, diagonal - 300f),
                new Vector2(diagonal - 300f, -300f)),
            7 => (new Vector2(diagonal - 300f, 300f),
                new Vector2(-300f, -diagonal + 300f)),
            8 => (new Vector2(0, -reach), new Vector2(0, reach)),
            9 => (new Vector2(-reach, 0), new Vector2(reach, 0)),
            10 => (new Vector2(corner, -corner), new Vector2(-corner, corner)),
            11 => (new Vector2(-corner, -corner), new Vector2(corner, corner)),
            _ => (new Vector2(0, reach), new Vector2(0, -reach * 0.26f))
        };
        start = c + a;
        end = c + b;
        float right = Main.maxTilesX * 16f - 160f;
        float bottom = Main.maxTilesY * 16f - 160f;
        // Translate a whole segment at world edges; clipping each end separately
        // would shorten the dash and desynchronize its warning line.
        Vector2 shift = Vector2.Zero;
        if (Math.Min(start.X, end.X) < 160f)
            shift.X = 160f - Math.Min(start.X, end.X);
        else if (Math.Max(start.X, end.X) > right)
            shift.X = right - Math.Max(start.X, end.X);
        if (Math.Min(start.Y, end.Y) < 160f)
            shift.Y = 160f - Math.Min(start.Y, end.Y);
        else if (Math.Max(start.Y, end.Y) > bottom)
            shift.Y = bottom - Math.Max(start.Y, end.Y);
        start += shift;
        end += shift;
        return true;
    }

    private void CreateUltimateWarnings() {
        // A warning starts ninety musical ticks before its leg and remains visible for
        // the first part of the dash. It is never moved or manually removed.
        for (int leg = 0; leg < 13; leg++) {
            int bit = 1 << leg;
            if ((ultimateWarningMask & bit) != 0) continue;
            float begin = UltimateDashBegin(leg);
            if (Timer < begin - 90f || Timer >= begin ||
                !TryUltimateRoute(leg, out Vector2 start, out Vector2 end)) continue;
            ultimateWarningMask |= bit;
            Vector2 direction = Vector2.Normalize(end - start);
            WarningLineParticle.Spawn(start - direction * 25f,
                direction.ToRotation() + MathHelper.PiOver2,
                Vector2.Distance(start, end) + 50f,
                (begin - Timer + 24f) / appliedTempo,
                width: (Math.Abs(direction.Y) > Math.Abs(direction.X) ? 136f : 96f) * NPC.scale,
                opacity: 0.8f);
            CreateTeleportWarning(start, begin - Timer);
        }
        if ((ultimateWarningMask & (1 << 13)) == 0 &&
            (ultimateFocusMask & (1 << 6)) != 0 && Timer >= 780f && Timer < 925f) {
            ultimateWarningMask |= 1 << 13;
            for (int i = 0; i < 8; i++) {
                float angle = -MathHelper.PiOver2 + i * MathHelper.TwoPi / 8f;
                CreateBoomerangWarning(ultimateImpact, angle, 925f - Timer + 24f);
            }
        }
    }

    private void SpawnUltimateDarkness() {
        if (ultimateDarkness?.IsAlive != true) {
            ultimateDarkness = ScreenMaskParticle.Spawn(Vector2.Zero, alpha: 0f);
            ultimateDarkness.drawLayer = ParticleLayer.BeforeNPCs;
            ultimateDarkness.SetTexture(ModAsset.WarningPixel.Value);
            ultimateDarkness.color = Color.Black;
            ultimateDarkness.fadeOutTicks = 30;
            ultimateDarkness.timeLeft = ultimateDarkness.maxTimeLeft = 31;
        }
        ultimateDarkness.alpha = MathHelper.Lerp(ultimateDarkness.alpha, 0.62f, 0.075f);
        ultimateDarkness.timeLeft = 31;
    }

    private void SpawnUltimateChargeParticles() {
        for (int pulse = 12; pulse <= 124; pulse += 16)
            if (PassedTime(pulse))
                AbsorptionEffectParticle.Spawn(
                    NPC.Center);

        // The last streak fades out before the first teleport at tick 180.
        if (Timer >= 132f || (int)(Timer / 2f) == (int)(previousTimer / 2f)) return;
        for (int i = 0; i < (Timer < 72f ? 3 : 5); i++) {
            Vector2 offset = Main.rand.NextVector2CircularEdge(420f, 320f);
            GlowStreakParticle streak = GlowStreakParticle.Spawn(
                NPC.Center + offset, -Vector2.Normalize(offset) * Main.rand.NextFloat(8f, 12f));
            streak.color = new Color(125, 200, 255);
            streak.drawSize = new Vector2(7f, 62f);
            streak.rotation = offset.ToRotation() - MathHelper.PiOver2;
            streak.timeLeft = streak.maxTimeLeft = 35;
        }
    }

    private void DrawUltimateCharge(SpriteBatch spriteBatch, Vector2 screenPos) {
        if (CurrentMove != Move.Ultimate || Timer >= 180f || IsEcho) return;
        float intensity = GuidaUtils.Smoothstep(0f, 150f, Timer) *
            GuidaUtils.Smoothstep(180f, 158f, Timer);
        Texture2D flare = ModAsset.TeleportFlare.Value;
        Vector2 center = NPC.Center - screenPos;
        Color tint = new Color(140, 205, 255, 0) * (0.34f * intensity);
        float size = MathHelper.Lerp(5.5f, 2.2f, intensity) * NPC.width / flare.Width;
        spriteBatch.Draw(flare, center, null, tint, Timer * 0.06f,
            flare.Size() * 0.5f, size, SpriteEffects.None, 0f);
        spriteBatch.Draw(flare, center, null, tint, -Timer * 0.04f,
            flare.Size() * 0.5f, size * 0.78f, SpriteEffects.None, 0f);
    }

    private void UpdateUltimateHammerVisuals() {
        if (Timer < 855f || Timer >= 945f) {
            if (Timer >= 945f) ReleaseHammer();
            return;
        }
        if (hammerParticle?.IsAlive != true) {
            hammerParticle = KingSlimeHammerParticle.Spawn(
                NPC.Center, alpha: 0f);
            hammerParticle.SetHolder(NPC);
        }
        float raise = GuidaUtils.Smoothstep(855f, 890f, Timer);
        float strike = GuidaUtils.Smoothstep(890f, 925f, Timer);
        hammerParticle.grip = NPC.Center + new Vector2(0f, -NPC.height * 0.12f);
        hammerParticle.position = Vector2.Lerp(
            hammerParticle.grip - Vector2.UnitY * (85f + 85f * raise),
            NPC.Bottom - Vector2.UnitY * 12f, strike * strike);
        hammerParticle.headRotation = hammerParticle.rotation = 0f;
        hammerParticle.scale = 1.4f + 2.1f * strike;
        hammerParticle.alpha = GuidaUtils.Smoothstep(855f, 865f, Timer);
        hammerParticle.starOpacity = raise * GuidaUtils.Smoothstep(895f, 880f, Timer);
        hammerParticle.starRotation = Timer * 0.11f;
        hammerParticle.timeLeft = 2;
    }

    private void UltimateHammerImpact() {
        NPC.Center = ultimateImpact;
        NPC.velocity = Vector2.Zero;
        grounded = true;
        NPC.localAI[0] = 0f;
        Land(true);
        SpawnHammerImpact(false);
        if (Main.netMode != NetmodeID.MultiplayerClient) {
            Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Bottom, Vector2.Zero,
                ModContent.ProjectileType<KingSlimeUltimateImpactEffect>(), 0,
                8f, Main.myPlayer);
            for (int i = 0; i < 8; i++)
                LaunchBoomerang(NPC.Center, -MathHelper.PiOver2 + i * MathHelper.TwoPi / 8f);
            NPC.netUpdate = true;
        }
    }

    private bool dying;
    private bool deathFinished;
    private float deathTimer;
    private bool deathBurstCreated;
    private bool deathMusicStopped;
    private bool deathOpeningShockCreated;

    public override bool CheckDead() {
        if (IsEcho || deathFinished) return true;
        dying = true;
        StopDeathMusic();
        CreateDeathOpeningShock();
        NPC.life = 1;
        NPC.dontTakeDamage = true;
        NPC.damage = 0;
        NPC.velocity = Vector2.Zero;
        NPC.netUpdate = true;
        return false;
    }

    private void StopDeathMusic() {
        if (deathMusicStopped || Main.netMode == NetmodeID.Server) return;
        deathMusicStopped = true;
        Music = 0;
        if (Main.curMusic >= 0 && Main.curMusic < Main.musicFade.Length)
            Main.musicFade[Main.curMusic] = 0f;
    }

    private void WriteDeathState(BinaryWriter writer) {
        writer.Write(deathTimer);
    }

    private void ReadDeathState(BinaryReader reader, bool active) {
        dying = active;
        deathTimer = active ? reader.ReadSingle() : 0f;
    }

    private void DeathAnimation() {
        deathTimer++;
        CreateDeathOpeningShock();
        NPC.dontTakeDamage = true;
        NPC.damage = 0;
        NPC.velocity = Vector2.Zero;
        NPC.noGravity = true;
        NPC.noTileCollide = true;
        StopDeathMusic();
        visualTicks++;
        landingCompression = 0.13f + 0.13f * (float)Math.Sin(deathTimer * 0.42f);
        UpdateBodyRotation();
        UpdateBodyPose();
        UpdateCrownPhysics();
        if (Main.netMode != NetmodeID.Server && deathTimer < 126f)
            UpdateInteriorVisuals();

        if (deathTimer == 1f) {
            ReleaseSpear();
            ReleaseGrapple();
            ReleasePotion();
            ReleaseRope();
            ReleaseUmbrella();
            ReleaseHammer();
            ReleaseFireWand();
            ReleaseSlimeStaff();
            ReleaseShortsword();
        }
        if (Main.netMode != NetmodeID.Server) {
            UpdateCinematicCamera();
            if (deathTimer >= 133f && deathTimer < 138f)
                ScreenPresentationSystem.ShowDeathBurstFlash();
            if (deathTimer < 138f && (int)deathTimer % 7 == 0) {
                for (int i = 0; i < (deathTimer < 55f ? 2 : 3); i++) {
                    Vector2 offset = Main.rand.NextVector2Circular(
                        NPC.width * 0.58f, NPC.height * 0.46f);
                    KingSlimeGelSplashParticle leak = KingSlimeGelSplashParticle.Spawn(NPC.Center + offset,
                            offset * 0.055f + Main.rand.NextVector2Circular(1f, 1f),
                            scale: Main.rand.NextFloat(0.10f, 0.22f));
                    leak.color = Color.Lerp(new Color(95, 165, 255),
                        new Color(185, 225, 255), Main.rand.NextFloat());
                    leak.opacity = Main.rand.NextFloat(0.38f, 0.62f);
                    leak.growth = Main.rand.NextFloat(0.008f, 0.015f);
                    leak.rotation = Main.rand.NextFloat(MathHelper.TwoPi);
                    TwistCircleParticle.Spawn(leak.position, 1.8f, 20, 0.09f);
                }
                if ((int)deathTimer % 21 == 0)
                    SoundEngine.PlaySound(KingSlimeSound.GelBurst with {
                        Volume = 0.55f, PitchVariance = 0.12f
                    }, NPC.Center);
            }
            if (deathTimer < 132f && (int)deathTimer % 3 == 0) {
                Vector2 direction = Main.rand.NextVector2Unit();
                Dust dust = Dust.NewDustPerfect(NPC.Center + direction *
                    Main.rand.NextFloat(12f, 55f), DustID.t_Slime,
                    direction * Main.rand.NextFloat(1.4f, 4.2f),
                    100, new Color(170, 220, 255), Main.rand.NextFloat(1.1f, 1.8f));
                dust.noGravity = true;
            }
        }
        if (deathTimer >= 138f) {
            CreateDeathBurst();
        }
        if (deathTimer < 142f || Main.netMode == NetmodeID.MultiplayerClient) return;
        deathFinished = true;
        NPC.life = 0;
        NPC.netUpdate = true;
        NPC.checkDead();
    }

    private void CreateDeathOpeningShock() {
        if (deathOpeningShockCreated) return;
        deathOpeningShockCreated = true;
        CreateDeathShock(28f, 8f, 82, 0.95f, 2.4f);
    }

    private void CreateDeathShock(float cameraStrength, float waveSize,
        int waveTime, float waveStrength, float effectScale) {
        if (Main.netMode == NetmodeID.Server) return;
        SoundEngine.PlaySound(KingSlimeSound.BossDeath, NPC.Center);
        SoundEngine.PlaySound(KingSlimeSound.GelBurst, NPC.Center);
        TwistCircleParticle.Spawn(NPC.Center, waveSize, waveTime, waveStrength);
        RoarEffectParticle.Spawn(NPC.Center, scale: effectScale);
        Main.instance.CameraModifiers.Add(new PunchCameraModifier(
            NPC.Center, Main.rand.NextVector2Unit(), cameraStrength, 6f, 20,
            1000f, FullName));
    }

    private void CreateDeathBurst() {
        if (deathBurstCreated || Main.netMode == NetmodeID.Server) return;
        deathBurstCreated = true;
        ReleaseCrown();
        CreateDeathShock(40f, 8f, 80, 1f, 2.8f);
        ScreenPresentationSystem.ShowDeathBurstFlash();
        ScreenPresentationSystem.ShowTitle(180,
            Language.GetTextValue("Mods.ReverieMod.KingSlimeTitles.Theme"),
            new Color(78, 171, 233),
            Language.GetTextValue("Mods.ReverieMod.KingSlimeTitles.Defeated"),
            new Color(222, 242, 255));
        for (int i = 0; i < 14; i++) {
            Vector2 direction = Main.rand.NextVector2Unit();
            KingSlimeGelSplashParticle gel = KingSlimeGelSplashParticle.Spawn(NPC.Center + direction *
                    Main.rand.NextFloat(0f, 85f), direction * Main.rand.NextFloat(2f, 8f),
                    scale: Main.rand.NextFloat(0.22f, 0.48f));
            gel.color = Color.Lerp(new Color(75, 140, 255),
                new Color(175, 225, 255), Main.rand.NextFloat());
            gel.growth = Main.rand.NextFloat(0.018f, 0.040f);
            gel.opacity = Main.rand.NextFloat(0.56f, 0.85f);
            gel.rotation = Main.rand.NextFloat(MathHelper.TwoPi);
            gel.angVelocity = Main.rand.NextFloat(-0.07f, 0.07f);
            gel.timeLeft = gel.maxTimeLeft = Main.rand.Next(22, 36);
        }
        Splash(70, 8f);
    }

}
