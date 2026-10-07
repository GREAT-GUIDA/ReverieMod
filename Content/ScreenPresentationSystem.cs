using System;
using System.Collections.Generic;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using ReverieMod.Content.Particles;
using KingSlimeBoss = ReverieMod.Content.KingSlime.KingSlime;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;

namespace ReverieMod.Content;

// Shared screen-space presentation for boss chapter cards and cinematic bars.
public class ScreenPresentationSystem : ModSystem {
    private static bool titleActive;
    private static int titleDuration;
    private static uint startedAt;
    private static string subtitle;
    private static string headline;
    private static Color subtitleColor;
    private static Color headlineColor;
    private static bool easingBarsOut;
    private static uint lastBarFrame;
    private static bool fadeBurstShown;
    private static uint lastBlinkFrame;
    private static bool burstFlashActive;
    private static uint burstFlashStartedAt;
    private static readonly List<TitleScreenEffectParticle> titleParticles = new();
    internal static bool DrawingTitleEffects { get; private set; }

    public static void ShowTitle(int duration, string subtitleText, Color secondaryColor,
        string headlineText, Color primaryColor) {
        if (Main.dedServ) return;
        titleActive = true;
        titleDuration = Math.Max(32, duration);
        startedAt = (uint)Main.GameUpdateCount;
        subtitle = subtitleText;
        headline = headlineText;
        subtitleColor = secondaryColor;
        headlineColor = primaryColor;
        fadeBurstShown = false;
        lastBlinkFrame = 0;
        TwistCircleParticle.Spawn(Main.screenPosition + new Vector2(
            Main.screenWidth * 0.5f, Main.screenHeight * 0.2f),
            2.5f, 24, 0.6f);
    }

    public static void ShowDeathBurstFlash() {
        if (Main.dedServ || burstFlashActive) return;
        burstFlashActive = true;
        burstFlashStartedAt = (uint)Main.GameUpdateCount;
    }

    public override void PostUpdateEverything() {
        if (Main.dedServ || Main.gameMenu) return;
        float titleFlash = titleActive
            ? FlashEnvelope(unchecked((uint)Main.GameUpdateCount - startedAt), 19f)
            : 0f;
        float burstFlash = burstFlashActive
            ? FlashEnvelope(unchecked((uint)Main.GameUpdateCount - burstFlashStartedAt), 27f)
            : 0f;
        ScreenTwistSystem.UBloomIntensity = Math.Max(
            ScreenTwistSystem.UBloomIntensity,
            Math.Max(titleFlash * 0.58f, burstFlash * 1.15f));
        if (burstFlashActive &&
            unchecked((uint)Main.GameUpdateCount - burstFlashStartedAt) >= 27)
            burstFlashActive = false;
    }

