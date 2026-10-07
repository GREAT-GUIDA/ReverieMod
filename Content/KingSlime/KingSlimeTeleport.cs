using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using ReverieMod.Content.Particles;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using System.Collections.Generic;
using System.IO;

namespace ReverieMod.Content.KingSlime;

public partial class KingSlime {
    private Vector2 teleportDestination;
    private bool teleportReady;
    private bool teleportOccurred;
    private bool teleportWarningCreated;
    private bool IsTeleporting => CurrentMove == Move.Teleport ||
        CurrentMove == Move.TeleportSpearRush || CurrentMove == Move.TeleportHammerSlam ||
        CurrentMove == Move.TeleportShuriken;
    private float TeleportMoment => CurrentMove switch {
        Move.TeleportHammerSlam => 48f,
        Move.TeleportShuriken => 48f,
        Move.TeleportSpearRush => 54f,
        _ => 60f
    };

    private bool TryFindTeleportDestination(Player target, out Vector2 destination) {
        int heading = Math.Abs(target.velocity.X) > 1f
            ? Math.Sign(target.velocity.X) : target.direction;
        if (heading == 0) heading = 1;
        float preferredX = target.Center.X + target.velocity.X * 23f +
            heading * (NPC.width * 0.5f + target.width * 0.5f + 105f) +
            SplitJumpOffset();
        float predictedFootY = target.Bottom.Y +
            MathHelper.Clamp(target.velocity.Y * 16f, -180f, 180f);
        float minX = NPC.width * 0.5f + 32f;
        float maxX = Main.maxTilesX * 16f - minX;
        int[] offsets = { 0, 24, -24, 48, -48, 80, -80, 120, -120 };
        foreach (int offset in offsets) {
            float x = MathHelper.Clamp(preferredX + offset, minX, maxX);
            Vector2 probe = new(x - NPC.width * 0.5f,
                predictedFootY - NPC.height);
            if (!TryGroundSurface(probe, predictedFootY - 36f,
                    predictedFootY + 270f, 0f, false, false, out float groundY))
                continue;
            Vector2 topLeft = new(x - NPC.width * 0.5f, groundY - NPC.height);
            if (!Collision.SolidCollision(topLeft, NPC.width, NPC.height)) {
                destination = topLeft + new Vector2(NPC.width, NPC.height) * 0.5f;
                return true;
            }
        }
        destination = Vector2.Zero;
        return false;
    }

    private float TeleportOpacity() {
        if (dying || !IsTeleporting) return 1f;
        float opacity = Timer < TeleportMoment
            ? 1f - GuidaUtils.Smoothstep(TeleportMoment - 14f, TeleportMoment, Timer)
            : GuidaUtils.Smoothstep(TeleportMoment, TeleportMoment + 17f, Timer);
        if (teleportChainReady) {
            float nextMoment = teleportChainMove == Move.TeleportSpearRush ? 54f : 48f;
            opacity = MathHelper.Lerp(opacity,
                1f - GuidaUtils.Smoothstep(nextMoment - 14f, nextMoment, 45f),
                GuidaUtils.Smoothstep(90f, 135f, actionElapsed));
        }
        return opacity;
    }

    private void Teleport(Player target) {
        motionMode = MotionMode.Scripted;
        NPC.velocity = Vector2.Zero;
        NPC.damage = 0;
        if (!teleportReady) return;

        ShowTeleportWarning();

        if (!teleportOccurred && Timer >= TeleportMoment)
            PerformTeleport(true);

        if (SlotFinished(90f)) {
            motionMode = MotionMode.Natural;
            AdvancePattern(target);
        }
    }

    private void ShowTeleportWarning() {
        if (!teleportReady) return;
        if (!teleportWarningCreated && Timer >= (CurrentMove == Move.Teleport ? 6f : 1f) &&
            Main.netMode != NetmodeID.Server) {
            teleportWarningCreated = true;
            CreateTeleportWarning(teleportDestination, TeleportMoment - Timer);
        }
    }

