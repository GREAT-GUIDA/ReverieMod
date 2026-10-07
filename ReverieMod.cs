using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace ReverieMod {
    public class ReverieMod : Mod {
    }

    public class TwistCircleTestPlayer : ModPlayer {
        public override void PostUpdate() {
            if (Player.whoAmI != Main.myPlayer || Main.gameMenu || Main.gamePaused ||
                !Main.hasFocus || Main.playerInventory || Main.mapFullscreen ||
                Player.mouseInterface || !Main.mouseLeft || !Main.mouseLeftRelease)
                return;

            //TwistCircleParticle.Spawn(Main.MouseWorld, 2, 60, 0.5f, style: TwistCircleStyle.Circle);
        }
    }
}
