using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using ReverieMod.Content;
using ReverieMod.Content.Particles;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.Localization;
using Microsoft.Xna.Framework.Graphics;

namespace ReverieMod.Content.KingSlime;

public partial class KingSlime {
    internal bool CinematicBarsActive =>
        !IsEcho && !NPC.IsABestiaryIconDummy &&
        (dying || CurrentMove == Move.Intro);

    internal float CinematicBarProgress {
        get {
            if (!CinematicBarsActive) return 0f;
            if (dying)
                return GuidaUtils.Smoothstep(0f, 20f, deathTimer);
            return GuidaUtils.Smoothstep(0f, 24f, Timer);
        }
    }

    private void StyleBodyWarning(WarningLineParticle line) {
        line.lineWidth = 96f * NPC.scale;
        line.alpha = 0.8f;
        line.color = new Color(105, 185, 255);
    }

    private void StyleVerticalBodyWarning(WarningLineParticle line) {
        StyleBodyWarning(line);
        line.lineWidth = 136f * NPC.scale;
    }

    // The boss itself enters for one measure, roars for one measure, then the
    // first phase starts exactly on the next whole-measure boundary.
    private void UpdateIntroTimeline(Player target) {
        if (measureTicks >= 180f && CurrentMove == Move.Intro) {
            introComplete = true;
            if (NPC.life * 10 <= NPC.lifeMax * 7) pendingSplit = true;
            NPC.netUpdate = true;
            StartNextPattern(target);
        }
    }

    private void Intro(Player target) {
        NPC.damage = 0;
        if (Timer < 84f) {
            if (!introStarted) {
                introStarted = true;
                if (Main.netMode != NetmodeID.MultiplayerClient) {
                    NPC.Center = new Vector2(NPC.Center.X,
                        Math.Max(160f, target.Top.Y - 760f));
                    NPC.velocity = Vector2.Zero;
                    NPC.localAI[0] = 1f;
                    grounded = false;
                    NPC.netUpdate = true;
                }
            }
            if (!introWarningCreated && Main.netMode != NetmodeID.Server) {
                introWarningCreated = true;
                Vector2 warningStart = new(NPC.Center.X,
                    Math.Max(160f, target.Top.Y - 760f) + NPC.height * 0.5f);
                WarningLineParticle warning = ParticleManager.Instance.NewParticle<WarningLineParticle>(
                    warningStart, Vector2.Zero);
                warning.lineRotation = MathHelper.Pi;
                warning.lineLength = Math.Max(200f, target.Bottom.Y - warningStart.Y);
                StyleVerticalBodyWarning(warning);
                warning.time = 82f;
            }
            // Give the landing column a visible lead before the boss starts falling.
            if (Timer < 8f) {
                motionMode = MotionMode.Controlled;
                NPC.velocity = Vector2.Zero;
                return;
            }
            if (PassedTime(8f)) {
                NPC.velocity = Vector2.UnitY * 4f;
                NPC.netUpdate = true;
            }
            if (LandedFromAir()) {
                NPC.localAI[0] = 0f;
                NPC.velocity = Vector2.Zero;
                Land(false);
            }
            if (!grounded) motionMode = MotionMode.FastFall;
            else NPC.velocity.X *= 0.75f;
            return;
        }

        float roarTime = Timer - 84f;
        NPC.velocity.X *= grounded ? 0.7f : 0.94f;
        if (LandedFromAir()) {
            NPC.localAI[0] = 0f;
            NPC.velocity = Vector2.Zero;
            Land(false);
        }

        float roarEnvelope = GuidaUtils.Smoothstep(10f, 24f, roarTime) *
            GuidaUtils.Smoothstep(96f, 76f, roarTime);
        landingCompression = Math.Max(landingCompression,
            (0.13f + 0.07f * (float)Math.Sin(roarTime * 0.34f)) * roarEnvelope);
        NPC.rotation = (float)Math.Sin(roarTime * 0.23f) * 0.075f * roarEnvelope;
        if (Main.netMode == NetmodeID.Server) return;

        if (PassedTime(102f)) {
            SoundEngine.PlaySound(KingSlimeSound.Roar, NPC.Center);
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(NPC.Center,
                Main.rand.NextVector2CircularEdge(1f, 1f), 15f, 6f, 100));
            Main.instance.CameraModifiers.Add(new PunchCameraModifier(NPC.Center,
                Main.rand.NextVector2CircularEdge(1f, 1f), 20f, 4f, 20));
        }
        if (PassedTime(148f))
            ScreenPresentationSystem.ShowTitle(132,
                Language.GetTextValue("Mods.ReverieMod.KingSlimeTitles.Theme"),
                new Color(245, 218, 131),
                Language.GetTextValue("Mods.ReverieMod.KingSlimeTitles.Intro"),
                new Color(106, 207, 255));
        if (PassedTime(102f) || PassedTime(120f) || PassedTime(138f))
            ParticleManager.Instance.NewParticle<RoarEffectParticle>(
                NPC.Center, Vector2.Zero, scale: NPC.scale);
        ScreenTwistSystem.URadialBlurIntensity =
            (1f + (float)Math.Sin(roarTime * 0.5f)) * 0.3f *
            GuidaUtils.Smoothstep(18f, 27f, roarTime) *
            GuidaUtils.Smoothstep(88f, 64f, roarTime);
        ScreenTwistSystem.URadialBlurPosition =
            (NPC.Center - Main.screenPosition) / Main.ScreenSize.ToVector2();
        if (roarTime < 18f || roarTime > 58f ||
            (int)roarTime / 5 == (int)(previousTimer - 84f) / 5)
            return;

