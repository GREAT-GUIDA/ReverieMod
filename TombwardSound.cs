using Terraria.Audio;
using Terraria.ID;

namespace ReverieMod;

// Shared sound styles: existing Terraria sounds need no asset requests.
internal static class TombwardSound {
    public static SoundStyle EyeHit => SoundID.NPCHit1 with { Volume = 0.55f, PitchVariance = 0.15f };
    public static SoundStyle EyeRoar => SoundID.Roar with { Volume = 0.8f, Pitch = -0.2f, MaxInstances = 2 };
    public static SoundStyle EyeRush => SoundID.Item9 with { Volume = 0.65f, Pitch = -0.45f, PitchVariance = 0.12f };
    public static SoundStyle EyeCharge => SoundID.Item8 with { Volume = 0.5f, Pitch = -0.5f };
    public static SoundStyle EyeBeam => SoundID.Item33 with { Volume = 0.6f, Pitch = -0.4f };
    public static SoundStyle EyeDeath => SoundID.NPCDeath1 with { Volume = 0.9f, Pitch = -0.45f };
}
