using System;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace DewGemSlotCount.patch;

[HarmonyPatch(typeof(UI_InGame_SkillButton_GemGroup))]
public static class UI_InGame_SkillButton_GemGroup_Patch
{
    private const float DualLineUpY = 100f;
    private const float DualLineDownY = -100f;

    private static readonly System.Reflection.FieldInfo LastAppliedGemCountField =
        AccessTools.Field(typeof(UI_InGame_SkillButton_GemGroup), "_lastAppliedGemCount");

    private static bool _hasLoggedLayoutError;

    [HarmonyPrefix]
    [HarmonyPatch("LogicUpdate")]
    public static void LogicUpdate_Prefix(UI_InGame_SkillButton_GemGroup __instance)
    {
        // v1.4 selects groups[count - 1] here and caches the count. Build the
        // missing groups BEFORE that selection, even when OptimizeUI is off.
        // Do not replace vanilla LogicUpdate: it also populates activeGemSlots.
        AddGemCountUI(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnStateChanged")]
    public static void OnStateChanged_Postfix(UI_InGame_SkillButton_GemGroup __instance,
        EditSkillManager.ModeType mode)
    {
        // Keep vanilla scaling, fading, interaction and raycast handling.
        AddGemCountUI(__instance);
        if (__instance == null || __instance.groups == null ||
            DewGemSlotCount.Instance == null || !DewGemSlotCount.Instance.Config.OptimizeUI)
        {
            return;
        }

        for (int i = 4; i < __instance.groups.Length; i++)
        {
            var group = __instance.groups[i];
            if (group == null) continue;
            int split = (group.transform.childCount + 1) / 2;
            float inset = mode == EditSkillManager.ModeType.None ? 0f : 20f;
            SmoothMoveY(group, 0, split, DualLineDownY + inset, __instance.gemGroupAnimDuration);
            SmoothMoveY(group, split, group.transform.childCount, DualLineUpY - inset,
                __instance.gemGroupAnimDuration);
        }
    }

    private static void AddGemCountUI(UI_InGame_SkillButton_GemGroup instance)
    {
        if (instance == null || instance.groups == null || instance.groups.Length >= Constant.MaxGemCount)
        {
            return;
        }

        // Instantiate can invoke OnEnable before SetActive(false) when the
        // template is active. GemSlot.OnEnable dereferences DewPlayer.local.
        // Match vanilla LogicUpdate's readiness guard before creating anything.
        if (DewPlayer.local == null || DewPlayer.local.hero == null)
        {
            return;
        }

        // Validate before resizing, so vanilla never receives a partially filled array.
        if (instance.groups.Length < 4 || instance.groups[3] == null)
        {
            LogLayoutError("The four-slot UI template is missing.");
            return;
        }

        var template = instance.groups[3];
        if (template.transform.childCount != 4)
        {
            LogLayoutError("The four-slot UI template has an unexpected hierarchy.");
            return;
        }
        for (int i = 0; i < 4; i++)
        {
            if (template.transform.GetChild(i).GetComponent<UI_InGame_GemSlot>() == null)
            {
                LogLayoutError("A slot component is missing from the UI template.");
                return;
            }
        }

        int oldLength = instance.groups.Length;
        var groups = new GameObject[Constant.MaxGemCount];
        Array.Copy(instance.groups, groups, oldLength);
        try
        {
            for (int i = oldLength; i < groups.Length; i++)
            {
                var clone = UnityEngine.Object.Instantiate(template, instance.transform, false);
                groups[i] = clone;
                // Vanilla fills activeGemSlots only when activating an inactive group.
                clone.SetActive(false);
                clone.name = $"DewGemSlotCount_{i + 1}";
                var group = clone.transform;
                while (group.childCount < i + 1)
                {
                    UnityEngine.Object.Instantiate(template.transform.GetChild(0).gameObject, group, false);
                }
                for (int slot = 0; slot < group.childCount; slot++)
                {
                    group.GetChild(slot).GetComponent<UI_InGame_GemSlot>().slotIndex = slot;
                }
                SetupDualLineLayout(group, i + 1);
            }
        }
        catch (Exception ex)
        {
            for (int i = oldLength; i < groups.Length; i++)
            {
                if (groups[i] != null) UnityEngine.Object.Destroy(groups[i]);
            }
            LogLayoutError(ex.ToString());
            return;
        }

        instance.groups = groups;
        // Also recover if the count was cached before the groups were extended.
        LastAppliedGemCountField?.SetValue(instance, int.MinValue);
    }

    private static void LogLayoutError(string message)
    {
        if (_hasLoggedLayoutError) return;
        _hasLoggedLayoutError = true;
        Debug.LogWarning("[DewGemSlotCount] Cannot extend gem slot UI: " + message);
    }

    private static void SetupDualLineLayout(Transform group, int totalSlots)
    {
        int split = (totalSlots + 1) / 2;
        float spacing = 50f * (1f - totalSlots * 0.02f);
        ArrangeLine(group, 0, split, DualLineDownY, spacing);
        ArrangeLine(group, split, totalSlots - split, DualLineUpY, spacing);
    }

    private static void ArrangeLine(Transform group, int startIndex, int slotCount, float yPos, float spacing)
    {
        float startX = -(slotCount - 1) * spacing / 2f;
        for (int i = 0; i < slotCount; i++)
        {
            group.GetChild(startIndex + i).localPosition = new Vector3(startX + i * spacing, yPos, 0f);
        }
    }

    private static void SmoothMoveY(GameObject group, int startIndex, int endIndex, float value, float duration)
    {
        for (int i = startIndex; i < endIndex; i++)
        {
            var child = group.transform.GetChild(i);
            child.DOKill(complete: true);
            child.DOLocalMoveY(value, duration).SetUpdate(isIndependentUpdate: true);
        }
    }
}
