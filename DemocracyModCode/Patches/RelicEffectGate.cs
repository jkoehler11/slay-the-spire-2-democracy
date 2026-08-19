using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using DemocracyMod.DemocracyModCode;

namespace DemocracyMod.DemocracyModCode.Patches;

/// <summary>
/// Suppresses a relic's "upon pickup" effect (RelicModel.AfterObtained) while rewards are
/// being captured, so an effect relic (Astrolabe, Calling Bell, Pandora's Box, ...) does
/// NOT fire the moment its reward is granted during the loot / ancient selection. The
/// effect instead fires when the relic lands on its FINAL owner — during the transfer
/// (RelicCmd.Obtain runs again with the capture flag cleared) or, for an uncontested
/// keep, via VoteManager firing AfterObtained explicitly after the vote resolves.
///
/// AfterObtained is public virtual with ~40 relic overrides, so a Harmony prefix on the
/// base method would be bypassed by virtual dispatch to the overrides. Instead we
/// transpile the ONE call site — RelicCmd.&lt;Obtain&gt;d__1.MoveNext — replacing
/// "callvirt RelicModel.AfterObtained" with a call to InvokeAfterObtained, which checks
/// the capture flags and either defers (returns a completed Task) or runs the real
/// effect via normal virtual dispatch.
/// </summary>
public static class RelicEffectGate
{
    /// <summary>Transpiler replacement for RelicModel.AfterObtained.</summary>
    public static Task InvokeAfterObtained(RelicModel relic)
    {
        if (RewardPool.IsRewardPhaseActive || RewardPool.IsAncientRewardPhaseActive)
        {
            MainFile.LogVote(string.Format(
                "Democracy: deferred relic effect [{0}] until selection resolves.",
                relic?.GetType().Name ?? "?"));
            return Task.CompletedTask;
        }
        if (RewardPool.IsDemocracyFlowActive)
        {
            MainFile.LogVote(string.Format(
                "Democracy: suppressed relic effect [{0}] during flow (will fire after).",
                relic?.GetType().Name ?? "?"));
            return Task.CompletedTask;
        }
        return relic.AfterObtained();
    }

    private static bool IsAfterObtained(CodeInstruction code)
    {
        if (code.opcode != OpCodes.Callvirt) return false;
        if (code.operand is MethodBase mb)
            return mb.Name == "AfterObtained" && mb.DeclaringType == typeof(RelicModel);
        return false;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var gate = AccessTools.Method(typeof(RelicEffectGate), nameof(InvokeAfterObtained));
        if (gate == null)
        {
            MainFile.Logger.Info("Democracy: RelicEffectGate — gate method not found, leaving Obtain untouched.");
            foreach (var c in instructions) yield return c;
            yield break;
        }

        int replaced = 0;
        foreach (var code in instructions)
        {
            if (IsAfterObtained(code))
            {
                code.opcode = OpCodes.Call;
                code.operand = gate;
                replaced++;
            }
            yield return code;
        }
        MainFile.Logger.Info(string.Format("Democracy: RelicEffectGate — replaced {0} AfterObtained call(s).", replaced));
    }

    /// <summary>
    /// Patches RelicCmd.&lt;Obtain&gt;d__1.MoveNext. The state machine is a compiler-
    /// generated nested type, so it can't be targeted by a [HarmonyPatch] attribute on a
    /// compile-time type; resolve it via reflection and patch manually.
    /// </summary>
    public static void Patch(Harmony harmony)
    {
        var sm = typeof(RelicCmd).GetNestedType("<Obtain>d__1", BindingFlags.NonPublic);
        if (sm == null)
        {
            MainFile.Logger.Info("Democracy: RelicEffectGate — RelicCmd.<Obtain>d__1 not found; relic effects will NOT be suppressed.");
            return;
        }
        var moveNext = sm.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance);
        if (moveNext == null)
        {
            MainFile.Logger.Info("Democracy: RelicEffectGate — MoveNext not found; relic effects will NOT be suppressed.");
            return;
        }
        harmony.Patch(moveNext, transpiler: new HarmonyMethod(typeof(RelicEffectGate), nameof(Transpiler)));
        MainFile.Logger.Info("Democracy: RelicEffectGate — relic on-obtain effect suppression active.");
    }
}