    private void CreateTeleportWarning(Vector2 destination, float musicalTicks) {
        TeleportWarningParticle warning = ParticleManager.Instance.NewParticle<TeleportWarningParticle>(
            destination + Vector2.UnitY * NPC.height * 0.5f, Vector2.Zero);
        warning.width = NPC.width;
        warning.height = NPC.height;
        warning.beatPhase = measureTicks % 22.5f;
        warning.beatSpeed = appliedTempo;
        warning.timeLeft = warning.maxTimeLeft =
            Math.Max(1, (int)Math.Ceiling(musicalTicks / appliedTempo));
    }

    private void PerformTeleport(bool landAtDestination) {
        teleportOccurred = true;
        ReleaseCrown();
        SpawnTeleportDust(NPC.Center, false);
        NPC.Center = teleportDestination;
        NPC.localAI[0] = landAtDestination ? 0f : 1f;
        grounded = landAtDestination;
        landingCompression = landAtDestination ? 0.34f : 0f;
        SpawnTeleportDust(NPC.Center, true);
        if (Main.netMode != NetmodeID.Server) {
            TwistCircleParticle.Spawn(NPC.Center, 6f, 25, 0.15f);
        }
        SoundEngine.PlaySound(KingSlimeSound.Teleport, NPC.Center);
        NPC.netUpdate = true;
    }

    private void SpawnTeleportDust(Vector2 center, bool arriving) {
        if (Main.netMode == NetmodeID.Server) return;
        for (int i = 0; i < 45; i++) {
            int index = Dust.NewDust(center - new Vector2(NPC.width * 0.6f,
                    NPC.height * 0.6f), (int)(NPC.width * 1.2f), NPC.height,
                DustID.t_Slime, 0f, 0f, 150, new Color(78, 136, 255, 80), 1.8f);
            Dust dust = Main.dust[index];
            dust.noGravity = true;
            dust.velocity *= arriving ? 1.8f : 0.5f;
        }
    }

    private bool teleportChainDecided;
    private bool teleportChainReady;
    private bool teleportChainPreviewCreated;
    private bool chainedTeleportAction;
    private Move teleportChainMove;
    private Vector2 teleportChainDestination;
    private float teleportChainDirection;
    private float teleportChainRushDistance;
    private KingSlimeSpearParticle queuedSpearParticle;

    private bool IsTeleportAttack => CurrentMove is Move.TeleportSpearRush or
        Move.TeleportHammerSlam or Move.TeleportShuriken;

    private void PrepareTeleportChain(Player target) {
        if (tempoStage == 0 || IsEcho || !IsTeleportAttack || Timer < 90f) return;
        if (Main.netMode != NetmodeID.MultiplayerClient && !teleportChainDecided) {
            teleportChainDecided = true;
            float chainChance = ultimateUsed
                ? MathHelper.Lerp(0.5f, 0.8f, MathHelper.Clamp(
                    (0.20f - NPC.life / (float)NPC.lifeMax) / 0.15f, 0f, 1f))
                : 0.5f;
            if (!pendingSplit && Main.rand.NextFloat() < chainChance) {
                Vector2 predictedEnd = CurrentMove == Move.TeleportSpearRush
                    ? teleportDestination + MoveValue.ToRotationVector2() * teleportRushDistance
                    : new Vector2(teleportDestination.X, target.Bottom.Y - NPC.height * 0.5f);
                List<(Move move, Vector2 destination, float direction, float distance)> options = new();
                if (TryPlanTeleportAttack(target, Move.TeleportSpearRush,
                        out Vector2 spearDestination, out float spearDirection,
                        out float spearDistance, predictedEnd)) {
                    Vector2 endpoint = spearDestination + spearDirection.ToRotationVector2() * spearDistance;
                    if (Vector2.DistanceSquared(predictedEnd, spearDestination) >= 300f * 300f &&
                        Vector2.DistanceSquared(predictedEnd, endpoint) >= 200f * 200f)
                        options.Add((Move.TeleportSpearRush, spearDestination, spearDirection, spearDistance));
                }
                if (TryPlanTeleportAttack(target, Move.TeleportHammerSlam,
                        out Vector2 hammerDestination, out _, out _, predictedEnd))
                    options.Add((Move.TeleportHammerSlam, hammerDestination, 0f, 0f));
                options.Add((Move.TeleportShuriken,
                    FindTeleportShurikenDestination(target), 0f, 0f));
                if (options.Count > 0) {
                    var choice = options[Main.rand.Next(options.Count)];
                    teleportChainReady = true;
                    teleportChainMove = choice.move;
                    teleportChainDestination = choice.destination;
                    teleportChainDirection = choice.direction;
                    teleportChainRushDistance = choice.distance;
                }
            }
            NPC.netUpdate = true;
        }
        if (teleportChainReady && !teleportChainPreviewCreated && Main.netMode != NetmodeID.Server)
            CreateTeleportChainPreview(target);
        if (teleportChainReady && teleportChainMove == Move.TeleportSpearRush &&
            Main.netMode != NetmodeID.Server)
            UpdateQueuedSpear();
    }

