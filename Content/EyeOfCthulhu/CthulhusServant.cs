using System;
using System.IO;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace ReverieMod.Content.EyeOfCthulhu;

public class CthulhusServant : ModNPC {
    private int encounterId;
    private EyeOfCthulhu Boss {
        get {
            EyeOfCthulhu eye = EyeOfCthulhu.FindOwner((int)NPC.ai[0]);
            return eye != null && eye.EncounterId == encounterId ? eye : null;
        }
    }
    public override void OnSpawn(IEntitySource source) => encounterId = EyeOfCthulhu.FindOwner((int)NPC.ai[0])?.EncounterId ?? 0;
    private float Age => NPC.ai[3];
    private float LockTime => 55f + NPC.ai[2] * 22f;
    private float LaunchTime => 100f + NPC.ai[2] * 22f;
    private Vector2 direction;
    public override string Texture => ModAsset.CthulhusServant_Mod;
    public override void SetStaticDefaults() {
        Main.npcFrameCount[Type] = 2;
        NPCID.Sets.TrailCacheLength[Type] = 8;
        NPCID.Sets.TrailingMode[Type] = 1;
        NPCID.Sets.NPCBestiaryDrawOffset.Add(Type, new NPCID.Sets.NPCBestiaryDrawModifiers { Hide = true });
    }
    public override void SetDefaults() {
        NPC.width = NPC.height = 24;
        NPC.lifeMax = 75;
        NPC.defense = 2;
        NPC.damage = 0;
        NPC.aiStyle = -1;
        NPC.noGravity = true;
        NPC.noTileCollide = true;
        NPC.knockBackResist = 0.35f;
        NPC.HitSound = TombwardSound.EyeHit;
        NPC.DeathSound = TombwardSound.EyeDeath;
        NPC.value = 0f;
        NPC.netAlways = true;
        NPC.npcSlots = 0f;
    }
    public override void SendExtraAI(BinaryWriter writer) { writer.Write(direction.X); writer.Write(direction.Y); writer.Write(encounterId); }
    public override void ReceiveExtraAI(BinaryReader reader) { direction = new Vector2(reader.ReadSingle(), reader.ReadSingle()); encounterId = reader.ReadInt32(); }
    public override bool CheckActive() => false;
    public override bool CanHitPlayer(Player target, ref int cooldownSlot) {
        cooldownSlot = ImmunityCooldownID.Bosses;
        return Boss?.CombatActive == true && Age >= LaunchTime && Age < LaunchTime + 36f;
    }
    public override void AI() {
        EyeOfCthulhu owner = Boss;
        if (owner?.CombatActive != true || owner.State != EyeOfCthulhu.Attack.Servants) {
            NPC.damage = 0;
            NPC.active = false;
            if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
            return;
        }
        NPC.ai[3]++;
        NPC.damage = 0;
        NPC.timeLeft = 600;
        Player target = Main.player[owner.NPC.target];
        if (Age < LockTime) {
            Vector2 destination = owner.ArenaCenter + NPC.ai[1].ToRotationVector2() * 270f;
            Vector2 offset = destination - NPC.Center;
            NPC.velocity = Vector2.Lerp(NPC.velocity, offset * 0.08f, 0.15f);
            if (NPC.velocity.Length() > 13f) NPC.velocity = NPC.velocity.SafeNormalize(Vector2.Zero) * 13f;
            NPC.rotation = NPC.rotation.AngleLerp((target.Center - NPC.Center).ToRotation() - MathHelper.PiOver2, 0.2f);
        }
        if (Age == LockTime && Main.netMode != NetmodeID.MultiplayerClient) {
            direction = (target.Center + target.velocity * 8f - NPC.Center).SafeNormalize(Vector2.UnitY);
            NPC.velocity = Vector2.Zero;
            NPC.netUpdate = true;
        }
        if (Age >= LockTime && Age < LaunchTime) {
            NPC.velocity *= 0.8f;
            NPC.rotation = direction.ToRotation() - MathHelper.PiOver2;
            if (Age == LockTime && Main.netMode != NetmodeID.Server)
                SoundEngine.PlaySound(TombwardSound.EyeCharge, NPC.Center);
        }
        if (Age == LaunchTime) {
            NPC.velocity = direction * 16f;
            if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
            if (Main.netMode != NetmodeID.Server) {
                SoundEngine.PlaySound(TombwardSound.EyeRush, NPC.Center);
                EyeOfCthulhu.Pulse(NPC.Center, 0.35f);
            }
        }
        if (Age >= LaunchTime && Age < LaunchTime + 36f)
            NPC.damage = Main.masterMode ? 42 : Main.expertMode ? 32 : 24;
        if (Age >= LaunchTime + 36f) {
            NPC.velocity *= 0.95f;
            NPC.alpha = (int)(255f * GuidaUtils.Smoothstep(LaunchTime + 36f, LaunchTime + 65f, Age));
        }
        if (Age >= LaunchTime + 65f) {
            NPC.active = false;
            if (Main.netMode != NetmodeID.MultiplayerClient) NPC.netUpdate = true;
        }
        if (Main.netMode != NetmodeID.MultiplayerClient && (int)Age % 45 == 0) NPC.netUpdate = true;
    }
    public override void FindFrame(int frameHeight) {
        NPC.frameCounter++;
        NPC.frame.Y = ((int)NPC.frameCounter / 6 % 2) * frameHeight;
    }
    public override void HitEffect(NPC.HitInfo hit) => EyeOfCthulhu.BloodBurst(NPC.Center, NPC.life <= 0 ? 12 : 3, 2.5f);
    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
        Texture2D texture = ModAsset.CthulhusServant.Value;
        float alpha = NPC.Opacity;
        if (Age >= LockTime && Age < LaunchTime && direction != Vector2.Zero)
            EyeDraw.Warning(spriteBatch, NPC.Center, NPC.Center + direction * 580f,
                GuidaUtils.Smoothstep(LockTime, LockTime + 8f, Age) * 0.65f, 24f);
        if (Age >= LaunchTime) {
            for (int i = NPC.oldPos.Length - 1; i > 0; i--) {
                if (NPC.oldPos[i] == Vector2.Zero) continue;
                spriteBatch.Draw(texture, NPC.oldPos[i] + NPC.Size * 0.5f - screenPos, NPC.frame,
                    EyeDraw.Additive(EyeDraw.Crimson, (1f - i / (float)NPC.oldPos.Length) * 0.4f * alpha),
                    NPC.rotation, NPC.frame.Size() * 0.5f, 1.2f, SpriteEffects.None, 0f);
            }
        }
        EyeDraw.Glow(spriteBatch, NPC.Center, 42f, EyeDraw.Crimson, 0.2f * alpha);
        spriteBatch.Draw(texture, NPC.Center - screenPos, NPC.frame, Color.Lerp(drawColor, Color.White, 0.55f) * alpha,
            NPC.rotation, NPC.frame.Size() * 0.5f, 1.2f, SpriteEffects.None, 0f);
        return false;
    }
}
