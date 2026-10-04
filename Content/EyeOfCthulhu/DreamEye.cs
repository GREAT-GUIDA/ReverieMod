using System;
using System.IO;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using ReverieMod.Content.Particles;

namespace ReverieMod.Content.EyeOfCthulhu;

// Dream Eye (旧梦之眼) - A new Eye of Cthulhu boss with custom attacks and visual effects.
// Sprite: NPC_4.png is 110x996, six frames of 166 pixels each.
// Frames 0-2: intact eye (pupil at bottom, tendrils at top).
// Frames 3-5: opened maw with teeth.
[AutoloadBossHead]
public partial class DreamEye : ModNPC {
    private enum Phase { Intro, Gaze, ShatterTransition, Maw, NightmareFall, Nightmare, Death }
    private enum Move {
        Intro,
        GazeDash, WeepingTears, IrisBloom, ServantWeave,
        FrenzyCharge, Hemorrhage, RiftAmbush, Devour,
        ThousandEyes, DreamRay
    }

    private Phase CurrentPhase {
        get => (Phase)(int)NPC.ai[0];
        set => NPC.ai[0] = (float)value;
    }
    private Move CurrentMove {
        get => (Move)(int)NPC.ai[1];
        set => NPC.ai[1] = (float)value;
    }
    private ref float Timer => ref NPC.ai[2];
    private ref float MoveParam => ref NPC.ai[3];

    // Visual state (client-side)
    private float visualTicks;
    private float pupilScale = 1f;
    private float pupilTargetScale = 1f;
    private Vector2 pupilOffset = Vector2.Zero;
    private float irisRotation;
    private float glowPulse;
    private int currentSpriteFrame;
    private bool introWarningCreated;
    private Vector2[] trailPositions = new Vector2[12];
    private int trailHead;

    // Combat state
    private bool isEnraged;
    private float enragedTimer;
    private int summonedServants;
    private const int MaxServants = 4;
    private bool shatterComplete;
    private float previousTimer;

    // Network sync
    private ushort actionSerial;

    public override string Texture => ModAsset.NPC_4_Mod;
    public override string BossHeadTexture => ModAsset.NPC_4_Mod;

    public override void SetStaticDefaults() {
        Main.npcFrameCount[Type] = 6;
        NPCID.Sets.BossBestiaryPriority.Add(Type);
        NPCID.Sets.MPAllowedEnemies[Type] = true;
        NPCID.Sets.TrailCacheLength[Type] = 10;
        NPCID.Sets.TrailingMode[Type] = 3;
    }

    public override void SetDefaults() {
        NPC.width = 100;
        NPC.height = 110;
        NPC.damage = 15;
        NPC.defense = 12;
        NPC.lifeMax = 2800;
        NPC.knockBackResist = 0f;
        NPC.value = Item.buyPrice(gold: 3);
        NPC.boss = true;
        NPC.noGravity = true;
        NPC.noTileCollide = true;
        NPC.netAlways = true;
        NPC.HitSound = SoundID.NPCHit9;
        NPC.DeathSound = SoundID.NPCDeath1;
        NPC.aiStyle = -1;

        if (!Main.dedServ) {
            Music = MusicLoader.GetMusicSlot(Mod, "Assets/Music/KingSlime");
        }
    }