    private void UpdateQueuedSpear() {
        if (queuedSpearParticle?.IsAlive != true) {
            queuedSpearParticle = ParticleManager.Instance.NewParticle<KingSlimeSpearParticle>(
                NPC.Center, Vector2.Zero, alpha: 0f, scale: 2.5f);
            queuedSpearParticle.SetHolder(NPC);
        }
        float turn = GuidaUtils.Smoothstep(95f, 131f, actionElapsed);
        float angle = teleportChainDirection + MathHelper.TwoPi * (1f - turn);
        Vector2 direction = angle.ToRotationVector2();
        float arrivalRadius = NPC.width * 0.42f + 8f -
            30f * GuidaUtils.Smoothstep(42f, 70f, 45f);
        queuedSpearParticle.position = NPC.Center + direction * arrivalRadius -
            Vector2.UnitY * NPC.height * 0.09f;
        queuedSpearParticle.rotation = angle + MathHelper.PiOver4;
        queuedSpearParticle.alpha = GuidaUtils.Smoothstep(99f, 121f, actionElapsed);
        queuedSpearParticle.tipStarOpacity = GuidaUtils.Smoothstep(32f, 52f, 45f) *
            GuidaUtils.Smoothstep(113f, 134f, actionElapsed);
        queuedSpearParticle.tipStarRotation = MathHelper.TwoPi * 1.25f *
            GuidaUtils.Smoothstep(32f, 78f, 45f) *
            GuidaUtils.Smoothstep(113f, 135f, actionElapsed);
        queuedSpearParticle.timeLeft = 2;
    }

    private void ReleaseQueuedSpear() {
        if (queuedSpearParticle?.IsAlive == true && queuedSpearParticle.held)
            queuedSpearParticle.Release(new Vector2(0f, -2f));
        queuedSpearParticle = null;
    }

    private void CreateTeleportChainPreview(Player target) {
        teleportChainPreviewCreated = true;
        float remaining = 135f - Timer;
        float teleportMoment = teleportChainMove == Move.TeleportSpearRush ? 54f : 48f;
        CreateTeleportWarning(teleportChainDestination, remaining + teleportMoment - 45f);
        if (teleportChainMove == Move.TeleportSpearRush) {
            WarningLineParticle warning = ParticleManager.Instance.NewParticle<WarningLineParticle>(
                teleportChainDestination, Vector2.Zero);
            warning.lineLength = teleportChainRushDistance;
            warning.lineRotation = teleportChainDirection + MathHelper.PiOver2;
            StyleBodyWarning(warning);
            warning.time = Math.Max(1f, (remaining + 78f - 45f) / appliedTempo);
            warning.arrowSpeed = appliedTempo;
        }
        else if (teleportChainMove == Move.TeleportHammerSlam) {
            WarningLineParticle warning = ParticleManager.Instance.NewParticle<WarningLineParticle>(
                teleportChainDestination + Vector2.UnitY * NPC.height * 0.5f, Vector2.Zero);
            warning.lineRotation = MathHelper.Pi;
            warning.lineLength = MathHelper.Clamp(target.Bottom.Y - teleportChainDestination.Y -
                NPC.height * 0.5f + 112f, 180f, 880f);
            StyleVerticalBodyWarning(warning);
            warning.time = Math.Max(1f, (remaining + 81f - 45f) / appliedTempo);
            warning.arrowSpeed = appliedTempo;
        }
    }

