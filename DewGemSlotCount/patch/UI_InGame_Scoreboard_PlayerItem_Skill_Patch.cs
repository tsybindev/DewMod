using System;
using System.Collections.Generic;
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
        if (!TryGetGemPrefab(template.transform, out var gemPrefab))
        {
            return;
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
                var gems = CollectGemChildren(group);
                while (gems.Count < quantity)
                {
                    var obj = UnityEngine.Object.Instantiate(gemPrefab, group, false);
                    var created = obj.GetComponent<UI_InGame_Scoreboard_PlayerItem_Skill_Gem>();
                    if (created == null)
                    {
                        throw new InvalidOperationException(
                            "Cloned scoreboard gem has no UI_InGame_Scoreboard_PlayerItem_Skill_Gem component.");
                    }
                    gems.Add(obj.transform);
                }
                for (int slot = 0; slot < gems.Count; slot++)
                {
                    gems[slot].GetComponent<UI_InGame_Scoreboard_PlayerItem_Skill_Gem>().index = slot;
                }
                SetupDualLineLayout(gems, quantity);
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

    private static bool TryGetGemPrefab(Transform templateGroup, out GameObject gemPrefab)
    {
        gemPrefab = null;
        if (templateGroup == null)
        {
            LogLayoutError("The four-slot scoreboard template is missing.");
            return false;
        }

        var gems = CollectGemChildren(templateGroup);
        if (gems.Count != 4)
        {
            LogLayoutError(
                $"The scoreboard template has an unexpected hierarchy (found {gems.Count} gems, expected 4).");
            return false;
        }

        gemPrefab = gems[gems.Count - 1].gameObject;
        return true;
    }

    private static List<Transform> CollectGemChildren(Transform group)
    {
        var gems = new List<Transform>();
        if (group == null) return gems;
        for (int c = 0; c < group.childCount; c++)
        {
            var child = group.GetChild(c);
            if (child.GetComponent<UI_InGame_Scoreboard_PlayerItem_Skill_Gem>() != null)
            {
                gems.Add(child);
            }
        }
        return gems;
    }

    private static void SetupDualLineLayout(List<Transform> gems, int totalSlots)
    {
        int num = Mathf.CeilToInt(totalSlots / 2f);
        int slotCount = totalSlots - num;
        ArrangeLine(gems, num, slotCount, 100f, 30f * (1f - totalSlots * 0.02f));
        ArrangeLine(gems, 0, num, -20f, 30f * (1f - totalSlots * 0.02f));
    }

    private static void ArrangeLine(List<Transform> gems, int startIndex, int slotCount, float yPos, float spacing)
    {
        float offset = -((slotCount - 1) * spacing / 2f);
        for (int i = 0; i < slotCount; i++)
        {
            gems[startIndex + i].localPosition = new Vector3(offset + i * spacing, yPos, 0f);
        }
    }
}
