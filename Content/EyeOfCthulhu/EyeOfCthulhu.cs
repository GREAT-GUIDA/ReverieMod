using System;
using System.IO;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using ReverieMod.Content;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace ReverieMod.Content.EyeOfCthulhu;

// A separate NPC with its own state machine. No vanilla AI or GlobalNPC hooks.
public partial class EyeOfCthulhu : ModNPC {
    internal enum Attack { Arrival, Servants, Gaze, Dash, Transform, BloodRain, Orbit, Echoes, Eclipse, Death }
    internal Attack State => (Attack)(int)NPC.ai[0];
    internal float Timer => NPC.ai[1];
    internal int Phase => (int)NPC.ai[2];
    private int Cycle { get => (int)NPC.ai[3]; set => NPC.ai[3] = value; }
    internal Vector2 ArenaCenter;
    private Vector2 lockedAim;
    private Vector2 dashDirection;
    private Vector2 dashStart;
    private float orbitStart;
    private float damageScale = 1f;
    private bool despawning;
    internal int EncounterId { get; private set; }
    private float flash;
    private bool arrivalTitleShown;
    private bool deathTitleShown;
    internal bool CombatActive => !despawning && State != Attack.Death && State != Attack.Transform;
    private bool Authority => Main.netMode != NetmodeID.MultiplayerClient;
    public override string Texture => ModAsset.EyeOfCthulhu_png_Mod;

