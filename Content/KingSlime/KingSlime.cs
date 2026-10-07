using System;
using System.IO;
using GuidaSharedCode;
using ReverieMod.Content.Particles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace ReverieMod.Content.KingSlime;

// An independent boss; the vanilla King Slime is not modified.
[AutoloadBossHead]
public partial class KingSlime : ModNPC {
    private enum Move { Hops, HighLeap, SpearRush, GrappleSlam, DrinkPotion, RopeGrenades, ShurikenFan, Split, UmbrellaRush, HammerSlam, Teleport, ShortswordThrust, FireWand, SlimeStaffRain, Intro, TeleportSpearRush, TeleportHammerSlam, PhaseTwoPotions, TeleportShuriken, Ultimate, Boomerang }
    // Attacks select an intent; the shared movement pass applies gravity and platform rules.
    private enum MotionMode { Natural, Controlled, Lift, FastFall, Dive, Rush, Merge, Scripted, GrappleFall }
    [Flags]
    private enum ExtraSyncFlags : ushort {
        Grapple = 1 << 0,
        Rope = 1 << 1,
        Split = 1 << 2,
        SpearRush = 1 << 3,
        UmbrellaRush = 1 << 4,
        TeleportRush = 1 << 5,
        Teleport = 1 << 6,
        Death = 1 << 7,
        Ultimate = 1 << 8,
        TeleportChain = 1 << 9,
        DamageReduction = 1 << 10
    }

    private Move CurrentMove { get => (Move)(int)NPC.ai[0]; set => NPC.ai[0] = (float)value; }
    private ref float Timer => ref NPC.ai[1];
    private ref float MoveValue => ref NPC.ai[2];
    // The echo uses ai[3] to identify its owner; both share action boundaries.
    private bool IsEcho => NPC.ai[3] < 0f;
    private bool CanPursueThroughTerrain => NPC.localAI[0] == 1f &&
        (CurrentMove != Move.RopeGrenades || Timer >= 140f);

    // The crown and compression are client-side drawing state.
    private float landingCompression;
    private KingSlimeCrownParticle crownParticle;
    private KingSlimeBodyTwistParticle bodyTwistParticle;
    private Vector2 bodyVisualScale;
    private bool bodyVisualInitialized;
    private float bodyShear;
    private float bodyShearVelocity;
    private float bodyRipple;
    private float bodyRippleVelocity;
    private Vector2 previousBodyVelocity;
    private bool bodyMotionInitialized;
    private bool grounded;
    private bool solidPassageActive;
    private MotionMode motionMode;
    private int wallImpactDirection;
    private float previousTimer;
    private int jumpTrailTicks;
    private KingSlimeSpearParticle spearParticle;
    private bool spearWarningCreated;
    private TrailParticle spearTipTrail;
    private Vector2 grappleAnchor;
    private KingSlimeGrappleParticle grappleParticle;
    private KingSlimePotionParticle potionParticle;
    private KingSlimeUmbrellaParticle umbrellaParticle;
    private KingSlimeHammerParticle hammerParticle;
    private bool hammerWarningCreated;
    private bool umbrellaWarningCreated;
    private KingSlimeCopperShortswordParticle shortswordParticle;
    private KingSlimeStaffParticle fireWandParticle;
    private KingSlimeStaffParticle slimeStaffParticle;
    private KingSlimePropParticle boomerangParticle;
    private TrailParticle bodyMotionTrail;
    private KingSlimePropParticle chestParticle;
    private KingSlimePropParticle woodChestParticle;
    private KingSlimePropParticle ninjaParticle;
    private int woodHostIndex = -1;
    private int woodTransferTicks;
    private KingSlimeRopeParticle ropeParticle;
    private Vector2 ropeLaunchPosition;
    private byte tempoStage;
    private float appliedTempo;
    private float splitTimer;
    private float splitMergeStart = -1f;
    private bool mergeIntoTwin;
    private bool pendingSplit;
    private int splitTwinIndex = -1;
    private float measureTicks;
    private bool measureBoundaryThisTick;
    private float visualTicks;
    private float spearRushDistanceScale = 1f;
    private float umbrellaRushDistanceScale = 1f;
    private float teleportRushDistance;
    private bool introComplete;
    private bool introStarted;
    private bool introWarningCreated;
    private int healthSlimeDrops;
    private int healthSlimeThresholdsReached;

    private bool IntroActive => !introComplete && !IsEcho && !NPC.IsABestiaryIconDummy;

    internal float TempoMultiplier => dying ? 1f : CurrentMove == Move.Ultimate ? 1.2f : tempoStage switch {
        1 => 1.2f,
        _ => 1f
    };

    private bool PassedTime(float time) => previousTimer < time && Timer >= time;

    public override string Texture => ModAsset.KingSlimeBody_Mod;
    public override string BossHeadTexture => ModAsset.KingSlimeBossHead_Mod;

    public override void SetStaticDefaults() {
        Main.npcFrameCount[Type] = 3;
        NPCID.Sets.BossBestiaryPriority.Add(Type);
        NPCID.Sets.MPAllowedEnemies[Type] = true;
    }

    public override void SetDefaults() {
        NPC.width = 124;
        NPC.height = 82;
        NPC.damage = 40;
        NPC.defense = 8;
        NPC.lifeMax = 2400;
        NPC.knockBackResist = 0f;
        NPC.value = Item.buyPrice(gold: 4);
        NPC.boss = true;
        NPC.noGravity = true;
        NPC.noTileCollide = true;
        NPC.netAlways = true;
        NPC.HitSound = KingSlimeSound.GelHit;
        // The staged death plays its own burst at the first break and final explosion.
        NPC.DeathSound = null;
        Music = MusicLoader.GetMusicSlot(Mod, "Assets/Music/KingSlime");
    }

    public override void OnSpawn(IEntitySource source) {
        NPC.ai[0] = (float)Move.Intro;
    }

    public override bool ModifyCollisionData(Rectangle victimHitbox, ref int immunityCooldownSlot,
        ref MultipliableFloat damageMultiplier, ref Rectangle npcHitbox) {
        if (CurrentMove == Move.ShortswordThrust && Timer >= 12f && Timer < 32f) {
            Vector2 tip = ShortswordHeldPosition() + ShortswordDirection() * 34f;
            npcHitbox.Inflate(-6, -4);
            npcHitbox = Rectangle.Union(npcHitbox,
                new Rectangle((int)tip.X - 21, (int)tip.Y - 21, 42, 42));
            return true;
        }
        npcHitbox.Inflate(-6, -4);
        return true;
    }

    public override void SendExtraAI(BinaryWriter writer) {
        ExtraSyncFlags flags = 0;
        if (CurrentMove == Move.GrappleSlam) flags |= ExtraSyncFlags.Grapple;
        if (CurrentMove == Move.RopeGrenades) flags |= ExtraSyncFlags.Rope;
        if (splitTimer > 0f) flags |= ExtraSyncFlags.Split;
        if (CurrentMove == Move.SpearRush) flags |= ExtraSyncFlags.SpearRush;
        if (CurrentMove == Move.UmbrellaRush) flags |= ExtraSyncFlags.UmbrellaRush;
        if (CurrentMove == Move.TeleportSpearRush) flags |= ExtraSyncFlags.TeleportRush;
        if (teleportReady) flags |= ExtraSyncFlags.Teleport;
        if (dying) flags |= ExtraSyncFlags.Death;
        if (CurrentMove == Move.Ultimate && !dying) flags |= ExtraSyncFlags.Ultimate;
        if (teleportChainReady || chainedTeleportAction) flags |= ExtraSyncFlags.TeleportChain;
        if (!IsEcho && currentDamageReduction > 0f) flags |= ExtraSyncFlags.DamageReduction;

        writer.Write((byte)NPC.localAI[0]); // airborne
        writer.Write((byte)NPC.localAI[2]); // wall rebounds
        writer.Write(grounded);
        writer.Write(solidPassageActive);
        writer.Write((ushort)flags);
        if ((flags & ExtraSyncFlags.Grapple) != 0) {
            writer.Write(grappleAnchor.X);
            writer.Write(grappleAnchor.Y);
        }
        if ((flags & ExtraSyncFlags.Rope) != 0) {
            writer.Write(ropeLaunchPosition.X);
            writer.Write(ropeLaunchPosition.Y);
        }
        if ((flags & ExtraSyncFlags.Split) != 0) {
            writer.Write(splitTimer);
            writer.Write(splitMergeStart);
            writer.Write((short)splitTwinIndex);
            writer.Write(mergeIntoTwin);
        }
        writer.Write(measureTicks);
        writer.Write(introComplete);
        writer.Write(tempoStage);
        if ((flags & ExtraSyncFlags.DamageReduction) != 0)
            writer.Write(currentDamageReduction);
        writer.Write(actionElapsed);
        writer.Write(actionSerial);
        if ((flags & ExtraSyncFlags.SpearRush) != 0) writer.Write(spearRushDistanceScale);
        if ((flags & ExtraSyncFlags.UmbrellaRush) != 0) writer.Write(umbrellaRushDistanceScale);
        if ((flags & ExtraSyncFlags.TeleportRush) != 0) writer.Write(teleportRushDistance);
        if ((flags & ExtraSyncFlags.Teleport) != 0) {
            writer.Write(teleportDestination.X);
            writer.Write(teleportDestination.Y);
        }
        if ((flags & ExtraSyncFlags.Death) != 0) WriteDeathState(writer);
        if ((flags & ExtraSyncFlags.Ultimate) != 0) WriteUltimateState(writer);
        if ((flags & ExtraSyncFlags.TeleportChain) != 0) WriteTeleportChain(writer);
    }

    public override void ReceiveExtraAI(BinaryReader reader) {
        NPC.localAI[0] = reader.ReadByte();
        NPC.localAI[2] = reader.ReadByte();
        grounded = reader.ReadBoolean();
        solidPassageActive = reader.ReadBoolean();
        ExtraSyncFlags flags = (ExtraSyncFlags)reader.ReadUInt16();
        grappleAnchor = (flags & ExtraSyncFlags.Grapple) != 0
            ? new Vector2(reader.ReadSingle(), reader.ReadSingle()) : Vector2.Zero;
        ropeLaunchPosition = (flags & ExtraSyncFlags.Rope) != 0
            ? new Vector2(reader.ReadSingle(), reader.ReadSingle()) : Vector2.Zero;
        if ((flags & ExtraSyncFlags.Split) != 0) {
            splitTimer = reader.ReadSingle();
            splitMergeStart = reader.ReadSingle();
            splitTwinIndex = reader.ReadInt16();
            mergeIntoTwin = reader.ReadBoolean();
        }
        else {
            splitTimer = 0f;
            splitMergeStart = -1f;
            splitTwinIndex = -1;
            mergeIntoTwin = false;
        }
        measureTicks = reader.ReadSingle();
        introComplete = reader.ReadBoolean();
        tempoStage = reader.ReadByte();
        currentDamageReduction = (flags & ExtraSyncFlags.DamageReduction) != 0
            ? reader.ReadSingle() : 0f;
        actionElapsed = reader.ReadSingle();
        ushort receivedActionSerial = reader.ReadUInt16();
        bool actionChanged = actionSerial != receivedActionSerial;
        if (actionChanged) {
            teleportOccurred = false;
            teleportWarningCreated = false;
            teleportHammerWarningCreated = false;
            spearWarningCreated = false;
            hammerWarningCreated = false;
            umbrellaWarningCreated = false;
            ultimateWarningMask = 0;
            ultimateLeg = -1;
        }
        actionSerial = receivedActionSerial;
        spearRushDistanceScale = (flags & ExtraSyncFlags.SpearRush) != 0
            ? reader.ReadSingle() : 1f;
        umbrellaRushDistanceScale = (flags & ExtraSyncFlags.UmbrellaRush) != 0
            ? reader.ReadSingle() : 1f;
        teleportRushDistance = (flags & ExtraSyncFlags.TeleportRush) != 0
            ? reader.ReadSingle() : 0f;
        teleportReady = (flags & ExtraSyncFlags.Teleport) != 0;
        teleportDestination = teleportReady
            ? new Vector2(reader.ReadSingle(), reader.ReadSingle()) : Vector2.Zero;
        ReadDeathState(reader, (flags & ExtraSyncFlags.Death) != 0);
        ReadUltimateState(reader, (flags & ExtraSyncFlags.Ultimate) != 0);
        if (appliedTempo == 0f) appliedTempo = TempoMultiplier;
        ReadTeleportChain(reader, actionChanged, (flags & ExtraSyncFlags.TeleportChain) != 0);
    }

    public override bool? CanFallThroughPlatforms() {
        if (NPC.target < 0 || NPC.target >= Main.maxPlayers) return false;
        // A target moving below the arena must not pull a windup through its
        // platform. During the rope attack, only the planned climb may move it.
        if (!CanPursueThroughTerrain) return false;
        Player target = Main.player[NPC.target];
        // All moves use the same next-step test, so fast falls stop passing through
        // the platform under the player before the feet cross its surface.
        return target.active && !target.dead &&
            target.Bottom.Y > NPC.Bottom.Y + Math.Max(NPC.velocity.Y, 0f) + 2f &&
            NPC.velocity.Y >= -0.5f;
    }

    public override void ModifyNPCLoot(NPCLoot npcLoot) {
        npcLoot.Add(ItemDropRule.Common(ItemID.Gel, 1, 40, 80));
    }

    public override bool? DrawHealthBar(byte hbPosition, ref float scale, ref Vector2 position) =>
        IsEcho ? false : null;

    public override void BossHeadSlot(ref int index) {
        if (IsEcho) index = -1;
    }

    public override void HitEffect(NPC.HitInfo hit) {
        if (Main.netMode == NetmodeID.Server) return;
        if (NPC.life <= 0 && !IsEcho) Splash(12, 3f);
        else if (Main.rand.NextBool(3)) Splash(2, 1.5f);
    }

    public override void OnKill() {
        DropHealthSlimes();
        if (Main.netMode != NetmodeID.Server) {
            if (dying) CreateDeathBurst();
            ReleaseCrown();
            ReleaseUmbrella();
            ReleaseHammer();
            ReleaseFireWand();
            ReleaseSlimeStaff();
        }
        if (!IsEcho && Main.netMode != NetmodeID.MultiplayerClient) RemoveSplitTwin();
    }