    private bool StartTeleportChain() {
        if (!IsTeleportAttack || !teleportChainReady) return false;
        Move next = teleportChainMove;
        Vector2 destination = teleportChainDestination;
        float direction = teleportChainDirection;
        float distance = teleportChainRushDistance;
        bool previewed = teleportChainPreviewCreated;
        KingSlimeSpearParticle preparedSpear = queuedSpearParticle;
        queuedSpearParticle = null;
        if (CurrentMove == Move.TeleportSpearRush) ReleaseSpear();
        Begin(next);
        if (next == Move.TeleportSpearRush && preparedSpear?.IsAlive == true)
            spearParticle = preparedSpear;
        chainedTeleportAction = true;
        Timer = 45f;
        actionElapsed = 45f;
        teleportDestination = destination;
        teleportReady = true;
        teleportRushDistance = distance;
        MoveValue = direction;
        NPC.velocity = Vector2.Zero;
        motionMode = MotionMode.Scripted;
        if (previewed) {
            teleportWarningCreated = true;
            spearWarningCreated = next == Move.TeleportSpearRush;
            teleportHammerWarningCreated = next == Move.TeleportHammerSlam;
        }
        phaseTwoRecent.Add((next, measureTicks));
        NPC.netUpdate = true;
        return true;
    }

    private void WriteTeleportChain(BinaryWriter writer) {
        writer.Write(teleportChainReady);
        writer.Write(chainedTeleportAction);
        writer.Write((byte)teleportChainMove);
        writer.Write(teleportChainDestination.X);
        writer.Write(teleportChainDestination.Y);
        writer.Write(teleportChainDirection);
        writer.Write(teleportChainRushDistance);
    }

    private void ReadTeleportChain(BinaryReader reader, bool actionChanged, bool active) {
        bool previewed = teleportChainPreviewCreated;
        teleportChainReady = active && reader.ReadBoolean();
        chainedTeleportAction = active && reader.ReadBoolean();
        if (active) {
            teleportChainMove = (Move)reader.ReadByte();
            teleportChainDestination = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            teleportChainDirection = reader.ReadSingle();
            teleportChainRushDistance = reader.ReadSingle();
        }
        else {
            teleportChainMove = default;
            teleportChainDestination = Vector2.Zero;
            teleportChainDirection = 0f;
            teleportChainRushDistance = 0f;
        }
        if (!actionChanged) return;
        teleportChainPreviewCreated = false;
        if (chainedTeleportAction && CurrentMove == Move.TeleportSpearRush &&
            queuedSpearParticle?.IsAlive == true) {
            spearParticle = queuedSpearParticle;
            queuedSpearParticle = null;
        }
        else ReleaseQueuedSpear();
        if (!chainedTeleportAction || !previewed) return;
        teleportWarningCreated = true;
        spearWarningCreated = CurrentMove == Move.TeleportSpearRush;
        teleportHammerWarningCreated = CurrentMove == Move.TeleportHammerSlam;
    }

