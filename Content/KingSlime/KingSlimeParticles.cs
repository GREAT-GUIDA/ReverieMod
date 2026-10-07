using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace ReverieMod.Content.KingSlime;

public class KingSlimeBodyTwistParticle : Particle, ITwistParticle {
    public Vector2 bodyScale;
    public float opacity;

    public bool DrawBehindNPCs => true;
    public float TwistOpacity => opacity;
    public override Texture2D Texture => ModAsset.KingSlimeBodyTwist.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.Twist;
        timeLeft = 2;
    }

    public override void AI() {
        if (!HasValidHolder() || --timeLeft <= 0) Kill();
    }

    public void DrawTwist(SpriteBatch spriteBatch) {
        spriteBatch.Draw(Texture, position - Main.screenPosition,
            new Rectangle(0, frame * 120, 174, 120), Color.White * opacity,
            rotation, new Vector2(87f, 120f), bodyScale, SpriteEffects.None, 0f);
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) => false;
}


// The boss places and sustains its simple props; chests alone animate open.
public class KingSlimePropParticle : Particle {
    public bool wooden;
    public bool ninja;
    public bool open;
    public Texture2D propTexture;
    private float openness;

    public override Texture2D Texture => propTexture ?? (ninja ? ModAsset.KingSlimeNinja.Value :
        wooden ? ModAsset.KingSlimeWoodChest.Value : ModAsset.KingSlimeChest.Value);
    public override Rectangle? SourceRectangle => propTexture != null || ninja
        ? null : new Rectangle(0, frame * 32, 32, 32);
    public override Vector2 Origin => propTexture != null || ninja
        ? Texture.Size() * 0.5f : new Vector2(16f, 16f);

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeNPCs;
        useLighting = true;
        timeLeft = 2;
    }

    public override void AI() {
        if (!HasValidHolder()) {
            Kill();
            return;
        }

        if (!ninja && propTexture == null) {
            openness = MathHelper.Lerp(openness, open ? 1f : 0f, 0.27f);
            frame = openness >= 0.72f ? 2 : openness >= 0.22f ? 1 : 0;
        }
        if (--timeLeft <= 0) Kill();
    }
}


// Shared release, ground sweep, two rebounds, and fade for discarded items.
public abstract class KingSlimeDroppedItemParticle : Particle {
    private int bounceCount;

    protected abstract Vector2 GroundContactPoint { get; }
    protected virtual float GroundHalfWidth => 5f;

    protected Vector2 RotatedLowerEnd(float reach, float angle) {
        Vector2 offset = new Vector2(0f, reach).RotatedBy(angle);
        return position + (offset.Y >= 0f ? offset : -offset);
    }

    protected void BeginDrop(Vector2 throwVelocity, float spin) {
        velocity = throwVelocity;
        angVelocity = spin;
        timeLeft = maxTimeLeft = 125;
    }

    protected void UpdateDrop() {
        oldPosition = position;
        velocity.X *= 0.992f;
        velocity.Y = Math.Min(velocity.Y + 0.34f, 13f);
        if (bounceCount < 2 && velocity.Y > 0f && TryGroundContact(out float fraction)) {
            position += velocity * fraction;
            velocity.X *= 0.72f;
            velocity.Y = bounceCount == 0
                ? -Math.Min(2.7f, Math.Max(1.2f, velocity.Y * 0.27f))
                : -Math.Min(1.8f, Math.Max(1.1f, velocity.Y * 0.5f));
            angVelocity *= -0.6f;
            bounceCount++;
        }
        else position += velocity;

        RotateDropped();
        if (timeLeft < 22) alpha = MathHelper.Clamp(timeLeft / 22f, 0f, 1f);
        if (--timeLeft <= 0) Kill();
    }

    protected virtual void RotateDropped() => rotation += angVelocity;

    private bool TryGroundContact(out float fraction) {
        fraction = 1f;
        Vector2 contact = GroundContactPoint;
        float nextY = contact.Y + velocity.Y;
        for (int y = (int)Math.Floor(contact.Y / 16f);
             y <= (int)Math.Floor(nextY / 16f); y++) {
            float tileTop = y * 16f;
            if (tileTop < contact.Y - 0.01f || tileTop > nextY) continue;
            float step = (tileTop - contact.Y) / velocity.Y;
            float contactX = contact.X + velocity.X * step;
            for (int x = (int)Math.Floor((contactX - GroundHalfWidth) / 16f);
                 x <= (int)Math.Floor((contactX + GroundHalfWidth) / 16f); x++) {
                if (!WorldGen.InWorld(x, y, 1) || Main.tile[x, y] == null ||
                    !WorldGen.SolidTile(x, y)) continue;
                fraction = step;
                return true;
            }
        }
        return false;
    }
}


