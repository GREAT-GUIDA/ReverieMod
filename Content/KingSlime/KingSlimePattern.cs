using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace ReverieMod.Content.KingSlime;

public partial class KingSlime {
    // Phase one uses four-measure phrases; phase two uses three-measure phrases.
    // Slots keep their musical durations; a late landing starts the next slot
    // immediately instead of inserting an idle measure.
    private byte patternKind;
    private byte patternStep;
    private float actionElapsed;
    private ushort actionSerial;
    private byte usedPatterns;
    private ulong usedMovementMoves;
    private ulong usedAttackMoves;
    private ulong usedHalfMeasureMoves;
    private ulong usedTwoMeasureMoves;
    private uint? lastPotionUseTick;
    private readonly List<(Move move, float started)> phaseTwoRecent = new();
    private byte lastPhaseTwoPattern;
    private float hopLaunchTimer = -1f;
    private bool forceNextTeleport;
    private ushort lastOwnerActionSerial;

    private bool SlotFinished(float ticks) => actionElapsed + 0.01f >= ticks;
    private bool AtWholeMeasure() => measureBoundaryThisTick ||
        measureTicks % 90f <= appliedTempo * 1.75f;

    private static Move ChooseUnseen(Move[] pool, ref ulong usedMask) {
        List<Move> unseen = new();
        foreach (Move move in pool)
            if ((usedMask & (1UL << (int)move)) == 0) unseen.Add(move);
        if (unseen.Count == 0) {
            usedMask = 0;
            unseen.AddRange(pool);
        }
        Move choice = unseen[Main.rand.Next(unseen.Count)];
        usedMask |= 1UL << (int)choice;
        return choice;
    }

    private bool CanDrinkPotion() => !IsEcho && splitTimer == 0f &&
        NPC.life * 5 < NPC.lifeMax * 4 &&
        (!lastPotionUseTick.HasValue ||
            unchecked(Main.GameUpdateCount - lastPotionUseTick.Value) >= 3600u);

    private bool CanUseShortsword(Player target) =>
        Vector2.DistanceSquared(NPC.Center, target.Center) <= 200f * 200f;

    private bool CanUseFireWand(Player target) =>
        Vector2.DistanceSquared(NPC.Center, target.Center) > 200f * 200f;

    private bool CanUseBoomerang(Player target) {
        float distance = Vector2.DistanceSquared(NPC.Center, target.Center);
        return distance > 300f * 300f && distance < 900f * 900f &&
            Math.Abs(NPC.Center.Y - target.Center.Y) < 120f;
    }

    private static bool IsSplitExclusiveMove(Move move) => move is
        Move.HammerSlam or Move.TeleportHammerSlam or Move.GrappleSlam or Move.UmbrellaRush;

    private bool SplitPartnerUsesExclusiveMove() {
        KingSlime partner = null;
        if (IsEcho) {
            int ownerIndex = -(int)NPC.ai[3] - 1;
            if (ownerIndex >= 0 && ownerIndex < Main.maxNPCs &&
                Main.npc[ownerIndex].active)
                partner = Main.npc[ownerIndex].ModNPC as KingSlime;
        }
        else if (TryGetSplitTwin(out NPC twin))
            partner = twin.ModNPC as KingSlime;
        return partner != null && IsSplitExclusiveMove(partner.CurrentMove);
    }

    private float SplitSideOffset() => IsEcho ? NPC.width * 0.75f :
        splitTimer >= 90f && splitMergeStart < 0f ? -NPC.width * 0.75f : 0f;

    private float SplitJumpOffset() => IsEcho ? 260f :
        splitTimer >= 90f && splitMergeStart < 0f ? -260f : 0f;

    private bool TryStartSplitMerge(Player target) {
        if (IsEcho || splitTimer < 90f || splitMergeStart >= 0f ||
            NPC.life * 5 > NPC.lifeMax * 2) return false;
        mergeIntoTwin = false;
        if (TryGetSplitTwin(out NPC twin))
            mergeIntoTwin = Vector2.DistanceSquared(twin.Center, target.Center) <
                Vector2.DistanceSquared(NPC.Center, target.Center);
        splitMergeStart = splitTimer;
        patternKind = 0;
        Begin(Move.Split);
        NPC.damage = 0;
        NPC.netUpdate = true;
        return true;
    }

