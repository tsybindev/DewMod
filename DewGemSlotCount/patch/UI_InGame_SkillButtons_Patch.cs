using HarmonyLib;
using UnityEngine;

namespace DewGemSlotCount.patch
{
    [HarmonyPatch(typeof(UI_InGame_SkillButtons))]
    public class UI_InGame_SkillButtons_Patch
    {
        [HarmonyPatch("OnStateChanged")]
        [HarmonyPostfix]
        public static void OnStateChanged_Postfix(UI_InGame_SkillButtons __instance, EditSkillManager.ModeType mode)
        {
            // Leave v1.4's animation, fading, raycasts and gamepad focus intact.
            // Only adjust the expanded layout; vanilla restores it on exit.
            if (__instance == null || DewGemSlotCount.Instance == null ||
                !DewGemSlotCount.Instance.Config.OptimizeUI ||
                mode == EditSkillManager.ModeType.None || __instance.expandedLayouts == null)
            {
                return;
            }

            foreach (var layout in __instance.expandedLayouts)
            {
                if (layout != null) layout.localScale = Vector3.one * 1.9f;
            }
        }
    }
}
