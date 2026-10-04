using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.GameContent.Creative;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using Terraria.GameContent.ItemDropRules;

namespace ReverieMod.Content.EyeOfCthulhu;

// Suspicious Looking Eyeball variant for Dream Eye boss
public class DreamEyeSummon : ModItem {
    public override string Texture => "Terraria/Images/Item_" + ItemID.SuspiciousLookingEye;

    public override void SetStaticDefaults() {
        CreativeItemSacrificesCatalog.Instance.SacrificeCountNeededByItemId[Type] = 3;
    }

    public override void SetDefaults() {
        Item.width = 20;
        Item.height = 20;
        Item.maxStack = 20;
        Item.value = Item.buyPrice(gold: 1);
        Item.rare = ItemRarityID.Blue;
        Item.useAnimation = 45;
        Item.useTime = 45;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.consumable = true;
    }

    public override bool CanUseItem(Player player) {
        // Can only use at night
        if (Main.dayTime) {
            return false;
        }

        // Can't use if boss is already active
        if (NPC.AnyNPCs(ModContent.NPCType<DreamEye>())) {
            return false;
        }

        return true;
    }

    public override bool? UseItem(Player player) {
        if (player.whoAmI == Main.myPlayer) {
            // Spawn boss above player
            Vector2 spawnPos = player.Center - new Vector2(0, 600);

            int bossType = ModContent.NPCType<DreamEye>();
            int npcIndex = NPC.NewNPC(player.GetSource_ItemUse(Item),
                (int)spawnPos.X, (int)spawnPos.Y, bossType);

            if (npcIndex >= 0 && npcIndex < Main.maxNPCs) {
                NPC boss = Main.npc[npcIndex];
                boss.netUpdate = true;

                // Boss summoned message
                if (Main.netMode == NetmodeID.SinglePlayer) {
                    Main.NewText("旧梦之眼已苏醒！", 175, 75, 255);
                } else if (Main.netMode == NetmodeID.Server) {
                    Terraria.Chat.ChatHelper.BroadcastChatMessage(
                        Terraria.Localization.NetworkText.FromLiteral("旧梦之眼已苏醒！"),
                        new Color(175, 75, 255));
                }

                SoundEngine.PlaySound(SoundID.Roar, player.Center);
            }
        }

        return true;
    }

    public override void AddRecipes() {
        CreateRecipe()
            .AddIngredient(ItemID.Lens, 6)
            .AddIngredient(ItemID.FallenStar, 3)
            .AddTile(TileID.DemonAltar)
            .Register();
    }
}
