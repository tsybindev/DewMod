using System.Collections.Generic;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace DewGemSlotCount.patch
{
    [HarmonyPatch(typeof(UI_InGame_SkillButtons))]
    public class UI_InGame_SkillButtons_Patch
    {
        // Dash (Movement) clearance: enlarged essence panels of Q/W/E/R overlap the
        // dash button while editing (Ctrl). Vanilla hides some HUD elements in edit
        // mode, but the dash must stay visible and clickable when gem counts above
        // four are configured. Lift it above the essence rows and draw its whole
        // container above the QWER containers on edit, restore after.
        // The lift is intentionally generous and the dash's own gem rows are packed
        // tight (see GemGroup patch): its cells must hug the dash instead of
        // spilling over the neighbouring memory rows.
        private const float MovementLiftY = 560f;
        private const int MovementFrontSortingOrder = 100;

        private static readonly System.Reflection.FieldInfo SkillTypeField =
            AccessTools.Field(typeof(UI_InGame_SkillButton), "skillType");

        private static readonly Dictionary<RectTransform, Vector2> OriginalPositions = new();
        private static readonly Dictionary<Transform, FrontCanvasState> FrontCanvasStates = new();

        private sealed class FrontCanvasState
        {
            public Canvas canvas;
            public bool added;
            public bool prevOverrideSorting;
            public int prevSortingOrder;
        }

        [HarmonyPatch("OnStateChanged")]
        [HarmonyPostfix]
        public static void OnStateChanged_Postfix(UI_InGame_SkillButtons __instance, EditSkillManager.ModeType mode)
        {
            // Leave v1.4's animation, fading, raycasts and gamepad focus intact.
            // Only adjust the expanded layout; vanilla restores it on exit.
            if (__instance != null && DewGemSlotCount.Instance != null &&
                DewGemSlotCount.Instance.Config.OptimizeUI &&
                mode != EditSkillManager.ModeType.None && __instance.expandedLayouts != null)
            {
                foreach (var layout in __instance.expandedLayouts)
                {
                    if (layout != null) layout.localScale = Vector3.one * 1.9f;
                }
            }

            // Functional (not cosmetic): keep the dash editable. Runs even with OptimizeUI off.
            EnsureMovementClearance(__instance, mode);
        }

        private static void EnsureMovementClearance(UI_InGame_SkillButtons instance, EditSkillManager.ModeType mode)
        {
            if (instance == null || instance.skillButtons == null || SkillTypeField == null) return;

            bool editing = mode != EditSkillManager.ModeType.None;

            foreach (var button in instance.skillButtons)
            {
                if (button == null) continue;
                HeroSkillLocation type;
                try
                {
                    type = (HeroSkillLocation)SkillTypeField.GetValue(button);
                }
                catch
                {
                    continue;
                }
                if (type != HeroSkillLocation.Movement) continue;

                if (editing && NeedsClearance())
                {
                    LiftMovement(instance, button);
                }
                else if (!editing)
                {
                    RestoreMovement(instance, button);
                }
            }
        }

        private static void LiftMovement(UI_InGame_SkillButtons instance, UI_InGame_SkillButton button)
        {
            // Lift the whole "M" container (button AND its gem group move together),
            // not just the button: the gem cells may live outside the button transform.
            var anchor = button.transform != null ? button.transform.parent as RectTransform : null;
            if (anchor == null) return;

            if (!OriginalPositions.TryGetValue(anchor, out var orig))
            {
                OriginalPositions[anchor] = anchor.anchoredPosition;
                orig = anchor.anchoredPosition;
            }

            // Vanilla fades hiddenWhenExpanded groups to alpha 0 in edit mode, and the
            // faded group can be an ANCESTOR of the dash button (not the button itself).
            // Un-fade every containing hidden group and kill its running fade tween,
            // otherwise a one-time alpha assignment loses to the still-running tween.
            foreach (var cg in button.GetComponentsInParent<CanvasGroup>())
            {
                if (cg == null || !IsHiddenWhenExpanded(instance, cg)) continue;
                cg.DOKill();
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            anchor.DOKill();
            anchor.DOAnchorPos(orig + Vector2.up * MovementLiftY, instance.hiddenWhenExpandedDuration)
                .SetUpdate(isIndependentUpdate: true);
            BringContainerToFront(instance, button);
        }

        private static void RestoreMovement(UI_InGame_SkillButtons instance, UI_InGame_SkillButton button)
        {
            var anchor = button.transform != null ? button.transform.parent as RectTransform : null;
            if (anchor == null || !OriginalPositions.TryGetValue(anchor, out var orig)) return;

            // Vanilla's own exit transition re-fades hidden groups back to alpha 1.
            anchor.DOKill();
            anchor.DOAnchorPos(orig, instance.hiddenWhenExpandedDuration)
                .SetUpdate(isIndependentUpdate: true);
            SendContainerToBack(instance, button);
        }

        // The dash button is nested (Skill Button < M < Movement), so reordering the
        // button within its own tiny parent cannot lift it above the QWER containers:
        // sibling order only matters between children of the same parent. Instead draw
        // the whole top-level Movement container above its siblings with an override
        // sorting Canvas. This changes render/raycast priority without touching the
        // HorizontalLayoutGroup order, so nothing jumps sideways.
        private static void BringContainerToFront(UI_InGame_SkillButtons instance, UI_InGame_SkillButton button)
        {
            var container = FindTopChild(instance.transform, button.transform);
            if (container == null) return;
            if (!FrontCanvasStates.TryGetValue(container, out var state) || state == null || state.canvas == null)
            {
                var canvas = container.GetComponent<Canvas>();
                state = new FrontCanvasState { canvas = canvas };
                if (canvas == null)
                {
                    canvas = container.gameObject.AddComponent<Canvas>();
                    state.canvas = canvas;
                    state.added = true;
                }
                else
                {
                    state.added = false;
                    state.prevOverrideSorting = canvas.overrideSorting;
                    state.prevSortingOrder = canvas.sortingOrder;
                }
                FrontCanvasStates[container] = state;
            }
            state.canvas.overrideSorting = true;
            state.canvas.sortingOrder = MovementFrontSortingOrder;
        }

        private static void SendContainerToBack(UI_InGame_SkillButtons instance, UI_InGame_SkillButton button)
        {
            var container = FindTopChild(instance.transform, button.transform);
            if (container == null) return;
            if (!FrontCanvasStates.TryGetValue(container, out var state) || state == null) return;
            FrontCanvasStates.Remove(container);
            if (state.canvas == null) return;
            if (state.added)
            {
                UnityEngine.Object.Destroy(state.canvas);
            }
            else
            {
                state.canvas.overrideSorting = state.prevOverrideSorting;
                state.canvas.sortingOrder = state.prevSortingOrder;
            }
        }

        private static Transform FindTopChild(Transform root, Transform leaf)
        {
            if (root == null || leaf == null || leaf == root) return null;
            Transform result = null;
            var current = leaf;
            while (current != null && current != root)
            {
                result = current;
                current = current.parent;
            }
            return current == root ? result : null;
        }

        private static bool IsHiddenWhenExpanded(UI_InGame_SkillButtons instance, CanvasGroup cg)
        {
            if (instance.hiddenWhenExpanded == null) return false;
            foreach (var hidden in instance.hiddenWhenExpanded)
            {
                if (hidden != null && hidden == cg) return true;
            }
            return false;
        }

        private static bool NeedsClearance()
        {
            // Enlarged two-row panels only exist when some skill holds more than four gems.
            // With vanilla-sized panels nothing overlaps, so vanilla layout is preserved.
            try
            {
                var hero = DewPlayer.local != null ? DewPlayer.local.hero : null;
                var skill = hero != null ? hero.Skill : null;
                if (skill == null) return false;
                return skill.GetMaxGemCount(HeroSkillLocation.Q) > 4
                    || skill.GetMaxGemCount(HeroSkillLocation.W) > 4
                    || skill.GetMaxGemCount(HeroSkillLocation.E) > 4
                    || skill.GetMaxGemCount(HeroSkillLocation.R) > 4
                    || skill.GetMaxGemCount(HeroSkillLocation.Identity) > 4
                    || skill.GetMaxGemCount(HeroSkillLocation.Movement) > 4;
            }
            catch
            {
                return false;
            }
        }
    }
}