// The discarded shortsword bounces twice, then falls through terrain.
public class KingSlimeCopperShortswordParticle : KingSlimeDroppedItemParticle {
    public bool held = true;

    public override Texture2D Texture => ModAsset.KingSlimeCopperShortsword.Value;
    protected override Vector2 GroundContactPoint =>
        RotatedLowerEnd(16f * scale, rotation);

    public override void SetDefaults() {
        base.SetDefaults();
        width = height = 12;
        timeLeft = 2;
        drawLayer = ParticleLayer.BeforeProjectiles;
        useLighting = true;
    }

    public void Release(Vector2 throwVelocity) {
        if (!held) return;
        held = false;
        BeginDrop(throwVelocity, throwVelocity.X >= 0f ? 0.11f : -0.11f);
    }

    public override void AI() {
        if (held) {
            if (HasValidHolder()) {
                timeLeft = 2;
                return;
            }
            Release(new Vector2(0f, -2f));
        }

        UpdateDrop();
    }
}


// The crown follows the boss with an asymmetric spring and bounces twice when released.
public class KingSlimeCrownParticle : KingSlimeDroppedItemParticle {
    private Vector2 previousAnchor;
    private bool initialized;
    private bool released;

    public override Texture2D Texture => ModAsset.Crown.Value;
    public override Vector2 Origin => new(Texture.Width * 0.5f, Texture.Height - 4f);
    protected override Vector2 GroundContactPoint => position + Vector2.UnitY * 3f;
    protected override float GroundHalfWidth => 18f;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeProjectiles;
        useLighting = true;
        timeLeft = 2;
    }

    public void Follow(Vector2 anchor, float bodyRotation) {
        if (released) return;
        if (!initialized) {
            position = anchor;
            velocity = Vector2.Zero;
            previousAnchor = anchor;
            rotation = bodyRotation * 0.4f;
            angVelocity = 0f;
            initialized = true;
        }

        Vector2 anchorVelocity = anchor - previousAnchor;
        previousAnchor = anchor;
        Vector2 normal = -Vector2.UnitY.RotatedBy(bodyRotation);
        Vector2 tangent = new(-normal.Y, normal.X);
        Vector2 offset = position - anchor;
        Vector2 relativeVelocity = velocity - anchorVelocity;
        float sideways = Vector2.Dot(offset, tangent);
        float above = Vector2.Dot(offset, normal);

        velocity.Y += 0.28f;
        velocity += tangent * MathHelper.Clamp(
            -sideways * 0.04f - Vector2.Dot(relativeVelocity, tangent) * 0.10f, -1.1f, 1.1f);
        if (above > 12f) {
            float outwardSpeed = Vector2.Dot(relativeVelocity, normal);
            velocity -= normal * Math.Min(
                (above - 12f) * 0.011f + Math.Max(outwardSpeed, 0f) * 0.07f, 0.55f);
        }
        if (above < 0f) {
            float closingSpeed = Vector2.Dot(relativeVelocity, normal);
            velocity += normal * MathHelper.Clamp(
                -above * 0.10f - closingSpeed * 0.10f, 0f, 1f) * 0.5f;
        }
        velocity *= 0.98f;
        position += velocity;

        float penetration = Vector2.Dot(position - anchor, normal);
        if (penetration < -10f) {
            position += normal * (-10f - penetration);
            float impactSpeed = Vector2.Dot(velocity - anchorVelocity, normal);
            if (impactSpeed < 0f) velocity -= normal * (impactSpeed * 0.5f);
        }
        if (penetration < 3f)
            velocity -= tangent * (Vector2.Dot(velocity - anchorVelocity, tangent) * 0.12f);

        // The tether has a maximum length; reaching it removes only outward
        // relative motion, preserving the crown's sideways inertia.
        Vector2 tether = position - anchor;
        if (tether.LengthSquared() > 200f * 200f) {
            Vector2 outward = Vector2.Normalize(tether);
            position = anchor + outward * 200f;
            float outwardSpeed = Vector2.Dot(velocity - anchorVelocity, outward);
            if (outwardSpeed > 0f) velocity -= outward * outwardSpeed;
        }

        float desiredRotation = bodyRotation * 0.4f +
            MathHelper.Clamp(Vector2.Dot(position - anchor, tangent) * 0.013f +
            Vector2.Dot(velocity - anchorVelocity, tangent) * 0.009f, -0.34f, 0.34f);
        angVelocity += MathHelper.WrapAngle(desiredRotation - rotation) * 0.06f;
        angVelocity *= 0.72f;
        rotation += angVelocity;
        timeLeft = 2;
    }

    public void Release() {
        if (released) return;
        released = true;
        BeginDrop(velocity + new Vector2(Main.rand.NextFloat(-0.9f, 0.9f), 0f),
            angVelocity + Main.rand.NextFloat(-0.045f, 0.045f));
    }

    public override void AI() {
        if (!released) {
            if (!HasValidHolder()) Release();
            else {
                timeLeft = 2;
                return;
            }
        }

        UpdateDrop();
    }
}


