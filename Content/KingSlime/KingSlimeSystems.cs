using System;
using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ReverieMod.Content.KingSlime;

// The summoned slime falls under this AI until landing, then resumes ordinary slime AI.
public class KingSlimeFallingSlimeGlobalNPC : GlobalNPC {
    public override bool InstancePerEntity => true;
    private bool falling;
    private float warnedY;
    private float dropX;

    public override bool AppliesToEntity(NPC entity, bool lateInstantiation) =>
        entity.type == NPCID.GreenSlime || entity.type == NPCID.BlueSlime ||
        entity.type == NPCID.RedSlime;

    public void BeginDrop(float landingY, float x) {
        falling = true;
        warnedY = landingY;
        dropX = x;
    }

    public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter) {
        bitWriter.WriteBit(falling);
        if (falling) {
            binaryWriter.Write(warnedY);
            binaryWriter.Write(dropX);
        }
    }

    public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader) {
        bool wasFalling = falling;
        falling = bitReader.ReadBit();
        warnedY = falling ? binaryReader.ReadSingle() : 0f;
        dropX = falling ? binaryReader.ReadSingle() : 0f;
        if (wasFalling && !falling) {
            npc.noGravity = false;
            npc.noTileCollide = false;
        }
    }

    public override bool PreAI(NPC npc) {
        if (!falling) return true;
        npc.position.X = dropX - npc.width * 0.5f;
        npc.velocity.X = 0f;
        if (npc.collideY && npc.Bottom.Y > warnedY - 150f) {
            falling = false;
            npc.noGravity = false;
            npc.noTileCollide = false;
            npc.velocity.Y = 0f;
            if (Main.netMode != NetmodeID.MultiplayerClient) npc.netUpdate = true;
            return true;
        }
        // Terraria's general gravity path also caps fall speed, so the summoned
        // section supplies its own gravity until ground contact.
        npc.noGravity = true;
        npc.noTileCollide = npc.Bottom.Y < warnedY - 150f;
        npc.velocity.Y = Math.Min(Math.Max(npc.velocity.Y + 0.55f, 6f), 11.5f);
        return false;
    }
}

internal static class KingSlimeMinionDamage {
    internal static void HitNearby(Vector2 center, float radius, int damage) {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        foreach (NPC slime in Main.ActiveNPCs) {
            if (slime.type != NPCID.GreenSlime && slime.type != NPCID.BlueSlime &&
                slime.type != NPCID.RedSlime && slime.type != NPCID.SlimeSpiked)
                continue;
            Rectangle hitbox = slime.Hitbox;
            Vector2 closest = new(
                MathHelper.Clamp(center.X, hitbox.Left, hitbox.Right),
                MathHelper.Clamp(center.Y, hitbox.Top, hitbox.Bottom));
            if (Vector2.DistanceSquared(center, closest) > radius * radius) continue;
            int direction = slime.Center.X < center.X ? -1 : 1;
            slime.SimpleStrikeNPC(damage, direction, knockBack: 3f,
                noPlayerInteraction: true);
        }
    }
}


// Destination-alpha blending keeps item shadows inside the slime silhouette.
public class KingSlimeShadowSystem : ModSystem {
    // Mesh topology is shared by both render passes so the mask and body coincide.
    private const int MeshColumns = 12;
    private const int MeshRows = 8;
    private static readonly VertexPositionColorTexture[] grid =
        new VertexPositionColorTexture[(MeshColumns + 1) * (MeshRows + 1)];
    private static readonly VertexPositionColorTexture[] triangles =
        new VertexPositionColorTexture[MeshColumns * MeshRows * 6];
    private static RenderTarget2D bodyMask;
    private static RenderTarget2D itemShadows;
    private static BasicEffect bodyEffect;
    private static readonly BlendState intersect = new() {
        ColorSourceBlend = Blend.DestinationAlpha,
        ColorDestinationBlend = Blend.Zero,
        AlphaSourceBlend = Blend.DestinationAlpha,
        AlphaDestinationBlend = Blend.Zero
    };

    public override void Unload() {
        bodyMask?.Dispose();
        itemShadows?.Dispose();
        bodyEffect?.Dispose();
        bodyMask = null;
        itemShadows = null;
        bodyEffect = null;
        intersect.Dispose();
    }

