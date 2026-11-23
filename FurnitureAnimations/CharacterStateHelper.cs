using System.Collections.Generic;
using UnityEngine;
using RuntimeGizmos;

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
        private static CharacterCustomization _cachedChar;
        private static FurnitureAnimationPlayer _cachedPlayer;

        // Запоминаем имя последней запущенной JSON-анимации для персонажа (чтобы кнопка работала и на паузе)
        private static readonly Dictionary<CharacterCustomization, string> _lastAnimatedClipByChar =
            new Dictionary<CharacterCustomization, string>();

        private static FurnitureAnimationPlayer GetCachedPlayer(CharacterCustomization character)
        {
            if (character == null) return null;

            if (_cachedChar != character || _cachedPlayer == null)
            {
                _cachedChar = character;
                _cachedPlayer = character.GetComponent<FurnitureAnimationPlayer>();
            }
            return _cachedPlayer;
        }

        public static void SetLastAnimatedClip(CharacterCustomization character, string clipName)
        {
            if (character != null && !string.IsNullOrEmpty(clipName))
                _lastAnimatedClipByChar[character] = clipName;
        }

        public static void ClearCachedAnimation(CharacterCustomization character)
        {
            if (character != null)
                _lastAnimatedClipByChar.Remove(character);
        }

        public static CharacterPoseState GetCurrentState(CharacterCustomization character)
        {
            if (character == null || character.anim == null)
                return CharacterPoseState.CustomPoseJSON;

            // 1. Активный плеер в реальном времени
            var player = GetCachedPlayer(character);
            if (player != null)
            {
                string playingName = player.GetPlayingAnimationName();
                if (!string.IsNullOrEmpty(playingName))
                {
                    _lastAnimatedClipByChar[character] = playingName;
                    return CharacterPoseState.PoseAnimationsModActive;
                }
            }

            // 2. Проверяем, не включена ли ванильная поза игры (если да — сбрасываем память о JSON-анимации)
            string currentCtrlName = (character.anim.runtimeAnimatorController?.name ?? "").ToLower();
            bool isDefaultIdle = currentCtrlName.Contains("idle") ||
                                 currentCtrlName.Contains("unarmed") ||
                                 string.IsNullOrEmpty(currentCtrlName);

            bool isGizmoActive = TransformGizmo.transformGizmo_ != null &&
                                 TransformGizmo.transformGizmo_.runTransformGizmo;

            if (!isDefaultIdle && !currentCtrlName.Contains("custom") && character.anim.enabled && !isGizmoActive)
            {
                _lastAnimatedClipByChar.Remove(character);
                return CharacterPoseState.GameAnimatorActive;
            }

            // 3. Если персонаж стоит на паузе после JSON-анимации (и не включено ручное редактирование гизмо)
            if (!isGizmoActive && _lastAnimatedClipByChar.ContainsKey(character))
            {
                return CharacterPoseState.PoseAnimationsModActive;
            }

            return CharacterPoseState.CustomPoseJSON;
        }

        public static string GetActiveModAnimationName(CharacterCustomization character)
        {
            var player = GetCachedPlayer(character);
            if (player != null)
            {
                string animName = player.GetPlayingAnimationName();
                if (!string.IsNullOrEmpty(animName))
                    return animName;
            }

            if (character != null && _lastAnimatedClipByChar.TryGetValue(character, out string cachedName) && !string.IsNullOrEmpty(cachedName))
            {
                return cachedName;
            }

            return "UnknownModAnimation";
        }
    }
}