    public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment) {
        if (Main.masterMode) {
            NPC.lifeMax = (int)(2800 * 1.5f * 1.25f * balance);
            NPC.damage = (int)(15 * 1.5f * 1.5f);
        } else if (Main.expertMode) {
            NPC.lifeMax = (int)(2800 * 1.5f * balance);
            NPC.damage = (int)(15 * 1.5f);
        } else {
            NPC.lifeMax = (int)(2800 * balance);
        }
        NPC.life = NPC.lifeMax;
    }

    public override void SendExtraAI(BinaryWriter writer) {
        writer.Write((byte)CurrentPhase);
        writer.Write((byte)CurrentMove);
        writer.Write(Timer);
        writer.Write(MoveParam);
        writer.Write(actionSerial);
        writer.Write(isEnraged);
        writer.Write(enragedTimer);
        writer.Write(summonedServants);
        writer.Write(shatterComplete);
    }

    public override void ReceiveExtraAI(BinaryReader reader) {
        CurrentPhase = (Phase)reader.ReadByte();
        Move oldMove = CurrentMove;
        CurrentMove = (Move)reader.ReadByte();
        bool moveChanged = oldMove != CurrentMove;
        Timer = reader.ReadSingle();
        MoveParam = reader.ReadSingle();
        actionSerial = reader.ReadUInt16();
        isEnraged = reader.ReadBoolean();
        enragedTimer = reader.ReadSingle();
        summonedServants = reader.ReadInt32();
        shatterComplete = reader.ReadBoolean();
    }

    public override void OnSpawn(IEntitySource source) {
        CurrentPhase = Phase.Intro;
        CurrentMove = Move.Intro;
        Timer = 0;
        NPC.alpha = 255;
    }

    public override void AI() {
        if (Main.netMode != NetmodeID.Server) {
            UpdateTrail();
            visualTicks++;
        }

        NPC.TargetClosest(true);
        Player target = Main.player[NPC.target];

        // Flee or despawn check
        if (!target.active || target.dead || Vector2.Distance(NPC.Center, target.Center) > 5600f) {
            NPC.velocity.Y -= 0.4f;
            if (NPC.timeLeft > 60) NPC.timeLeft = 60;
            return;
        }

        // Enrage during daytime
        if (!isEnraged && Main.dayTime && CurrentPhase != Phase.Intro) {
            isEnraged = true;
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                NPC.netUpdate = true;
            }
        }
        if (isEnraged) {
            enragedTimer += 1f;
            NPC.damage = NPC.defDamage * 2;
            NPC.defense = NPC.defDefense + 10;
        }

        // Pupil animation
        UpdatePupilVisuals(target);

        // Phase transitions
        float healthPercent = NPC.life / (float)NPC.lifeMax;
        if (CurrentPhase == Phase.Gaze && healthPercent <= 0.60f && Main.netMode != NetmodeID.MultiplayerClient) {
            BeginShatterTransition();
        } else if (CurrentPhase == Phase.Maw && healthPercent <= 0.25f && Main.netMode != NetmodeID.MultiplayerClient) {
            BeginNightmareFall();
        }

        previousTimer = Timer;
        Timer += 1f;

        // Execute current phase AI
        switch (CurrentPhase) {
            case Phase.Intro:
                IntroPhase(target);
                break;
            case Phase.Gaze:
                GazePhaseAI(target);
                break;
            case Phase.ShatterTransition:
                ShatterTransitionPhase(target);
                break;
            case Phase.Maw:
                MawPhaseAI(target);
                break;
            case Phase.NightmareFall:
                NightmareFallPhase(target);
                break;
            case Phase.Nightmare:
                NightmarePhaseAI(target);
                break;
        }
    }

    private void UpdatePupilVisuals(Player target) {
        // Pupil follows target
        Vector2 toTarget = target.Center - NPC.Center;
        float distance = toTarget.Length();
        if (distance > 0) {
            Vector2 targetOffset = Vector2.Normalize(toTarget) * Math.Min(distance * 0.015f, 10f);
            pupilOffset = Vector2.Lerp(pupilOffset, targetOffset, 0.12f);
        }

        // Pupil scale animation
        pupilScale = MathHelper.Lerp(pupilScale, pupilTargetScale, 0.08f);

        // Iris rotation
        irisRotation += 0.008f;

        // Glow pulse
        float targetGlow = (CurrentMove == Move.GazeDash || CurrentMove == Move.DreamRay) ? 1.5f : 0.3f;
        glowPulse = MathHelper.Lerp(glowPulse, targetGlow, 0.04f);
    }

    private void UpdateTrail() {
        if ((int)(visualTicks) % 2 == 0) {
            trailPositions[trailHead] = NPC.Center;
            trailHead = (trailHead + 1) % trailPositions.Length;
        }
    }

    private void BeginShatterTransition() {
        CurrentPhase = Phase.ShatterTransition;
        Timer = 0;
        NPC.velocity = Vector2.Zero;
        NPC.netUpdate = true;

        if (Main.netMode != NetmodeID.Server) {
            SoundEngine.PlaySound(SoundID.NPCDeath1, NPC.Center);
        }
    }

    private void BeginNightmareFall() {
        CurrentPhase = Phase.NightmareFall;
        Timer = 0;
        NPC.velocity *= 0.5f;
        NPC.netUpdate = true;
    }

    private bool PassedTime(float time) => previousTimer < time && Timer >= time;

    private void Begin(Move next) {
        CurrentMove = next;
        if (Main.netMode != NetmodeID.MultiplayerClient) actionSerial++;
        Timer = 0;
        MoveParam = 0;
        NPC.netUpdate = true;
    }

    // Intro: Eye descends from above
    private void IntroPhase(Player target) {
        NPC.alpha = Math.Max(0, NPC.alpha - 5);

        if (Timer < 60) {
            // Descend
            Vector2 spawnPos = target.Center - new Vector2(0, 600);
            NPC.Center = Vector2.Lerp(NPC.Center, spawnPos, 0.04f);
            NPC.velocity *= 0.9f;
        } else if (Timer < 120) {
            // Glare
            pupilTargetScale = GuidaUtils.Smoothstep(60f, 90f, Timer);

            if (Main.netMode != NetmodeID.Server && Timer >= 80 && Timer < 120) {
                ScreenTwistSystem.URadialBlurIntensity = 0.12f * GuidaUtils.Smoothstep(80f, 95f, Timer);
                ScreenTwistSystem.URadialBlurPosition = (NPC.Center - Main.screenPosition) / Main.ScreenSize.ToVector2();
            }
        } else {
            CurrentPhase = Phase.Gaze;
            Timer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                ChooseNextMove();
            }
        }
    }

    // Gaze phase AI (60-100% health)
    private void GazePhaseAI(Player target) {
        switch (CurrentMove) {
            case Move.GazeDash:
                GazeDashAttack(target);
                break;
            case Move.WeepingTears:
                WeepingTearsAttack(target);
                break;
            case Move.IrisBloom:
                IrisBloomAttack(target);
                break;
            case Move.ServantWeave:
                ServantWeaveAttack(target);
                break;
        }
    }

    // Maw phase AI (25-60% health)
    private void MawPhaseAI(Player target) {
        switch (CurrentMove) {
            case Move.FrenzyCharge:
                FrenzyChargeAttack(target);
                break;
            case Move.Hemorrhage:
                HemorrhageAttack(target);
                break;
            case Move.RiftAmbush:
                RiftAmbushAttack(target);
                break;
            case Move.Devour:
                DevourAttack(target);
                break;
        }
    }

    // Nightmare phase AI (<25% health)
    private void NightmarePhaseAI(Player target) {
        switch (CurrentMove) {
            case Move.FrenzyCharge:
            case Move.Hemorrhage:
            case Move.RiftAmbush:
            case Move.Devour:
                MawPhaseAI(target);
                break;
            case Move.ThousandEyes:
                ThousandEyesAttack(target);
                break;
            case Move.DreamRay:
                DreamRayAttack(target);
                break;
        }
    }

    private void ShatterTransitionPhase(Player target) {
        NPC.velocity *= 0.95f;

        // Spin and crack
        NPC.rotation += 0.05f * (1f + Timer / 90f);
        pupilTargetScale = 0.3f + 0.5f * (float)Math.Sin(Timer * 0.15f);

        // Screen shake
        if (Main.netMode != NetmodeID.Server && (int)Timer % 20 == 0) {
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                NPC.Center, Main.rand.NextVector2CircularEdge(1f, 1f), 6f, 5f, 15));
        }

        // Particles
        if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(4)) {
            Vector2 particleVel = Main.rand.NextVector2CircularEdge(2f, 2f);
            SmokeParticle smoke = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                NPC.Center + Main.rand.NextVector2Circular(40, 40), particleVel);
            if (smoke != null) {
                smoke.startOpacity = 0.6f;
            }
        }

        if (Timer >= 90) {
            CurrentPhase = Phase.Maw;
            Timer = 0;
            shatterComplete = true;
            NPC.rotation = 0f;
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                ChooseNextMove();
            }
        }
    }

    private void NightmareFallPhase(Player target) {
        NPC.velocity *= 0.96f;

        // Darkness descends
        if (Main.netMode != NetmodeID.Server) {
            ScreenTwistSystem.UBloomIntensity = 0.3f * GuidaUtils.Smoothstep(0f, 60f, Timer);
        }

        if (Timer >= 60) {
            CurrentPhase = Phase.Nightmare;
            Timer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                ChooseNextMove();
            }
        }
    }

    private void ChooseNextMove() {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;

        Move[] availableMoves = CurrentPhase switch {
            Phase.Gaze => new[] { Move.GazeDash, Move.WeepingTears, Move.IrisBloom, Move.ServantWeave },
            Phase.Maw => new[] { Move.FrenzyCharge, Move.Hemorrhage, Move.RiftAmbush, Move.Devour },
            Phase.Nightmare => new[] { Move.FrenzyCharge, Move.Hemorrhage, Move.ThousandEyes, Move.DreamRay },
            _ => new[] { Move.GazeDash }
        };

        Move nextMove;
        do {
            nextMove = availableMoves[Main.rand.Next(availableMoves.Length)];
        } while (nextMove == CurrentMove && availableMoves.Length > 1);

        Begin(nextMove);
    }

    // Attack implementations are in DreamEyeAttacks.cs (partial class)

    public override void FindFrame(int frameHeight) {
        // Frames 0-2: intact eye (Gaze phase)
        // Frames 3-5: opened maw (Maw/Nightmare phases)
        int baseFrame = (CurrentPhase == Phase.Gaze || CurrentPhase == Phase.Intro) ? 0 : 3;

        if ((int)(visualTicks / 8f) != (int)((visualTicks - 1f) / 8f)) {
            currentSpriteFrame = (currentSpriteFrame + 1) % 3;
        }

        NPC.frame.Y = (baseFrame + currentSpriteFrame) * frameHeight;
    }

    public override void HitEffect(NPC.HitInfo hit) {
        if (Main.netMode == NetmodeID.Server) return;

        for (int i = 0; i < 2; i++) {
            Vector2 vel = Main.rand.NextVector2Circular(2f, 2f);
            SmokeParticle smoke = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                NPC.Center + Main.rand.NextVector2Circular(30, 30), vel);
            if (smoke != null) {
                smoke.startOpacity = 0.5f;
            }
        }

        if (NPC.life <= 0) {
            for (int i = 0; i < 25; i++) {
                Vector2 vel = Main.rand.NextVector2CircularEdge(6f, 6f);
                SmokeParticle smoke = ParticleManager.Instance?.NewParticle<SmokeParticle>(
                    NPC.Center, vel);
                if (smoke != null) {
                    smoke.scale = Main.rand.NextFloat(1f, 1.8f);
                }
            }
        }
    }

    public override void OnKill() {
        if (Main.netMode != NetmodeID.Server) {
            ScreenTwistSystem.UBloomIntensity = 0.6f;
            ScreenTwistSystem.URadialBlurIntensity = 0.4f;

            Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                NPC.Center, Main.rand.NextVector2CircularEdge(1f, 1f), 25f, 10f, 50));
        }
    }

    public override void ModifyNPCLoot(NPCLoot npcLoot) {
        npcLoot.Add(ItemDropRule.Common(ItemID.DemoniteOre, 1, 30, 87));
        npcLoot.Add(ItemDropRule.Common(ItemID.ShadowScale, 1, 20, 40));
        npcLoot.Add(ItemDropRule.Common(ItemID.LesserHealingPotion, 1, 3, 8));
    }

    public override bool? DrawHealthBar(byte hbPosition, ref float scale, ref Vector2 position) {
        scale = 1.5f;
        return null;
    }
}
