using System;
using System.IO;
using Terraria.DataStructures;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace ReverieMod.Content.EyeOfCthulhu;

// ai[0] is always the boss index. Derived types use only ai[1] and ai[2].
public abstract class CthulhuHazard : ModProjectile {
    private int encounterId;
    protected EyeOfCthulhu Boss {
        get {
            EyeOfCthulhu eye = EyeOfCthulhu.FindOwner((int)Projectile.ai[0]);
            return eye != null && eye.EncounterId == encounterId ? eye : null;
        }
    }
    public override void OnSpawn(IEntitySource source) => encounterId = EyeOfCthulhu.FindOwner((int)Projectile.ai[0])?.EncounterId ?? 0;
    public override void SendExtraAI(BinaryWriter writer) => writer.Write(encounterId);
    public override void ReceiveExtraAI(BinaryReader reader) => encounterId = reader.ReadInt32();
    public override string Texture => ModAsset.SoftCircle_Mod;
    public override void SetStaticDefaults() => ProjectileID.Sets.DrawScreenCheckFluff[Type] = 500;
    public override void SetDefaults() {
        Projectile.width = 18;
        Projectile.height = 18;
        Projectile.hostile = true;
        Projectile.penetrate = -1;
        Projectile.tileCollide = false;
        Projectile.ignoreWater = true;
        Projectile.timeLeft = 240;
        Projectile.netImportant = true;
        CooldownSlot = ImmunityCooldownID.Bosses;
    }
    protected bool KeepAlive() {
        if (Boss?.CombatActive == true) {
            if (Main.netMode != NetmodeID.MultiplayerClient && Projectile.timeLeft % 30 == 0) Projectile.netUpdate = true;
            return true;
        }
        Projectile.Kill();
        return false;
    }
    public override bool? CanDamage() => Boss?.CombatActive == true ? null : false;
}

// A fixed origin and direction during its 48-tick telegraph; phase two then
// sweeps slowly through a small arc. Rendering and collision share this axis.
public class CthulhuGaze : CthulhuHazard {
    public override void SetStaticDefaults() => ProjectileID.Sets.DrawScreenCheckFluff[Type] = 2000;
    private float Age => Projectile.ai[1];
    private float Delay => 48f;
    private Vector2 Axis => Projectile.velocity.SafeNormalize(Vector2.UnitY)
        .RotatedBy(Math.Max(0f, Age - Delay) * Projectile.ai[2]);
    private float Width => 32f * GuidaUtils.Smoothstep(0f, 8f, Age - Delay) *
        GuidaUtils.Smoothstep(Delay + 62f, Delay + 48f, Age);
    public override bool ShouldUpdatePosition() => false;
    public override void AI() {
        if (!KeepAlive()) return;
        Projectile.ai[1]++;
        if (Age == 1f && Main.netMode != NetmodeID.Server)
            SoundEngine.PlaySound(TombwardSound.EyeCharge, Projectile.Center);
        if (Age == Delay && Main.netMode != NetmodeID.Server) {
            SoundEngine.PlaySound(TombwardSound.EyeBeam, Projectile.Center);
            EyeOfCthulhu.Pulse(Projectile.Center, 1f);
        }
        if (Age >= Delay + 62f) Projectile.Kill();
        if (Age >= Delay && Main.netMode != NetmodeID.Server && (int)Age % 3 == 0) {
            Vector2 point = Projectile.Center + Axis * Main.rand.NextFloat(20f, 1700f);
            Dust d = Dust.NewDustPerfect(point, DustID.Blood, Axis * 2f, 0, new Color(255, 65, 110), 1.2f);
            d.noGravity = true;
        }
    }
    public override bool? CanDamage() => Boss?.CombatActive == true && Age >= Delay + 8f && Age < Delay + 52f;
    public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
        float point = 0f;
        return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
            Projectile.Center, Projectile.Center + Axis * 1800f, Width, ref point);
    }
    public override bool PreDraw(ref Color lightColor) {
        SpriteBatch sb = Main.spriteBatch;
        Vector2 start = Projectile.Center;
        if (Age < Delay) {
            float charge = GuidaUtils.Smoothstep(0f, Delay, Age);
            EyeDraw.Warning(sb, start, start + Axis * 1800f, 0.35f + charge * 0.55f, 32f);
            if (Projectile.ai[2] != 0f)
                EyeDraw.Line(sb, start, start + Axis.RotatedBy(52f * Projectile.ai[2]) * 1800f,
                    2f, EyeDraw.Additive(EyeDraw.Cyan, charge * 0.55f));
            EyeDraw.Ring(sb, start, 80f * (1f - charge) + 20f, EyeDraw.Crimson * charge, 3f);
        } else {
            float fade = GuidaUtils.Smoothstep(0f, 6f, Age - Delay) * GuidaUtils.Smoothstep(Delay + 62f, Delay + 48f, Age);
            EyeDraw.Line(sb, start, start + Axis * 1800f, 66f * fade, EyeDraw.Additive(EyeDraw.Crimson, 0.2f));
            EyeDraw.Line(sb, start, start + Axis * 1800f, Width, EyeDraw.Additive(new Color(255, 95, 125), 0.9f));
            EyeDraw.Line(sb, start, start + Axis * 1800f, 8f * fade, EyeDraw.Additive(new Color(255, 225, 235), 1f));
            EyeDraw.Glow(sb, start, 130f * fade, EyeDraw.Crimson, 0.6f);
        }
        return false;
    }
}