    public override void AI() {
        if (dying) {
            DeathAnimation();
            return;
        }
        KingSlime owner = null;
        if (IsEcho) {
            int ownerIndex = -(int)NPC.ai[3] - 1;
            if (ownerIndex < 0 || ownerIndex >= Main.maxNPCs ||
                !Main.npc[ownerIndex].active || Main.npc[ownerIndex].type != Type ||
                Main.npc[ownerIndex].ModNPC is not KingSlime royalOwner || royalOwner.IsEcho) {
                NPC.active = false;
                if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
                return;
            }
            owner = royalOwner;
            NPC.realLife = ownerIndex;
            NPC.boss = false;
            NPC.life = owner.NPC.life;
            NPC.lifeMax = owner.NPC.lifeMax;
            NPC.localAI[3] += owner.TempoMultiplier;
            NPC.dontTakeDamage = NPC.localAI[3] < 9f || owner.splitMergeStart >= 0f;
        }
        else if (splitTimer > 0) {
            splitTimer += TempoMultiplier;
        }

        // Keep the collision box and the drawing scale together as the slime loses mass.
        // Its feet and horizontal center stay in place while the box contracts.
        float size = 1.035f - MathHelper.Clamp(
            1f - NPC.life / (float)Math.Max(1, NPC.lifeMax), 0f, 1f) * 0.315f;
        if (IsEcho) {
            float birth = GuidaUtils.Smoothstep(0f, 23f, NPC.localAI[3]);
            float merge = owner.splitMergeStart >= 0f
                ? GuidaUtils.Smoothstep(owner.splitMergeStart,
                    owner.splitMergeStart + 90f, owner.splitTimer) : 0f;
            // Keep the collision probe valid even while the sprite fades in or out.
            size *= Math.Max(0.38f, 0.78f * birth * (1f - merge));
            NPC.alpha = (int)(255f * (1f - birth * (1f - merge)));
        }
        else if (splitTimer > 0) {
            float amount = GuidaUtils.Smoothstep(0f, 62f, splitTimer) *
                (splitMergeStart >= 0f
                    ? 1f - GuidaUtils.Smoothstep(splitMergeStart,
                        splitMergeStart + 90f, splitTimer) : 1f);
            size *= 1f - 0.22f * amount;
        }
        int width = (int)Math.Round(124f * size);
        int height = (int)Math.Round(84f * size) - 2;
        if (NPC.width != width || NPC.height != height) {
            Vector2 bottom = NPC.Bottom;
            float centerX = NPC.Center.X;
            NPC.width = width;
            NPC.height = height;
            NPC.position = new Vector2(centerX - width * 0.5f, bottom.Y - height);
            if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
        }
        NPC.scale = size;

        NPC.TargetClosest(false);
        Player target = Main.player[NPC.target];
        if (!target.active || target.dead || Vector2.DistanceSquared(NPC.Center, target.Center) > 3200f * 3200f) {
            NPC.velocity = Vector2.Lerp(NPC.velocity, new Vector2(0, -8), 0.05f);
            appliedTempo = 0f;
            NPC.timeLeft = Math.Min(NPC.timeLeft, 45);
            ReleaseSpear();
            ReleaseGrapple();
            ReleasePotion();
            ReleaseRope();
            ReleaseUmbrella();
            ReleaseHammer();
            ReleaseCrown();
            return;
        }

        DropHealthSlimes();

        if (IsEcho) tempoStage = owner.tempoStage;
        UpdateCombatDurability();
        float tempo = TempoMultiplier;
        NPC.velocity /= appliedTempo == 0f ? tempo : appliedTempo;
        appliedTempo = tempo;
        visualTicks += tempo;
        SpawnPhaseStreak();

        // 160 BPM in 4/4: one measure lasts 90 musical ticks.
        measureBoundaryThisTick = false;
        if (!IsEcho && !NPC.IsABestiaryIconDummy) {
            int oldMeasure = (int)(measureTicks / 90f);
            measureTicks += tempo;
            int newMeasure = (int)(measureTicks / 90f);
            measureBoundaryThisTick = newMeasure > oldMeasure;
        }
        else if (IsEcho) {
            measureTicks = owner.measureTicks;
            measureBoundaryThisTick = owner.measureBoundaryThisTick;
        }

        if (!IsEcho && Main.netMode != NetmodeID.MultiplayerClient) {
            if (introComplete && tempoStage == 0 && splitTimer == 0f &&
                CurrentMove != Move.Split &&
                NPC.life * 10 <= NPC.lifeMax * 7)
                pendingSplit = true;
        }

        if (IntroActive && Main.netMode != NetmodeID.MultiplayerClient)
            UpdateIntroTimeline(target);

        if (IsEcho && owner.splitMergeStart >= 0f) {
            MergeEcho(owner, target);
            return;
        }

        if (NPC.localAI[0] == 1 && grounded) NPC.localAI[1]++;
        else NPC.localAI[1] = 0;
        landingCompression *= 0.83f;
        NPC.damage = CurrentMove == Move.Hops ||
            (CurrentMove == Move.HighLeap && Timer >= 34 && NPC.localAI[0] == 1) ? ContactDamage(40) : 0;

        UpdateBodyRotation();

        previousTimer = Timer;
        Timer += tempo;
        actionElapsed += tempo;
        motionMode = MotionMode.Natural;
        bool syncedSplitAction = IsEcho && SyncSplitAction(owner, target);
        if (!syncedSplitAction) {
            PrepareTeleportChain(target);
            switch (CurrentMove) {
            case Move.Hops: Hops(target); break;
            case Move.HighLeap: HighLeap(target); break;
            case Move.SpearRush: SpearRush(target); break;
            case Move.GrappleSlam: GrappleSlam(target); break;
            case Move.DrinkPotion: DrinkPotion(target); break;
            case Move.PhaseTwoPotions: PhaseTwoPotions(target); break;
            case Move.RopeGrenades: RopeGrenades(target); break;
            case Move.ShurikenFan: ShurikenFan(target); break;
            case Move.Split: Split(target); break;
            case Move.UmbrellaRush: UmbrellaRush(target); break;
            case Move.HammerSlam: HammerSlam(target); break;
            case Move.Teleport: Teleport(target); break;
            case Move.TeleportSpearRush: TeleportSpearRush(target); break;
            case Move.TeleportHammerSlam: TeleportHammerSlam(target); break;
            case Move.TeleportShuriken: TeleportShuriken(target); break;
            case Move.ShortswordThrust: ShortswordThrust(target); break;
            case Move.FireWand: FireWand(target); break;
            case Move.SlimeStaffRain: SlimeStaffRain(target); break;
            case Move.Intro: Intro(target); break;
            case Move.Ultimate: Ultimate(target); break;
            case Move.Boomerang: Boomerang(target); break;
            }
        }
        UpdateCinematicCamera();
        if (CurrentMove == Move.SpearRush)
            NPC.damage = Timer >= 78 && Timer < 112 ? ContactDamage(50) : 0;
        if (CurrentMove == Move.GrappleSlam)
            NPC.damage = Timer >= 83 && Timer < 113 ? ContactDamage(50) : 0;
        if (CurrentMove == Move.UmbrellaRush)
            NPC.damage = Timer >= 80f && Timer < 106f ? ContactDamage(50) : 0;
        if (CurrentMove == Move.HammerSlam)
            NPC.damage = Timer >= 81f && Timer < 109f ? ContactDamage(50) : 0;
        if (CurrentMove == Move.TeleportSpearRush)
            NPC.damage = Timer >= 78f && previousTimer < 112f ? ContactDamage(50) : 0;
        if (CurrentMove == Move.TeleportHammerSlam)
            NPC.damage = Timer >= 81f && Timer < 109f && !grounded ? ContactDamage(50) : 0;
        if (CurrentMove == Move.ShortswordThrust)
            NPC.damage = Timer >= 12f && Timer < 32f ? ContactDamage(40) : 0;
        if (CurrentMove == Move.Split || motionMode is MotionMode.Merge or MotionMode.Lift)
            NPC.damage = 0;
        MoveWithTerrain(target);
    }

    private void UpdateBodyRotation() {
        if (dying) {
            float instability = MathHelper.Clamp(deathTimer / 120f, 0f, 1f);
            float wobble = (float)Math.Sin(deathTimer * 0.32f) *
                (0.07f + 0.12f * instability);
            NPC.rotation = MathHelper.Lerp(NPC.rotation, wobble, 0.7f);
            return;
        }
        float horizontalMotion = NPC.velocity.X * appliedTempo;
        float potionPoseTime = CurrentMove == Move.PhaseTwoPotions ? Timer % 30f : Timer;
        float targetTilt = IsTeleporting && Timer < TeleportMoment
            ? 0f
            : (CurrentMove == Move.DrinkPotion || CurrentMove == Move.PhaseTwoPotions)
            ? -MoveValue * 0.08f * GuidaUtils.Smoothstep(12f, 22f, potionPoseTime) *
                GuidaUtils.Smoothstep(35f, 29f, potionPoseTime)
            : (CurrentMove == Move.SpearRush || CurrentMove == Move.TeleportSpearRush) &&
                Timer >= 78 && Timer < 112
                ? MathHelper.Clamp(horizontalMotion * 0.009f, -0.18f, 0.18f)
            : CurrentMove == Move.UmbrellaRush && Timer >= 76f && Timer < 106f
                ? MathHelper.Clamp(horizontalMotion * 0.008f, -0.20f, 0.20f)
            : MathHelper.Clamp(horizontalMotion * 0.014f, -0.12f, 0.12f);
        NPC.rotation = MathHelper.Lerp(NPC.rotation, targetTilt, 0.13f);
    }

    private Vector2 BodyTargetScale() {
        if (dying) {
            float instability = GuidaUtils.Smoothstep(8f, 125f, deathTimer);
            float stretchX = 1f + (float)Math.Sin(deathTimer * 0.35f) *
                (0.16f + instability * 0.35f) +
                (float)Math.Sin(deathTimer * 0.79f) * 0.07f * instability;
            float stretchY = 1f + (float)Math.Sin(deathTimer * 0.43f + 1.7f) *
                (0.15f + instability * 0.33f) -
                (float)Math.Sin(deathTimer * 0.62f) * 0.07f * instability;
            return new Vector2(NPC.scale * Math.Max(0.48f, stretchX),
                NPC.scale * Math.Max(0.48f, stretchY));
        }
        float flightStretch = MathHelper.Clamp(Math.Abs(NPC.velocity.Y) / 56f, 0, 0.28f);
        if (CurrentMove == Move.GrappleSlam && Timer >= 21 && Timer < 83)
            flightStretch += 0.15f * GuidaUtils.Smoothstep(21, 42, Timer) *
                GuidaUtils.Smoothstep(83, 72, Timer);
        if (CurrentMove == Move.RopeGrenades && Timer >= 32 && Timer < 82)
            flightStretch += 0.12f * GuidaUtils.Smoothstep(32, 52, Timer) *
                GuidaUtils.Smoothstep(82, 70, Timer);
        if ((CurrentMove == Move.ShurikenFan || CurrentMove == Move.TeleportShuriken) &&
            Timer >= 60 && Timer < 97)
            flightStretch += 0.08f * GuidaUtils.Smoothstep(60, 69, Timer) *
                GuidaUtils.Smoothstep(97, 90, Timer);
        float charge = CurrentMove switch {
            Move.Hops when NPC.localAI[0] == 0 && MoveValue == 0f =>
                GuidaUtils.Smoothstep(2f, 12f, Timer) * 0.20f,
            Move.HighLeap when Timer < 34 => GuidaUtils.Smoothstep(5, 34, Timer) * 0.37f,
            Move.SpearRush or Move.TeleportSpearRush when Timer < 78 =>
                GuidaUtils.Smoothstep(8, 72, Timer) * 0.30f,
            Move.GrappleSlam when Timer < 21 => GuidaUtils.Smoothstep(2, 19, Timer) * 0.30f,
            Move.DrinkPotion when Timer >= 16 && Timer < 36 =>
                0.13f * GuidaUtils.Smoothstep(16, 25, Timer) * GuidaUtils.Smoothstep(36, 30, Timer),
            Move.PhaseTwoPotions => 0.13f * GuidaUtils.Smoothstep(11f, 19f, Timer % 30f) *
                GuidaUtils.Smoothstep(26f, 21f, Timer % 30f),
            Move.RopeGrenades when Timer < 32 => GuidaUtils.Smoothstep(5, 29, Timer) * 0.17f,
            Move.ShurikenFan when Timer < 12 => GuidaUtils.Smoothstep(2, 12, Timer) * 0.24f,
            Move.UmbrellaRush when Timer >= 62f && Timer < 76f =>
                GuidaUtils.Smoothstep(62f, 74f, Timer) * 0.24f,
            Move.HammerSlam or Move.TeleportHammerSlam when Timer >= 60f && Timer < 81f =>
                GuidaUtils.Smoothstep(63f, 79f, Timer) * 0.30f,
            _ => 0f
        };
        float birthPulse = IsEcho && NPC.localAI[3] < 25f
            ? 0.18f * (float)Math.Sin(MathHelper.Pi *
                GuidaUtils.Smoothstep(0f, 25f, NPC.localAI[3])) : 0f;
        float compression = MathHelper.Clamp(landingCompression + charge + birthPulse, 0, 0.5f);
        float climbPulse = CurrentMove == Move.RopeGrenades && Timer >= 32 && Timer < 82
            ? 0.05f * (0.5f + 0.5f * (float)Math.Sin((Timer - 32f) * 0.42f)) *
                GuidaUtils.Smoothstep(32, 44, Timer) * GuidaUtils.Smoothstep(82, 70, Timer)
            : 0f;
        float teleportScale = !IsTeleporting ? 1f : Timer < TeleportMoment
            ? MathHelper.Lerp(1f, 0.26f, GuidaUtils.Smoothstep(10f, TeleportMoment, Timer))
            : MathHelper.Lerp(0.26f, 1f, GuidaUtils.Smoothstep(TeleportMoment, TeleportMoment + 22f, Timer));
        float chainCharge = teleportChainReady
            ? GuidaUtils.Smoothstep(90f, 135f, actionElapsed) : 0f;
        if (chainCharge > 0f) {
            float nextMoment = teleportChainMove == Move.TeleportSpearRush ? 54f : 48f;
            float nextScale = MathHelper.Lerp(1f, 0.26f,
                GuidaUtils.Smoothstep(10f, nextMoment, 45f));
            teleportScale = MathHelper.Lerp(teleportScale, nextScale, chainCharge);
        }
        float warp = IsTeleporting && Timer < TeleportMoment
            ? (float)Math.Sin(Timer * 0.27f) *
                GuidaUtils.Smoothstep(6f, 19f, Timer) *
                GuidaUtils.Smoothstep(TeleportMoment, TeleportMoment - 9f, Timer)
            : 0f;
        warp += (float)Math.Sin(actionElapsed * 0.27f) * chainCharge * 0.8f;
        return new Vector2(
            (1f + compression * 0.60f - flightStretch * 0.46f + climbPulse) *
                NPC.scale * teleportScale * (1f + warp * 0.58f),
            (1f - compression + flightStretch - climbPulse * 0.7f) *
                NPC.scale * teleportScale * (1f - warp * 0.46f));
    }