        RoarLineEffectParticle line = ParticleManager.Instance.NewParticle<RoarLineEffectParticle>(
            NPC.Center, Vector2.Zero);
        line.rotation = Main.rand.NextFloat(MathHelper.TwoPi);

        // TombwardJourney's roar: a ring every five ticks, size 8, life 36,
        // strength 0.2, layered under the same camera punches and roar sound.
        TwistCircleParticle.Spawn(NPC.Center, 9f, 60, 0.25f);
        for (int i = 0; i < 3; i++) {
            Vector2 outward = Main.rand.NextVector2CircularEdge(1f, 1f);
            SmokeParticle smoke = ParticleManager.Instance.NewParticle<SmokeParticle>(
                NPC.Center + outward * NPC.width * 0.3f, outward * Main.rand.NextFloat(2f, 4f),
                alpha: 0f, scale: Main.rand.NextFloat(0.9f, 1.5f));
            smoke.startOpacity = 0.34f;
        }
    }

    private void SpawnPhaseStreak() {
        if (tempoStage == 0 || IsEcho || NPC.IsABestiaryIconDummy ||
            Main.netMode == NetmodeID.Server) return;

        float entrance = CurrentMove == Move.PhaseTwoPotions
            ? GuidaUtils.Smoothstep(86f, 90f, Timer) : 1f;
        if (Main.rand.NextFloat() >= 0.28f * entrance) return;

        Vector2 origin = NPC.Center + Vector2.UnitY * NPC.gfxOffY +
            new Vector2(Main.rand.NextFloat(-NPC.width * 0.53f, NPC.width * 0.53f),
                Main.rand.NextFloat(-NPC.height * 0.12f, NPC.height * 0.43f));
        GlowStreakParticle streak = ParticleManager.Instance.NewParticle<GlowStreakParticle>(
            origin, new Vector2(Main.rand.NextFloat(-0.22f, 0.22f),
                -Main.rand.NextFloat(2.4f, 3.7f)));
        streak.color = Color.Lerp(new Color(112, 185, 255), Color.White,
            Main.rand.NextFloat(0.38f, 0.76f));
        streak.alpha = Main.rand.NextFloat(0.62f, 0.84f);
        streak.drawSize = new Vector2(Main.rand.NextFloat(6f, 10f),
            Main.rand.NextFloat(34f, 54f)) * NPC.scale;
        streak.timeLeft = streak.maxTimeLeft = Main.rand.Next(20, 29);
    }

    private void DrawDeathFlares(SpriteBatch spriteBatch, Vector2 screenPos) {
        if (!dying || IsEcho || deathTimer >= 138f) return;

        Texture2D flare = ModAsset.KingSlimeDeathFlare.Value;
        Vector2 center = NPC.Center - screenPos +
            Vector2.UnitY * (NPC.gfxOffY + 4f);
        float progress = MathHelper.Clamp(deathTimer / 138f, 0f, 1f);
        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive,
            Main.DefaultSamplerState, DepthStencilState.None,
            RasterizerState.CullCounterClockwise, null,
            Main.GameViewMatrix.TransformationMatrix);
        for (int i = 0; i < 6; i++) {
            float phase = i * MathHelper.TwoPi / 6f;
            float rotation = phase + deathTimer * (i % 2 == 0 ? 1f : -1f) *
                (0.006f + i * 0.003f);
            float size = NPC.width / (float)flare.Width * (2.3f + i * 0.45f) *
                MathHelper.Lerp(0.6f, 4f, progress);
            Color glow = Color.Lerp(new Color(75, 145, 255),
                new Color(205, 235, 255), i / 5f);
            spriteBatch.Draw(flare, center, null, glow, rotation,
                flare.Size() * 0.5f, size, SpriteEffects.None, 0f);
        }
        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            Main.DefaultSamplerState, DepthStencilState.None,
            RasterizerState.CullCounterClockwise, null,
            Main.GameViewMatrix.TransformationMatrix);
    }

    private void DrawPhaseFlare(SpriteBatch spriteBatch, Vector2 screenPos) {
        if (tempoStage == 0 || IsEcho) return;

        Texture2D flare = ModAsset.TeleportFlare.Value;
        float entrance = CurrentMove == Move.PhaseTwoPotions
            ? GuidaUtils.Smoothstep(86f, 90f, Timer) : 1f;
        float charge = UltimateActive ? GuidaUtils.Smoothstep(0f, 145f, Timer) : 0f;
        ultimateFlareBoost = MathHelper.Lerp(ultimateFlareBoost, charge, 0.08f);
        float opacity = entrance * TeleportOpacity() * (1f + ultimateFlareBoost * 0.65f);
        float pulse = 1f + 0.055f * (float)System.Math.Sin(visualTicks * 0.08f);
        float drawScale = NPC.width * 2.05f * pulse / flare.Width *
            (1f + ultimateFlareBoost * 0.16f);
        Vector2 center = NPC.Center - screenPos + Vector2.UnitY * (NPC.gfxOffY + 4f);
        float rotation = visualTicks * 0.014f;

        // Zero alpha lets the pale flare add light in the existing world batch.
        Color outer = new Color(125, 190, 255) * (0.17f * opacity);
        outer.A = 0;
        Color inner = new Color(215, 235, 255) * (0.13f * opacity);
        inner.A = 0;
        spriteBatch.Draw(flare, center, null, outer, rotation,
            flare.Size() * 0.5f, drawScale, SpriteEffects.None, 0f);
        spriteBatch.Draw(flare, center, null, inner, -rotation,
            flare.Size() * 0.5f, drawScale * 0.88f, SpriteEffects.None, 0f);
    }

    private void DropHealthSlimes() {
        if (IsEcho || Main.netMode == NetmodeID.MultiplayerClient) return;
        // Count one-time 4% thresholds from maximum health. Healing cannot
        // retrigger an already crossed threshold.
        while (healthSlimeThresholdsReached < 25 &&
            NPC.life * 25 <= NPC.lifeMax * (24 - healthSlimeThresholdsReached))
            healthSlimeThresholdsReached++;
        if (splitTimer > 0f) {
            // Damage taken while split never produces a delayed slime on merging.
            healthSlimeDrops = healthSlimeThresholdsReached;
            return;
        }
        // Damage taken midair is queued, including when healing follows before
        // landing. The slime is released only once the boss has actual ground contact.
        if (!grounded) return;
        while (healthSlimeDrops < healthSlimeThresholdsReached) {
            healthSlimeDrops++;
            int type = healthSlimeDrops % 3 == 0
                ? NPCID.SlimeSpiked : NPCID.BlueSlime;
            float x = NPC.Center.X + Main.rand.NextFloat(-NPC.width * 0.35f,
                NPC.width * 0.35f);
            int index = NPC.NewNPC(NPC.GetSource_FromAI(), (int)x,
                (int)NPC.Bottom.Y - 12, type);
            if (index < 0 || index >= Main.maxNPCs) continue;
            NPC slime = Main.npc[index];
            slime.Center = new Vector2(x, NPC.Bottom.Y - slime.height * 0.5f - 6f);
            slime.velocity = new Vector2(Main.rand.NextFloat(-1.6f, 1.6f), 2f);
            slime.target = NPC.target;
            slime.netUpdate = true;
        }
    }

    private float difficultyDamageMult = 1f;
    private float fightElapsedTicks;
    private float damageBeforeReduction;
    private float currentDamageReduction;
    private int lastObservedLife = -1;

    private KingSlime DamageOwner() {
        if (!IsEcho) return this;
        int index = -(int)NPC.ai[3] - 1;
        return index >= 0 && index < Main.maxNPCs &&
            Main.npc[index].active && Main.npc[index].ModNPC is KingSlime owner
            ? owner : this;
    }

    private bool HasPhaseDamageReduction() =>
        CurrentMove is Move.Intro or Move.PhaseTwoPotions or Move.Split ||
        CurrentMove == Move.Ultimate && Timer < 180f;

    private void UpdateCombatDurability() {
        NPC.defense = tempoStage > 0 ? 14 : 10;
        if (IsEcho || NPC.IsABestiaryIconDummy ||
            Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient) return;

        if (lastObservedLife >= 0 && NPC.life < lastObservedLife) {
            damageBeforeReduction += (lastObservedLife - NPC.life) /
                ((1f - currentDamageReduction) *
                    (HasPhaseDamageReduction() ? 0.5f : 1f));
            NPC.netUpdate = true;
        }
        lastObservedLife = NPC.life;
        fightElapsedTicks++;
        float previousReduction = currentDamageReduction;
        float elapsedSeconds = fightElapsedTicks / 60f;
        float secondsLeftToTarget = 120f - elapsedSeconds;
        if (secondsLeftToTarget <= 0f || damageBeforeReduction <= 0f) {
            currentDamageReduction = 0f;
            if (previousReduction > 0f && Main.netMode == Terraria.ID.NetmodeID.Server)
                NPC.netUpdate = true;
            return;
        }

        float estimatedDps = damageBeforeReduction / elapsedSeconds;
        // Reduce only the damage that would otherwise finish the fight before two minutes.
        currentDamageReduction = MathHelper.Clamp(
            1f - NPC.life / (estimatedDps * secondsLeftToTarget), 0f, 0.4f);
        if (Main.netMode == Terraria.ID.NetmodeID.Server &&
            (previousReduction > 0f && currentDamageReduction == 0f ||
                currentDamageReduction > 0f && (int)fightElapsedTicks % 30 == 0))
            NPC.netUpdate = true;
    }

    public override void ModifyIncomingHit(ref NPC.HitModifiers modifiers) {
        KingSlime owner = DamageOwner();
        modifiers.FinalDamage *= (1f - owner.currentDamageReduction) *
            (owner.HasPhaseDamageReduction() ? 0.5f : 1f);
    }

    public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment) {
        if (Main.masterMode) {
            difficultyDamageMult = 1.5f * 1.5f;
            NPC.lifeMax = (int)Math.Round(2400f * 1.5f * 1.25f * balance);
        } else if (Main.expertMode) {
            difficultyDamageMult = 1.5f;
            NPC.lifeMax = (int)Math.Round(2400f * 1.5f * balance);
        } else {
            difficultyDamageMult = 1f;
            NPC.lifeMax = (int)Math.Round(2400f * balance);
        }

        NPC.life = NPC.lifeMax;
    }

    private int ContactDamage(int normalAmount) =>
        (int)Math.Round(normalAmount * difficultyDamageMult);
}
