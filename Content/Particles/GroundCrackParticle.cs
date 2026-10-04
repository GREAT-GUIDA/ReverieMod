using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace ReverieMod.Content.Particles;

public class GroundCrackParticle : Particle {
	// tileTarget 空地多为透明黑；用目标 RGB 做遮罩比 DestinationAlpha 更可靠。
	private static readonly BlendState CrackByTileTargetMask = new() {
		ColorSourceBlend = Blend.DestinationColor,
		ColorDestinationBlend = Blend.Zero,
		AlphaSourceBlend = Blend.DestinationAlpha,
		AlphaDestinationBlend = Blend.Zero
	};

	private RenderTarget2D _composite;
	private int _rtSize;
	private Vector2 _worldTopLeft;
	private Vector2 _drawScale;

	public override Texture2D Texture => ModAsset.Cracks.Value;

	public override void SetDefaults() {
		base.SetDefaults();
		drawLayer = ParticleLayer.BeforePlayers;
		timeLeft = maxTimeLeft = 260;
		useLighting = true;
		rotation = Main.rand.NextFloat(-0.12f, 0.12f);
		float uniform = 420f / Math.Max(Texture.Width, Texture.Height);
		_drawScale = new Vector2(
			uniform * Main.rand.NextFloat(1.52f, 1.78f),
			uniform * Main.rand.NextFloat(0.84f, 0.92f));
		Texture2D cracks = Texture;
		_rtSize = (int)Math.Ceiling(Math.Max(cracks.Width * _drawScale.X, cracks.Height * _drawScale.Y) * 1.08f);
	}

	public override void AI() {
		if (--timeLeft <= 0)
			Kill();
	}

	public override void Kill() {
		_composite?.Dispose();
		_composite = null;
		base.Kill();
	}

	static void DrawCracks(SpriteBatch spriteBatch, Texture2D cracks, Vector2 worldPos, Vector2 drawScale, float rot, Color tint) {
		spriteBatch.DrawWorld(cracks, worldPos, null, tint, rot, cracks.Size() / 2f, drawScale);
	}

	static void DrawCrackGlow(SpriteBatch spriteBatch, Vector2 worldPos, Vector2 drawScale, float rot, Color glowTint) {
		Texture2D glow = ModAsset.TexGlow.Value;
		spriteBatch.DrawWorld(glow, worldPos, null, glowTint, rot, glow.Size() / 2f, drawScale);
	}

	Color BuildCrackTint(float masterAlpha) {
		float a = masterAlpha * 0.72f;
		Color tint = Color.Lerp(new Color(58, 38, 24), new Color(14, 10, 7), 0.35f);
		tint = tint.MultiplyRGBA(Lighting.GetColor((int)(position.X / 16f), (int)((position.Y + 4f) / 16f)));
		tint *= a;
		tint.A = (byte)MathHelper.Clamp(tint.A, 0f, 255f);
		return tint;
	}

	Color BuildCrackGlowTint(Color crackTint) {
		Color glow = crackTint;
		glow.A = (byte)MathHelper.Clamp(crackTint.A * 0.11f, 0f, 255f);
		return glow;
	}

	static bool TryGetTileTargetSample(Rectangle destRt, out Rectangle src, out Rectangle dest) {
		src = default;
		dest = default;
		RenderTarget2D tileTarget = Main.instance.tileTarget;
		if (tileTarget is null || tileTarget.IsContentLost)
			return false;

		Vector2 worldTopLeft = new Vector2(destRt.X, destRt.Y);
		Vector2 local = worldTopLeft - Main.sceneTilePos;
		int srcX = (int)local.X;
		int srcY = (int)local.Y;
		int clipL = Math.Max(0, -srcX);
		int clipT = Math.Max(0, -srcY);
		int clipR = Math.Max(0, srcX + destRt.Width - tileTarget.Width);
		int clipB = Math.Max(0, srcY + destRt.Height - tileTarget.Height);
		src = new Rectangle(srcX + clipL, srcY + clipT, destRt.Width - clipL - clipR, destRt.Height - clipT - clipB);
		dest = new Rectangle(clipL, clipT, src.Width, src.Height);
		return src.Width > 0 && src.Height > 0;
	}

	public override bool PreDraw(SpriteBatch spriteBatch, Color lightColor) {
		float life = maxTimeLeft > 0 ? timeLeft / (float)maxTimeLeft : 0f;
		float elapsed = 1f - life;
		float fadeIn = Easing.ExpoOut(MathHelper.Clamp(elapsed / 0.01f, 0f, 1f));
		float fadeOut = Easing.SineOut(MathHelper.Clamp(life / 0.55f, 0f, 1f));
		float masterAlpha = fadeIn * fadeOut;
		if (masterAlpha <= 0.01f)
			return false;

		Texture2D cracks = Texture;
		Color tint = BuildCrackTint(masterAlpha);
		Color glowTint = BuildCrackGlowTint(tint);

		_worldTopLeft = position - new Vector2(_rtSize * 0.5f);
		var worldRect = new Rectangle((int)_worldTopLeft.X, (int)_worldTopLeft.Y, _rtSize, _rtSize);
		if (Main.drawToScreen || !TryGetTileTargetSample(worldRect, out Rectangle tileSrc, out Rectangle tileDest)) {
			DrawCracks(spriteBatch, cracks, position, _drawScale, rotation, tint);
			spriteBatch.EndAndBegin(BlendState.Additive);
			DrawCrackGlow(spriteBatch, position, _drawScale, rotation, glowTint);
			spriteBatch.EndAndBeginDefault();
			return false;
		}

		RenderTarget2D tileTarget = Main.instance.tileTarget;
		_composite ??= new RenderTarget2D(Main.instance.GraphicsDevice, _rtSize, _rtSize, false,
			SurfaceFormat.Color, DepthFormat.None);

		GraphicsDevice device = Main.instance.GraphicsDevice;
		if (Main.screenTarget is null) {
			DrawCracks(spriteBatch, cracks, position, _drawScale, rotation, tint);
			spriteBatch.EndAndBegin(BlendState.Additive);
			DrawCrackGlow(spriteBatch, position, _drawScale, rotation, glowTint);
			spriteBatch.EndAndBeginDefault();
			return false;
		}

		RenderTarget2D restore = Main.screenTarget;
		spriteBatch.End();
		device.SetRenderTarget(_composite);
		device.Clear(Color.Transparent);

		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
		spriteBatch.Draw(tileTarget, tileDest, tileSrc, Color.White);
		spriteBatch.End();

		spriteBatch.Begin(SpriteSortMode.Deferred, CrackByTileTargetMask, SamplerState.LinearClamp,
			DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
		spriteBatch.Draw(cracks, new Vector2(_rtSize * 0.5f), null, tint, rotation, cracks.Size() / 2f, _drawScale);
		spriteBatch.End();

		Texture2D glowTex = ModAsset.TexGlow.Value;
		spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp,
			DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
		spriteBatch.Draw(glowTex, new Vector2(_rtSize * 0.5f), null, glowTint, rotation, glowTex.Size() / 2f, _drawScale);
		spriteBatch.End();

		device.SetRenderTarget(restore);
		spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp,
			DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
		spriteBatch.Draw(_composite, _worldTopLeft - Main.screenPosition, Color.White);
		return false;
	}
}