    private bool teleportHammerWarningCreated;
    private bool TryPlanTeleportAttack(Player target, Move attack,
        out Vector2 destination, out float direction, out float rushDistance,
        Vector2? origin = null) {
        destination = Vector2.Zero;
        direction = 0f;
        rushDistance = 0f;
        Vector2 start = origin ?? NPC.Center;
        if (attack == Move.TeleportHammerSlam) {
            float predictedX = target.Center.X +
                MathHelper.Clamp(target.velocity.X * 14f, -200f, 200f) +
                SplitSideOffset();
            float predictedY = target.Top.Y +
                MathHelper.Clamp(target.velocity.Y * 8f, -100f, 100f);
            float apexY = start.Y - PredictHammerRise(target, start);
            int[] offsets = { 0, 48, -48, 96, -96, 144, -144 };
            foreach (float lower in new[] { 0f, 30f, 60f }) {
                foreach (int offset in offsets) {
                    Vector2 center = new(predictedX + offset, apexY + lower);
                    if (center.X < NPC.width || center.X > Main.maxTilesX * 16f - NPC.width ||
                        center.Y < NPC.height || center.Y > predictedY - NPC.height ||
                        Collision.SolidCollision(center - new Vector2(NPC.width, NPC.height) * 0.5f,
                            NPC.width, NPC.height))
                        continue;
                    destination = center;
                    return true;
                }
            }
            return false;
        }

        if (!TryFindTeleportDestination(target, out destination)) return false;
        Vector2 aim = target.Center + target.velocity * 9f - destination;
        // The destination is placed by its feet. On the same platform, aim
        // horizontally instead of using the two characters' different heights.
        if (Math.Abs(target.Bottom.Y - (destination.Y + NPC.height * 0.5f)) < 30f)
            aim.Y = 0f;
        if (aim.LengthSquared() < 1f) return false;
        direction = aim.ToRotation();
        Vector2 unit = aim.SafeNormalize(Vector2.UnitX);
        // Match the ordinary spear's 34-tick speed curve (about 890 pixels
        // before its distance scale), allowing only a small endpoint adjustment.
        Vector2 ordinaryAim = target.Center + Vector2.UnitX * SplitSideOffset() +
            target.velocity * 9f - start;
        if (Math.Abs(target.Bottom.Y - (start.Y + NPC.height * 0.5f)) < 30f)
            ordinaryAim.Y = 0f;
        float preferred = 890f * MathHelper.Clamp(ordinaryAim.Length() / 520f,
            0.85f, 1.12f);
        for (float trial = preferred; trial >= preferred - 80f; trial -= 16f) {
            Vector2 end = destination + unit * trial;
            if (end.X < NPC.width || end.X > Main.maxTilesX * 16f - NPC.width ||
                end.Y < NPC.height || end.Y > Main.maxTilesY * 16f - NPC.height)
                continue;
            if (Collision.SolidCollision(end - new Vector2(NPC.width, NPC.height) * 0.5f,
                    NPC.width, NPC.height)) continue;
            rushDistance = trial;
            return true;
        }
        return false;
    }

    private bool PlanTeleportAttack(Player target, Move attack) {
        if (!TryPlanTeleportAttack(target, attack, out Vector2 destination,
                out float direction, out float rushDistance)) return false;
        teleportDestination = destination;
        teleportReady = true;
        MoveValue = direction;
        teleportRushDistance = rushDistance;
        NPC.velocity = Vector2.Zero;
        motionMode = MotionMode.Scripted;
        NPC.netUpdate = true;
        return true;
    }

    private Vector2 FindTeleportShurikenDestination(Player target) {
        int heading = Math.Abs(target.velocity.X) > 0.8f
            ? Math.Sign(target.velocity.X) : target.direction;
        if (heading == 0) heading = 1;
        if (IsEcho) heading = 1;
        else if (splitTimer >= 90f && splitMergeStart < 0f) heading = -1;
        float x = target.Center.X + MathHelper.Clamp(target.velocity.X * 10f,
            -110f, 110f) + heading * 350f;
        float y = target.Top.Y + MathHelper.Clamp(target.velocity.Y * 7f,
            -80f, 80f) - 280f;
        foreach (float sideOffset in new[] { 0f, -48f, 48f, -96f, 96f }) {
            foreach (float upperOffset in new[] { 0f, -48f, 48f }) {
                Vector2 center = new(x + heading * sideOffset, y + upperOffset);
                if (center.X < NPC.width || center.X > Main.maxTilesX * 16f - NPC.width ||
                    center.Y < NPC.height || center.Y > Main.maxTilesY * 16f - NPC.height ||
                    Collision.SolidCollision(center - new Vector2(NPC.width, NPC.height) * 0.5f,
                        NPC.width, NPC.height)) continue;
                return center;
            }
        }
        // Keep the teleport attack's timing and projectile pattern when terrain
        // leaves no open spot around the player. The current position is safe.
        return NPC.Center;
    }