    public override void SetStaticDefaults() {
        Main.npcFrameCount[Type] = 6;
        NPCID.Sets.TrailCacheLength[Type] = 16;
        NPCID.Sets.TrailingMode[Type] = 1;
        NPCID.Sets.MPAllowedEnemies[Type] = true;
        NPCID.Sets.BossBestiaryPriority.Add(Type);
        NPCID.Sets.NPCBestiaryDrawModifiers draw = new() { PortraitScale = 0.8f };
        NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, draw);
    }

    public override void SetDefaults() {
        NPC.width = 92;
        NPC.height = 92;
        NPC.aiStyle = -1;
        NPC.lifeMax = 4200;
        NPC.damage = 0;
        NPC.defense = 12;
        NPC.knockBackResist = 0f;
        NPC.noGravity = true;
        NPC.noTileCollide = true;
        NPC.boss = true;
        NPC.netAlways = true;
        NPC.npcSlots = 10f;
        NPC.value = 0f;
        NPC.HitSound = TombwardSound.EyeHit;
        NPC.DeathSound = null;
        Music = MusicLoader.GetMusicSlot(ModAsset.EyeOfCthulhu_mp3_Mod);
    }

    public override void SetBestiary(BestiaryDatabase database, BestiaryEntry entry) {
        entry.Info.Add(BestiaryDatabaseNPCsPopulator.CommonTags.SpawnConditions.Times.NightTime);
    }

    // Borrow only the map icon. The encounter never creates a vanilla eye.
    public override void BossHeadSlot(ref int index) => index = NPCID.Sets.BossHeadTextures[NPCID.EyeofCthulhu];
    public override void BossHeadRotation(ref float rotation) => rotation = NPC.rotation;

    public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment) {
        NPC.lifeMax = (int)(4200f * balance * (Main.masterMode ? 1.65f : 1.35f));
        damageScale = Main.masterMode ? 1.8f : 1.35f;
    }

    public override void OnSpawn(IEntitySource source) {
        NPC.TargetClosest(false);
        if (Authority) EncounterId = Main.rand.Next(1, int.MaxValue);
        if (Authority && NPC.HasValidTarget) {
            ArenaCenter = Main.player[NPC.target].Center;
            NPC.Center = ArenaCenter + new Vector2(0f, -480f);
            NPC.netUpdate = true;
        }
    }

    public override void SendExtraAI(BinaryWriter writer) {
        writer.Write(ArenaCenter.X); writer.Write(ArenaCenter.Y);
        writer.Write(lockedAim.X); writer.Write(lockedAim.Y);
        writer.Write(dashDirection.X); writer.Write(dashDirection.Y);
        writer.Write(dashStart.X); writer.Write(dashStart.Y);
        writer.Write(orbitStart);
        writer.Write(despawning);
        writer.Write(EncounterId);
        writer.Write(damageScale);
    }

    public override void ReceiveExtraAI(BinaryReader reader) {
        ArenaCenter = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        lockedAim = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        dashDirection = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        dashStart = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        orbitStart = reader.ReadSingle();
        despawning = reader.ReadBoolean();
        EncounterId = reader.ReadInt32();
        damageScale = reader.ReadSingle();
    }

    public override bool CheckActive() => despawning;
    public override bool CanHitPlayer(Player target, ref int cooldownSlot) {
        cooldownSlot = ImmunityCooldownID.Bosses;
        return NPC.damage > 0 && CombatActive;
    }

    public override void AI() {
        if (State == Attack.Death) { Death(); UpdateVisuals(); return; }
        NPC.TargetClosest(false);
        Player target = NPC.HasValidTarget ? Main.player[NPC.target] : null;
        if (target == null || target.dead || Vector2.DistanceSquared(target.Center, NPC.Center) > 3200f * 3200f || despawning) {
            if (!despawning && Authority) {
                despawning = true;
                ClearHazards();
                NPC.netUpdate = true;
            }
            NPC.damage = 0;
            NPC.dontTakeDamage = true;
            NPC.velocity = Vector2.Lerp(NPC.velocity, new Vector2(0f, -14f), 0.055f);
            NPC.EncourageDespawn(60);
            return;
        }
        NPC.timeLeft = 750;
        NPC.damage = 0;
        NPC.defense = Phase > 0 ? 6 : 12;
        NPC.dontTakeDamage = State == Attack.Arrival || State == Attack.Transform;
        if (Authority && Phase == 0 && NPC.life <= NPC.lifeMax * 0.55f && State != Attack.Arrival) {
            NPC.ai[2] = 1f;
            ClearHazards();
            Change(Attack.Transform, target);
        }
        switch (State) {
            case Attack.Arrival: Arrival(target); break;
            case Attack.Servants: Servants(target); break;
            case Attack.Gaze: Gaze(target); break;
            case Attack.Dash: Dash(target); break;
            case Attack.Transform: Transform(target); break;
            case Attack.BloodRain: BloodRain(target); break;
            case Attack.Orbit: Orbit(target); break;
            case Attack.Echoes: Echoes(target); break;
            case Attack.Eclipse: Eclipse(target); break;
        }
        UpdateVisuals();
        NPC.ai[1]++;
        if (Authority && (int)Timer % 45 == 0) NPC.netUpdate = true;
    }

    private void Hover(Vector2 destination, float speed = 12f, float inertia = 16f) {
        Vector2 offset = destination - NPC.Center;
        Vector2 desired = offset.SafeNormalize(Vector2.Zero) * Math.Min(speed, offset.Length() * 0.09f);
        NPC.velocity = (NPC.velocity * (inertia - 1f) + desired) / inertia;
    }

    private void Face(Vector2 point, float amount = 0.16f) {
        NPC.rotation = NPC.rotation.AngleLerp((point - NPC.Center).ToRotation() - MathHelper.PiOver2, amount);
    }

    private void Change(Attack attack, Player target) {
        if (!Authority) return;
        NPC.ai[0] = (float)attack;
        NPC.ai[1] = 0f;
        NPC.damage = 0;
        NPC.dontTakeDamage = attack == Attack.Arrival || attack == Attack.Transform || attack == Attack.Death;
        ArenaCenter = target.Center;
        lockedAim = target.Center;
        dashDirection = Vector2.Zero;
        dashStart = NPC.Center;
        orbitStart = (NPC.Center - ArenaCenter).ToRotation();
        NPC.netUpdate = true;
    }

    private void Next(Player target) {
        if (!Authority) return;
        Cycle++;
        Attack next = Phase == 0 ? (Cycle % 3) switch {
            0 => Attack.Servants, 1 => Attack.Gaze, _ => Attack.Dash
        } : (Cycle % 6) switch {
            0 => Attack.BloodRain, 1 => Attack.Echoes, 2 => Attack.Orbit,
            3 => Attack.Gaze, 4 => Attack.Dash, _ => Attack.Eclipse
        };
        Change(next, target);
    }

    private int ContactDamage(int amount) => (int)(amount * damageScale);
    // Terraria multiplies hostile projectile damage by 2/4/6 when hurting a player.
    internal int ShotDamage(int amount) => Math.Max(1, (int)(amount * damageScale / (Main.masterMode ? 6f : Main.expertMode ? 4f : 2f)));

    private void Shoot<T>(Vector2 position, Vector2 velocity, int damage, float parameter = 0f, float extra = 0f) where T : ModProjectile {
        if (!Authority) return;
        Projectile.NewProjectile(NPC.GetSource_FromAI(), position, velocity, ModContent.ProjectileType<T>(),
            ShotDamage(damage), 0f, Main.myPlayer, NPC.whoAmI, parameter, extra);
    }

    private void Arrival(Player target) {
        Hover(ArenaCenter + new Vector2(0f, -260f), 6f);
        Face(target.Center);
        if (!arrivalTitleShown && Timer >= 88f && Main.netMode != NetmodeID.Server) {
            arrivalTitleShown = true;
            ScreenPresentationSystem.ShowTitle(132,
                Language.GetTextValue("Mods.ReverieMod.EyeOfCthulhuTitles.Theme"),
                new Color(190, 148, 255),
                Language.GetTextValue("Mods.ReverieMod.EyeOfCthulhuTitles.Intro"),
                new Color(255, 82, 125));
        }
        if (Timer == 48f) Roar(0.8f);
        if (Timer >= 120f) Change(Attack.Servants, target);
    }

    private void Servants(Player target) {
        Hover(ArenaCenter + new Vector2(0f, -290f), 9f);
        Face(target.Center);
        if (Timer == 24f && Authority) {
            // Leave a 135-degree opening below the player. Each survivor launches
            // separately, rather than forcing simultaneous dodges through a wall.
            for (int i = 0; i < 6; i++) {
                float angle = -MathHelper.Pi + i * MathHelper.PiOver4;
                int n = NPC.NewNPC(NPC.GetSource_FromAI(), (int)NPC.Center.X, (int)NPC.Center.Y,
                    ModContent.NPCType<CthulhusServant>(), ai0: NPC.whoAmI, ai1: angle, ai2: i);
                if (n < Main.maxNPCs) {
                    Main.npc[n].Center = NPC.Center;
                    Main.npc[n].target = NPC.target;
                    Main.npc[n].netUpdate = true;
                }
            }
        }
        if (Timer == 32f) Pulse(NPC.Center, 0.8f);
        // A visible travelling fan pressures movement after the servants settle.
        if (Timer == 140f || Timer == 190f) Fan(target.Center, 5, 0.22f, 5.8f, 22);
        if (Timer >= 300f) Next(target);
    }

    private void Gaze(Player target) {
        Hover(ArenaCenter + new Vector2((Cycle % 2 == 0 ? -1f : 1f) * 310f, -220f), 10f);
        Face(Timer < 65f ? target.Center : lockedAim);
        if (Timer == 65f && Authority) {
            lockedAim = target.Center + target.velocity * 10f;
            NPC.velocity *= 0.2f;
            NPC.netUpdate = true;
            Shoot<CthulhuGaze>(NPC.Center, (lockedAim - NPC.Center).SafeNormalize(Vector2.UnitY), 32, 0f,
                Phase > 0 ? (Cycle % 2 == 0 ? 0.005f : -0.005f) : 0f);
        }
        if (Timer >= 65f && Timer < 166f) NPC.velocity *= 0.86f;
        if (Timer == 113f) Roar(0.4f);
        if (Timer >= 210f) Next(target);
    }

    private void Dash(Player target) {
        int legLength = Phase > 0 ? 105 : 120;
        int legs = Phase > 0 ? 3 : 2;
        int leg = (int)Timer / legLength;
        int tick = (int)Timer % legLength;
        if (leg >= legs) { NPC.velocity *= 0.87f; if (Timer >= legs * legLength + 40f) Next(target); return; }
        if (tick < 36) {
            float side = (leg + Cycle) % 2 == 0 ? -1f : 1f;
            Hover(target.Center + new Vector2(side * 410f, leg % 2 == 0 ? -170f : 110f), 17f, 10f);
            Face(target.Center);
        }
        if (tick == 36 && Authority) {
            lockedAim = target.Center + target.velocity * 12f;
            dashDirection = (lockedAim - NPC.Center).SafeNormalize(Vector2.UnitY);
            dashStart = NPC.Center;
            NPC.velocity = Vector2.Zero;
            NPC.netUpdate = true;
        }
        if (tick >= 36 && tick < 66) {
            NPC.velocity *= 0.7f;
            Face(NPC.Center + dashDirection * 100f, 0.3f);
        }
        if (tick == 66) {
            NPC.velocity = dashDirection * (Phase > 0 ? 27f : 23f);
            if (Authority) NPC.netUpdate = true;
            RushBurst();
        }
        if (tick >= 66 && tick < 91) {
            NPC.damage = ContactDamage(Phase > 0 ? 42 : 32);
            Face(NPC.Center + NPC.velocity, 0.5f);
        }
        if (tick >= 91) NPC.velocity *= 0.87f;
    }

    private void Transform(Player target) {
        Hover(ArenaCenter + new Vector2(0f, -220f), 7f);
        NPC.rotation += 0.055f + GuidaUtils.Smoothstep(0f, 100f, Timer) * 0.27f;
        if (Timer == 84f) { Roar(1.3f); BloodBurst(NPC.Center, 42, 6f); flash = 1f; }
        if (Timer >= 160f) { Cycle = -1; Next(target); }
    }

    private void BloodRain(Player target) {
        Hover(ArenaCenter + new Vector2((float)Math.Sin(Timer * 0.022f) * 290f, -320f), 11f);
        Face(target.Center);
        // Two lanes are deliberately omitted; bolts never track after spawning.
        if (Timer >= 60f && Timer <= 180f && (int)Timer % 40 == 20) {
            int wave = ((int)Timer - 60) / 40;
            for (int i = -4; i <= 4; i++) {
                if (i == wave % 3 - 1 || i == wave % 3) continue;
                Vector2 start = ArenaCenter + new Vector2(i * 105f, -440f);
                Shoot<CthulhuBlood>(start, new Vector2((wave % 2 == 0 ? 1f : -1f) * 0.8f, 5.3f), 25);
            }
            Pulse(NPC.Center, 0.55f);
        }
        if (Timer >= 260f) Next(target);
    }

    private void Orbit(Player target) {
        // Center is captured once. The boss does not drag an arena onto the player.
        float angle = orbitStart + GuidaUtils.Smoothstep(0f, 220f, Timer) * MathHelper.TwoPi;
        Vector2 destination = ArenaCenter + angle.ToRotationVector2() * 350f;
        Hover(destination, 18f, 7f);
        Face(ArenaCenter);
        if (Timer >= 55f && Timer <= 175f && (int)Timer % 30 == 25)
            Fan(ArenaCenter, 3, 0.25f, 6.8f, 24);
        if (Timer == 55f) Shoot<CthulhuIris>(ArenaCenter, Vector2.Zero, 28, MathHelper.PiOver2);
        if (Timer >= 265f) Next(target);
    }

    private void Echoes(Player target) {
        Hover(ArenaCenter + new Vector2(0f, -320f), 11f);
        Face(target.Center);
        if (Timer == 40f || Timer == 100f || Timer == 160f) {
            float angle = ((int)(Timer - 40f) / 60) * MathHelper.Pi / 3f + (Cycle % 2 == 0 ? 0.25f : -0.25f);
            Vector2 direction = angle.ToRotationVector2();
            Shoot<CthulhuEcho>(ArenaCenter - direction * 470f, Vector2.Zero, 32, 0f, angle);
            Pulse(NPC.Center, 0.55f);
        }
        if (Timer == 224f) Fan(target.Center, 5, 0.23f, 6f, 22);
        if (Timer >= 300f) Next(target);
    }

    private void Eclipse(Player target) {
        Hover(ArenaCenter + new Vector2(0f, -310f), 10f);
        Face(target.Center);
        if (Timer == 20f) { Roar(0.7f); Shoot<CthulhuIris>(ArenaCenter, Vector2.Zero, 30, MathHelper.PiOver2); }
        if (Timer == 120f) {
            // A horizontal echo arrives only after the annulus has disappeared.
            Shoot<CthulhuEcho>(ArenaCenter - new Vector2(470f, 0f), Vector2.Zero, 34, 0f, 0f);
        }
        if (Timer == 210f) Fan(target.Center, 7, 0.19f, 6.5f, 24);
        if (Timer >= 300f) Next(target);
    }

    private void Fan(Vector2 point, int count, float spread, float speed, int damage) {
        Vector2 direction = (point - NPC.Center).SafeNormalize(Vector2.UnitY);
        for (int i = 0; i < count; i++)
            Shoot<CthulhuBlood>(NPC.Center + direction * 52f, direction.RotatedBy((i - (count - 1) * 0.5f) * spread) * speed, damage);
    }

    internal static EyeOfCthulhu FindOwner(int index) => index >= 0 && index < Main.maxNPCs &&
        Main.npc[index].active && Main.npc[index].ModNPC is EyeOfCthulhu eye ? eye : null;

    private void ClearHazards() {
        if (!Authority) return;
        foreach (Projectile shot in Main.ActiveProjectiles) {
            if (shot.ModProjectile is CthulhuHazard && (int)shot.ai[0] == NPC.whoAmI) shot.Kill();
        }
        foreach (NPC servant in Main.ActiveNPCs) {
            if (servant.ModNPC is CthulhusServant && (int)servant.ai[0] == NPC.whoAmI) {
                servant.active = false;
                servant.netUpdate = true;
                if (Main.netMode == NetmodeID.Server) NetMessage.SendData(MessageID.SyncNPC, number: servant.whoAmI);
            }
        }
    }

    public override bool CheckDead() {
        if (State == Attack.Death) return Timer >= 90f;
        NPC.life = 1;
        NPC.dontTakeDamage = true;
        NPC.damage = 0;
        NPC.ai[0] = (float)Attack.Death;
        NPC.ai[1] = 0f;
        NPC.velocity *= 0.1f;
        ClearHazards();
        NPC.netUpdate = true;
        return false;
    }

    private void Death() {
        NPC.damage = 0;
        NPC.dontTakeDamage = true;
        NPC.velocity *= 0.92f;
        NPC.rotation += 0.015f;
        if (!deathTitleShown && Main.netMode != NetmodeID.Server) {
            deathTitleShown = true;
            ScreenPresentationSystem.ShowTitle(180,
                Language.GetTextValue("Mods.ReverieMod.EyeOfCthulhuTitles.Theme"),
                new Color(255, 72, 116),
                Language.GetTextValue("Mods.ReverieMod.EyeOfCthulhuTitles.Defeated"),
                new Color(255, 225, 235));
        }
        if (Timer == 1f) Roar(0.8f);
        if (Timer >= 90f && Authority) {
            NPC.life = 0;
            NPC.HitEffect();
            NPC.checkDead();
            NPC.netUpdate = true;
            return;
        }
        NPC.ai[1]++;
    }

    public override void HitEffect(NPC.HitInfo hit) {
        if (Main.netMode == NetmodeID.Server) return;
        BloodBurst(NPC.Center, NPC.life <= 0 ? 48 : 4, NPC.life <= 0 ? 8f : 2f);
    }

    public override void OnKill() {
        ClearHazards();
        if (Main.netMode != NetmodeID.Server) {
            SoundEngine.PlaySound(TombwardSound.EyeDeath, NPC.Center);
            Pulse(NPC.Center, 2f);
        }
    }
}