// The grapple fades naturally after the boss stops maintaining it.
public class KingSlimeGrappleParticle : Particle {
    public Vector2 launchPosition;
    public Vector2 chainEnd;
    public float slack;
    private bool released;

    public override Texture2D Texture => ModAsset.KingSlimeGrappleHook.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeNPCs;
        timeLeft = 2;
        cutOffscreen = false;
    }

    public void Release() {
        if (released) return;
        released = true;
        timeLeft = 18;
        maxTimeLeft = timeLeft;
    }

    public override void AI() {
        if (!released && !HasValidHolder()) Release();
        if (released) {
            chainEnd = Vector2.Lerp(chainEnd, position, 0.16f);
            rotation += 0.045f;
            alpha = Math.Min(alpha, timeLeft / (float)maxTimeLeft);
        }
        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        Texture2D chain = ModAsset.KingSlimeGrappleChain.Value;
        Vector2 from = position;
        Vector2 to = chainEnd;
        float length = Vector2.Distance(from, to);
        if (length > 2f && length < 1400f) {
            float chainScale = scale * 0.72f;
            Color chainColor = lightColor.MultiplyRGBA(new Color(195, 215, 230)) * (alpha * 0.85f);
            for (float distance = 0f; distance < length; distance += chain.Height * chainScale * 0.85f) {
                float t = distance / length;
                float nextT = Math.Min((distance + 3f) / length, 1f);
                Vector2 point = ChainPoint(from, to, t);
                Vector2 direction = ChainPoint(from, to, nextT) - point;
                spriteBatch.Draw(chain, point - Main.screenPosition, null, chainColor,
                    direction.ToRotation() - MathHelper.PiOver2, chain.Size() * 0.5f,
                    chainScale, SpriteEffects.None, 0f);
            }
        }

        spriteBatch.Draw(Texture, position - Main.screenPosition, null,
            lightColor * alpha, rotation,
            Texture.Size() * 0.5f, scale, SpriteEffects.None, 0f);
        return false;
    }

    private Vector2 ChainPoint(Vector2 from, Vector2 to, float t) {
        Vector2 middle = (from + to) * 0.5f + new Vector2(0f, slack);
        return from * ((1f - t) * (1f - t)) + middle * (2f * (1f - t) * t) + to * (t * t);
    }
}


// The hammer handle stretches to its head; the released hammer tumbles as one particle.
public class KingSlimeHammerParticle : KingSlimeDroppedItemParticle {
    private Vector2 looseHandleOffset;

    public bool held = true;
    public Vector2 grip;
    public float headRotation;
    public float starOpacity;
    public float starRotation;

    public override Texture2D Texture => ModAsset.KingSlimeHammerHead.Value;
    protected override Vector2 GroundContactPoint =>
        RotatedLowerEnd(Texture.Height * scale * 0.4f, headRotation);

    public override void SetDefaults() {
        base.SetDefaults();
        width = height = 16;
        timeLeft = 2;
        drawLayer = ParticleLayer.BeforePlayers;
        useLighting = true;
    }

    public void Release(Vector2 throwVelocity) {
        if (!held) return;
        held = false;
        looseHandleOffset = grip - position;
        starOpacity = 0f;
        BeginDrop(throwVelocity, throwVelocity.X >= 0f ? 0.09f : -0.09f);
    }

