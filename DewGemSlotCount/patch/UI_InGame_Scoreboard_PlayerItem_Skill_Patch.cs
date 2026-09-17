using System;
using HarmonyLib;
using UnityEngine;

namespace DewGemSlotCount.patch;

[HarmonyPatch(typeof(UI_InGame_Scoreboard_PlayerItem_Skill))]
public static class UI_InGame_Scoreboard_PlayerItem_Skill_Patch
{
    private static readonly System.Reflection.FieldInfo GemObjectsField =
        AccessTools.Field(typeof(UI_InGame_Scoreboard_PlayerItem_Skill), "gemObjects234");

    private static bool _hasLoggedLayoutError;

    [HarmonyPrefix]
    [HarmonyPatch("UpdateInfo")]
    public static void UpdateInfo_Prefix(UI_InGame_Scoreboard_PlayerItem_Skill __instance)
    {
        // Extend data only. Vanilla still updates visibility, icons, charges and bindings.
        // Slot availability must not depend on the cosmetic OptimizeUI setting.
        if (__instance == null || GemObjectsField == null) return;
        var gemObjects = GemObjectsField.GetValue(__instance) as GameObject[];
        if (gemObjects == null || gemObjects.Length >= Constant.MaxGemCount - 1) return;
        if (gemObjects.Length < 3 || gemObjects[2] == null)
        {
            LogLayoutError("The four-slot scoreboard template is missing.");
            return;
        }

        var template = gemObjects[2];
        if (template.transform.childCount != 4)
        {
            LogLayoutError("The scoreboard template has an unexpected hierarchy.");
            return;
        }
        for (int i = 0; i < 4; i++)
        {
            if (template.transform.GetChild(i).GetComponent<UI_InGame_Scoreboard_PlayerItem_Skill_Gem>() == null)
            {
                LogLayoutError("A gem component is missing from the scoreboard template.");
                return;
            }
        }

        int oldLength = gemObjects.Length;
        var groups = new GameObject[Constant.MaxGemCount - 1];
        Array.Copy(gemObjects, groups, oldLength);
        try
        {
            for (int i = oldLength; i < groups.Length; i++)
            {
                int quantity = i + 2;
                var clone = UnityEngine.Object.Instantiate(template, template.transform.parent, false);
                groups[i] = clone;
                clone.SetActive(false);
                clone.name = $"{quantity} Gems";
                var group = clone.transform;
                while (group.childCount < quantity)
                {
                    UnityEngine.Object.Instantiate(template.transform.GetChild(0).gameObject, group, false);
                }
                for (int slot = 0; slot < group.childCount; slot++)
                {
                    group.GetChild(slot).GetComponent<UI_InGame_Scoreboard_PlayerItem_Skill_Gem>().index = slot;
                }
                SetupDualLineLayout(group, quantity);
            }
            GemObjectsField.SetValue(__instance, groups);
        }
        catch (Exception ex)
        {
            for (int i = oldLength; i < groups.Length; i++)
            {
                if (groups[i] != null) UnityEngine.Object.Destroy(groups[i]);
            }
            LogLayoutError(ex.ToString());
        }
    }

    private static void LogLayoutError(string message)
    {
        if (_hasLoggedLayoutError) return;
        _hasLoggedLayoutError = true;
        Debug.LogWarning("[DewGemSlotCount] Cannot extend scoreboard UI: " + message);
    }

    private static void SetupDualLineLayout(Transform group, int totalSlots)
    {
        int num = Mathf.CeilToInt(totalSlots / 2f);
        int slotCount = totalSlots - num;
        ArrangeLine(group, num, slotCount, 100f, 30f * (1f - totalSlots * 0.02f));
        ArrangeLine(group, 0, num, -20f, 30f * (1f - totalSlots * 0.02f));
    }

    private static void ArrangeLine(Transform group, int startIndex, int slotCount, float yPos, float spacing)
    {
        float offset = -((slotCount - 1) * spacing / 2f);
        for (int i = 0; i < slotCount; i++)
        {
            group.GetChild(startIndex + i).localPosition = new Vector3(offset + i * spacing, yPos, 0f);
        }
    }
}
