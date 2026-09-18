using System;
using HarmonyLib;
using UnityEngine;

namespace DewGemSlotCount.patch;

// Vanilla tooltip describers index fixed-size lists that only cover the vanilla
// gem counts. Hovering (or gamepad-selecting) an extended cell (index 5+) can
// throw ArgumentOutOfRangeException inside vanilla code, which spams crash
// reports and can leave the selection UI stuck. Swallow exactly that error for
// this renderer; anything else still propagates. Worst case a single tooltip
// frame is skipped.
[HarmonyPatch(typeof(UI_Tooltip_EquippedGemDescriber), "DoInGameTooltip")]
public static class UI_Tooltip_EquippedGemDescriber_Patch
{
    private static bool _hasWarned;

    [HarmonyFinalizer]
    public static Exception Finalizer(Exception __exception)
    {
        if (__exception is ArgumentOutOfRangeException)
        {
            if (!_hasWarned)
            {
                _hasWarned = true;
                Debug.LogWarning(
                    "[DewGemSlotCount] Suppressed vanilla equipped-gem tooltip index error " +
                    "on an extended gem cell.");
            }
            return null;
        }
        return __exception;
    }
}