    public override void AI() {
        if (held) {
            if (HasValidHolder()) {
                timeLeft = 2;
                return;
            }
            Release(new Vector2(0f, -2f));
        }

        UpdateDrop();
    }

    protected override void RotateDropped() {
        headRotation += angVelocity;
        looseHandleOffset = looseHandleOffset.RotatedBy(angVelocity);
        grip = position + looseHandleOffset;
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        if (Main.dedServ || alpha <= 0f) return false;
        Color tint = color.MultiplyRGBA(lightColor) * alpha;
        Texture2D handle = ModAsset.KingSlimeHammerHandle.Value;
        Vector2 shaft = position - grip;
        float handleRotation = held ? 0f : shaft.ToRotation() + MathHelper.PiOver2;
        spriteBatch.Draw(handle, grip - Main.screenPosition, null, tint, handleRotation,
            new Vector2(handle.Width * 0.5f, handle.Height - 4f),
            new Vector2(Math.Min(scale, 1.25f), shaft.Length() / (handle.Height - 11f)),
            SpriteEffects.None, 0f);
        spriteBatch.Draw(Texture, position - Main.screenPosition, null, tint, headRotation,
            Texture.Size() * 0.5f, scale, SpriteEffects.None, 0f);

        if (held && starOpacity > 0f) {
            Texture2D glow = TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
            Color light = new Color(180, 220, 255, 0) * (alpha * starOpacity);
            Vector2 center = position - Main.screenPosition;
            Vector2 origin = glow.Size() * 0.5f;
            float turn = starRotation + headRotation;
            spriteBatch.Draw(glow, center, null, light, turn, origin,
                new Vector2(0.95f, 1.9f), SpriteEffects.None, 0f);
            spriteBatch.Draw(glow, center, null, light, turn + MathHelper.PiOver2,
                origin, new Vector2(0.95f, 1.9f), SpriteEffects.None, 0f);
        }
        return false;
    }
}


// The discarded potion falls through terrain and fades after two bounces.
public class KingSlimePotionParticle : KingSlimeDroppedItemParticle {
    public bool held = true;
    public bool empty;
    public int variant;

    public override Texture2D Texture => variant switch {
        1 => ModAsset.KingSlimePotionYellow.Value,
        2 => ModAsset.KingSlimePotionGreen.Value,
        3 => ModAsset.KingSlimePotionSilver.Value,
        _ => ModAsset.KingSlimePotionHealing.Value
    };

    public override Rectangle? SourceRectangle => new Rectangle(0,
        empty ? Texture.Height / 2 : 0, Texture.Width, Texture.Height / 2);

    public override Vector2 Origin => new(Texture.Width * 0.5f, Texture.Height * 0.25f);
    protected override Vector2 GroundContactPoint =>
        RotatedLowerEnd(SourceRectangle.Value.Height * scale * 0.5f, rotation) +
        Vector2.UnitY * 2f;
    protected override float GroundHalfWidth => 6f;

    public override void SetDefaults() {
        base.SetDefaults();
        width = 12;
        height = 12;
        timeLeft = 2;
        drawLayer = ParticleLayer.BeforeProjectiles;
        useLighting = true;
    }

    public void Release(Vector2 throwVelocity) {
        if (!held) return;
        held = false;
        empty = true;
        BeginDrop(throwVelocity, Math.Sign(throwVelocity.X) * 0.11f);
    }

    public override void AI() {
        if (held) {
            if (!HasValidHolder())
                Release(new Vector2(0f, -2f));
            else {
                timeLeft = 2;
                return;
            }
        }

        UpdateDrop();
    }
}


// KingSlimeRope.png has the tip on top and the repeating section below.
// When released, the rope retracts and fades without further boss updates.
public class KingSlimeRopeParticle : Particle {
    public Vector2 launchPosition;
    public Vector2 lowerEnd;
    private bool released;

    public override Texture2D Texture => ModAsset.KingSlimeRope.Value;

    private int RopeFrameHeight => Texture.Height / 2;

    private Rectangle SectionFrame => new(0, RopeFrameHeight, Texture.Width, RopeFrameHeight);

    private Rectangle EndFrame => new(0, 0, Texture.Width, RopeFrameHeight);

    public override Rectangle? SourceRectangle => EndFrame;

    public override Vector2 Origin => EndFrame.Size() * 0.5f;

    public override void SetDefaults() {
        base.SetDefaults();
        drawLayer = ParticleLayer.BeforeNPCs;
        timeLeft = 2;
        cutOffscreen = false;
    }