    private void PlanTeleportShuriken(Player target) {
        teleportDestination = FindTeleportShurikenDestination(target);
        teleportReady = true;
        NPC.velocity = Vector2.Zero;
        motionMode = MotionMode.Scripted;
        NPC.netUpdate = true;
    }

    private void TeleportShuriken(Player target) {
        NPC.damage = 0;
        if (!teleportReady) {
            motionMode = MotionMode.Scripted;
            NPC.velocity = Vector2.Zero;
            return;
        }
        ShowTeleportWarning();
        if (Timer < 48f) {
            motionMode = MotionMode.Scripted;
            NPC.velocity = Vector2.Zero;
            return;
        }
        if (!teleportOccurred) PerformTeleport(false);
        if (Timer < 60f) {
            motionMode = MotionMode.Controlled;
            NPC.velocity = Vector2.Zero;
            return;
        }
        if (PassedTime(60f)) {
            MoveValue = (target.Center + target.velocity * 5f -
                (NPC.Center - Vector2.UnitY * NPC.height * 0.08f)).ToRotation();
            NPC.netUpdate = true;
        }
        ShurikenFan(target);
    }

    private Move ResolveTeleportAttack(Player target, Move attack) {
        if (attack != Move.TeleportSpearRush) return attack;
        if (!TryPlanTeleportAttack(target, attack, out Vector2 destination,
                out float direction, out float rushDistance))
            return Vector2.Distance(NPC.Center, target.Center) > 900f
                ? Move.UmbrellaRush : Move.TeleportShuriken;
        Vector2 endpoint = destination + direction.ToRotationVector2() * rushDistance;
        return Vector2.DistanceSquared(destination, NPC.Center) < 300f * 300f ||
            Vector2.DistanceSquared(endpoint, NPC.Center) < 200f * 200f
            ? Move.SpearRush : attack;
    }

    private void TeleportSpearRush(Player target) {
        NPC.damage = 0;
        if (!teleportReady) { motionMode = MotionMode.Scripted; NPC.velocity = Vector2.Zero; return; }
        ShowTeleportWarning();
        if (Timer < 54f) {
            motionMode = MotionMode.Scripted;
            NPC.velocity = Vector2.Zero;
            return;
        }
        // This destination was found on a surface, so keep its grounded state.
        // The rush itself can still pass through blocks; after it ends the
        // shared terrain pass will catch the platform under the player.
        if (!teleportOccurred) PerformTeleport(true);
        if (Timer < 78f) {
            motionMode = MotionMode.Scripted;
            NPC.velocity = Vector2.Zero;
            return;
        }
        if (PassedTime(78f)) {
            KingSlimeSound.PlayRush(NPC.Center);
            GroundEffects(heavy: true, landing: false);
        }
        if (previousTimer < 112f) {
            motionMode = MotionMode.Rush;
            float now = MathHelper.Clamp((Timer - 78f) / 34f, 0f, 1f);
            float before = MathHelper.Clamp((previousTimer - 78f) / 34f, 0f, 1f);
            float easedNow = now * now * (3f - 2f * now);
            float easedBefore = before * before * (3f - 2f * before);
            Vector2 direction = new((float)Math.Cos(MoveValue), (float)Math.Sin(MoveValue));
            NPC.velocity = direction * (teleportRushDistance *
                (easedNow - easedBefore) / appliedTempo);
            return;
        }
        NPC.velocity.X *= grounded ? 0.78f : 0.94f;
        if (SlotFinished(135f) && grounded) AdvancePattern(target);
    }

