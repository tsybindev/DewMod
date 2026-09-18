using HarmonyLib;
using UnityEngine;

namespace DewGemSlotCount.patch;

// Vanilla gamepad navigation cycles skills Q..Identity (0..4): both
// TryGetNextRelevant* methods stop/wrap at skill 4, so Movement (5) skill and
// gem cells are unreachable from a gamepad even when the dash holds gem slots.
//
// Worse, the vanilla methods never actually WALK: they advance blindly without
// checking selectability mid-loop and can only ever return the start slot (or
// false). As a result dpad Left/Right/Up/Down cannot move gem selection at all.
// Both methods are reimplemented here as genuine walks: every stepped-to slot
// is selectability-checked and the first selectable one is returned, with the
// bound widened to Movement. Skills without slots are skipped by those same
// checks, so behaviour only changes where vanilla was stuck.
[HarmonyPatch(typeof(EditSkillManager))]
public static class EditSkillManager_Patch
{
    private const int MaxSkillValue = (int)HeroSkillLocation.Movement;
    private const int MaxWalkSteps = 512;

    [HarmonyPrefix]
    [HarmonyPatch("TryGetNextRelevantGemSlot")]
    public static bool TryGetNextRelevantGemSlot_Prefix(
        EditSkillManager __instance, bool next, bool skipStart, bool canWrap,
        out GemLocation loc, ref bool __result)
    {
        loc = default;
        try
        {
            return WidenedGemSlot(__instance, next, skipStart, canWrap, out loc, ref __result);
        }
        catch (System.Exception ex)
        {
            // Never break vanilla navigation: fall back to the original method.
            Debug.LogWarning("[DewGemSlotCount] Gem nav fallback to vanilla: " + ex.Message);
            loc = default;
            return true;
        }
    }

    private static bool WidenedGemSlot(
        EditSkillManager __instance, bool next, bool skipStart, bool canWrap,
        out GemLocation loc, ref bool __result)
    {
        loc = default;
        var skill = DewPlayer.local != null && DewPlayer.local.hero != null
            ? DewPlayer.local.hero.Skill
            : null;
        if (__instance == null || skill == null)
        {
            __result = false;
            return false;
        }

        GemLocation start = __instance.selectedGemSlot ?? default;
        GemLocation cur = start;
        bool first = true;
        int steps = 0;
        while (true)
        {
            if (++steps > MaxWalkSteps)
            {
                __result = false;
                return false;
            }
            if (first)
            {
                if (!skipStart && __instance.IsSlotSelectable(cur))
                {
                    loc = cur;
                    __result = true;
                    return false;
                }
                first = false;
            }

            if (next)
            {
                cur.index++;
                if (cur.index >= skill.GetMaxGemCount(cur.skill))
                {
                    cur.index = 0;
                    if ((int)cur.skill >= MaxSkillValue)
                    {
                        if (!canWrap)
                        {
                            __result = false;
                            return false;
                        }
                        cur.skill = (HeroSkillLocation)0;
                    }
                    else
                    {
                        cur.skill = (HeroSkillLocation)((int)cur.skill + 1);
                    }
                }
            }
            else
            {
                cur.index--;
                if (cur.index < 0)
                {
                    if ((int)cur.skill <= 0)
                    {
                        if (!canWrap)
                        {
                            __result = false;
                            return false;
                        }
                        cur.skill = (HeroSkillLocation)MaxSkillValue;
                    }
                    else
                    {
                        cur.skill = (HeroSkillLocation)((int)cur.skill - 1);
                    }
                    cur.index = skill.GetMaxGemCount(cur.skill) - 1;
                }
            }

            // Genuine walk: return the first selectable slot stepped onto.
            if (__instance.IsSlotSelectable(cur))
            {
                loc = cur;
                __result = true;
                return false;
            }

            if (cur == start)
            {
                __result = false;
                return false;
            }
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch("TryGetNextRelevantSkillLocation")]
    public static bool TryGetNextRelevantSkillLocation_Prefix(
        EditSkillManager __instance, bool next, bool skipStart, bool canWrap,
        out HeroSkillLocation loc, ref bool __result)
    {
        loc = default;
        try
        {
            return WidenedSkillLocation(__instance, next, skipStart, canWrap, out loc, ref __result);
        }
        catch (System.Exception ex)
        {
            // Never break vanilla navigation: fall back to the original method.
            Debug.LogWarning("[DewGemSlotCount] Skill nav fallback to vanilla: " + ex.Message);
            loc = default;
            return true;
        }
    }

    private static bool WidenedSkillLocation(
        EditSkillManager __instance, bool next, bool skipStart, bool canWrap,
        out HeroSkillLocation loc, ref bool __result)
    {
        loc = default;
        if (__instance == null || DewPlayer.local == null || DewPlayer.local.hero == null)
        {
            __result = false;
            return false;
        }

        HeroSkillLocation start = __instance.selectedSkillSlot ?? default;
        HeroSkillLocation cur = start;
        bool first = true;
        int steps = 0;
        while (true)
        {
            if (++steps > MaxWalkSteps)
            {
                __result = false;
                return false;
            }
            if (first)
            {
                if (!skipStart && __instance.IsSlotSelectable(cur))
                {
                    loc = cur;
                    __result = true;
                    return false;
                }
                first = false;
            }

            if (next)
            {
                if ((int)cur >= MaxSkillValue)
                {
                    if (!canWrap)
                    {
                        __result = false;
                        return false;
                    }
                    cur = (HeroSkillLocation)0;
                }
                else
                {
                    cur = (HeroSkillLocation)((int)cur + 1);
                }
            }
            else
            {
                if ((int)cur <= 0)
                {
                    if (!canWrap)
                    {
                        __result = false;
                        return false;
                    }
                    cur = (HeroSkillLocation)MaxSkillValue;
                }
                else
                {
                    cur = (HeroSkillLocation)((int)cur - 1);
                }
            }

            // Genuine walk: return the first selectable skill stepped onto.
            if (__instance.IsSlotSelectable(cur))
            {
                loc = cur;
                __result = true;
                return false;
            }

            if (cur == start)
            {
                __result = false;
                return false;
            }
        }
    }
}