    public void Release() {
        if (released) return;
        released = true;
        timeLeft = 20;
        maxTimeLeft = timeLeft;
    }

    public override void AI() {
        if (!released && !HasValidHolder()) Release();
        if (released) {
            lowerEnd = Vector2.Lerp(lowerEnd, position, 0.2f);
            alpha = Math.Min(alpha, timeLeft / (float)maxTimeLeft);
        }
        if (--timeLeft <= 0) Kill();
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
        Rectangle sectionFrame = SectionFrame;
        Rectangle endFrame = EndFrame;
        Vector2 span = lowerEnd - position;
        float length = span.Length();
        if (length > 2f && length < 1400f) {
            Vector2 direction = span / length;
            float spacing = sectionFrame.Height * scale * 0.85f;
            Color ropeColor = lightColor * alpha;
            for (float distance = endFrame.Height * scale * 0.65f;
                 distance < length; distance += spacing) {
                Vector2 point = position + direction * distance;
                spriteBatch.Draw(Texture, point - Main.screenPosition, sectionFrame, ropeColor,
                    direction.ToRotation() - MathHelper.PiOver2, sectionFrame.Size() * 0.5f,
                    scale, SpriteEffects.None, 0f);
            }
        }
        spriteBatch.Draw(Texture, position - Main.screenPosition, endFrame, lightColor * alpha,
            rotation, endFrame.Size() * 0.5f, scale, SpriteEffects.None, 0f);
        return false;
    }
}


// The released spear tumbles, bounces twice, then falls through terrain.
public class KingSlimeSpearParticle : KingSlimeDroppedItemParticle {
    public bool held = true;
    public float tipStarOpacity;
    public float tipStarRotation;
    public bool tipRushStreak;

    public override Texture2D Texture => ModAsset.KingSlimeSpear.Value;
    public Vector2 TipPosition => position + new Vector2(17f, -18f).RotatedBy(rotation) * scale;
    protected override Vector2 GroundContactPoint {
        get {
            Vector2 tail = position + new Vector2(-15f, 15f).RotatedBy(rotation) * scale;
            Vector2 lowerEnd = TipPosition.Y > tail.Y ? TipPosition : tail;
            return lowerEnd + Vector2.UnitY * 5f;
        }
    }
    public Texture2D RushStreakTexture => TextureAssets.Extra[ExtrasID.ThePerfectGlow].Value;
    public float RushStreakRotation => rotation - MathHelper.PiOver4 - MathHelper.PiOver2;
    public Vector2 RushStreakScale => new Vector2(1.35f, 3.4f);

    public override void SetDefaults() {
        base.SetDefaults();
        width = 10;
        height = 10;
        timeLeft = 2;
        drawLayer = ParticleLayer.BeforeProjectiles;
        useLighting = true;
    }

    public void Release(Vector2 throwVelocity) {
        if (!held) return;
        held = false;
        BeginDrop(throwVelocity, (throwVelocity.X >= 0f ? 1f : -1f) * 0.09f);
    }

    public override void AI() {
        if (held) {
            if (!HasValidHolder()) {
                Release(new Vector2(0f, -2f));
            }
            else {
                timeLeft = 2;
                return;
            }
        }

        UpdateDrop();
    }

    public override void PostDraw(SpriteBatch spriteBatch, Color lightColor) {
        if (!held || tipStarOpacity <= 0f) return;

        // The two crossed, stretched glows follow TombwardJourney's dive-warning star.
        Texture2D star = RushStreakTexture;
        Color starColor = Color.Lerp(new Color(85, 180, 255), Color.White, 0.5f) *
            (alpha * tipStarOpacity);
        starColor.A = 0;
        Vector2 drawPosition = TipPosition - Main.screenPosition;
        Vector2 origin = star.Size() * 0.5f;
        if (tipRushStreak) {
            // One enlarged bar replaces the crossed star throughout the rush.
            spriteBatch.Draw(star, drawPosition, null, starColor,
                RushStreakRotation, origin, RushStreakScale);
            return;
        }
        float starScale = 1.18f * tipStarOpacity;
        float rotation1 = MathHelper.PiOver2 + rotation + tipStarRotation;
        spriteBatch.Draw(star, drawPosition, null, starColor, rotation1, origin,
            new Vector2(starScale, (2f + Math.Abs((float)Math.Sin(rotation1))) * 0.9f) * 0.6f);
        float rotation2 = rotation1 - MathHelper.PiOver2;
        spriteBatch.Draw(star, drawPosition, null, starColor, rotation2, origin,
            new Vector2(starScale, (2f + Math.Abs((float)Math.Sin(rotation2))) * 0.9f) * 0.6f);
    }
}