    private void TeleportHammerSlam(Player target) {
        NPC.damage = 0;
        if (!teleportReady) { motionMode = MotionMode.Scripted; NPC.velocity = Vector2.Zero; return; }
        ShowTeleportWarning();
        ShowTeleportHammerWarning(target);
        if (Timer < 48f) {
            motionMode = MotionMode.Scripted;
            NPC.velocity = Vector2.Zero;
            return;
        }
        if (!teleportOccurred) PerformTeleport(false);
        if (Timer < 81f) {
            motionMode = MotionMode.Scripted;
            NPC.velocity = Vector2.Zero;
            if (PassedTime(63f)) SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
            return;
        }
        if (PassedTime(81f)) {
            NPC.velocity = new Vector2(0f, 5f);
            SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
        }
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
            NPC.velocity.X = 0f;
            NPC.velocity.Y = Math.Min(NPC.velocity.Y + 2.6f, 30f);
            if (Timer >= 108f && !grounded) Timer = 108f;
            return;
        }
        NPC.velocity.X *= grounded ? 0.72f : 0.95f;
        if (PassedTime(120f)) ReleaseHammer();
        if (SlotFinished(135f) && grounded) AdvancePattern(target);
    }

    private void UpdateTeleportSpearVisuals() {
        if (spearParticle?.IsAlive != true) {
            spearParticle = ParticleManager.Instance.NewParticle<KingSlimeSpearParticle>(
                NPC.Center, Vector2.Zero, alpha: 0f, scale: 2.5f);
            spearParticle.SetHolder(NPC);
        }
        float turn = GuidaUtils.Smoothstep(8f, 42f, Timer);
        float angle = MoveValue + MathHelper.TwoPi * (1f - turn);
        Vector2 direction = new((float)Math.Cos(angle), (float)Math.Sin(angle));
        float pullBack = 30f * GuidaUtils.Smoothstep(42f, 70f, Timer) *
            GuidaUtils.Smoothstep(84f, 78f, Timer);
        spearParticle.position = NPC.Center + direction *
            (NPC.width * 0.42f + 8f - pullBack) - Vector2.UnitY * NPC.height * 0.09f;
        spearParticle.rotation = angle + MathHelper.PiOver4;
        spearParticle.alpha = GuidaUtils.Smoothstep(4f, 20f, Timer);
        spearParticle.tipStarOpacity = GuidaUtils.Smoothstep(32f, 52f, Timer);
        spearParticle.tipStarRotation = MathHelper.TwoPi * 1.25f *
            GuidaUtils.Smoothstep(32f, 78f, Timer);
        spearParticle.tipRushStreak = Timer >= 78f;
        spearParticle.timeLeft = 2;

        if (Timer >= 8f && Timer < 78f && !spearWarningCreated) {
            WarningLineParticle warning = ParticleManager.Instance.NewParticle<WarningLineParticle>(
                teleportDestination, Vector2.Zero);
            spearWarningCreated = true;
            warning.lineLength = teleportRushDistance;
            StyleBodyWarning(warning);
            warning.time = Math.Max(1f, (78f - Timer) / appliedTempo);
            warning.arrowSpeed = appliedTempo;
            warning.lineRotation = MoveValue + MathHelper.PiOver2;
        }
        if (Timer >= 78f && previousTimer < 112f) {
            UpdateBodyMotionTrail();
            UpdateSpearTipTrail();
            SpawnSpearRushEffects(new Vector2((float)Math.Cos(MoveValue),
                (float)Math.Sin(MoveValue)), PassedTime(78f), PassedTime(92f));
        }
    }

    private void ShowTeleportHammerWarning(Player target) {
        if (Timer < 10f || Timer >= 81f || teleportHammerWarningCreated ||
            Main.netMode == NetmodeID.Server) return;
        WarningLineParticle warning = ParticleManager.Instance.NewParticle<WarningLineParticle>(
            teleportDestination + Vector2.UnitY * NPC.height * 0.5f, Vector2.Zero);
        teleportHammerWarningCreated = true;
        warning.lineRotation = MathHelper.Pi;
        warning.lineLength = MathHelper.Clamp(target.Bottom.Y - teleportDestination.Y -
            NPC.height * 0.5f + 112f, 180f, 880f);
        StyleVerticalBodyWarning(warning);
        warning.time = Math.Max(1f, (81f - Timer) / appliedTempo);
        warning.arrowSpeed = appliedTempo;
    }
}
