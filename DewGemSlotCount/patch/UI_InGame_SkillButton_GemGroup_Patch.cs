using System;
using System.Collections.Generic;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace DewGemSlotCount.patch;

[HarmonyPatch(typeof(UI_InGame_SkillButton_GemGroup))]
public static class UI_InGame_SkillButton_GemGroup_Patch
{
    private const float DualLineUpY = 100f;
    private const float DualLineDownY = -100f;

    // The dash button is lifted high above the memory rows while editing, so its
    // own gem rows use a packed layout: cells hug the dash instead of spilling
    // over the neighbouring rows.
    private const float CompactLineUpY = 110f;
    private const float CompactLineDownY = -110f;

    private static readonly System.Reflection.FieldInfo LastAppliedGemCountField =
        AccessTools.Field(typeof(UI_InGame_SkillButton_GemGroup), "_lastAppliedGemCount");

    private static readonly System.Reflection.FieldInfo ButtonField =
        AccessTools.Field(typeof(UI_InGame_SkillButton_GemGroup), "_button");

    private static readonly System.Reflection.FieldInfo SkillTypeField =
        AccessTools.Field(typeof(UI_InGame_SkillButton), "skillType");

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
            var slots = CollectSlotChildren(group.transform);
            if (slots.Count == 0) continue;
            bool compact = IsMovementGroup(__instance);
            float upY = compact ? CompactLineUpY : DualLineUpY;
            float downY = compact ? CompactLineDownY : DualLineDownY;
            int split = (slots.Count + 1) / 2;
            float inset = mode == EditSkillManager.ModeType.None ? 0f : 20f;
            SmoothMoveSlots(slots, 0, split, downY + inset, __instance.gemGroupAnimDuration);
            SmoothMoveSlots(slots, split, slots.Count, upY - inset,
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
        // Slot-bearing children are detected by component instead of assuming a fixed
        // child index: later UI refactors may reorder or wrap group children, and
        // hard-coding GetChild(0) would then clone the wrong object.
        if (instance.groups.Length < 4 || instance.groups[3] == null)
        {
            LogLayoutError("The four-slot UI template is missing.");
            return;
        }

        if (!TryGetSlotPrefab(instance.groups[3].transform, out var slotPrefab))
        {
            return;
        }

        int oldLength = instance.groups.Length;
        var groups = new GameObject[Constant.MaxGemCount];
        Array.Copy(instance.groups, groups, oldLength);
        try
        {
            for (int i = oldLength; i < groups.Length; i++)
            {
                var clone = UnityEngine.Object.Instantiate(instance.groups[3], instance.transform, false);
                groups[i] = clone;
                // Vanilla fills activeGemSlots only when activating an inactive group.
                clone.SetActive(false);
                clone.name = $"DewGemSlotCount_{i + 1}";
                var group = clone.transform;
                var slots = CollectSlotChildren(group);
                while (slots.Count < i + 1)
                {
                    var obj = UnityEngine.Object.Instantiate(slotPrefab, group, false);
                    var created = obj.GetComponent<UI_InGame_GemSlot>();
                    if (created == null)
                    {
                        throw new InvalidOperationException(
                            "Cloned gem slot has no UI_InGame_GemSlot component.");
                    }
                    slots.Add(obj.transform);
                }
                for (int slot = 0; slot < slots.Count; slot++)
                {
                    slots[slot].GetComponent<UI_InGame_GemSlot>().slotIndex = slot;
                }
                SetupDualLineLayout(slots, i + 1, IsMovementGroup(instance));
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

    private static bool TryGetSlotPrefab(Transform templateGroup, out GameObject slotPrefab)
    {
        slotPrefab = null;
        if (templateGroup == null)
        {
            LogLayoutError("The four-slot UI template is missing.");
            return false;
        }

        var slots = CollectSlotChildren(templateGroup);
        if (slots.Count != 4)
        {
            LogLayoutError(
                $"The four-slot UI template has an unexpected hierarchy (found {slots.Count} gem slots, expected 4).");
            return false;
        }

        // Copy the last slot-bearing child: it is guaranteed to share the vanilla
        // structure even if non-slot children were added around the slots.
        slotPrefab = slots[slots.Count - 1].gameObject;
        return true;
    }

    private static List<Transform> CollectSlotChildren(Transform group)
    {
        var slots = new List<Transform>();
        if (group == null) return slots;
        for (int c = 0; c < group.childCount; c++)
        {
            var child = group.GetChild(c);
            if (child.GetComponent<UI_InGame_GemSlot>() != null)
            {
                slots.Add(child);
            }
        }
        return slots;
    }

    private static bool IsMovementGroup(UI_InGame_SkillButton_GemGroup instance)
    {
        try
        {
            if (ButtonField == null || SkillTypeField == null || instance == null) return false;
            var button = ButtonField.GetValue(instance) as UI_InGame_SkillButton;
            if (button == null) return false;
            return (HeroSkillLocation)SkillTypeField.GetValue(button) == HeroSkillLocation.Movement;
        }
        catch
        {
            return false;
        }
    }

    private static void SetupDualLineLayout(List<Transform> slots, int totalSlots, bool compact = false)
    {
        int split = (totalSlots + 1) / 2;
        float spacing = compact
            ? 38f * (1f - totalSlots * 0.02f)
            : 50f * (1f - totalSlots * 0.02f);
        float upY = compact ? CompactLineUpY : DualLineUpY;
        float downY = compact ? CompactLineDownY : DualLineDownY;
        ArrangeLine(slots, 0, split, downY, spacing);
        ArrangeLine(slots, split, totalSlots - split, upY, spacing);
    }

    private static void ArrangeLine(List<Transform> slots, int startIndex, int slotCount, float yPos, float spacing)
    {
        float startX = -(slotCount - 1) * spacing / 2f;
        for (int i = 0; i < slotCount; i++)
        {
            slots[startIndex + i].localPosition = new Vector3(startX + i * spacing, yPos, 0f);
        }
    }

    private static void SmoothMoveSlots(List<Transform> slots, int startIndex, int endIndex, float value, float duration)
    {
        for (int i = startIndex; i < endIndex && i < slots.Count; i++)
        {
            var child = slots[i];
            child.DOKill(complete: true);
            child.DOLocalMoveY(value, duration).SetUpdate(isIndependentUpdate: true);
        }
    }
}