    private bool TryStartPendingSplit() {
        if (!pendingSplit || IsEcho) return false;
        // The current action has finished. Hold its recovery until the next
        // whole measure, instead of requiring its last frame to hit the beat.
        if (!AtWholeMeasure()) return true;
        patternKind = 0;
        pendingSplit = false;
        Begin(Move.Split);
        return true;
    }

    private void StartNextPattern(Player target) {
        if (Main.netMode == NetmodeID.MultiplayerClient || IsEcho) return;
        if (IntroActive) return;
        if (TryStartPendingSplit()) return;
        if (!ultimateUsed && !IsEcho && NPC.life * 5 <= NPC.lifeMax) {
            ultimateUsed = true;
            patternKind = 0;
            Begin(Move.Ultimate);
            NPC.netUpdate = true;
            return;
        }
        if (tempoStage > 0) {
            StartPhaseTwoPattern(target);
            return;
        }
        List<byte> unused = new();
        for (byte i = 1; i <= 3; i++)
            if ((splitTimer == 0f && !IsEcho || i != 2) &&
                (usedPatterns & (1 << i)) == 0) unused.Add(i);
        if (unused.Count == 0) {
            usedPatterns = 0;
            unused.AddRange(splitTimer > 0f || IsEcho
                ? new byte[] { 1, 3 } : new byte[] { 1, 2, 3 });
        }
        patternKind = unused[Main.rand.Next(unused.Count)];
        usedPatterns |= (byte)(1 << patternKind);
        patternStep = 0;
        StartPatternStep(target);
    }

    private void AdvancePattern(Player target) {
        if (Main.netMode == NetmodeID.MultiplayerClient || IsEcho) return;
        if (IntroActive) return;
        if (TryStartPendingSplit()) return;
        if (tempoStage == 0 && patternKind != 0 &&
            patternStep >= (patternKind == 3 ? 3 : 2) && TryStartSplitMerge(target)) return;
        if (StartTeleportChain()) return;
        if (tempoStage > 0) {
            if (patternKind < 4 || patternKind > 6 ||
                patternStep + 1 >= (patternKind == 4 ? 2 : patternKind == 5 ? 3 : 2)) {
                patternKind = 0;
                StartNextPattern(target);
                return;
            }
            patternStep++;
            StartPhaseTwoStep(target);
            return;
        }
        if (patternKind == 0 || patternStep >= (patternKind == 3 ? 3 : 2)) {
            StartNextPattern(target);
            return;
        }
        patternStep++;
        StartPatternStep(target);
    }

    private void StartPatternStep(Player target) {
        Vector2 destination = Vector2.Zero;
        bool plannedTeleport = false;
        Move next = SelectPatternMove(target, ref destination, ref plannedTeleport);
        Begin(next);
        if (next == Move.DrinkPotion) {
            lastPotionUseTick = Main.GameUpdateCount;
        }
        if (plannedTeleport) {
            teleportDestination = destination;
            teleportReady = true;
            NPC.velocity = Vector2.Zero;
            motionMode = MotionMode.Scripted;
        }
        if (next == Move.TeleportSpearRush || next == Move.TeleportHammerSlam)
            PlanTeleportAttack(target, next);
        else if (next == Move.TeleportShuriken)
            PlanTeleportShuriken(target);
        NPC.netUpdate = true;
    }

    private bool SyncSplitAction(KingSlime owner, Player target) {
        if (!IsEcho || Main.netMode == NetmodeID.MultiplayerClient ||
            owner.splitTimer < 90f || owner.splitMergeStart >= 0f ||
            owner.CurrentMove == Move.Split ||
            owner.actionSerial == lastOwnerActionSerial) return false;
        lastOwnerActionSerial = owner.actionSerial;
        patternKind = owner.patternKind;
        patternStep = owner.patternStep;
        StartPatternStep(target);
        return true;
    }