// A bright, small blood tear, with an 18-tick harmless birth and no homing.
public class CthulhuBlood : CthulhuHazard {
    public override void SetStaticDefaults() {
        base.SetStaticDefaults();
        ProjectileID.Sets.TrailCacheLength[Type] = 12;
        ProjectileID.Sets.TrailingMode[Type] = 2;
    }
    public override void SetDefaults() {
        base.SetDefaults();
        Projectile.width = Projectile.height = 16;
        Projectile.timeLeft = 200;
    }
    public override void AI() {
        if (!KeepAlive()) return;
        Projectile.ai[2]++;
        if (Projectile.ai[2] >= 200f) { Projectile.Kill(); return; }
        Projectile.velocity = Projectile.velocity.RotatedBy(Projectile.ai[1]);
        Projectile.rotation = Projectile.velocity.ToRotation();
        if (Main.netMode == NetmodeID.Server) return;
        Lighting.AddLight(Projectile.Center, 0.5f, 0.04f, 0.08f);
        if ((int)Projectile.ai[2] % 4 == 0) {
            Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.Blood, -Projectile.velocity * 0.15f, 40, default, 1.2f);
            d.noGravity = true;
        }
    }
    public override bool? CanDamage() => Boss?.CombatActive == true && Projectile.ai[2] >= 18f && Projectile.ai[2] < 182f;
    public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
        Vector2 closest = Vector2.Clamp(Projectile.Center, targetHitbox.TopLeft(), targetHitbox.BottomRight());
        return Vector2.DistanceSquared(Projectile.Center, closest) <= 8f * 8f;
    }
    public override bool PreDraw(ref Color lightColor) {
        SpriteBatch sb = Main.spriteBatch;
        float fade = GuidaUtils.Smoothstep(0f, 18f, Projectile.ai[2]) * GuidaUtils.Smoothstep(0f, 18f, 200f - Projectile.ai[2]);
        for (int i = Projectile.oldPos.Length - 1; i > 0; i--) {
            if (Projectile.oldPos[i] == Vector2.Zero) continue;
            Vector2 p = Projectile.oldPos[i] + Projectile.Size * 0.5f;
            EyeDraw.Glow(sb, p, 16f * (1f - i / (float)Projectile.oldPos.Length), EyeDraw.Crimson, fade * 0.45f);
        }
        EyeDraw.Glow(sb, Projectile.Center, 33f, EyeDraw.Crimson, fade * 0.7f);
        EyeDraw.Line(sb, Projectile.Center - Projectile.velocity.SafeNormalize(Vector2.UnitY) * 12f,
            Projectile.Center + Projectile.velocity.SafeNormalize(Vector2.UnitY) * 5f, 8f,
            EyeDraw.Additive(new Color(255, 205, 220), fade));
        if (Projectile.ai[2] < 18f)
            EyeDraw.Ring(sb, Projectile.Center, 26f * (1f - Projectile.ai[2] / 18f) + 9f, EyeDraw.Crimson * 0.7f, 2f);
        return false;
    }
}

// The echo is a projectile, never an invulnerable NPC. Only the moving eye
// hurts; the long warning path itself does not become a persistent hitbox.
public class CthulhuEcho : CthulhuHazard {
    private float Age => Projectile.ai[1];
    private Vector2 Axis => Projectile.ai[2].ToRotationVector2();
    private Vector2 Origin => Projectile.Center - Axis * Math.Max(0f, Age - 60f) * 28f;
    public override string Texture => ModAsset.EyeOfCthulhu_png_Mod;
    public override void SetStaticDefaults() {
        ProjectileID.Sets.TrailCacheLength[Type] = 12;
        ProjectileID.Sets.TrailingMode[Type] = 0;
        ProjectileID.Sets.DrawScreenCheckFluff[Type] = 1200;
    }
    public override void SetDefaults() {
        base.SetDefaults();
        Projectile.width = Projectile.height = 76;
        Projectile.timeLeft = 96;
    }
    public override bool ShouldUpdatePosition() => false;
    public override void AI() {
        if (!KeepAlive()) return;
        Projectile.ai[1]++;
        if (Age > 60f) Projectile.Center += Axis * 28f;
        if (Age == 60f && Main.netMode != NetmodeID.Server) {
            SoundEngine.PlaySound(TombwardSound.EyeRush, Projectile.Center);
            EyeOfCthulhu.Pulse(Projectile.Center, 0.7f);
        }
        if (Age >= 94f) Projectile.Kill();
    }
    public override bool? CanDamage() => Boss?.CombatActive == true && Age > 60f && Age < 92f;
    public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
        float point = 0f;
        return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
            Projectile.Center - Axis * 28f, Projectile.Center, 60f, ref point);
    }
    public override bool PreDraw(ref Color lightColor) {
        SpriteBatch sb = Main.spriteBatch;
        if (Age < 60f) {
            EyeDraw.Warning(sb, Origin, Origin + Axis * 940f, GuidaUtils.Smoothstep(0f, 18f, Age) * 0.8f, 76f);
            EyeDraw.Eye(sb, Projectile.Center, Axis.ToRotation() - MathHelper.PiOver2, 3 + (int)(Age / 7f) % 3,
                EyeDraw.Additive(new Color(245, 85, 120), 0.25f + Age / 180f), new Vector2(0.82f));
        } else {
            for (int i = 10; i >= 0; i--) {
                float fade = (1f - i / 12f) * GuidaUtils.Smoothstep(96f, 84f, Age);
                EyeDraw.Eye(sb, Projectile.Center - Axis * i * 20f, Axis.ToRotation() - MathHelper.PiOver2,
                    3 + (int)(Age / 7f) % 3, EyeDraw.Additive(new Color(255, 75, 115), fade * 0.35f), new Vector2(0.86f));
            }
            EyeDraw.Eye(sb, Projectile.Center, Axis.ToRotation() - MathHelper.PiOver2, 3 + (int)(Age / 7f) % 3,
                new Color(255, 185, 195) * 0.85f, new Vector2(0.86f));
        }
        return false;
    }
}