    public override void OnWorldUnload() {
        titleActive = false;
        easingBarsOut = false;
        fadeBurstShown = false;
        burstFlashActive = false;
        titleParticles.Clear();
        DrawingTitleEffects = false;
    }

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) {
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index < 0) index = layers.Count;
        layers.Insert(index, new LegacyGameInterfaceLayer("ReverieMod: Screen Presentation",
            () => {
                Draw(Main.spriteBatch);
                return true;
            }, InterfaceScaleType.None));
    }

    private static void Draw(SpriteBatch spriteBatch) {
        if (Main.gameMenu) return;
        DrawTitle(spriteBatch);
        DrawScreenFlash(spriteBatch);
        DrawTitleParticles(spriteBatch);
        DrawBars(spriteBatch);
    }

    private static float FlashEnvelope(float age, float end) =>
        Smooth(age, 0f, 5f) * (1f - Smooth(age, 5f, end));

    private static void DrawScreenFlash(SpriteBatch spriteBatch) {
        float titleFlash = titleActive
            ? FlashEnvelope(unchecked((uint)Main.GameUpdateCount - startedAt), 19f)
            : 0f;
        float burstFlash = burstFlashActive
            ? FlashEnvelope(unchecked((uint)Main.GameUpdateCount - burstFlashStartedAt), 27f)
            : 0f;
        float opacity = Math.Max(titleFlash * 0.34f, burstFlash * 0.72f);
        if (opacity <= 0f) return;
        spriteBatch.Draw(TextureAssets.MagicPixel.Value,
            new Rectangle(0, 0, Main.screenWidth, Main.screenHeight),
            new Rectangle(0, 0, 1, 1), Color.White * opacity);
    }

    private static void DrawTitleParticles(SpriteBatch spriteBatch) {
        titleParticles.RemoveAll(particle => !particle.IsAlive);
        if (titleParticles.Count == 0) return;

        DrawingTitleEffects = true;
        try {
            foreach (TitleScreenEffectParticle particle in titleParticles)
                particle.PreDraw(spriteBatch, Color.White);
        }
        finally {
            DrawingTitleEffects = false;
            // EffectParticle restores the world batch; this interface layer uses
            // unscaled screen coordinates for the final cinematic bars.
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                Main.DefaultSamplerState, DepthStencilState.None,
                RasterizerState.CullCounterClockwise, null, Matrix.Identity);
        }
    }

    private static void DrawBars(SpriteBatch spriteBatch) {
        float progress = 0f;
        bool active = false;
        foreach (NPC npc in Main.ActiveNPCs) {
            if (npc.ModNPC is KingSlimeBoss slime && slime.CinematicBarsActive) {
                active = true;
                progress = Math.Max(progress, slime.CinematicBarProgress);
            }
        }
        if (active) {
            easingBarsOut = true;
            lastBarFrame = (uint)Main.GameUpdateCount;
        }
        else if (easingBarsOut) {
            float elapsed = unchecked((uint)Main.GameUpdateCount - lastBarFrame);
            progress = 1f - GuidaUtils.Smoothstep(0f, 28f, elapsed);
            if (elapsed >= 28f) easingBarsOut = false;
        }
        int height = (int)(Main.screenHeight * 0.105f * progress);
        if (height <= 0) return;
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        Rectangle source = new(0, 0, 1, 1);
        spriteBatch.Draw(pixel, new Rectangle(0, 0, Main.screenWidth, height), source,
            Color.Black);
        spriteBatch.Draw(pixel,
            new Rectangle(0, Main.screenHeight - height, Main.screenWidth, height),
            source, Color.Black);
    }

    private static void DrawTitle(SpriteBatch spriteBatch) {
        if (!titleActive) return;
        float age = unchecked((uint)Main.GameUpdateCount - startedAt);
        if (age >= titleDuration) {
            titleActive = false;
            return;
        }

        Vector2 center = new(Main.screenWidth * 0.5f,
            Main.screenHeight * 0.2f);
        float fadeStart = titleDuration * 0.8f;
        if (age >= fadeStart && !fadeBurstShown) {
            fadeBurstShown = true;
            SpawnFadeBurst(center);
        }
        if (age >= fadeStart && (int)age % 4 == 0 &&
            lastBlinkFrame != (uint)Main.GameUpdateCount) {
            lastBlinkFrame = (uint)Main.GameUpdateCount;
            SpawnBlink(center);
            SpawnBlink(center);
        }

        float reveal = Smooth(age, 0f, 24f);
        float dissolve = Smooth(age, fadeStart, titleDuration);
        float opacity = reveal * (1f - dissolve);
        ShadowGlow(spriteBatch, center, 740f * reveal, 180f, 0.34f * opacity);
        DrawAdditiveTitleBackground(spriteBatch, center, age, opacity);
        DrawDissolvingText(spriteBatch, subtitle,
            center + new Vector2(0f, -29f - (1f - reveal) * 12f),
            subtitleColor * (0.6f * opacity * Smooth(age, 6f, 28f)),
            1.34f, dissolve);
        DrawDissolvingText(spriteBatch, headline,
            center + new Vector2(0f, 14f + (1f - reveal) * 26f),
            headlineColor * opacity,
            MathHelper.Lerp(2.2f, 2.02f, reveal), dissolve);

    }

    private static void SpawnFadeBurst(Vector2 center) {
        TitleFadeEffectParticle effect = ParticleManager.Instance
            .NewParticle<TitleFadeEffectParticle>(center, Vector2.Zero);
        effect.tint = headlineColor;
        titleParticles.Add(effect);
        TwistCircleParticle.Spawn(Main.screenPosition + center,
            1.0f, 30, 0.5f);
        for (int i = 0; i < 9; i++) SpawnBlink(center);
    }

    private static void SpawnBlink(Vector2 center) {
        Vector2 outward = Main.rand.NextVector2CircularEdge(1f, 1f);
        TitleBlinkParticle blink = ParticleManager.Instance.NewParticle<TitleBlinkParticle>(
            center + outward * Main.rand.NextFloat(35f, 170f),
            outward * Main.rand.NextFloat(3.5f, 7f));
        blink.tint = Color.Lerp(headlineColor, subtitleColor, Main.rand.NextFloat());
        blink.size = Main.rand.NextFloat(0.65f, 1.25f);
        titleParticles.Add(blink);
    }

    private static void DrawDissolvingText(SpriteBatch spriteBatch, string text,
        Vector2 position, Color color, float size, float dissolve) {
        var font = FontAssets.MouseText.Value;
        Vector2 origin = font.MeasureString(text) * 0.5f;
        float clearBorder = Smooth(dissolve, 0f, 0.55f);
        if (dissolve > 0f) {
            for (int i = 0; i < 7; i++) {
                float angle = i * MathHelper.TwoPi / 7f +
                    (float)Main.GameUpdateCount * 0.09f;
                Vector2 offset = new Vector2((float)Math.Cos(angle),
                    (float)Math.Sin(angle * 1.4f)) * (3f + 19f * dissolve);
                DynamicSpriteFontExtensionMethods.DrawString(spriteBatch, font,
                    text, position + offset, color * (0.44f * dissolve),
                    0f, origin, size * (1f + 0.055f * dissolve),
                    SpriteEffects.None, 0f);
            }
        }
        if (clearBorder > 0f)
            DynamicSpriteFontExtensionMethods.DrawString(spriteBatch, font,
                text, position, color * clearBorder, 0f, origin, size,
                SpriteEffects.None, 0f);
        if (clearBorder < 1f)
            Utils.DrawBorderString(spriteBatch, text, position,
                color * (1f - clearBorder), size, 0.5f, 0.5f, -1);
    }

    private static float Smooth(float value, float start, float end) {
        float t = MathHelper.Clamp((value - start) / (end - start), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static void ShadowGlow(SpriteBatch spriteBatch, Vector2 center,
        float width, float height, float opacity) {
        Texture2D glow = ModAsset.TeleportGlow.Value;
        spriteBatch.Draw(glow, center, null, Color.Black * opacity, 0f,
            glow.Size() * 0.5f, new Vector2(width / glow.Width, height / glow.Height),
            SpriteEffects.None, 0f);
    }

    private static void DrawAdditiveTitleBackground(SpriteBatch spriteBatch,
        Vector2 center, float age, float opacity) {
        Texture2D flash = ModAsset.TitleFlash01.Value;
        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive,
            SamplerState.LinearClamp, DepthStencilState.None,
            RasterizerState.CullCounterClockwise, null, Matrix.Identity);
        spriteBatch.Draw(flash, center, null, Color.White * (0.64f * opacity),
            0f, flash.Size() * 0.5f,
            3.5f * (1f + 0.035f * (float)Math.Sin(age * 0.05f)),
            SpriteEffects.None, 0f);
        spriteBatch.End();
        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
            Main.DefaultSamplerState, DepthStencilState.None,
            RasterizerState.CullCounterClockwise, null, Matrix.Identity);
    }

}
