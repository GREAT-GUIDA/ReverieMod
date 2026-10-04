using System;
using GuidaSharedCode;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.CameraModifiers;
using Terraria.ID;
using Terraria.ModLoader;
using ReverieMod.Content.Particles;

namespace ReverieMod.Content.EyeOfCthulhu;

// Attack pattern implementations for DreamEye
public partial class DreamEye : ModNPC {

    // ===== GAZE PHASE ATTACKS (100-60% HP) =====

    // GazeDash: Three sequential sight-line dashes with telegraph warnings
    private void GazeDashAttack(Player target) {
        int dashIndex = (int)MoveParam;

        if (dashIndex < 3) {
            // Telegraph (30 ticks)
            if (Timer < 30) {
                NPC.velocity *= 0.92f;
                pupilTargetScale = 1.2f + 0.3f * GuidaUtils.Smoothstep(0f, 30f, Timer);

                // Warning line
                if (Main.netMode != NetmodeID.Server && Timer == 5f) {
                    Vector2 toTarget = target.Center - NPC.Center;
                    float distance = toTarget.Length();
                    WarningLineParticle warning = ParticleManager.Instance?
                        .NewParticle<WarningLineParticle>(NPC.Center, Vector2.Zero);
                    if (warning != null) {
                        warning.lineRotation = toTarget.ToRotation();
                        warning.lineLength = distance;
                        warning.lineWidth = 80f;
                        warning.alpha = 0.7f;
                        warning.color = new Color(100, 180, 255);
                        warning.time = 25f;
                    }
                }
            }
            // Dash (40 ticks)
            else if (Timer < 70) {
                if (PassedTime(30f)) {
                    Vector2 dashDir = Vector2.Normalize(target.Center - NPC.Center);
                    NPC.velocity = dashDir * (isEnraged ? 22f : 18f);
                    NPC.rotation = NPC.velocity.ToRotation() + MathHelper.PiOver2;
                    pupilTargetScale = 0.6f;

                    if (Main.netMode != NetmodeID.Server) {
                        SoundEngine.PlaySound(SoundID.Item8, NPC.Center);
                    }
                    NPC.netUpdate = true;
                }
                NPC.velocity *= 0.97f;
            }
            // Recovery
            else {
                NPC.velocity *= 0.93f;
                NPC.rotation *= 0.9f;
                pupilTargetScale = 1f;

                if (Timer >= 90) {
                    Timer = 0;
                    MoveParam = dashIndex + 1;
                    NPC.netUpdate = true;
                }
            }
        } else {
            // All dashes complete
            if (Timer >= 30) {
                ChooseNextMove();
            }
        }
    }

