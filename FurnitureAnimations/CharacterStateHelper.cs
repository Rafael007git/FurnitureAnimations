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

        // ИСПРАВЛЕНИЕ В CHARACTERSTATEHELPER.CS (внутри класса PoseAnimationsBridge):

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void PlayExternalAnimation(CharacterCustomization character, string animationName)
        {
            try
            {
                // 1. Проверяем наличие анимации в базе данных оригинального мода
                if (PoseAnimations.BepInExPlugin.animationDict != null &&
                    PoseAnimations.BepInExPlugin.animationDict.TryGetValue(animationName, out var animData))
                {
                    Plugin.Log.LogWarning($"[PoseBridge] Запуск анимации '{animationName}' через динамический вызов конструктора...");

                    if (PoseAnimations.BepInExPlugin.currentlyPosing != null)
                    {
                        if (PoseAnimations.BepInExPlugin.currentlyPosing.ContainsKey(character))
                        {
                            PoseAnimations.BepInExPlugin.currentlyPosing.Remove(character);
                        }

                        // 2. ОБХОД ОШИБКИ CS1729 ЧЕРЕЗ АКТИВАТОР:
                        // Динамически создаем объект PoseAnimationInstance в рантайме.
                        // Если декомпилятор ошибся со структурой, активатор сам найдет конструктор (character, animData).
                        object instance = Activator.CreateInstance(typeof(PoseAnimations.PoseAnimationInstance), new object[] { character, animData });

                        if (instance != null)
                        {
                            // Кладим созданный объект в статический словарь оригинального мода
                            PoseAnimations.BepInExPlugin.currentlyPosing[character] = (PoseAnimations.PoseAnimationInstance)instance;
                            Plugin.Log.LogInfo($"[PoseBridge] Персонаж {character.name} успешно запущен через Активатор!");
                        }
                    }
                }
                else
                {
                    Plugin.Log.LogError($"[PoseBridge] Сбой: Анимация '{animationName}' отсутствует в базе мода!");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseBridge] Критический краш создания PoseAnimationInstance: {ex.Message}");
            }
        }

    }
}