// A shrinking annulus with a fixed 80-degree exit. Both the band and its gap
// are rendered from the very same radius/angle that collision evaluates.
public class CthulhuIris : CthulhuHazard {
    private float Age => Projectile.ai[2];
    private float Radius => MathHelper.Lerp(440f, 130f, GuidaUtils.Smoothstep(45f, 135f, Age));
    private bool InGap(float angle) => Math.Abs(MathHelper.WrapAngle(angle - Projectile.ai[1])) < 0.7f;
    public override void SetDefaults() { base.SetDefaults(); Projectile.timeLeft = 155; }
    public override bool ShouldUpdatePosition() => false;
    public override void AI() {
        if (!KeepAlive()) return;
        Projectile.ai[2]++;
        if (Age >= 155f) { Projectile.Kill(); return; }
        if (Age == 45f && Main.netMode != NetmodeID.Server)
            SoundEngine.PlaySound(TombwardSound.EyeCharge, Projectile.Center);
    }
    public override bool? CanDamage() => Boss?.CombatActive == true && Age >= 45f && Age < 138f;
    public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
        // Segment tests also catch the edges of large player hitboxes at the gap.
        for (int i = 0; i < 128; i++) {
            float a = i * MathHelper.TwoPi / 128f;
            float b = (i + 1) * MathHelper.TwoPi / 128f;
            if (InGap(a) || InGap(b)) continue;
            float point = 0f;
            if (Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                Projectile.Center + a.ToRotationVector2() * Radius,
                Projectile.Center + b.ToRotationVector2() * Radius, 22f, ref point)) return true;
        }
        return false;
    }
    public override bool PreDraw(ref Color lightColor) {
        SpriteBatch sb = Main.spriteBatch;
        float fade = GuidaUtils.Smoothstep(0f, 18f, Age) * GuidaUtils.Smoothstep(155f, 135f, Age);
        for (int i = 0; i < 128; i++) {
            float a = i * MathHelper.TwoPi / 128f;
            float b = (i + 1) * MathHelper.TwoPi / 128f;
            if (InGap(a) || InGap(b)) continue;
            Vector2 start = Projectile.Center + a.ToRotationVector2() * Radius;
            Vector2 end = Projectile.Center + b.ToRotationVector2() * Radius;
            if (Age < 45f) {
                EyeDraw.Line(sb, start, end, 22f, EyeDraw.Additive(EyeDraw.Crimson, fade * 0.15f));
                EyeDraw.Line(sb, start, end, 3f, EyeDraw.Additive(EyeDraw.Crimson, fade * 0.85f));
            } else {
                EyeDraw.Line(sb, start, end, 38f, EyeDraw.Additive(EyeDraw.Crimson, fade * 0.18f));
                EyeDraw.Line(sb, start, end, 22f, EyeDraw.Additive(EyeDraw.Crimson, fade * 0.65f));
                EyeDraw.Line(sb, start, end, 5f, EyeDraw.Additive(new Color(255, 205, 225), fade));
            }
        }
        // Pale blue rails frame the actual escape opening, with arrows outward.
        for (int side = -1; side <= 1; side += 2) {
            Vector2 direction = (Projectile.ai[1] + side * 0.7f).ToRotationVector2();
            EyeDraw.Line(sb, Projectile.Center + direction * (Radius - 38f), Projectile.Center + direction * (Radius + 40f),
                3f, EyeDraw.Additive(EyeDraw.Cyan, fade * 0.8f));
        }
        Vector2 gap = Projectile.ai[1].ToRotationVector2();
        EyeDraw.Arrows(sb, Projectile.Center + gap * (Radius - 65f), Projectile.Center + gap * (Radius + 90f), EyeDraw.Cyan * fade, 20f);
        return false;
    }
}