    private Vector2 BodyDrawScale() => bodyVisualInitialized ? bodyVisualScale : BodyTargetScale();

    private int BodyFrame() {
        if (NPC.IsABestiaryIconDummy) return 0;
        bool rushingWithSpear = CurrentMove == Move.SpearRush && Timer >= 78 && Timer < 112;
        return grounded && !rushingWithSpear ? (int)(visualTicks / 12f) % 2 : 2;
    }

    private void UpdateBodyPose() {
        if (Main.netMode == NetmodeID.Server) return;
        UpdateBodyElasticity();
        Vector2 targetScale = BodyTargetScale();
        bodyVisualScale = bodyVisualInitialized
            ? Vector2.Lerp(bodyVisualScale, targetScale, dying ? 0.65f : 0.22f)
            : targetScale;
        bodyVisualInitialized = true;
        UpdateBodyTwist();
    }

    private void UpdateBodyTwist() {
        if (NPC.IsABestiaryIconDummy) return;
        if (bodyTwistParticle == null || !bodyTwistParticle.IsAlive) {
            bodyTwistParticle = KingSlimeBodyTwistParticle.Spawn(
                NPC.Bottom);
            bodyTwistParticle.SetHolder(NPC);
        }

        bodyTwistParticle.position = NPC.Bottom + new Vector2(0f, NPC.gfxOffY + 4f);
        bodyTwistParticle.bodyScale = BodyDrawScale();
        bodyTwistParticle.rotation = NPC.rotation;
        bodyTwistParticle.frame = BodyFrame();
        bodyTwistParticle.opacity = (0.20f + 0.03f * (float)Math.Sin(visualTicks * 0.08f)) *
            TeleportOpacity() * (IsEcho ? 1f - NPC.alpha / 255f : 1f) *
            (dying ? 1f - GuidaUtils.Smoothstep(128f, 138f, deathTimer) : 1f);
        bodyTwistParticle.timeLeft = 2;
    }

    private void UpdateBodyElasticity() {
        if (!bodyMotionInitialized) {
            previousBodyVelocity = NPC.velocity;
            bodyMotionInitialized = true;
        }
        float verticalChange = previousBodyVelocity.Y - NPC.velocity.Y;
        previousBodyVelocity = NPC.velocity;
        bodyRippleVelocity += MathHelper.Clamp(verticalChange * 0.035f, -0.75f, 0.75f)
            - bodyRipple * 0.12f;
        bodyRippleVelocity *= 0.81f;
        bodyRipple = MathHelper.Clamp(bodyRipple + bodyRippleVelocity, -1.3f, 1.3f);

        bodyShearVelocity += (-NPC.velocity.X * 0.92f - bodyShear) * 0.11f;
        bodyShearVelocity *= 0.76f;
        bodyShear = MathHelper.Clamp(bodyShear + bodyShearVelocity, -22f, 22f);
    }

    // x spans -1..1 across the sprite and y spans 0..1 from crown to feet.
    // The feet stay fixed while the upper body lags and the sides pulse outward.
    private Vector2 BodyDeformation(float x, float y) {
        float middle = (float)Math.Sin(MathHelper.Pi * y);
        float wave = bodyRipple * middle +
            bodyRippleVelocity * 0.45f * (float)Math.Sin(MathHelper.TwoPi * y);
        float upper = (1f - y) * (1f - y);
        float breath = grounded ? (float)Math.Sin(visualTicks * 0.075f) : 0f;
        return new Vector2(
            bodyShear * upper + x * (wave * 15f + landingCompression * 35f * middle * y +
                breath * 2f * middle),
            -(1f - x * x) * (wave * 7.2f + breath * 1.9f * middle) +
            Math.Abs(x) * landingCompression * 4.5f * middle * y) * NPC.scale;
    }

    private Vector2 CrownAnchor() {
        Vector2 bottom = NPC.Bottom + new Vector2(0, NPC.gfxOffY + 4f);
        float crownHeight = BodyDrawScale().Y;
        if (!dying && ((IsTeleporting && Timer < TeleportMoment) || teleportChainReady))
            crownHeight = Math.Max(crownHeight, NPC.scale * 0.9f);
        return bottom + (new Vector2(0, -96f * crownHeight) +
            BodyDeformation(0f, 0.2f)).RotatedBy(NPC.rotation);
    }

    private Vector2 InteriorPosition(float phase) {
        float angle = visualTicks * 0.028f + phase;
        float x = (float)Math.Cos(angle) * 28f * NPC.scale;
        float y = (float)Math.Sin(angle) * 12f * NPC.scale;
        Vector2 scale = BodyDrawScale();
        Vector2 local = new(x, -48f * scale.Y + y);
        Vector2 bottom = NPC.Bottom + new Vector2(0f, NPC.gfxOffY + 4f);
        return bottom + (local + BodyDeformation(
            MathHelper.Clamp(x / (87f * scale.X), -1f, 1f),
            MathHelper.Clamp(1f + local.Y / (120f * scale.Y), 0f, 1f)))
            .RotatedBy(NPC.rotation);
    }

    private float InteriorRotation(float phase) => NPC.rotation * 0.65f +
        (float)Math.Sin(visualTicks * 0.028f + phase) * 0.18f;

    private bool WoodItemBeingTaken() => CurrentMove switch {
        Move.SpearRush => Timer >= 0f && Timer < 27f,
        Move.TeleportSpearRush => Timer < 28f,
        Move.RopeGrenades => Timer >= 5f && Timer < 31f,
        Move.ShurikenFan or Move.TeleportShuriken => Timer >= 60f && Timer < 98f,
        Move.UmbrellaRush => Timer >= 9f && Timer < 25f,
        Move.Boomerang => Timer < 30f,
        Move.FireWand => Timer < 19f,
        Move.SlimeStaffRain => Timer < 24f || Timer >= 35f && Timer < 150f,
        Move.Ultimate => Timer >= 75f && Timer < 120f ||
            Timer >= 398f && Timer < 440f,
        _ => false
    };

    private bool GoldItemBeingTaken() => CurrentMove switch {
        Move.GrappleSlam => Timer >= 1f && Timer < 20f,
        Move.DrinkPotion => Timer < 14f,
        Move.PhaseTwoPotions => Timer < 90f && Timer % 30f < 12f,
        Move.RopeGrenades => Timer >= 72f && Timer < 122f,
        Move.HammerSlam => Timer >= 60f && Timer < 75f,
        Move.TeleportHammerSlam => Timer >= TeleportMoment && Timer < 75f,
        Move.ShortswordThrust => Timer < 12f,
        Move.Ultimate => Timer >= 848f && Timer < 890f,
        Move.Teleport or Move.TeleportSpearRush or Move.TeleportHammerSlam or
            Move.TeleportShuriken =>
            Timer >= TeleportMoment && Timer < TeleportMoment + 24f,
        _ => false
    };

    private void UpdateInteriorVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        if (IsEcho) {
            int ownerIndex = -(int)NPC.ai[3] - 1;
            if (ownerIndex >= 0 && ownerIndex < Main.maxNPCs &&
                Main.npc[ownerIndex].active &&
                Main.npc[ownerIndex].ModNPC is KingSlime owner &&
                owner.woodHostIndex == NPC.whoAmI)
                owner.FollowWoodChest(this);
            return;
        }
        KingSlime woodHost = this;
        if (TryGetSplitTwin(out NPC twin) && twin.ModNPC is KingSlime echo)
            woodHost = echo;
        if (chestParticle?.IsAlive != true) {
            chestParticle = KingSlimePropParticle.Spawn(
                InteriorPosition(0f));
            chestParticle.SetHolder(NPC);
        }
        chestParticle.position = InteriorPosition(0f);
        chestParticle.rotation = InteriorRotation(0f);
        chestParticle.open = GoldItemBeingTaken() ||
            (woodHost != this && WoodItemBeingTaken());
        chestParticle.alpha = TeleportOpacity();
        chestParticle.timeLeft = 2;

        if (woodChestParticle?.IsAlive != true) {
            woodChestParticle = KingSlimePropParticle.Spawn(
                woodHost.InteriorPosition(MathHelper.TwoPi / 3f));
            woodChestParticle.SetHolder(NPC);
            woodChestParticle.wooden = true;
            woodHostIndex = woodHost.NPC.whoAmI;
            woodTransferTicks = 0;
        }
        if (woodHostIndex != woodHost.NPC.whoAmI) {
            woodHostIndex = woodHost.NPC.whoAmI;
            woodTransferTicks = 8;
        }
        woodChestParticle.timeLeft = 2;
        if (woodHost == this) FollowWoodChest(this);