    // The schedule and candidate pools live here; each move owns only its animation.
    private Move SelectPatternMove(Player target, ref Vector2 destination,
        ref bool plannedTeleport) {
        if (patternStep == 0 || ((patternKind == 2 || patternKind == 3) && patternStep == 1)) {
            float distance = Vector2.Distance(NPC.Center, target.Center);
            float teleportChance = MathHelper.Clamp((distance - 200f) / 1400f,
                0f, 1f);
            plannedTeleport = (forceNextTeleport || Main.rand.NextFloat() < teleportChance) &&
                TryFindTeleportDestination(target, out destination);
            if (plannedTeleport) forceNextTeleport = false;
            return plannedTeleport ? Move.Teleport : Move.Hops;
        }
        if (patternKind == 1 && patternStep == 1) {
            float horizontalDistance = Math.Abs(target.Center.X - NPC.Center.X);
            bool split = IsEcho || splitTimer >= 90f;
            List<Move> pool = split
                ? new List<Move> { Move.UmbrellaRush, Move.GrappleSlam }
                : horizontalDistance < 300f
                    ? new List<Move> { Move.HighLeap, Move.SpearRush }
                    : horizontalDistance < 500f
                        ? new List<Move> { Move.HighLeap, Move.UmbrellaRush,
                            Move.GrappleSlam, Move.SpearRush }
                        : new List<Move> { Move.HighLeap, Move.UmbrellaRush, Move.GrappleSlam };
            if (split && horizontalDistance < 500f &&
                TryPlanTeleportAttack(target, Move.TeleportSpearRush,
                    out _, out _, out _)) pool.Add(Move.TeleportSpearRush);
            if (split && SplitPartnerUsesExclusiveMove()) {
                pool.RemoveAll(IsSplitExclusiveMove);
                if (pool.Count == 0) pool.Add(Move.SpearRush);
            }
            return ChooseUnseen(pool.ToArray(), ref usedMovementMoves);
        }
        if (patternKind == 2)
            return ChooseUnseen(new[] { Move.RopeGrenades, Move.SlimeStaffRain },
                ref usedTwoMeasureMoves);
        if (patternKind == 3 && patternStep == 2) {
            List<Move> pool = new();
            if (CanUseFireWand(target)) pool.Add(Move.FireWand);
            if (CanUseShortsword(target)) pool.Add(Move.ShortswordThrust);
            if (CanUseBoomerang(target)) pool.Add(Move.Boomerang);
            if (CanDrinkPotion()) pool.Add(Move.DrinkPotion);
            return ChooseUnseen(pool.ToArray(), ref usedHalfMeasureMoves);
        }
        if (IsEcho || splitTimer >= 90f) {
            List<Move> pool = new();
            if (TryPlanTeleportAttack(target, Move.TeleportSpearRush,
                    out _, out _, out _)) pool.Add(Move.TeleportSpearRush);
            if (TryPlanTeleportAttack(target, Move.TeleportHammerSlam,
                    out _, out _, out _)) pool.Add(Move.TeleportHammerSlam);
            pool.Add(Move.TeleportShuriken);
            if (SplitPartnerUsesExclusiveMove()) pool.RemoveAll(IsSplitExclusiveMove);
            return ChooseUnseen(pool.ToArray(), ref usedAttackMoves);
        }
        return ChooseUnseen(new[] { Move.SpearRush, Move.HammerSlam,
            Move.ShurikenFan }, ref usedAttackMoves);
    }

    private void StartPhaseTwoPattern(Player target) {
        List<byte> candidates = new();
        for (int pass = 0; pass < 3 && candidates.Count == 0; pass++) {
            for (byte kind = 4; kind <= 6; kind++) {
                if (pass < 2 && kind == lastPhaseTwoPattern ||
                    PhaseTwoChoices(target, kind, 0, pass > 0).Count == 0) continue;
                if (kind == 5 && PhaseTwoChoices(target, 5, 1, pass > 0).Count == 0) continue;
                if (kind == 6 && PhaseTwoChoices(target, 6, 0, pass > 0).Count < 2) continue;
                candidates.Add(kind);
            }
        }
        if (candidates.Count == 0) {
            patternKind = 0;
            Begin(Move.Hops);
            return;
        }
        patternKind = CanDrinkPotion() && candidates.Contains(5)
            ? (byte)5 : candidates[Main.rand.Next(candidates.Count)];
        lastPhaseTwoPattern = patternKind;
        patternStep = 0;
        StartPhaseTwoStep(target);
    }