    // WeepingTears: Arcing tear projectiles that fall and form blood puddles
    private void WeepingTearsAttack(Player target) {
        // Hover above target
        Vector2 hoverPos = target.Center - new Vector2(0, 350);
        Vector2 toHover = hoverPos - NPC.Center;
        NPC.velocity = toHover * 0.03f;

        // Fire tears
        if (Timer >= 60 && Timer < 180 && (int)Timer % 12 == 0) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                Vector2 spawnPos = NPC.Center + new Vector2(Main.rand.NextFloat(-30, 30), 40);
                Vector2 tearVel = new Vector2(Main.rand.NextFloat(-2f, 2f), Main.rand.NextFloat(4f, 7f));

                int proj = Projectile.NewProjectile(NPC.GetSource_FromAI(), spawnPos, tearVel,
                    ModContent.ProjectileType<DreamTear>(), ContactDamage(20), 2f, Main.myPlayer);

                if (Main.rand.NextBool(5)) {
                    // Large tear that splits
                    if (proj >= 0 && proj < Main.maxProjectiles) {
                        Main.projectile[proj].scale = 1.5f;
                        Main.projectile[proj].ai[0] = 1f; // Split flag
                    }
                }
            }
        }

        pupilTargetScale = 1.2f + 0.1f * (float)Math.Sin(Timer * 0.1f);

        if (Timer >= 240) {
            ChooseNextMove();
        }
    }

    // IrisBloom: Counter-rotating rings of curving shards plus aimed fan
    private void IrisBloomAttack(Player target) {
        // Move to side of player
        Vector2 offset = new Vector2(Main.rand.NextBool() ? 400 : -400, -100);
        Vector2 idealPos = target.Center + offset;
        NPC.velocity = (idealPos - NPC.Center) * 0.025f;

        irisRotation += 0.04f;

        // Ring bursts
        if (PassedTime(60f) || PassedTime(90f)) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                int shardCount = 12;
                float baseRot = PassedTime(60f) ? 0f : MathHelper.Pi / shardCount;
                for (int i = 0; i < shardCount; i++) {
                    float angle = MathHelper.TwoPi * i / shardCount + baseRot;
                    Vector2 vel = angle.ToRotationVector2() * 6f;

                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, vel,
                        ModContent.ProjectileType<CurvingShard>(), ContactDamage(18), 1.5f,
                        Main.myPlayer, PassedTime(60f) ? 1f : -1f); // Spin direction
                }

                if (Main.netMode != NetmodeID.Server) {
                    SoundEngine.PlaySound(SoundID.Item9, NPC.Center);
                }
            }
        }

        // Aimed fan
        if (PassedTime(120f)) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                Vector2 toTarget = Vector2.Normalize(target.Center - NPC.Center);
                for (int i = -2; i <= 2; i++) {
                    float angle = toTarget.ToRotation() + i * 0.15f;
                    Vector2 vel = angle.ToRotationVector2() * 10f;

                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, vel,
                        ModContent.ProjectileType<CurvingShard>(), ContactDamage(22), 2f, Main.myPlayer);
                }
            }
        }

        if (Timer >= 180) {
            ChooseNextMove();
        }
    }

    // ServantWeave: Summon eye servants from rifts, orbit then sequential dashes
    private void ServantWeaveAttack(Player target) {
        NPC.velocity *= 0.94f;

        // Summon servants
        if (summonedServants < MaxServants && Timer >= 40 && (Timer - 40) % 30 == 0) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                float angle = MathHelper.TwoPi * summonedServants / MaxServants;
                Vector2 spawnPos = NPC.Center + angle.ToRotationVector2() * 200f;

                int servant = NPC.NewNPC(NPC.GetSource_FromAI(), (int)spawnPos.X, (int)spawnPos.Y,
                    ModContent.NPCType<EyeServant>());

                if (servant >= 0 && servant < Main.maxNPCs) {
                    Main.npc[servant].ai[0] = NPC.whoAmI; // Master index
                    Main.npc[servant].ai[1] = angle; // Orbit angle
                    Main.npc[servant].netUpdate = true;
                }

                summonedServants++;
                NPC.netUpdate = true;

                if (Main.netMode != NetmodeID.Server) {
                    // Rift visual
                    RiftParticle rift = ParticleManager.Instance?.NewParticle<RiftParticle>(
                        spawnPos, Vector2.Zero);
                    if (rift != null) {
                        rift.scale = 1.2f;
                        rift.timeLeft = rift.maxTimeLeft = 20;
                    }
                    SoundEngine.PlaySound(SoundID.Item8, spawnPos);
                }
            }
        }

        if (Timer >= 200) {
            ChooseNextMove();
        }
    }

    // ===== MAW PHASE ATTACKS (60-25% HP) =====

    // FrenzyCharge: Predictive chain charges (3-4 charges)
    private void FrenzyChargeAttack(Player target) {
        int chargeIndex = (int)MoveParam;
        int maxCharges = isEnraged ? 4 : 3;

        if (chargeIndex < maxCharges) {
            if (Timer < 20) {
                // Predict target position
                Vector2 predictedPos = target.Center + target.velocity * 15f;
                Vector2 toPredict = predictedPos - NPC.Center;
                NPC.velocity = toPredict * 0.02f;

                NPC.rotation = toPredict.ToRotation() + MathHelper.PiOver2;
            } else if (Timer < 50) {
                if (PassedTime(20f)) {
                    Vector2 chargeDir = Vector2.Normalize(target.Center + target.velocity * 10f - NPC.Center);
                    NPC.velocity = chargeDir * 24f;
                    NPC.netUpdate = true;
                }
                NPC.velocity *= 0.98f;
            } else {
                NPC.velocity *= 0.9f;
                NPC.rotation *= 0.92f;

                if (Timer >= 65) {
                    Timer = 0;
                    MoveParam = chargeIndex + 1;
                    NPC.netUpdate = true;
                }
            }
        } else {
            NPC.velocity *= 0.94f;
            if (Timer >= 30) {
                ChooseNextMove();
            }
        }
    }

    // Hemorrhage: Spinning blood sprinkler + expanding rings
    private void HemorrhageAttack(Player target) {
        // Spin in place
        NPC.velocity *= 0.93f;
        NPC.rotation += 0.12f;

        // Fire blood projectiles in spiral
        if (Timer >= 40 && Timer < 160 && (int)Timer % 3 == 0) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                float angle = Timer * 0.25f;
                Vector2 vel = angle.ToRotationVector2() * 8f;

                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, vel,
                    ModContent.ProjectileType<BloodGlob>(), ContactDamage(24), 2f, Main.myPlayer);
            }
        }

        // Expanding rings
        if (PassedTime(80f) || PassedTime(120f)) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                int count = 16;
                for (int i = 0; i < count; i++) {
                    float angle = MathHelper.TwoPi * i / count;
                    Vector2 vel = angle.ToRotationVector2() * 5f;

                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, vel,
                        ModContent.ProjectileType<BloodGlob>(), ContactDamage(26), 2.5f, Main.myPlayer);
                }
            }
        }

        if (Timer >= 180) {
            NPC.rotation = 0f;
            ChooseNextMove();
        }
    }

    // RiftAmbush: 4 eyelid rifts, 3 shoot needles, 1 bursts out
    private void RiftAmbushAttack(Player target) {
        NPC.velocity *= 0.95f;

        // Spawn rifts
        if (PassedTime(30f)) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                int realRift = Main.rand.Next(4);
                for (int i = 0; i < 4; i++) {
                    float angle = MathHelper.PiOver2 * i;
                    Vector2 riftPos = target.Center + angle.ToRotationVector2() * 350f;

                    int proj = Projectile.NewProjectile(NPC.GetSource_FromAI(), riftPos, Vector2.Zero,
                        ModContent.ProjectileType<AmbushRift>(), ContactDamage(28), 0f, Main.myPlayer,
                        i == realRift ? 1f : 0f); // Real rift flag
                }

                MoveParam = 1f; // Rifts spawned flag
                NPC.netUpdate = true;
            }
        }

        // Teleport to real rift position at Timer=90
        if (MoveParam > 0 && PassedTime(90f)) {
            // The rift projectile handles the burst; boss just waits
        }

        if (Timer >= 150) {
            ChooseNextMove();
        }
    }

    // Devour: Suction pull, chomp lunge, tooth projectile spit
    private void DevourAttack(Player target) {
        if (Timer < 60) {
            // Suction phase
            NPC.velocity *= 0.9f;
            Vector2 toTarget = target.Center - NPC.Center;
            float distance = toTarget.Length();

            if (distance > 100f) {
                // Pull player
                Vector2 pullDir = -Vector2.Normalize(toTarget);
                target.velocity += pullDir * 0.3f;
            }

            pupilTargetScale = 1.5f;
        } else if (Timer < 90) {
            // Chomp lunge
            if (PassedTime(60f)) {
                Vector2 lungeDir = Vector2.Normalize(target.Center - NPC.Center);
                NPC.velocity = lungeDir * 20f;
                NPC.rotation = lungeDir.ToRotation() + MathHelper.PiOver2;
                NPC.damage = ContactDamage(40);
                NPC.netUpdate = true;
            }
            NPC.velocity *= 0.96f;
        } else if (Timer < 150) {
            // Spit teeth
            NPC.velocity *= 0.92f;
            NPC.rotation *= 0.9f;
            NPC.damage = NPC.defDamage;

            if ((int)Timer % 10 == 0 && Main.netMode != NetmodeID.MultiplayerClient) {
                Vector2 toTarget = Vector2.Normalize(target.Center - NPC.Center);
                Vector2 vel = toTarget.RotatedByRandom(0.3f) * Main.rand.NextFloat(10f, 14f);

                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, vel,
                    ModContent.ProjectileType<ToothProjectile>(), ContactDamage(22), 3f, Main.myPlayer);
            }
        }

        if (Timer >= 180) {
            ChooseNextMove();
        }
    }

    // ===== NIGHTMARE PHASE ATTACKS (<25% HP) =====

    // ThousandEyes: Ring of watcher eyes firing sequential gaze beams
    private void ThousandEyesAttack(Player target) {
        NPC.velocity *= 0.94f;

        // Spawn watcher eyes in ring
        if (PassedTime(30f)) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                int eyeCount = 8;
                float radius = 400f;
                for (int i = 0; i < eyeCount; i++) {
                    float angle = MathHelper.TwoPi * i / eyeCount;
                    Vector2 spawnPos = NPC.Center + angle.ToRotationVector2() * radius;

                    int proj = Projectile.NewProjectile(NPC.GetSource_FromAI(), spawnPos, Vector2.Zero,
                        ModContent.ProjectileType<WatcherEye>(), ContactDamage(26), 0f, Main.myPlayer,
                        angle, i * 15f); // Angle and fire delay
                }
            }
        }

        if (Timer >= 240) {
            ChooseNextMove();
        }
    }

    // DreamRay: Sweeping mouth deathray
    private void DreamRayAttack(Player target) {
        if (Timer < 60) {
            // Position above and to side
            Vector2 aimPos = target.Center + new Vector2(300f * (Main.rand.NextBool() ? 1 : -1), -250f);
            NPC.velocity = (aimPos - NPC.Center) * 0.04f;

            pupilTargetScale = 1.8f;
            glowPulse = 1.5f;
        } else if (Timer < 180) {
            // Fire and sweep
            if (PassedTime(60f)) {
                if (Main.netMode != NetmodeID.MultiplayerClient) {
                    Vector2 toTarget = Vector2.Normalize(target.Center - NPC.Center);

                    int proj = Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, toTarget,
                        ModContent.ProjectileType<DreamBeam>(), ContactDamage(35), 5f, Main.myPlayer);
                }

                if (Main.netMode != NetmodeID.Server) {
                    SoundEngine.PlaySound(SoundID.Zombie104, NPC.Center);
                    Main.instance.CameraModifiers.Add(new PunchCameraModifier(
                        NPC.Center, Main.rand.NextVector2CircularEdge(1f, 1f), 10f, 6f, 30));
                }
            }

            NPC.velocity *= 0.98f;
        } else {
            pupilTargetScale = 1f;
            glowPulse = 0.3f;
        }

        if (Timer >= 210) {
            ChooseNextMove();
        }
    }

    private int ContactDamage(int baseAmount) =>
        (int)(baseAmount * (Main.masterMode ? 2.25f : Main.expertMode ? 1.5f : 1f));
}
