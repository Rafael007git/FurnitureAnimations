using UnityEngine;
using RuntimeGizmos;
using BepInEx.Bootstrap; // Обязательно для Chainloader

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
        // Глобальный флаг: установлен ли мод PoseAnimations у пользователя?
        public static readonly bool IsPoseAnimationsInstalled;

        static CharacterStateHelper()
        {
            // Безопасно проверяем BepInEx на наличие GUID нужного мода
            IsPoseAnimationsInstalled = Chainloader.PluginInfos.ContainsKey("aedenthorn.PoseAnimations");
        }

        public static CharacterPoseState GetCurrentState(CharacterCustomization character)
        {
            if (character == null || character.anim == null)
                return CharacterPoseState.CustomPoseJSON;

            // --- ПРОВЕРКА СОСТОЯНИЯ 3: Внешний JSON плеер ---
            // Вызываем чужой код ТОЛЬКО через изолированный мост, если мод реально установлен
            if (IsPoseAnimationsInstalled && PoseAnimationsBridge.IsModActiveAndPosing(character))
            {
                return CharacterPoseState.PoseAnimationsModActive;
            }

            string currentCtrlName = (character.anim.runtimeAnimatorController?.name ?? "").ToLower();
            bool isGizmoActive = TransformGizmo.transformGizmo_ != null &&
                                 TransformGizmo.transformGizmo_.runTransformGizmo;

            // --- ПРОВЕРКА СОСТОЯНИЯ 2: Ручная сборка (Гизмо) ---
            if (character.anim.enabled == false || currentCtrlName.Contains("custom") || isGizmoActive)
            {
                return CharacterPoseState.CustomPoseJSON;
            }

            // --- ПРОВЕРКА СОСТОЯНИЯ 1: Родной пресет игры ---
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
            // Если мода нет, возвращаем заглушку, чужие типы не трогаем
            if (!IsPoseAnimationsInstalled) return "UnknownModAnimation";
            return PoseAnimationsBridge.GetAnimName(character);
        }
    }

    // Изолированный внутренний класс-мост. 
    // Среда выполнения Mono / .NET никогда не станет компилировать или загружать этот класс в память,
    // пока к нему не обратятся физически из кода выше.
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
    }
}