    private List<Move> PhaseTwoChoices(Player target, byte kind, byte step,
        bool ignoreRecent = false) {
        phaseTwoRecent.RemoveAll(entry => measureTicks - entry.started >= 360f);
        float distance = Vector2.Distance(NPC.Center, target.Center);
        bool near = distance < 300f;
        bool far = distance > 900f;
        List<Move> pool = new();
        bool oneMeasure = kind == 4 && step == 1 || kind == 5 && step == 2;
        bool halfMeasure = kind == 5 && step == 0;
        bool twoMeasures = kind == 4 && step == 0;
        if (oneMeasure) {
            pool.Add(Move.Hops);
            if (!IsEcho && TryFindTeleportDestination(target, out _))
                pool.Add(Move.Teleport);
        }
        else if (twoMeasures) {
            if (!far) pool.AddRange(new[] { Move.RopeGrenades, Move.SlimeStaffRain });
        }
        else if (halfMeasure) {
            if (!far) {
                if (CanUseFireWand(target)) pool.Add(Move.FireWand);
                if (CanUseShortsword(target)) pool.Add(Move.ShortswordThrust);
                if (CanUseBoomerang(target)) pool.Add(Move.Boomerang);
            }
            if (CanDrinkPotion()) pool.Add(Move.DrinkPotion);
        }
        else {
            // Phase two always uses the teleporting version of the fan.
            pool.Add(Move.TeleportShuriken);
            if (!near) pool.AddRange(new[] { Move.UmbrellaRush, Move.GrappleSlam });
            // Teleport attacks can close distance or punish a nearby player.
            if (TryPlanTeleportAttack(target, Move.TeleportSpearRush,
                    out _, out _, out _)) pool.Add(Move.TeleportSpearRush);
            if (TryPlanTeleportAttack(target, Move.TeleportHammerSlam,
                    out _, out _, out _)) pool.Add(Move.TeleportHammerSlam);
        }
        if (!oneMeasure && !ignoreRecent)
            pool.RemoveAll(move => phaseTwoRecent.Exists(entry => entry.move == move));
        return pool;
    }

    private void StartPhaseTwoStep(Player target) {
        List<Move> pool = PhaseTwoChoices(target, patternKind, patternStep);
        if (pool.Count == 0)
            pool = PhaseTwoChoices(target, patternKind, patternStep, true);
        if (pool.Count == 0) {
            patternKind = 0;
            Begin(Move.Hops);
            return;
        }
        bool useTeleport = pool.Contains(Move.Teleport) &&
            (forceNextTeleport || Main.rand.NextFloat() < MathHelper.Clamp(
                (Vector2.Distance(NPC.Center, target.Center) - 200f) / 1400f, 0f, 1f));
        if (!useTeleport) pool.Remove(Move.Teleport);
        Move next = pool.Contains(Move.DrinkPotion) ? Move.DrinkPotion
            : useTeleport ? Move.Teleport
            : Vector2.DistanceSquared(NPC.Center, target.Center) > 900f * 900f &&
                pool.Contains(Move.TeleportShuriken) ? Move.TeleportShuriken
            : pool[Main.rand.Next(pool.Count)];
        Vector2 destination = Vector2.Zero;
        if (next == Move.Teleport && !TryFindTeleportDestination(target, out destination)) {
            next = Move.Hops;
        }
        next = ResolveTeleportAttack(target, next);
        Begin(next);
        if (next != Move.Hops && next != Move.Teleport)
            phaseTwoRecent.Add((next, measureTicks));
        if (next == Move.DrinkPotion) {
            lastPotionUseTick = Main.GameUpdateCount;
        }
        if (next == Move.Teleport) {
            forceNextTeleport = false;
            teleportDestination = destination;
            teleportReady = true;
            NPC.velocity = Vector2.Zero;
            motionMode = MotionMode.Scripted;
        }
        if ((next == Move.TeleportSpearRush || next == Move.TeleportHammerSlam) &&
            !PlanTeleportAttack(target, next)) {
            phaseTwoRecent.RemoveAt(phaseTwoRecent.Count - 1);
            Move fallback = Vector2.Distance(NPC.Center, target.Center) > 900f
                ? Move.UmbrellaRush : Move.TeleportShuriken;
            Begin(fallback);
            if (fallback == Move.TeleportShuriken) PlanTeleportShuriken(target);
            phaseTwoRecent.Add((fallback, measureTicks));
        }
        if (next == Move.TeleportShuriken) PlanTeleportShuriken(target);
        NPC.netUpdate = true;
    }

}