// Both wands are held by the boss, then make two small bounces on full blocks.
public class KingSlimeStaffParticle : KingSlimeDroppedItemParticle {
    public bool summoning;
    public bool held = true;

    public override Texture2D Texture => summoning
        ? ModAsset.KingSlimeStaff.Value : ModAsset.KingSlimeFireWand.Value;
    public override Vector2 Origin => summoning
        ? spriteDirection < 0 ? new Vector2(Texture.Width - 3f, Texture.Height - 5f)
            : new Vector2(3f, Texture.Height - 5f)
        : base.Origin;
    protected override Vector2 GroundContactPoint =>
        RotatedLowerEnd((summoning ? 8f : 13f) * scale, rotation);

    public override void SetDefaults() {
        base.SetDefaults();
        width = height = 12;
        timeLeft = 2;
        drawLayer = ParticleLayer.BeforeProjectiles;
        useLighting = true;
    }

    public void Release(Vector2 throwVelocity) {
        if (!held) return;
        held = false;
        BeginDrop(throwVelocity, throwVelocity.X >= 0f ? 0.09f : -0.09f);
    }

    public override void AI() {
        if (held) {
            if (HasValidHolder()) {
                timeLeft = 2;
                return;
            }
            Release(new Vector2(0f, -2f));
        }

        UpdateDrop();
    }
}


// The released umbrella bounces twice on a full block, then falls through terrain.
public class KingSlimeUmbrellaParticle : KingSlimeDroppedItemParticle {
    public bool held = true;
    public float flutter;

    public override Texture2D Texture => ModAsset.KingSlimeUmbrella.Value;
    public override Rectangle? SourceRectangle =>
        new Rectangle(0, frame * (Texture.Height / 3), Texture.Width, Texture.Height / 3);
    public override Vector2 Origin => new(Texture.Width * 0.5f, Texture.Height / 6f);
    protected override Vector2 GroundContactPoint =>
        RotatedLowerEnd(25f * scale, rotation) + Vector2.UnitY * 3f;

    public override void SetDefaults() {
        base.SetDefaults();
        width = 12;
        height = 12;
        timeLeft = 2;
        frame = 2;
        drawLayer = ParticleLayer.BeforeProjectiles;
        useLighting = true;
    }

    public void Release(Vector2 throwVelocity) {
        if (!held) return;
        held = false;
        frame = 2;
        flutter = 0f;
        BeginDrop(throwVelocity, (throwVelocity.X >= 0f ? 1f : -1f) * 0.10f);
    }

    public override void AI() {
        if (held) {
            if (HasValidHolder()) {
                timeLeft = 2;
                return;
            }
            Release(new Vector2(0f, -2f));
        }

        UpdateDrop();
    }

    public override void Draw(SpriteBatch spriteBatch, Color lightColor) {
        Vector2 drawScale = new(scale * (1f + flutter), scale * (1f - flutter * 0.35f));
        spriteBatch.Draw(Texture, position - Main.screenPosition, SourceRectangle,
            color.MultiplyRGBA(lightColor) * alpha, rotation, Origin, drawScale,
            SpriteEffects.None, 0f);
    }
}

// The ring keeps the irregular silhouette of the source texture. Several
// offset, rotating instances make the final burst feel like liquid, not a flash.
public class KingSlimeGelSplashParticle : Particle {
    public float growth = 0.025f;
    public float opacity = 0.72f;

    public override Texture2D Texture => ModAsset.TexSplashCircle.Value;

    public override void SetDefaults() {
        base.SetDefaults();
        timeLeft = maxTimeLeft = 30;
        drawLayer = ParticleLayer.BeforeProjectiles;
        cutOffscreen = false;
    }

    public override void AI() {
        base.AI();
        velocity *= 0.93f;
        float age = 1f - timeLeft / (float)maxTimeLeft;
        scale += growth * (1f - age * 0.45f);
        alpha = opacity * GuidaUtils.Smoothstep(0f, 0.12f, age) *
            GuidaUtils.Smoothstep(1f, 0.55f, age);
    }
}