        if (ninjaParticle?.IsAlive != true) {
            ninjaParticle = KingSlimePropParticle.Spawn(
                InteriorPosition(MathHelper.TwoPi * 2f / 3f));
            ninjaParticle.SetHolder(NPC);
            ninjaParticle.ninja = true;
        }
        ninjaParticle.position = InteriorPosition(MathHelper.TwoPi * 2f / 3f);
        ninjaParticle.rotation = InteriorRotation(MathHelper.TwoPi * 2f / 3f);
        ninjaParticle.alpha = TeleportOpacity();
        ninjaParticle.timeLeft = 2;
    }

    private void FollowWoodChest(KingSlime host) {
        if (woodChestParticle?.IsAlive != true) return;
        Vector2 target = host.InteriorPosition(MathHelper.TwoPi / 3f);
        woodChestParticle.position = woodTransferTicks > 0
            ? Vector2.Lerp(woodChestParticle.position, target, 0.55f) : target;
        if (woodTransferTicks > 0) woodTransferTicks--;
        woodChestParticle.rotation = host.InteriorRotation(MathHelper.TwoPi / 3f);
        woodChestParticle.alpha = (1f - host.NPC.alpha / 255f) *
            host.TeleportOpacity();
        woodChestParticle.open = host.WoodItemBeingTaken() ||
            (host.IsEcho && host.GoldItemBeingTaken());
        woodChestParticle.timeLeft = 2;
    }

    private void UpdateCrownPhysics() {
        if (Main.netMode == NetmodeID.Server) return;
        if (NPC.life <= 0) {
            ReleaseCrown();
            return;
        }
        if (IsEcho) return;
        if (!dying && IsTeleporting && Timer >= TeleportMoment && !teleportOccurred) return;
        if (!dying && IsTeleporting && teleportOccurred && Timer < TeleportMoment + 4f) return;
        if (crownParticle == null || !crownParticle.IsAlive) {
            crownParticle = KingSlimeCrownParticle.Spawn(
                !dying && IsTeleporting && teleportOccurred
                    ? InteriorPosition(0f) : CrownAnchor(), scale: 0.85f);
            crownParticle.SetHolder(NPC);
        }
        if (!dying && IsTeleporting && teleportOccurred && Timer < TeleportMoment + 24f) {
            float lift = GuidaUtils.Smoothstep(TeleportMoment + 4f, TeleportMoment + 24f, Timer);
            crownParticle.position = Vector2.Lerp(InteriorPosition(0f) - Vector2.UnitY * 10f,
                CrownAnchor(), lift) - Vector2.UnitY * (float)Math.Sin(lift * MathHelper.Pi) * 13f;
            crownParticle.rotation = NPC.rotation * 0.4f + (1f - lift) * 0.22f;
            crownParticle.velocity = Vector2.Zero;
            crownParticle.timeLeft = 2;
            return;
        }
        crownParticle.Follow(CrownAnchor(), NPC.rotation);
    }

    private void ReleaseCrown() {
        if (crownParticle?.IsAlive == true) crownParticle.Release();
        crownParticle = null;
    }

    private bool WantsLowerPlatform() => CanFallThroughPlatforms() == true;

    private bool DroppingThroughPlatform() => WantsLowerPlatform() &&
        !Collision.SolidCollision(new Vector2(NPC.position.X + 3f, NPC.Bottom.Y + 1f),
            NPC.width - 6, 5);

    private bool LandedFromAir() => NPC.localAI[0] == 1 && grounded &&
        (NPC.oldVelocity.Y > 1.2f || NPC.localAI[1] >= 4);

    private void GetTileCollisionBody(out Vector2 offset, out int width, out int height) {
        int sideInset = Math.Max(8, (int)Math.Round(NPC.width * 0.09f));
        int topInset = Math.Max(6, (int)Math.Round(NPC.height * 0.12f));
        offset = new Vector2(sideInset, topInset);
        width = NPC.width - sideInset * 2;
        height = NPC.height - topInset;
    }

    private bool FarAbovePlayer(Player target) => target.Top.Y - NPC.Bottom.Y > 224f;
    private bool FarBelowPlayer(Player target) => NPC.Top.Y - target.Bottom.Y > 224f;

    private bool IgnoreSolidTiles(Player target) {
        bool requested = motionMode == MotionMode.Lift ||
            CurrentMove == Move.TeleportSpearRush && Timer >= 78f && previousTimer < 112f ||
            (CanPursueThroughTerrain && (FarAbovePlayer(target) ||
                (FarBelowPlayer(target) && NPC.velocity.Y < 0f)));
        bool previouslyActive = solidPassageActive;
        if (requested) solidPassageActive = true;
        else if (solidPassageActive) {
            GetTileCollisionBody(out Vector2 offset, out int width, out int height);
            // Finish crossing a block before restoring collision near the player.
            if (!Collision.SolidCollision(NPC.position + offset, width, height))
                solidPassageActive = false;
        }
        if (solidPassageActive != previouslyActive && Main.netMode != NetmodeID.MultiplayerClient)
            NPC.netUpdate = true;
        return solidPassageActive;
    }

    private float JumpImpulse(float desiredSpeed) {
        float speed = desiredSpeed * 1.08f;
        Player target = Main.player[NPC.target];
        if (FarAbovePlayer(target) || FarBelowPlayer(target)) return -speed;
        GetTileCollisionBody(out Vector2 offset, out int width, out int height);
        float clearance = 0f;
        for (int distance = 16; distance <= 960; distance += 16) {
            if (Collision.SolidCollision(NPC.position + offset - new Vector2(0, distance),
                width, height)) break;
            clearance = distance;
        }
        return -Math.Min(speed,
            Math.Max(3.8f, (float)Math.Sqrt(0.68f * clearance) * 0.94f));
    }

    private void LaunchJump(float horizontal, float verticalSpeed, bool heavy) {
        NPC.velocity = new Vector2(horizontal, JumpImpulse(verticalSpeed));
        NPC.localAI[0] = 1;
        NPC.netUpdate = true;
        SoundEngine.PlaySound(KingSlimeSound.Jump, NPC.Center);
        GroundEffects(heavy, landing: false);
    }

    private void HandleAirborneWallBounce() {
        if (CurrentMove == Move.Hops && NPC.localAI[0] == 1 && NPC.collideX &&
            NPC.velocity.Y >= 0f && NPC.localAI[2] == 0) {
            // A wall may bounce the slime away, but the player cannot redirect it in flight.
            NPC.velocity.Y = JumpImpulse(7f);
            NPC.velocity.X = -wallImpactDirection * 2f;
            NPC.localAI[2] = 1;
            NPC.netUpdate = true;
        }
    }

    private void TryStepUp() {
        if (Math.Abs(NPC.velocity.X) < 0.5f || NPC.velocity.Y < 0f || DroppingThroughPlatform() ||
            !grounded) return;
        int aheadX = (int)((NPC.Center.X + Math.Sign(NPC.velocity.X) * (NPC.width * 0.5f + 18f)) / 16f);
        int feetY = (int)(NPC.Bottom.Y / 16f);
        if (!WorldGen.InWorld(aheadX, feetY, 4)) return;
        GetTileCollisionBody(out Vector2 offset, out int width, out int height);
        Vector2 steppedBodyPosition = NPC.position + offset;
        Vector2 steppedVelocity = NPC.velocity;
        float stepSpeed = NPC.stepSpeed;
        float gfxOffY = NPC.gfxOffY;
        Collision.StepUp(ref steppedBodyPosition, ref steppedVelocity, width, height,
            ref stepSpeed, ref gfxOffY);
        Vector2 steppedPosition = steppedBodyPosition - offset;
        float rise = NPC.position.Y - steppedPosition.Y;
        if (rise <= 0.5f || Collision.SolidCollision(steppedBodyPosition,
                width, (int)Math.Ceiling(rise))) return;

        // StepUp checks the leading tile; a wide body also needs room above its middle and rear.
        NPC.position = steppedPosition;
        NPC.velocity = steppedVelocity;
        NPC.stepSpeed = stepSpeed;
        NPC.gfxOffY = gfxOffY;
        NPC.netUpdate = true;
    }

    private bool IntersectsFullTile(Vector2 position, int width, int height) {
        int firstX = (int)Math.Floor(position.X / 16f);
        int lastX = (int)Math.Floor((position.X + width - 0.01f) / 16f);
        int firstY = (int)Math.Floor(position.Y / 16f);
        int lastY = (int)Math.Floor((position.Y + height - 0.01f) / 16f);
        for (int x = firstX; x <= lastX; x++) {
            for (int y = firstY; y <= lastY; y++) {
                if (!WorldGen.InWorld(x, y, 1)) return true;
                if (Main.tile[x, y] != null && WorldGen.SolidTile(x, y)) return true;
            }
        }
        return false;
    }

    private bool TryEjectMinorTileOverlap(ref Vector2 position, float horizontalVelocity) {
        GetTileCollisionBody(out Vector2 bodyOffset, out int width, out int height);
        Vector2 bodyPosition = position + bodyOffset;
        // A narrower box must be clear: otherwise this is a floor, ceiling, or deep overlap.
        if (!IntersectsFullTile(bodyPosition, width, height) ||
            IntersectsFullTile(bodyPosition + new Vector2(6f, 0f), width - 12, height))
            return false;

        int away = horizontalVelocity > 0f ? -1 : 1;
        Vector2[] directions = {
            new(away, 0f), new(0f, -1f), new(0f, 1f), new(-away, 0f),
            new(away, -1f), new(away, 1f), new(-away, -1f), new(-away, 1f)
        };
        for (int distance = 1; distance <= 10; distance++) {
            foreach (Vector2 direction in directions) {
                Vector2 offset = direction * distance;
                if (IntersectsFullTile(bodyPosition + offset, width, height)) continue;
                position += offset;
                return true;
            }
        }
        return false;
    }

    private bool TryOffsetSpearRush(Vector2 probePosition, Vector2 step, out Vector2 movement) {
        movement = Vector2.Zero;
        Vector2 sideways = new Vector2(-step.Y, step.X);
        if (sideways.LengthSquared() < 0.01f) return false;
        sideways.Normalize();
        if (sideways.Y > 0f) sideways = -sideways;

        // Sweep sideways before advancing so the small rush probe can skirt a
        // ledge without jumping through the block between its two positions.
        for (int distance = 8; distance <= 48; distance += 8) {
            for (int side = 0; side < 2; side++) {
                Vector2 offset = sideways * (side == 0 ? distance : -distance);
                bool clear = true;
                for (int travelled = 3; travelled <= distance; travelled += 3) {
                    if (!IntersectsFullTile(probePosition + offset *
                            (Math.Min(travelled, distance) / (float)distance), 24, 24)) continue;
                    clear = false;
                    break;
                }
                if (!clear || IntersectsFullTile(probePosition + offset, 24, 24) ||
                    IntersectsFullTile(probePosition + offset + step, 24, 24)) continue;
                movement = offset + step;
                return true;
            }
        }
        return false;
    }

    private void MoveRushWithTerrain(Player target, bool ignoreSolid) {
        Vector2 wantedVelocity = NPC.velocity;
        // Only a small centered probe hits complete blocks; the NPC hitbox is unchanged.
        Vector2 probePosition = NPC.Center - new Vector2(12f);
        Vector2 moved = ignoreSolid ? wantedVelocity : Vector2.Zero;
        if (!ignoreSolid) {
            int steps = Math.Max(1, (int)Math.Ceiling(wantedVelocity.Length() / 3f));
            Vector2 step = wantedVelocity / steps;
            for (int i = 0; i < steps; i++) {
                if (!IntersectsFullTile(probePosition + moved + step, 24, 24)) {
                    moved += step;
                    continue;
                }
                if (CurrentMove != Move.SpearRush ||
                    !TryOffsetSpearRush(probePosition + moved, step, out Vector2 offsetStep)) break;
                moved += offsetStep;
            }
        }

        bool collided = Vector2.DistanceSquared(moved, wantedVelocity) > 0.01f;
        NPC.oldVelocity = wantedVelocity;
        NPC.collideX = collided && Math.Abs(wantedVelocity.X) > 0.01f;
        NPC.collideY = collided && Math.Abs(wantedVelocity.Y) > 0.01f;
        bool wasGrounded = grounded;
        grounded = false;
        if ((collided || wasGrounded) && Main.netMode != NetmodeID.MultiplayerClient)
            NPC.netUpdate = true;

        Vector2 finalPosition = NPC.position + moved;
        NPC.position = finalPosition;
        if (!ignoreSolid && !WantsLowerPlatform() &&
            TrySnapToPlayerPlatform(target, out float platformY)) {
            NPC.position.Y = platformY - NPC.height;
            moved.Y = 0f;
            grounded = true;
            if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
        }
        NPC.velocity = moved;
        UpdateJumpTrail();
        UpdateBodyPose();
        UpdateCrownPhysics();
        UpdateInteriorVisuals();
        UpdateSpearVisuals(target);
        UpdateUmbrellaVisuals();
        UpdateHammerVisuals();
        UpdateShortswordVisuals();
        UpdateStaffVisuals();
        UpdateBoomerangVisuals();
        NPC.position -= moved;
    }

    private float ApplyGravity() {
        float gravity = motionMode switch {
            MotionMode.Controlled or MotionMode.Lift or MotionMode.Merge => 0f,
            MotionMode.FastFall => 0.54f,
            MotionMode.Dive => 1.1f,
            MotionMode.GrappleFall => 0.82f,
            MotionMode.Natural when CurrentMove == Move.Hops => 0.74f,
            MotionMode.Natural when CurrentMove == Move.HighLeap => 0.65f,
            _ => 0.45f
        };
        float fallSpeed = motionMode switch {
            MotionMode.Controlled or MotionMode.Lift or MotionMode.Merge => float.MaxValue,
            MotionMode.FastFall => 18f,
            MotionMode.Dive => 28f,
            MotionMode.GrappleFall => 23f,
            MotionMode.Natural when CurrentMove == Move.Hops ||
                CurrentMove == Move.HighLeap => float.MaxValue,
            _ => 14.5f
        };
        if (motionMode == MotionMode.Natural && !grounded) {
            float nearApex = 1f - MathHelper.Clamp(Math.Abs(NPC.velocity.Y) /
                (4.4f * appliedTempo), 0f, 1f);
            gravity *= 1f - (CurrentMove == Move.Hops || CurrentMove == Move.HighLeap
                ? 0.86f : 0.78f) * nearApex;
        }
        NPC.velocity.Y = Math.Min(NPC.velocity.Y + gravity * appliedTempo * appliedTempo,
            fallSpeed * appliedTempo);
        return gravity * appliedTempo * appliedTempo;
    }

    private void MoveWithTerrain(Player target) {
        // AI stores velocity per musical tick; the collision pass moves once
        // per game tick and leaves the physical velocity for Terraria to apply.
        if (motionMode != MotionMode.Scripted) NPC.velocity *= appliedTempo;
        if (motionMode == MotionMode.Merge || motionMode == MotionMode.Scripted) {
            if (!IsTeleporting)
                grounded = false;
            NPC.collideX = false;
            NPC.collideY = false;
            NPC.oldVelocity = NPC.velocity;
            NPC.gfxOffY = MathHelper.Lerp(NPC.gfxOffY, 0f, 0.2f);
            NPC.position += NPC.velocity;
            UpdateBodyPose();
            UpdateCrownPhysics();
            UpdateInteriorVisuals();
            if (motionMode == MotionMode.Scripted) {
                UpdateSpearVisuals(target);
                UpdateGrappleVisuals();
                UpdateUmbrellaVisuals();
                UpdateHammerVisuals();
                UpdateShortswordVisuals();
                UpdateStaffVisuals();
                UpdateBoomerangVisuals();
            }
            NPC.position -= NPC.velocity;
            return;
        }
        bool ignoreSolid = IgnoreSolidTiles(target);
        if (motionMode == MotionMode.Rush) {
            MoveRushWithTerrain(target, ignoreSolid);
            return;
        }
        if (!ignoreSolid && !WantsLowerPlatform() &&
            TrySnapToPlayerPlatform(target, out float platformY)) {
            NPC.position.Y = platformY - NPC.height;
            NPC.velocity.Y = 0f;
            grounded = true;
            if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
        }
        if (grounded && !ignoreSolid) {
            Vector2 position = NPC.position;
            if (TryEjectMinorTileOverlap(ref position, NPC.velocity.X)) {
                NPC.position = position;
                if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
            }
        }
        if (!ignoreSolid) TryStepUp();
        Vector2 startPosition = NPC.position;
        bool wasGrounded = grounded;
        float gravity = ApplyGravity();
        Vector2 wantedVelocity = NPC.velocity;
        bool dropThroughPlatforms = WantsLowerPlatform();
        GetTileCollisionBody(out Vector2 bodyOffset, out int collisionWidth, out int bodyHeight);
        // Every move uses the smaller body for block contact. Landing is scanned
        // separately with the full bottom width below.
        bool rising = wantedVelocity.Y < 0f;
        int collisionHeight = rising || !grounded ? bodyHeight / 2 : bodyHeight;
        if (!rising && !grounded) bodyOffset.Y += bodyHeight - collisionHeight;
        Vector2 collisionPosition = NPC.position + bodyOffset;
        Vector2 tileVelocity = ignoreSolid ? wantedVelocity :
            Collision.TileCollision(collisionPosition, wantedVelocity,
                collisionWidth, collisionHeight, dropThroughPlatforms, dropThroughPlatforms);
        Vector2 tilePosition = NPC.position + tileVelocity;
        Vector2 finalPosition = tilePosition;
        Vector2 finalVelocity = tileVelocity;
        if (!ignoreSolid) {
            Vector4 slope = Collision.SlopeCollision(collisionPosition + tileVelocity, tileVelocity,
                collisionWidth, collisionHeight, gravity, dropThroughPlatforms);
            finalPosition = new Vector2(slope.X, slope.Y) - bodyOffset;
            finalVelocity = new Vector2(slope.Z, slope.W);
        }

        // A wide slime can rest on a slope below its side even when the engine's
        // slope check, which is centered on an edge, does not report that contact.
        bool onGround = wantedVelocity.Y >= 0f &&
            (tileVelocity.Y < wantedVelocity.Y - 0.01f || finalPosition.Y < tilePosition.Y - 0.01f);
        if (wantedVelocity.Y >= 0f && TryGroundSurface(finalPosition,
                startPosition.Y + NPC.height, finalPosition.Y + NPC.height,
                wasGrounded ? Math.Abs(wantedVelocity.X) + 1f : 0f,
                dropThroughPlatforms, ignoreSolid, out float groundY)) {
            finalPosition.Y = groundY - NPC.height;
            finalVelocity.Y = 0f;
            onGround = true;
        }

        NPC.oldVelocity = wantedVelocity;
        NPC.collideX = Math.Abs(wantedVelocity.X - tileVelocity.X) > 0.01f ||
            Math.Abs(tileVelocity.X - finalVelocity.X) > 0.01f;
        if (NPC.collideX) wallImpactDirection = Math.Sign(wantedVelocity.X);
        NPC.collideY = Math.Abs(wantedVelocity.Y - tileVelocity.Y) > 0.01f ||
            Math.Abs(tileVelocity.Y - finalVelocity.Y) > 0.01f ||
            Math.Abs(tilePosition.Y - finalPosition.Y) > 0.01f;
        float beforeEjectionY = finalPosition.Y;
        if (!ignoreSolid && onGround && NPC.collideX &&
            TryEjectMinorTileOverlap(ref finalPosition, wantedVelocity.X)) {
            finalVelocity.X = 0f;
            if (Math.Abs(finalPosition.Y - beforeEjectionY) > 0.01f) onGround = false;
            if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
        }
        grounded = onGround;
        if (grounded != wasGrounded && Main.netMode != NetmodeID.MultiplayerClient)
            NPC.netUpdate = true;

        // With noTileCollide, Terraria adds velocity after AI. Pre-offset the position so
        // its single remaining move ends at the position resolved by our collision pass.
        NPC.position = finalPosition;
        NPC.velocity = finalVelocity;
        UpdateJumpTrail();
        UpdateBodyPose();
        UpdateCrownPhysics();
        UpdateInteriorVisuals();
        UpdateSpearVisuals(target);
        UpdateGrappleVisuals();
        UpdatePotionVisuals();
        UpdateRopeVisuals();
        UpdateUmbrellaVisuals();
        UpdateHammerVisuals();
        UpdateShortswordVisuals();
        UpdateStaffVisuals();
        UpdateBoomerangVisuals();
        NPC.position -= finalVelocity;
    }

    private void UpdateJumpTrail() {
        bool jumping = !grounded && CurrentMove != Move.RopeGrenades && NPC.localAI[0] == 1;
        if (!jumping) {
            jumpTrailTicks = 0;
            return;
        }

        jumpTrailTicks++;
        if (Main.netMode == NetmodeID.Server || (jumpTrailTicks > 12 && jumpTrailTicks % 3 != 0)) return;
        float strength = MathHelper.Clamp(1f - jumpTrailTicks / 42f, 0f, 1f);
        int count = jumpTrailTicks <= 8 ? 2 : 1;
        for (int i = 0; i < count; i++) {
            SmokeParticle smoke = SmokeParticle.Spawn(
                NPC.Bottom - NPC.velocity * 0.7f +
                new Vector2(Main.rand.NextFloat(-NPC.width * 0.18f, NPC.width * 0.18f),
                    -NPC.height * 0.12f + Main.rand.NextFloat(-5f, 3f)),
                new Vector2(-NPC.velocity.X * 0.16f + Main.rand.NextFloat(-0.6f, 0.6f),
                    -NPC.velocity.Y * 0.08f - 0.2f + Main.rand.NextFloat(-0.3f, 0.3f)),
                alpha: 0f,
                scale: NPC.scale * Main.rand.NextFloat(0.8f, 1.15f) * (0.65f + strength * 0.35f));
            smoke.startOpacity = 0.16f + strength * 0.43f;
            smoke.rotation = Main.rand.NextFloat(-0.4f, 0.4f);
            smoke.angVelocity = Main.rand.NextFloat(-0.012f, 0.012f);
        }
    }

    private bool TryGroundSurface(Vector2 position, float previousBottom, float bottom,
        float followDistance, bool dropThroughPlatforms, bool ignoreSolid, out float groundY) {
        groundY = float.MaxValue;
        float left = position.X + 1f;
        float right = position.X + NPC.width - 1f;
        int firstX = (int)(left / 16f);
        int lastX = (int)(right / 16f);
        int firstY = (int)((Math.Min(previousBottom, bottom) - 2f) / 16f);
        int lastY = (int)((Math.Max(previousBottom, bottom) + followDistance + 2f) / 16f);
        for (int x = firstX; x <= lastX; x++) {
            for (int y = firstY; y <= lastY; y++) {
                if (!WorldGen.InWorld(x, y, 2)) continue;
                Tile tile = Main.tile[x, y];
                if (tile == null || !tile.HasTile || tile.IsActuated) continue;
                bool platform = Main.tileSolidTop[tile.TileType];
                if (platform && (dropThroughPlatforms || tile.TileFrameY != 0)) continue;
                if (!platform && (ignoreSolid || !Main.tileSolid[tile.TileType])) continue;

                float tileLeft = x * 16f;
                float overlapLeft = Math.Max(left, tileLeft);
                float overlapRight = Math.Min(right, tileLeft + 16f);
                if (overlapLeft >= overlapRight) continue;
                float surface = y * 16f + (tile.IsHalfBlock ? 8f : 0f);
                if (tile.Slope == SlopeType.SlopeDownLeft)
                    surface += overlapLeft - tileLeft;
                else if (tile.Slope == SlopeType.SlopeDownRight)
                    surface += tileLeft + 16f - overlapRight;

                if (surface < previousBottom - 0.5f || surface > bottom + followDistance + 0.5f)
                    continue;
                if (surface < groundY) groundY = surface;
            }
        }
        return groundY != float.MaxValue;
    }

    // A fast move can place the feet a few pixels through a platform. Recover
    // across all ordinary moves when the player is standing on that platform.
    private bool TrySnapToPlayerPlatform(Player target, out float surfaceY) {
        surfaceY = 0f;
        if (NPC.velocity.Y < -0.5f || Math.Abs(target.velocity.Y) > 0.8f)
            return false;
        float bottom = NPC.Bottom.Y;
        int tileY = (int)Math.Round(target.Bottom.Y / 16f);
        float tileTop = tileY * 16f;
        if (bottom <= tileTop + 0.5f || bottom > tileTop + 12f ||
            Math.Abs(target.Bottom.Y - tileTop) > 4f)
            return false;

        bool playerSupported = false;
        for (int x = (int)(target.Left.X / 16f); x <= (int)(target.Right.X / 16f); x++) {
            if (IsPlatformTop(x, tileY)) { playerSupported = true; break; }
        }
        if (!playerSupported) return false;
        bool slimeSupported = false;
        for (int x = (int)(NPC.Left.X / 16f); x <= (int)(NPC.Right.X / 16f); x++) {
            if (IsPlatformTop(x, tileY)) { slimeSupported = true; break; }
        }
        if (!slimeSupported || Collision.SolidCollision(
                new Vector2(NPC.Left.X, tileTop - NPC.height), NPC.width, NPC.height))
            return false;
        surfaceY = tileTop;
        return true;
    }

    private static bool IsPlatformTop(int x, int y) {
        if (!WorldGen.InWorld(x, y, 2)) return false;
        Tile tile = Main.tile[x, y];
        return tile != null && tile.HasTile && !tile.IsActuated &&
            Main.tileSolidTop[tile.TileType] && tile.TileFrameY == 0;
    }

    // The split is a phase layered over the usual moves. After the opening burst,
    // the owner sets action boundaries and the echo chooses its own move for each slot.
    private void Split(Player target) {
        if (IsEcho) {
            NPC.velocity.X *= 0.92f;
            return;
        }
        if (splitTimer == 0) {
            splitTimer = 1;
            NPC.netUpdate = true;
        }

        if (splitTimer >= 90f && splitMergeStart < 0f) {
            StartNextPattern(target);
            return;
        }

        if (splitMergeStart < 0f) {
            NPC.velocity.X *= 0.78f;
            landingCompression = Math.Max(landingCompression,
                0.28f * GuidaUtils.Smoothstep(4f, 25f, Timer) *
                GuidaUtils.Smoothstep(43f, 27f, Timer));
            if (PassedTime(26f)) {
                int direction = target.Center.X >= NPC.Center.X ? 1 : -1;
                if (Main.netMode != NetmodeID.MultiplayerClient) SpawnSplitTwin(direction);
                NPC.velocity = new Vector2(-direction * 5.2f, JumpImpulse(7.5f));
                NPC.localAI[0] = 1;
                grounded = false;
                NPC.netUpdate = true;
                SoundEngine.PlaySound(KingSlimeSound.GelBurst, NPC.Center);
                GroundEffects(heavy: true, landing: false);
                Splash(16, 4.2f);
            }
            if (Timer >= 90f) {
                if (splitTwinIndex < 0 && Main.netMode != NetmodeID.MultiplayerClient) {
                    splitTimer = 0;
                    StartNextPattern(target);
                }
                else StartNextPattern(target);
            }
            return;
        }

        if (!TryGetSplitTwin(out NPC twin)) {
            if (Main.netMode != NetmodeID.MultiplayerClient) CompleteSplit(target);
            return;
        }

        if (mergeIntoTwin)
            MoveTowardMergePoint(new Vector2(twin.Center.X,
                twin.Bottom.Y - NPC.height * 0.5f));
        else {
            motionMode = MotionMode.Natural;
            NPC.localAI[0] = 0;
            NPC.velocity.X *= 0.72f;
        }
        landingCompression = Math.Max(landingCompression,
            0.13f * GuidaUtils.Smoothstep(splitMergeStart,
                splitMergeStart + 30f, splitTimer));
        if (splitTimer >= splitMergeStart + 90f)
            CompleteSplit(target);
    }

    private void SpawnSplitTwin(int direction) {
        if (TryGetSplitTwin(out _)) return;
        int index = NPC.NewNPC(NPC.GetSource_FromAI(), (int)NPC.Center.X,
            (int)NPC.Center.Y, Type);
        if (index < 0 || index >= Main.maxNPCs) return;
        NPC twin = Main.npc[index];
        twin.Center = NPC.Center + new Vector2(direction * 10f, -8f);
        twin.ai[0] = (float)Move.Split;
        twin.ai[1] = 0f;
        twin.ai[2] = 0f;
        twin.ai[3] = -NPC.whoAmI - 1f;
        twin.realLife = NPC.whoAmI;
        twin.boss = false;
        twin.damage = 0;
        twin.dontTakeDamage = true;
        twin.life = NPC.life;
        twin.lifeMax = NPC.lifeMax;
        twin.velocity = new Vector2(direction * 5.2f, JumpImpulse(7.5f)) * appliedTempo;
        if (twin.ModNPC is KingSlime echo) {
            echo.tempoStage = tempoStage;
            echo.appliedTempo = appliedTempo;
            echo.lastOwnerActionSerial = actionSerial;
            echo.measureTicks = measureTicks;
        }
        twin.alpha = 255;
        twin.netUpdate = true;
        splitTwinIndex = index;
        NPC.netUpdate = true;
    }

    private void MergeEcho(KingSlime owner, Player target) {
        ReleaseSpear();
        ReleaseGrapple();
        ReleasePotion();
        ReleaseRope();
        ReleaseUmbrella();
        NPC.damage = 0;
        NPC.localAI[0] = 0;
        if (owner.mergeIntoTwin) {
            motionMode = MotionMode.Natural;
            NPC.velocity.X *= 0.72f;
        }
        else MoveTowardMergePoint(new Vector2(owner.NPC.Center.X,
            owner.NPC.Bottom.Y - NPC.height * 0.5f));
        landingCompression = Math.Max(landingCompression, 0.11f);
        MoveWithTerrain(target);
    }

    private void MoveTowardMergePoint(Vector2 destination) {
        motionMode = MotionMode.Merge;
        Vector2 desired = Vector2.Clamp((destination - NPC.Center) * 0.16f,
            new Vector2(-36f, -36f), new Vector2(36f, 36f));
        NPC.velocity = Vector2.Lerp(NPC.velocity, desired, 0.24f);
        NPC.rotation = MathHelper.Lerp(NPC.rotation,
            MathHelper.Clamp(NPC.velocity.X * 0.012f, -0.14f, 0.14f), 0.16f);
        if (Main.netMode != NetmodeID.Server && (int)visualTicks % 3 == 0) {
            SmokeParticle smoke = SmokeParticle.Spawn(
                NPC.Center + Main.rand.NextVector2Circular(NPC.width * 0.3f, NPC.height * 0.25f),
                -NPC.velocity * 0.12f + Main.rand.NextVector2Circular(0.5f, 0.5f),
                alpha: 0f, scale: NPC.scale * Main.rand.NextFloat(0.8f, 1.2f));
            smoke.drawLayer = ParticleLayer.BeforeProjectiles;
            smoke.startOpacity = 0.23f;
            smoke.timeLeft = 20;
            smoke.maxTimeLeft = 20;
        }
    }

    private bool TryGetSplitTwin(out NPC twin) {
        if (splitTwinIndex >= 0 && splitTwinIndex < Main.maxNPCs) {
            twin = Main.npc[splitTwinIndex];
            if (twin.active && twin.type == Type && twin.ai[3] == -NPC.whoAmI - 1f)
                return true;
        }
        twin = null;
        return false;
    }

    private void RemoveSplitTwin() {
        if (TryGetSplitTwin(out NPC twin)) {
            twin.active = false;
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.SyncNPC, number: twin.whoAmI);
        }
        splitTwinIndex = -1;
    }

    private void CompleteSplit(Player target) {
        if (mergeIntoTwin && TryGetSplitTwin(out NPC mergeTarget)) {
            NPC.Center = new Vector2(mergeTarget.Center.X,
                mergeTarget.Bottom.Y - NPC.height * 0.5f);
            NPC.velocity = mergeTarget.velocity;
        }
        if (Main.netMode != NetmodeID.MultiplayerClient) RemoveSplitTwin();
        else {
            if (TryGetSplitTwin(out NPC twin)) twin.active = false;
            splitTwinIndex = -1;
        }
        if (Main.netMode != NetmodeID.Server) {
            Splash(20, 3.8f);
            GroundEffects(heavy: false, landing: false);
            SoundEngine.PlaySound(KingSlimeSound.GelBurst, NPC.Center);
        }
        splitTimer = 0;
        splitMergeStart = -1f;
        mergeIntoTwin = false;
        tempoStage = 1;
        NPC.defense = 12;
        NPC.velocity *= 0.35f;
        landingCompression = 0.35f;
        NPC.netUpdate = true;
        patternKind = 0;
        Begin(Move.PhaseTwoPotions);
    }

    private void Hops(Player target) {
        if (LandedFromAir()) {
            if (!IsEcho && !IntroActive && hopLaunchTimer >= 0f &&
                Timer - hopLaunchTimer <= 18f)
                forceNextTeleport = true;
            hopLaunchTimer = -1f;
            NPC.localAI[0] = 0;
            NPC.velocity.Y = 0;
            NPC.velocity.X *= 0.38f;
            NPC.localAI[2] = 0;
            NPC.netUpdate = true;
            Land(false);
        }
        if (NPC.localAI[0] == 0 && grounded) {
            NPC.velocity.X *= 0.8f;
            if (MoveValue == 0f && Timer >= 12f) {
                float dx = target.Center.X + SplitJumpOffset() +
                    target.velocity.X * 4f - NPC.Center.X;
                float horizontal = MathHelper.Clamp(dx / 34f, -9.8f, 9.8f);
                float jumpSpeed = 16f + MathHelper.Clamp(
                    (NPC.Center.Y - target.Center.Y - 48f) * 0.02f, 0f, 7f);
                MoveValue = 1f;
                hopLaunchTimer = Timer;
                LaunchJump(horizontal, jumpSpeed, heavy: false);
                return;
            }
            if (MoveValue == 1f && SlotFinished(90f)) AdvancePattern(target);
        }
        else if (NPC.localAI[0] == 1) HandleAirborneWallBounce();
    }

    private void HighLeap(Player target) {
        if (Timer < 34) {
            NPC.velocity.X *= 0.72f;
            if (!grounded) Timer = Math.Min(Timer, 20);
            return;
        }
        if (PassedTime(34)) {
            float dx = target.Center.X + SplitJumpOffset() +
                target.velocity.X * 6f - NPC.Center.X;
            float horizontal = MathHelper.Clamp(dx / 78f, -11.8f, 11.8f);
            LaunchJump(horizontal, 21.2f, heavy: true);
        }
        else if (LandedFromAir()) {
            NPC.localAI[0] = 0;
            NPC.velocity = Vector2.Zero;
            NPC.netUpdate = true;
            Land(true);
        }
        if (SlotFinished(135f) && NPC.localAI[0] == 0 && grounded)
            AdvancePattern(target);
    }

    private void SpearRush(Player target) {
        if (Timer < 78) {
            NPC.velocity.X *= grounded ? 0.72f : 0.94f;
            if (PassedTime(32)) {
                Vector2 aim = target.Center + Vector2.UnitX * SplitSideOffset() +
                    target.velocity * 9f - NPC.Center;
                if (grounded && Math.Abs(target.Bottom.Y - NPC.Bottom.Y) < 30f) aim.Y = 0f;
                MoveValue = aim.ToRotation();
                spearRushDistanceScale = MathHelper.Clamp(aim.Length() / 520f, 0.85f, 1.12f);
                NPC.netUpdate = true;
            }
            return;
        }

        if (Timer < 112) {
            motionMode = MotionMode.Rush;
            if (PassedTime(78)) {
                KingSlimeSound.PlayRush(NPC.Center);
                GroundEffects(heavy: true, landing: false);
                NPC.netUpdate = true;
            }
            // Lock the heading when the warning appears; speed rises and then falls
            // along that line, with only the terrain probe allowed to detour.
            float progress = MathHelper.Clamp((Timer - 78f) / 34f, 0f, 1f);
            float speed = MathHelper.Lerp(8f, 34f,
                GuidaUtils.Smoothstep(0f, 0.32f, progress));
            speed *= 1f - 0.68f * GuidaUtils.Smoothstep(0.68f, 1f, progress);
            Vector2 direction = new Vector2((float)Math.Cos(MoveValue), (float)Math.Sin(MoveValue));
            NPC.velocity = direction * speed * spearRushDistanceScale;
            return;
        }

        NPC.velocity.X *= grounded ? 0.80f : 0.96f;
        // A measure and a half at 160 BPM is 135 ticks. Stay in recovery if the
        // terrain delays landing rather than starting the next hop in midair.
        if (SlotFinished(135f) && grounded) AdvancePattern(target);
    }

    private void GrappleSlam(Player target) {
        if (Timer < 21f) {
            NPC.velocity.X *= grounded ? 0.72f : 0.94f;
            if (!grounded) Timer = Math.Min(Timer, 12f);
            if (PassedTime(1f)) {
                // The hook grips a fixed point in the sky. Aim is locked before the lift.
                float aimX = target.Center.X + SplitSideOffset() +
                    MathHelper.Clamp(target.velocity.X * 16f, -130f, 130f);
                float aimY = target.Top.Y + MathHelper.Clamp(target.velocity.Y * 8f, -80f, 80f);
                // The lift has a fixed release beat. Keep its hanging point
                // inside the distance the body can actually cover before it.
                Vector2 plannedHangingCenter = new(aimX, Math.Min(aimY, NPC.Top.Y) - 280f);
                Vector2 travel = plannedHangingCenter - NPC.Center;
                if (travel.Length() > 430f)
                    plannedHangingCenter = NPC.Center + Vector2.Normalize(travel) * 430f;
                grappleAnchor = plannedHangingCenter - new Vector2(0f, 145f);
                grappleAnchor.X = MathHelper.Clamp(grappleAnchor.X, 80f, Main.maxTilesX * 16f - 80f);
                grappleAnchor.Y = Math.Max(grappleAnchor.Y, 80f);
                NPC.netUpdate = true;
                SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Top);
            }
            return;
        }

        if (Timer > 88f && LandedFromAir()) {
            NPC.localAI[0] = 0;
            NPC.velocity = Vector2.Zero;
            Timer = 113f;
            NPC.netUpdate = true;
            Land(true);
        }
        if (Timer >= 113f && !grounded && NPC.localAI[0] == 1f) Timer = 112f;
        motionMode = Timer < 83f ? MotionMode.Lift : Timer < 113f ? MotionMode.GrappleFall : MotionMode.Natural;
        Vector2 hangingCenter = grappleAnchor + new Vector2(0f, 145f);
        if (Timer < 83f) {
            if (Timer >= 71f)
                hangingCenter += new Vector2((float)Math.Sin((Timer - 71f) * 0.25f) * 3f,
                    (float)Math.Sin((Timer - 71f) * 0.4f) * 2f);
            if (PassedTime(21f)) {
                grounded = false;
                NPC.localAI[0] = 1;
                NPC.velocity = new Vector2(NPC.velocity.X * 0.35f, -4f);
                NPC.netUpdate = true;
                GroundEffects(heavy: false, landing: false);
                SoundEngine.PlaySound(KingSlimeSound.Mechanism, NPC.Center);
            }
            Vector2 error = hangingCenter - NPC.Center;
            float response = Timer < 69f ? 0.135f : 0.055f;
            Vector2 desiredVelocity = new Vector2(
                MathHelper.Clamp(error.X * response, -18f, 18f),
                MathHelper.Clamp(error.Y * response, -24f, 12f));
            NPC.velocity = Vector2.Lerp(NPC.velocity, desiredVelocity,
                Timer < 69f ? 0.19f : 0.16f);
            if (Timer >= 71f) NPC.velocity *= 0.82f;
            return;
        }

        if (PassedTime(83f)) {
            // Commit to this X position so the descending hit can be dodged.
            NPC.velocity = new Vector2(0f, 3f);
            NPC.netUpdate = true;
            ReleaseGrapple();
            SoundEngine.PlaySound(KingSlimeSound.Impact, NPC.Center);
        }
        if (Timer < 113f) {
            NPC.velocity.X *= 0.8f;
            if (Timer >= 112f && !grounded) Timer = 112f;
            return;
        }

        NPC.velocity.X *= grounded ? 0.75f : 0.96f;
        if (Timer >= 113f && SlotFinished(135f) && grounded)
            AdvancePattern(target);
    }

    private void DrinkPotion(Player target) {
        NPC.velocity.X *= grounded ? 0.67f : 0.95f;
        if (!grounded && Timer < 5f) {
            Timer = 0f;
            return;
        }
        if (PassedTime(3f)) {
            MoveValue = target.Center.X >= NPC.Center.X ? 1f : -1f;
            NPC.netUpdate = true;
        }
        if (PassedTime(18f)) SoundEngine.PlaySound(KingSlimeSound.Drink, NPC.Center);
        if (PassedTime(27f) && Main.netMode != NetmodeID.MultiplayerClient) {
            int restored = Math.Min((int)Math.Ceiling(NPC.lifeMax * 0.05f), NPC.lifeMax - NPC.life);
            if (restored > 0) {
                NPC.life += restored;
                lastObservedLife = NPC.life;
                NPC.HealEffect(restored);
                NPC.netUpdate = true;
            }
        }
        if (PassedTime(38f)) ReleasePotion();
        if (Timer >= 38f && SlotFinished(45f)) AdvancePattern(target);
    }

    private void PhaseTwoPotions(Player target) {
        NPC.velocity.X *= grounded ? 0.67f : 0.95f;
        if (PassedTime(2f)) {
            MoveValue = target.Center.X >= NPC.Center.X ? 1f : -1f;
            NPC.netUpdate = true;
        }
        for (int i = 0; i < 3; i++) {
            if (PassedTime(17f + i * 30f))
                SoundEngine.PlaySound(KingSlimeSound.Drink, NPC.Center);
            if (PassedTime(26f + i * 30f)) ReleasePotion();
        }
        if (SlotFinished(90f)) AdvancePattern(target);
    }

    private void RopeGrenades(Player target) {
        NPC.damage = 0;
        NPC.velocity.X *= grounded ? 0.68f : 0.9f;
        if (Timer < 32f) {
            if (!grounded) Timer = Math.Min(Timer, 8f);
            if (PassedTime(10f)) {
                ropeLaunchPosition = NPC.Bottom;
                MoveValue = Math.Max(80f, NPC.Top.Y - 390f);
                NPC.netUpdate = true;
                SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Top);
            }
            return;
        }

        motionMode = Timer < 140f ? MotionMode.Lift :
            NPC.localAI[0] == 1 ? MotionMode.FastFall : MotionMode.Natural;
        if (Timer < 140f) {
            if (PassedTime(32f)) {
                grounded = false;
                NPC.localAI[0] = 1;
                NPC.velocity = new Vector2(0f, -3f);
                NPC.netUpdate = true;
                GroundEffects(heavy: false, landing: false);
                SoundEngine.PlaySound(KingSlimeSound.Mechanism, NPC.Center);
            }
            float targetCenterY = MoveValue + 85f + NPC.height * 0.5f;
            float response = Timer < 82f ? 0.10f : 0.04f;
            float desiredY = MathHelper.Clamp((targetCenterY - NPC.Center.Y) * response,
                Timer < 82f ? -13f : -4f, Timer < 82f ? 5f : 4f);
            NPC.velocity.Y = MathHelper.Lerp(NPC.velocity.Y, desiredY,
                Timer < 82f ? 0.17f : 0.22f);
            NPC.velocity.X *= 0.2f;

            for (int throwTime = 78; throwTime <= 113; throwTime += 7) {
                if (!PassedTime(throwTime)) continue;
                landingCompression = 0.17f;
                SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
                ThrowGrenade(target);
            }
            return;
        }

        if (PassedTime(140f)) {
            ReleaseRope();
            NPC.velocity = new Vector2(0f, Math.Max(NPC.velocity.Y, 2.5f));
            NPC.netUpdate = true;
        }
        if (Timer > 145f && LandedFromAir()) {
            NPC.localAI[0] = 0;
            NPC.velocity = Vector2.Zero;
            NPC.netUpdate = true;
            Land(true);
        }
        // Two measures at 160 BPM are 180 ticks; wait longer only if still airborne.
        if (SlotFinished(180f) && grounded && NPC.localAI[0] == 0)
            AdvancePattern(target);
    }

    private void ShurikenFan(Player target) {
        NPC.damage = 0;
        if (Timer < 12f) {
            NPC.velocity.X *= grounded ? 0.72f : 0.94f;
            if (!grounded) Timer = Math.Min(Timer, 6f);
            return;
        }
        if (PassedTime(12f)) {
            float horizontal = MathHelper.Clamp((target.Center.X - NPC.Center.X) * 0.015f,
                -3f, 3f);
            LaunchJump(horizontal, 16f, heavy: false);
            return;
        }

        if (Timer < 60f || (Timer < 97f && NPC.localAI[0] == 1f &&
                !grounded && NPC.velocity.Y < -0.6f)) {
            NPC.velocity.X *= 0.985f;
            if (Timer > 24f && grounded) {
                NPC.localAI[0] = 0;
                NPC.velocity = Vector2.Zero;
                Timer = 123f;
                NPC.netUpdate = true;
                return;
            }
            if (Timer > 32f && !grounded && NPC.velocity.Y >= -0.6f) {
                Timer = 60f;
                MoveValue = (target.Center + target.velocity * 5f -
                    (NPC.Center - Vector2.UnitY * NPC.height * 0.08f)).ToRotation();
                NPC.velocity = Vector2.Zero;
                NPC.netUpdate = true;
            }
            else if (Timer >= 59f) Timer = 59f;
            return;
        }

        if (Timer > 102f && LandedFromAir()) {
            NPC.localAI[0] = 0;
            NPC.velocity = Vector2.Zero;
            Timer = 123f;
            NPC.netUpdate = true;
            Land(true);
        }
        if (Timer >= 123f && !grounded && NPC.localAI[0] == 1f) Timer = 122f;
        motionMode = Timer < 97f ? MotionMode.Controlled : Timer < 123f ? MotionMode.Dive : MotionMode.Natural;
        if (Timer < 97f) {
            NPC.velocity = Vector2.Zero;
            for (int throwTime = 61; throwTime <= 89; throwTime += 4) {
                if (!PassedTime(throwTime)) continue;
                int index = (throwTime - 61) / 4;
                Vector2 throwBase = NPC.Center - Vector2.UnitY * (NPC.height * 0.08f);
                float angle = MoveValue + (index - 3.5f) * 0.34f;
                landingCompression = 0.12f;
                SoundEngine.PlaySound(KingSlimeSound.ItemUse, NPC.Center);
                if (Main.netMode == NetmodeID.MultiplayerClient) continue;
                Vector2 direction = angle.ToRotationVector2();
                Vector2 origin = throwBase + direction * (NPC.width * 0.38f);
                int projectileIndex = Projectile.NewProjectile(NPC.GetSource_FromAI(), origin,
                    direction * 12.5f * appliedTempo,
                    ModContent.ProjectileType<KingSlimeShuriken>(), 20, 2f, Main.myPlayer,
                    ai1: angle);
                if (projectileIndex >= 0 && projectileIndex < Main.maxProjectiles &&
                    Main.projectile[projectileIndex].ModProjectile is KingSlimeShuriken shuriken)
                    shuriken.SetTempo(appliedTempo);
            }
            return;
        }

        if (PassedTime(97f)) {
            NPC.velocity = new Vector2(0f, 4f);
            NPC.netUpdate = true;
        }
        if (Timer < 123f) {
            if (Timer >= 122f && !grounded) Timer = 122f;
            return;
        }
        if (Timer >= 123f && SlotFinished(135f) && grounded)
            AdvancePattern(target);
    }

    private void ThrowGrenade(Player target) {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        float direction = target.Center.X >= NPC.Center.X ? 1f : -1f;
        Vector2 origin = NPC.Center + new Vector2(direction * NPC.width * 0.34f,
            -NPC.height * 0.19f);
        float upwardSpeed = 7f;
        float gravity = KingSlimeGrenade.FlightGravity;
        // Only the target's vertical offset is capped. Horizontal range and
        // prediction still use the actual player position.
        float drop = Math.Max(target.Center.Y - origin.Y, -48f);
        float flightTime = (upwardSpeed + (float)Math.Sqrt(Math.Max(0f,
            upwardSpeed * upwardSpeed + 2f * gravity * drop))) / gravity;
        float landingX = target.Center.X + target.velocity.X * (flightTime / appliedTempo) * 0.35f +
            Main.rand.NextFloat(-640f, 640f);
        Vector2 velocity = new Vector2(
            (landingX - origin.X) / flightTime, -upwardSpeed) * appliedTempo;
        int index = Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, velocity,
            ModContent.ProjectileType<KingSlimeGrenade>(), 20, 2f, Main.myPlayer,
            ai1: target.whoAmI);
        if (index >= 0 && index < Main.maxProjectiles &&
            Main.projectile[index].ModProjectile is KingSlimeGrenade grenade)
            grenade.SetTempo(appliedTempo);
    }

    private void ReleaseRope() {
        if (ropeParticle?.IsAlive == true) ropeParticle.Release();
        ropeParticle = null;
    }

    private void UpdateRopeVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        if (CurrentMove != Move.RopeGrenades || Timer < 10f || Timer >= 140f) {
            ReleaseRope();
            return;
        }
        if (ropeParticle == null || !ropeParticle.IsAlive) {
            ropeParticle = KingSlimeRopeParticle.Spawn(
                ropeLaunchPosition, alpha: 0f, scale: 1.8f);
            ropeParticle.SetHolder(NPC);
            ropeParticle.launchPosition = ropeLaunchPosition;
        }
        float extension = GuidaUtils.Smoothstep(10f, 32f, Timer);
        ropeParticle.position = Vector2.Lerp(ropeParticle.launchPosition,
            new Vector2(ropeParticle.launchPosition.X, MoveValue), extension);
        ropeParticle.lowerEnd = ropeLaunchPosition;
        ropeParticle.alpha = GuidaUtils.Smoothstep(10f, 17f, Timer);
        ropeParticle.timeLeft = 2;
    }

    private void ReleasePotion() {
        if (potionParticle?.IsAlive == true && potionParticle.held) {
            float direction = MoveValue == 0f ? 1f : MoveValue;
            potionParticle.Release(new Vector2(direction * 4.5f, -3.8f));
        }
        potionParticle = null;
    }

    private void UpdatePotionVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        bool phasePotions = CurrentMove == Move.PhaseTwoPotions;
        bool healingPotion = CurrentMove == Move.DrinkPotion;
        float poseTime = phasePotions ? Timer % 30f : Timer;
        if ((!phasePotions && !healingPotion) || poseTime < 3f ||
            poseTime >= (phasePotions ? 26f : 38f)) {
            if (!phasePotions && !healingPotion) ReleasePotion();
            return;
        }

        if (potionParticle == null || !potionParticle.IsAlive) {
            potionParticle = KingSlimePotionParticle.Spawn(
                NPC.Center, alpha: 0f, scale: 2.5f);
            potionParticle.SetHolder(NPC);
            potionParticle.variant = phasePotions ? (int)(Timer / 30f) + 1 : 0;
        }

        float direction = MoveValue == 0f ? 1f : MoveValue;
        float lift = GuidaUtils.Smoothstep(3f, phasePotions ? 11f : 14f, poseTime);
        float drink = GuidaUtils.Smoothstep(phasePotions ? 12f : 16f,
            phasePotions ? 19f : 25f, poseTime);
        float returnBack = GuidaUtils.Smoothstep(phasePotions ? 21f : 28f,
            phasePotions ? 25f : 37f, poseTime);
        Vector2 from = new Vector2(direction * NPC.width * 0.55f, NPC.height * 0.22f);
        Vector2 mouth = new Vector2(direction * NPC.width * 0.23f, -NPC.height * 0.27f);
        Vector2 offset = Vector2.Lerp(from, mouth, lift) +
            new Vector2(-direction * 9f * drink + direction * 14f * returnBack,
                -(float)Math.Sin(MathHelper.Pi * lift) * 8f + 5f * drink + 4f * returnBack) +
            new Vector2(direction * NPC.width * 0.10f, NPC.height * 0.12f);
        potionParticle.position = NPC.Center + offset.RotatedBy(NPC.rotation);
        potionParticle.rotation = NPC.rotation - direction * 0.88f *
            GuidaUtils.Smoothstep(phasePotions ? 10f : 12f,
                phasePotions ? 18f : 20f, poseTime) *
            GuidaUtils.Smoothstep(phasePotions ? 24f : 31f,
                phasePotions ? 20f : 27f, poseTime);
        potionParticle.alpha = GuidaUtils.Smoothstep(3f, 8f, poseTime);
        potionParticle.empty = poseTime >= (phasePotions ? 20f : 27f);
        potionParticle.timeLeft = 2;
    }

    private void ReleaseGrapple() {
        if (grappleParticle?.IsAlive == true) grappleParticle.Release();
        grappleParticle = null;
    }

    private void UpdateGrappleVisuals() {
        if (Main.netMode == NetmodeID.Server) return;
        if (CurrentMove != Move.GrappleSlam || Timer < 1f || Timer >= 83f) {
            ReleaseGrapple();
            return;
        }

        if (grappleParticle == null || !grappleParticle.IsAlive) {
            grappleParticle = KingSlimeGrappleParticle.Spawn(
                NPC.Top, alpha: 0f, scale: 2.5f);
            grappleParticle.SetHolder(NPC);
            grappleParticle.launchPosition = NPC.Top + new Vector2(0f, 7f);
        }

        float flight = GuidaUtils.Smoothstep(3f, 20f, Timer);
        Vector2 arc = new Vector2((grappleAnchor.X >= grappleParticle.launchPosition.X ? 1f : -1f) *
            35f, -42f) * (float)Math.Sin(MathHelper.Pi * flight);
        grappleParticle.position = Vector2.Lerp(grappleParticle.launchPosition, grappleAnchor, flight) + arc;
        grappleParticle.chainEnd = NPC.Top + new Vector2(0f, 7f);
        grappleParticle.rotation = MathHelper.TwoPi * GuidaUtils.Smoothstep(3f, 17f, Timer);
        grappleParticle.slack = (1f - flight) * 15f;
        grappleParticle.alpha = GuidaUtils.Smoothstep(1f, 7f, Timer);
        grappleParticle.timeLeft = 2;

    }

    private void ReleaseSpear() {
        if (spearParticle?.IsAlive == true && spearParticle.held) {
            float angle = spearParticle.rotation - MathHelper.PiOver4;
            Vector2 direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
            spearParticle.position = NPC.Center + direction * (NPC.width * 0.42f + 8f) -
                Vector2.UnitY * (NPC.height * 0.09f);
            spearParticle.Release(direction * 5f + new Vector2(Math.Sign(direction.X) * 2f, -4.5f));
        }
        spearParticle = null;
    }

    private void UpdateSpearVisuals(Player target) {
        if (Main.netMode == NetmodeID.Server) return;
        if (CurrentMove == Move.Ultimate) {
            UpdateUltimateSpearVisuals();
            return;
        }
        bool teleportRush = CurrentMove == Move.TeleportSpearRush;
        if ((!teleportRush && CurrentMove != Move.SpearRush) ||
            (teleportRush ? previousTimer >= 112f : Timer >= 112f)) {
            ReleaseSpear();
            return;
        }

        if (spearParticle == null || !spearParticle.IsAlive) {
            spearParticle = KingSlimeSpearParticle.Spawn(NPC.Center,
                alpha: 0f, scale: 2.5f);
            spearParticle.SetHolder(NPC);
        }

        float aimAngle = MoveValue;
        if (teleportRush)
            aimAngle += MathHelper.TwoPi *
                (1f - GuidaUtils.Smoothstep(8f, 42f, Timer));
        else if (Timer < 42) {
            float aimDirection = (target.Center - NPC.Center).ToRotation();
            if (Timer >= 32)
                aimDirection += MathHelper.WrapAngle(MoveValue - aimDirection) *
                    GuidaUtils.Smoothstep(32f, 42f, Timer);
            aimAngle = aimDirection + MathHelper.TwoPi * GuidaUtils.Smoothstep(8f, 42f, Timer);
        }
        if (!teleportRush && Timer >= 78 && NPC.velocity.LengthSquared() > 1f)
            aimAngle = NPC.velocity.ToRotation();
        Vector2 direction = new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle));
        float pullBack = 30f * GuidaUtils.Smoothstep(42f, 70f, Timer) *
            GuidaUtils.Smoothstep(84f, 78f, Timer);
        spearParticle.position = NPC.Center + direction * (NPC.width * 0.42f + 8f - pullBack) -
            Vector2.UnitY * (NPC.height * 0.09f);
        spearParticle.rotation = aimAngle + MathHelper.PiOver4;
        spearParticle.alpha = GuidaUtils.Smoothstep(4f, 20f, Timer);
        spearParticle.tipStarOpacity = GuidaUtils.Smoothstep(32f, 52f, Timer);
        spearParticle.tipStarRotation = MathHelper.TwoPi * 1.25f *
            GuidaUtils.Smoothstep(32f, 78f, Timer);
        spearParticle.tipRushStreak = Timer >= 78;
        spearParticle.timeLeft = 2;

        if (Timer >= (teleportRush ? 8f : 32f) && Timer < 78f &&
            !spearWarningCreated) {
            WarningLineParticle.Spawn(teleportRush ? teleportDestination :
                    NPC.Center - MoveValue.ToRotationVector2() * 24f,
                MoveValue + MathHelper.PiOver2,
                teleportRush ? teleportRushDistance : 780f * spearRushDistanceScale,
                ((teleportRush ? 78f : 87f) - Timer) / appliedTempo,
                width: 96f * NPC.scale,
                opacity: 0.8f, arrowSpeed: appliedTempo);
            spearWarningCreated = true;
        }

        if (Timer >= 78f && (!teleportRush || previousTimer < 112f)) {
            UpdateBodyMotionTrail();
            UpdateSpearTipTrail();
            SpawnSpearRushEffects(new Vector2((float)Math.Cos(MoveValue), (float)Math.Sin(MoveValue)),
                PassedTime(78f), PassedTime(92f));
        }
    }

    private void SpawnSpearRushEffects(Vector2 direction, bool openingCircle, bool secondCircle) {
        Vector2 sideways = new Vector2(-direction.Y, direction.X);

        if (openingCircle || secondCircle) {
            RushCircleParticle circle = RushCircleParticle.Spawn(
                NPC.Center - direction * (NPC.width * (openingCircle ? 0.28f : 0.14f)),
                direction * (openingCircle ? 3.8f : 2.4f),
                alpha: openingCircle ? 0.7f : 0.48f);
            circle.color = new Color(115, 195, 255);
            circle.rotation = direction.ToRotation();
            circle.drawScale = openingCircle ? new Vector2(0.44f, 0.56f) : new Vector2(0.34f, 0.44f);
            circle.growthPerTick = openingCircle ? new Vector2(0.040f, 0.055f) : new Vector2(0.032f, 0.045f);
            circle.velocityDrag = 0.9f;
            circle.fadeInEnd = 0.08f;
            circle.fadeOutStart = 0.3f;
            circle.timeLeft = circle.maxTimeLeft = openingCircle ? 23 : 18;
        }

        float speed = NPC.velocity.Length();
        if ((int)Timer % 4 == 0) return;
        Vector2 spawnPosition = NPC.Center - direction * Main.rand.NextFloat(4f, NPC.width * 0.65f) +
            sideways * Main.rand.NextFloat(-NPC.height * 0.65f, NPC.height * 0.65f);
        SpeedLineParticle line = SpeedLineParticle.Spawn(
            spawnPosition, -direction * Main.rand.NextFloat(2f, 5f),
            alpha: Main.rand.NextFloat(0.30f, 0.46f));
        line.color = new Color(155, 210, 255);
        line.rotation = direction.ToRotation() - MathHelper.PiOver2;
        line.drawScale = new Vector2(Main.rand.NextFloat(0.15f, 0.25f),
            Main.rand.NextFloat(0.95f, 1.6f)) * MathHelper.Lerp(0.75f, 1f,
            MathHelper.Clamp(speed / 27f, 0f, 1f));
        line.velocityDrag = 0.94f;
        line.fadeInEnd = 0.12f;
        line.fadeOutStart = 0.55f;
        line.timeLeft = line.maxTimeLeft = Main.rand.Next(12, 19);
    }

    private void UpdateSpearTipTrail() {
        if (spearTipTrail == null || !spearTipTrail.IsAlive) {
            spearTipTrail = TrailParticle.Spawn(spearParticle.TipPosition);
            Texture2D glow = spearParticle.RushStreakTexture;
            // Give the sampled glows visible alpha; an alpha-zero tint makes
            // this trail nearly disappear after its per-sample fade.
            spearTipTrail.SetUp(10, glow, glow.Bounds, BlendState.AlphaBlend, 0.52f);
            spearTipTrail.drawLayer = ParticleLayer.BeforePlayers;
            spearTipTrail.color = new Color(150, 210, 255, 175);
            spearTipTrail.useTrailScale2 = true;
            spearTipTrail.externalSamplesOnly = true;
        }
        spearTipTrail.position = spearParticle.TipPosition;
        spearTipTrail.trailScale2 = spearParticle.RushStreakScale;
        spearTipTrail.PushTrailSample(spearTipTrail.position,
            spearParticle.RushStreakRotation, SpriteEffects.None);
        if (spearTipTrail.trailEnd < 10) spearTipTrail.trailEnd++;
        spearTipTrail.trailStart = 0;
        spearTipTrail.timeLeft = 11;
    }

    private void UpdateBodyMotionTrail() =>
        UpdateBodyMotionTrail(ref bodyMotionTrail, false);

    private void UpdateBodyMotionTrail(ref TrailParticle trail, bool newSegment,
        bool whiteAfterimage = false) {
        Vector2 bottom = NPC.Bottom + new Vector2(0f, NPC.gfxOffY + 4f);
        if (newSegment || trail?.IsAlive != true) {
            trail = TrailParticle.Spawn(bottom);
            Texture2D body = ModAsset.KingSlimeBody.Value;
            trail.SetUp(10, body, new Rectangle(0, 240, 174, 120),
                BlendState.AlphaBlend, whiteAfterimage ? 0.52f : 0.44f);
            trail.drawLayer = ParticleLayer.BeforeNPCs;
            trail.color = whiteAfterimage ? Color.White : new Color(145, 195, 255);
            trail.trailAfterImage = whiteAfterimage ? 1f : 0f;
            trail.trailInterpolation = 2;
            trail.useCustomTrailOrigin = true;
            trail.trailOrigin = new Vector2(87f, 120f);
            trail.useTrailScale2 = true;
            trail.externalSamplesOnly = true;
        }
        trail.position = bottom;
        trail.trailScale2 = BodyDrawScale();
        trail.PushTrailSample(bottom, NPC.rotation, SpriteEffects.None);
        trail.trailEnd = 10;
        trail.trailStart = 0;
        trail.timeLeft = 11;
        // Stop sampling at the end of a movement; the trail fades on its own.
    }

    private void Begin(Move next) {
        ReleaseQueuedSpear();
        if (CurrentMove == Move.UmbrellaRush &&
            next != CurrentMove)
            ReleaseUmbrella();
        if ((CurrentMove == Move.HammerSlam ||
                CurrentMove == Move.TeleportHammerSlam ||
                CurrentMove == Move.Ultimate) &&
            next != CurrentMove)
            ReleaseHammer();
        CurrentMove = next;
        if (Main.netMode != NetmodeID.MultiplayerClient) actionSerial++;
        motionMode = MotionMode.Natural;
        NPC.damage = 0;
        Timer = 0;
        actionElapsed = 0f;
        spearRushDistanceScale = 1f;
        umbrellaRushDistanceScale = 1f;
        teleportRushDistance = 0f;
        MoveValue = 0;
        hopLaunchTimer = -1f;
        teleportReady = false;
        teleportOccurred = false;
        teleportWarningCreated = false;
        teleportHammerWarningCreated = false;
        spearWarningCreated = false;
        hammerWarningCreated = false;
        umbrellaWarningCreated = false;
        ultimateWarningMask = 0;
        ultimateLeg = -1;
        teleportChainReady = false;
        teleportChainPreviewCreated = false;
        chainedTeleportAction = false;
        NPC.localAI[0] = 0;
        NPC.localAI[1] = 0;
        NPC.localAI[2] = 0;
        NPC.netUpdate = true;
    }

    private void Land(bool heavy) {
        landingCompression = heavy ? 0.42f : 0.27f;
        SoundEngine.PlaySound(KingSlimeSound.GelBurst, NPC.Bottom);
        if (grounded) GroundEffects(heavy, landing: true);
    }

    private void GroundEffects(bool heavy, bool landing) {
        if (Main.netMode == NetmodeID.Server) return;

        // Two larger puffs travel outward from either side of the body.
        for (int direction = -1; direction <= 1; direction += 2) {
            for (int i = 0; i < 2; i++) {
                SmokeParticle smoke = SmokeParticle.Spawn(
                    NPC.Bottom + new Vector2(direction * (NPC.width * 0.48f + i * 9f), -12f - i * 3f),
                    new Vector2(direction * (heavy ? 5.2f : 4.2f) * (i == 0 ? 1f : 0.72f),
                        i == 0 ? -0.85f : -1.65f),
                    alpha: 0f,
                    scale: NPC.scale * (heavy ? 1.9f : 1.45f) * (i == 0 ? 1f : 0.72f));
                smoke.drawLayer = ParticleLayer.BeforeProjectiles;
                smoke.startOpacity = 0.72f;
                smoke.timeLeft = i == 0 ? 36 : 34;
                smoke.maxTimeLeft = smoke.timeLeft;
                smoke.rotation = Main.rand.NextFloat(-0.2f, 0.2f);
            }
        }

        // Smaller puffs start at random points across the full underside.
        int count = heavy ? 11 : 9;
        for (int i = 0; i < count; i++) {
            float offsetX = Main.rand.NextFloat(-NPC.width * 0.70f, NPC.width * 0.70f);
            SmokeParticle smoke = SmokeParticle.Spawn(
                NPC.Bottom + new Vector2(offsetX, Main.rand.NextFloat(-4f, 0f)),
                new Vector2(offsetX / NPC.width * (heavy ? 2.2f : 1.6f),
                    -Main.rand.NextFloat(0.5f, heavy ? 1.5f : 1.1f)),
                alpha: 0f,
                scale: NPC.scale * Main.rand.NextFloat(heavy ? 0.85f : 0.75f,
                    heavy ? 1.1f : 0.95f));
            smoke.startOpacity = heavy ? 0.58f : 0.48f;
            smoke.rotation = Main.rand.NextFloat(-0.3f, 0.3f);
            smoke.angVelocity = Main.rand.NextFloat(-0.01f, 0.01f);
        }

        if (!landing) return;
        for (int i = 0; i < (heavy ? 28 : 16); i++) {
            float offsetX = Main.rand.NextFloat(-NPC.width * 0.68f, NPC.width * 0.68f);
            Vector2 velocity = new Vector2(
                Math.Sign(offsetX) * Main.rand.NextFloat(0.3f, heavy ? 3.0f : 2.2f),
                -Main.rand.NextFloat(1.1f, heavy ? 3.5f : 2.3f));
            Dust.NewDustPerfect(NPC.Bottom + new Vector2(offsetX, Main.rand.NextFloat(-5f, 2f)),
                DustID.t_Slime, velocity, 80, new Color(180, 225, 255),
                Main.rand.NextFloat(0.8f, heavy ? 1.25f : 1.05f));
        }
    }

    private void Splash(int count, float speed) {
        if (Main.netMode == NetmodeID.Server) return;
        for (int i = 0; i < count; i++) {
            Dust.NewDustPerfect(NPC.Bottom + Main.rand.NextVector2Circular(48f * NPC.scale, 13f * NPC.scale), DustID.t_Slime,
                Main.rand.NextVector2Circular(speed, speed) - Vector2.UnitY,
                80, new Color(180, 225, 255), Main.rand.NextFloat(0.8f, 1.3f) * NPC.scale);
        }
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
        // PreDraw receives lighting color before vanilla applies NPC.alpha.
        if (IsEcho) drawColor *= 1f - NPC.alpha / 255f;
        drawColor *= TeleportOpacity() * (dying
            ? 1f - GuidaUtils.Smoothstep(128f, 138f, deathTimer) : 1f);
        Color bodyColor = drawColor * 0.84f;
        Rectangle source = new Rectangle(0, BodyFrame() * 120, 174, 120);
        Texture2D body = ModAsset.KingSlimeBody.Value;
        Vector2 bottom = NPC.Bottom - screenPos + new Vector2(0, NPC.gfxOffY + 4f);
        Vector2 scale = BodyDrawScale();

        if (NPC.IsABestiaryIconDummy) {
            // The portrait draws in a UI batch, so keep its render target and
            // transform untouched. Combat still uses the deforming mesh below.
            DrawInteriorPortrait(spriteBatch, screenPos, drawColor);
            spriteBatch.Draw(body, bottom, source, bodyColor, NPC.rotation,
                new Vector2(87f, 120f), scale, SpriteEffects.None, 0f);
            DrawInteriorPortrait(spriteBatch, screenPos, drawColor * 0.12f);
            Texture2D crown = ModAsset.Crown.Value;
            spriteBatch.Draw(crown, CrownAnchor() - screenPos, null, drawColor,
                NPC.rotation * 0.4f, new Vector2(crown.Width * 0.5f, crown.Height - 4f),
                0.85f, SpriteEffects.None, 0f);
            return false;
        }

        DrawDeathFlares(spriteBatch, screenPos);
        DrawPhaseFlare(spriteBatch, screenPos);
        DrawUltimateCharge(spriteBatch, screenPos);
        
        if (KingSlimeShadowSystem.Draw(spriteBatch, body, source, bottom, NPC.rotation,
            scale, bodyColor, BodyDeformation, batch => DrawItemShadows(batch, screenPos))) {
            DrawInteriorOverlay(spriteBatch, screenPos, drawColor);
            return false;
        }
        
        spriteBatch.Draw(body, bottom, source, bodyColor, NPC.rotation,
            new Vector2(87, 120), scale, SpriteEffects.None, 0);
        DrawInteriorOverlay(spriteBatch, screenPos, drawColor);
        return false;
    }

    private void DrawInteriorOverlay(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
        Color faint = drawColor * 0.12f;
        if (IsEcho) {
            int ownerIndex = -(int)NPC.ai[3] - 1;
            if (ownerIndex >= 0 && ownerIndex < Main.maxNPCs &&
                Main.npc[ownerIndex].active &&
                Main.npc[ownerIndex].ModNPC is KingSlime owner &&
                owner.woodHostIndex == NPC.whoAmI)
                DrawInteriorItem(spriteBatch, owner.woodChestParticle, screenPos, faint);
            return;
        }
        DrawInteriorItem(spriteBatch, chestParticle, screenPos, faint);
        DrawInteriorItem(spriteBatch, ninjaParticle, screenPos, faint);
        if (woodHostIndex == NPC.whoAmI)
            DrawInteriorItem(spriteBatch, woodChestParticle, screenPos, faint);
    }

    private static void DrawInteriorItem(SpriteBatch spriteBatch, Particle item,
        Vector2 screenPos, Color tint) {
        if (item?.IsAlive != true) return;
        spriteBatch.Draw(item.Texture, item.position - screenPos, item.SourceRectangle,
            tint, item.rotation, item.Origin, item.scale, SpriteEffects.None, 0f);
    }

    private void DrawInteriorPortrait(SpriteBatch spriteBatch, Vector2 screenPos, Color tint) {
        Texture2D gold = ModAsset.KingSlimeChest.Value;
        Texture2D wood = ModAsset.KingSlimeWoodChest.Value;
        Texture2D ninja = ModAsset.KingSlimeNinja.Value;
        Rectangle closed = new(0, 0, 32, 32);
        spriteBatch.Draw(gold, InteriorPosition(0f) - screenPos, closed, tint,
            InteriorRotation(0f), new Vector2(16f, 16f), 1f, SpriteEffects.None, 0f);
        spriteBatch.Draw(wood, InteriorPosition(MathHelper.TwoPi / 3f) - screenPos, closed, tint,
            InteriorRotation(MathHelper.TwoPi / 3f), new Vector2(16f, 16f), 1f,
            SpriteEffects.None, 0f);
        spriteBatch.Draw(ninja, InteriorPosition(MathHelper.TwoPi * 2f / 3f) - screenPos,
            null, tint, InteriorRotation(MathHelper.TwoPi * 2f / 3f),
            new Vector2(ninja.Width * 0.5f, ninja.Height * 0.5f), 1f,
            SpriteEffects.None, 0f);
    }

    private void DrawItemShadows(SpriteBatch spriteBatch, Vector2 screenPos) {
        if (IsEcho && NPC.alpha > 0) return;
        // Small downward, inward projections follow the actual item particles.
        DrawItemShadow(spriteBatch, spearParticle, screenPos);
        DrawItemShadow(spriteBatch, grappleParticle, screenPos);
        DrawItemShadow(spriteBatch, potionParticle, screenPos);
        DrawItemShadow(spriteBatch, ropeParticle, screenPos);
        DrawItemShadow(spriteBatch, umbrellaParticle, screenPos);
        DrawItemShadow(spriteBatch, hammerParticle, screenPos);
        DrawItemShadow(spriteBatch, shortswordParticle, screenPos);
        DrawItemShadow(spriteBatch, fireWandParticle, screenPos);
        DrawItemShadow(spriteBatch, boomerangParticle, screenPos);
        DrawItemShadow(spriteBatch, slimeStaffParticle, screenPos);
        int grenadeType = ModContent.ProjectileType<KingSlimeGrenade>();
        int shurikenType = ModContent.ProjectileType<KingSlimeShuriken>();
        for (int i = 0; i < Main.maxProjectiles; i++) {
            Projectile item = Main.projectile[i];
            bool isGrenade = item.type == grenadeType;
            if (!item.active || (!isGrenade && item.type != shurikenType) ||
                (isGrenade && item.ai[0] == 2f) ||
                Math.Abs(item.Center.X - NPC.Center.X) > NPC.width ||
                Math.Abs(item.Center.Y - NPC.Center.Y) > NPC.height) continue;
            Vector2 projected = item.Center +
                new Vector2((NPC.Center.X - item.Center.X) * 0.18f, 10f);
            Texture2D texture = isGrenade ? ModAsset.KingSlimeGrenade.Value : ModAsset.KingSlimeShuriken.Value;
            spriteBatch.Draw(texture, projected - screenPos, null,
                new Color(8, 18, 32) * 0.23f, item.rotation,
                texture.Size() * 0.5f, item.scale, SpriteEffects.None, 0f);
        }

        if (crownParticle?.IsAlive == true) {
            float heightAboveHead = Math.Max(0f, CrownAnchor().Y - crownParticle.position.Y);
            Vector2 projected = crownParticle.position + Vector2.UnitY *
                (2f + heightAboveHead * 0.8f);
            spriteBatch.Draw(crownParticle.Texture, projected - screenPos, null,
                new Color(8, 18, 32) * (crownParticle.alpha * 0.32f), crownParticle.rotation,
                crownParticle.Origin, crownParticle.scale, SpriteEffects.None, 0f);
        }
    }

    private void DrawItemShadow(SpriteBatch spriteBatch, Particle item, Vector2 screenPos) {
        if (item?.IsAlive != true || item.alpha <= 0f || item.Texture == null) return;
        Vector2 projected = item.position + new Vector2((NPC.Center.X - item.position.X) * 0.18f, 10f);
        spriteBatch.Draw(item.Texture, projected - screenPos, item.SourceRectangle,
            new Color(8, 18, 32) * (item.alpha * 0.28f), item.rotation,
            item.Origin, item.scale, SpriteEffects.None, 0f);
    }
}