    private static bool EnsureTargets() {
        if (Main.dedServ || Main.graphics?.GraphicsDevice == null) return false;
        int width = Main.screenTarget?.Width ?? Main.screenWidth;
        int height = Main.screenTarget?.Height ?? Main.screenHeight;
        if (width <= 0 || height <= 0) return false;
        if (bodyEffect == null || bodyEffect.IsDisposed)
            bodyEffect = new BasicEffect(Main.graphics.GraphicsDevice) {
                TextureEnabled = true,
                VertexColorEnabled = true
            };
        if (bodyMask != null && !bodyMask.IsDisposed && bodyMask.Width == width && bodyMask.Height == height &&
            itemShadows != null && !itemShadows.IsDisposed && itemShadows.Width == width && itemShadows.Height == height)
            return true;

        bodyMask?.Dispose();
        itemShadows?.Dispose();
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        bodyMask = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        itemShadows = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        return true;
    }

    public static bool Draw(SpriteBatch spriteBatch, Texture2D body, Rectangle frame,
        Vector2 drawBottom, float rotation, Vector2 scale, Color drawColor,
        Func<float, float, Vector2> deform, Action<SpriteBatch> drawItemShadows) {
        if (!EnsureTargets()) return false;

        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] previousTargets = device.GetRenderTargets();
        VertexPositionColorTexture[] mesh = BuildBodyMesh(body, frame, drawBottom,
            rotation, scale, deform);
        spriteBatch.End();

        device.SetRenderTarget(bodyMask);
        device.Clear(Color.Transparent);
        DrawBodyMesh(device, body, mesh, Color.White);

        device.SetRenderTarget(itemShadows);
        device.Clear(Color.Transparent);
        BeginWorld(spriteBatch);
        drawItemShadows(spriteBatch);
        spriteBatch.End();

        device.SetRenderTarget(bodyMask);
        spriteBatch.Begin(SpriteSortMode.Immediate, intersect, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
        spriteBatch.Draw(itemShadows, Vector2.Zero, Color.White);
        spriteBatch.End();

        if (previousTargets.Length == 0) device.SetRenderTarget(null);
        else device.SetRenderTargets(previousTargets);
        DrawBodyMesh(device, body, mesh, drawColor);

        spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
        spriteBatch.Draw(bodyMask, Vector2.Zero, Color.White);
        spriteBatch.End();
        BeginWorld(spriteBatch);
        return true;
    }

    private static void BeginWorld(SpriteBatch spriteBatch) => spriteBatch.Begin(
        SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
        DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

    private static VertexPositionColorTexture[] BuildBodyMesh(Texture2D texture, Rectangle frame,
        Vector2 bottom, float rotation, Vector2 scale, Func<float, float, Vector2> deform) {
        Matrix view = Main.GameViewMatrix.TransformationMatrix;
        for (int row = 0; row <= MeshRows; row++) {
            float y = row / (float)MeshRows;
            for (int column = 0; column <= MeshColumns; column++) {
                float u = column / (float)MeshColumns;
                Vector2 local = new Vector2((u * frame.Width - 87f) * scale.X,
                    (y * frame.Height - 120f) * scale.Y);
                Vector2 screen = Vector2.Transform(bottom +
                    (local + deform(u * 2f - 1f, y)).RotatedBy(rotation), view);
                Vector2 uv = new Vector2(
                    (frame.X + 0.5f + u * (frame.Width - 1f)) / texture.Width,
                    (frame.Y + 0.5f + y * (frame.Height - 1f)) / texture.Height);
                grid[row * (MeshColumns + 1) + column] = new VertexPositionColorTexture(
                    new Vector3(screen, 0f), Color.White, uv);
            }
        }

        int next = 0;
        for (int row = 0; row < MeshRows; row++) {
            for (int column = 0; column < MeshColumns; column++) {
                int topLeft = row * (MeshColumns + 1) + column;
                int bottomLeft = topLeft + MeshColumns + 1;
                triangles[next++] = grid[topLeft];
                triangles[next++] = grid[bottomLeft];
                triangles[next++] = grid[topLeft + 1];
                triangles[next++] = grid[topLeft + 1];
                triangles[next++] = grid[bottomLeft];
                triangles[next++] = grid[bottomLeft + 1];
            }
        }
        return triangles;
    }

    private static void DrawBodyMesh(GraphicsDevice device, Texture2D texture,
        VertexPositionColorTexture[] mesh, Color tint) {
        for (int i = 0; i < mesh.Length; i++) mesh[i].Color = tint;
        device.BlendState = BlendState.AlphaBlend;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
        device.SamplerStates[0] = Main.DefaultSamplerState;
        bodyEffect.Texture = texture;
        bodyEffect.World = Matrix.Identity;
        bodyEffect.View = Matrix.Identity;
        bodyEffect.Projection = Matrix.CreateOrthographicOffCenter(0f, device.Viewport.Width,
            device.Viewport.Height, 0f, 0f, 1f);
        foreach (EffectPass pass in bodyEffect.CurrentTechnique.Passes) {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, mesh, 0, mesh.Length / 3);
        }
    }
}
