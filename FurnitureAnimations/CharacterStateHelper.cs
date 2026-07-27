using System;
using System.Collections.Generic;
using UnityEngine;
using RuntimeGizmos;
using BepInEx.Bootstrap;

namespace FurnitureAnimationsMod
{
    public enum CharacterPoseState
    {
        GameAnimatorActive,
        CustomPoseJSON,
        PoseAnimationsModActive
    }

    public static class CharacterStateHelper
    {
        public static readonly bool IsPoseAnimationsInstalled;

        static CharacterStateHelper()
        {
            IsPoseAnimationsInstalled = Chainloader.PluginInfos.ContainsKey("aedenthorn.PoseAnimations");
        }

        public static CharacterPoseState GetCurrentState(CharacterCustomization character)
        {
            if (character == null || character.anim == null)
                return CharacterPoseState.CustomPoseJSON;

            if (IsPoseAnimationsInstalled && PoseAnimationsBridge.IsModActiveAndPosing(character))
            {
                return CharacterPoseState.PoseAnimationsModActive;
            }

            string currentCtrlName = (character.anim.runtimeAnimatorController?.name ?? "").ToLower();
            bool isGizmoActive = TransformGizmo.transformGizmo_ != null &&
                                 TransformGizmo.transformGizmo_.runTransformGizmo;

            if (character.anim.enabled == false || currentCtrlName.Contains("custom") || isGizmoActive)
            {
                return CharacterPoseState.CustomPoseJSON;
            }

            bool isDefaultIdle = currentCtrlName.Contains("idle") ||
                                 currentCtrlName.Contains("unarmed") ||
                                 string.IsNullOrEmpty(currentCtrlName);

            if (!isDefaultIdle)
            {
                return CharacterPoseState.GameAnimatorActive;
            }

            return CharacterPoseState.CustomPoseJSON;
        }

        public static string GetActiveModAnimationName(CharacterCustomization character)
        {
            if (!IsPoseAnimationsInstalled) return "UnknownModAnimation";
            return PoseAnimationsBridge.GetAnimName(character);
        }
    }

    internal static class PoseAnimationsBridge
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static bool IsModActiveAndPosing(CharacterCustomization character)
        {
            try
            {
                if (PoseAnimations.BepInExPlugin.modEnabled != null &&
                    PoseAnimations.BepInExPlugin.modEnabled.Value &&
                    PoseAnimations.BepInExPlugin.started)
                {
                    if (PoseAnimations.BepInExPlugin.currentlyPosing != null &&
                        PoseAnimations.BepInExPlugin.currentlyPosing.TryGetValue(character, out var instance))
                    {
                        return instance != null && instance.data != null;
                    }
                }
            }
            catch { }
            return false;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static string GetAnimName(CharacterCustomization character)
        {
            try
            {
                if (PoseAnimations.BepInExPlugin.currentlyPosing != null &&
                    PoseAnimations.BepInExPlugin.currentlyPosing.TryGetValue(character, out var instance))
                {
                    return instance?.data?.name ?? "UnknownModAnimation";
                }
            }
            catch { }
            return "UnknownModAnimation";
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void PlayExternalAnimation(CharacterCustomization character, string animationName)
        {
            try
            {
                // 1. Проверяем, существует ли вообще такая анимация в базе данных мода (строка 675)
                if (PoseAnimations.BepInExPlugin.animationDict != null &&
                    PoseAnimations.BepInExPlugin.animationDict.ContainsKey(animationName))
                {
                    Plugin.Log.LogInfo($"[PoseBridge] Анимация '{animationName}' верифицирована. Ищем её игровой объект-кнопку...");

                    // 2. Ищем сгенерированный автором трансформ позы в глобальном реестре игры (строка 355)
                    if (RM.code != null && RM.code.allFreePoses != null)
                    {
                        Transform targetPoseTransform = null;
                        foreach (Transform t in RM.code.allFreePoses.items)
                        {
                            if (t != null && t.name == animationName)
                            {
                                targetPoseTransform = t;
                                break;
                            }
                        }

                        // 3. Эмулируем клик игрока для бесшовного запуска! (строка 359)
                        if (targetPoseTransform != null)
                        {
                            Plugin.Log.LogWarning($"[PoseBridge] Инициализируем принудительный нативный старт анимации через PoseButtonClicked!");
                            PoseAnimations.BepInExPlugin.PoseButtonClicked(targetPoseTransform);
                        }
                        else
                        {
                            Plugin.Log.LogError($"[PoseBridge] Сбой: Объект позы '{animationName}' не зарегистрирован в RM.code.allFreePoses!");
                        }
                    }
                }
                else
                {
                    Plugin.Log.LogError($"[PoseBridge] Сбой: Анимация '{animationName}' отсутствует в animationDict мода!");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseBridge] Критический краш при вызове PoseButtonClicked: {ex.Message}");
            }
        }
    }
}
