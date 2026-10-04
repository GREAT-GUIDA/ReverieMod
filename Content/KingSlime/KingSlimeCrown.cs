using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace ReverieMod.Content.KingSlime;

public class KingSlimeCrown : ModItem {
    public override string Texture => ModAsset.Crown_Mod;

    public override void SetDefaults() {
        Item.width = 38;
        Item.height = 28;
        Item.maxStack = 20;
        Item.useTime = 30;
        Item.useAnimation = 30;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.consumable = true;
        Item.rare = ItemRarityID.Blue;
        Item.value = Item.buyPrice(silver: 80);
    }

    public override bool CanUseItem(Player player) =>
        !NPC.AnyNPCs(ModContent.NPCType<KingSlime>()) && TryFindSpawn(player, out _);

    public override bool? UseItem(Player player) {
        if (!TryFindSpawn(player, out Vector2 spawn)) return false;
        if (Main.netMode != NetmodeID.MultiplayerClient) {
            NPC.NewNPC(player.GetSource_ItemUse(Item), (int)spawn.X, (int)spawn.Y,
                ModContent.NPCType<KingSlime>());
        }
        return true;
    }

    private static bool TryFindSpawn(Player player, out Vector2 spawn) {
        for (int rise = 100; rise <= 380; rise += 40) {
            for (int distance = 220; distance <= 420; distance += 40) {
                for (int side = 0; side < 2; side++) {
                    int direction = side == 0 ? player.direction : -player.direction;
                    Vector2 candidate = player.Center + new Vector2(direction * distance, -rise);
                    Point tile = candidate.ToTileCoordinates();
                    if (!WorldGen.InWorld(tile.X, tile.Y, 8) ||
                        Collision.SolidCollision(candidate - new Vector2(62, 42), 124, 84)) continue;
                    spawn = candidate;
                    return true;
                }
            }
        }
        spawn = Vector2.Zero;
        return false;
    }

    public override void AddRecipes() {
        CreateRecipe()
            .AddIngredient(ItemID.SlimeCrown)
            .AddIngredient(ItemID.Gel, 20)
            .AddTile(TileID.WorkBenches)
            .Register();
    }
}