internal static class KingSlimeSound {
    public static SoundStyle Roar => SoundID.Roar;
    public static SoundStyle GelHit => SoundID.NPCHit1 with { Volume = 0.65f, PitchVariance = 0.15f };
    // Terraria's NPC_Killed_1 with its first 20 ms of quiet audio removed.
    public static SoundStyle GelBurst => new("ReverieMod/Assets/Sounds/KingSlimeGelBurst") { Volume = 0.75f };
    public static SoundStyle BossDeath => new("ReverieMod/Assets/Sounds/KingSlimeBossDeath") { Volume = 1f, MaxInstances = 2 };
    public static SoundStyle Jump => SoundID.Item154 with { Volume = 0.65f, Pitch = -0.25f };
    public static SoundStyle ItemUse => SoundID.Item1 with { Volume = 0.75f };
    public static void PlayRush(Vector2 position) =>
        SoundEngine.PlaySound(ItemUse with { Volume = 1f }, position);
    public static SoundStyle Mechanism => SoundID.Item7 with { Volume = 0.6f };
    public static SoundStyle Impact => SoundID.Item14 with { Volume = 0.7f };
    public static SoundStyle Drink => SoundID.Item3 with { Volume = 0.7f, Pitch = -0.12f };
    public static SoundStyle Teleport => SoundID.Item8 with { Volume = 0.62f, Pitch = -0.18f };
    public static SoundStyle Cast => SoundID.Item20 with { Volume = 0.65f };
}
