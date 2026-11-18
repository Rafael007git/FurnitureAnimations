using System;
using System.Collections.Generic;
using UnityEngine;

namespace FurnitureAnimationsMod
{
    public static class FurnitureAnimAPI
    {
        public static bool HasCustomAnimations(Furniture furniture)
        {
            if (furniture == null) return false;
            string furnitureName = furniture.name.Replace("(Clone)", "").Trim();
            return ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config) &&
                   config != null &&
                   config.InteractionPoses != null &&
                   config.InteractionPoses.Count > 0;
        }

        public static void PlayRandomPoseForCompanion(Furniture furniture, CharacterCustomization companion)
        {
            if (furniture == null || companion == null) return;

            try
            {
                if (furniture.poses != null && furniture.poses.items != null && furniture.poses.items.Count > 0)
                {
                    int targetIdx = 0;
                    Transform poseTrans = furniture.poses.items[targetIdx] as Transform;
                    Pose chosenPose = poseTrans != null ? poseTrans.GetComponent<Pose>() : null;

                    if (chosenPose != null)
                    {
                        furniture.DoPose(chosenPose);

                        string ctrlName = chosenPose.controller != null ? chosenPose.controller.name : "UnarmedController";
                        PoseIconClickDioramaPatch.ProcessPoseClick(companion, furniture, chosenPose.name, ctrlName);

                        var existingAudio = companion.gameObject.GetComponent<AnimationAudioManager>();
                        if (existingAudio == null)
                        {
                            string cleanFurnName = furniture.name.Replace("(Clone)", "").Trim();
                            if (ConfigManager.LoadedConfigs.TryGetValue(cleanFurnName, out FurnitureConfig furnCfg) &&
                                furnCfg != null && furnCfg.InteractionPoses != null)
                            {
                                PoseData matchedPose = furnCfg.InteractionPoses.Find(p =>
                                    p != null && (p.DisplayName == chosenPose.name || p.ControllerName == chosenPose.name));

                                if (matchedPose != null && !string.IsNullOrEmpty(matchedPose.ControllerName))
                                {
                                    var audioMgr = companion.gameObject.AddComponent<AnimationAudioManager>();
                                    audioMgr.Initialize(matchedPose.ControllerName, true, furniture);
                                }
                            }
                        }
                        return;
                    }
                }

                string furnitureName = furniture.name.Replace("(Clone)", "").Trim();
                if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config) &&
                    config.InteractionPoses != null && config.InteractionPoses.Count > 0)
                {
                    PoseData poseData = config.InteractionPoses[0];
                    if (poseData != null)
                    {
                        PoseIconClickDioramaPatch.ProcessPoseClick(companion, furniture, poseData.DisplayName, "UnarmedController");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[FurnitureAnimAPI] Failed to start pose/animation for companion '{companion.name}': {ex.Message}");
            }
        }

        public static void StopCompanionAnimation(CharacterCustomization companion)
        {
            if (companion == null) return;

            try
            {
                var animPlayer = companion.gameObject.GetComponent<FurnitureAnimationPlayer>();
                if (animPlayer != null)
                {
                    UnityEngine.Object.DestroyImmediate(animPlayer);
                }

                var audioMgr = companion.gameObject.GetComponent<AnimationAudioManager>();
                if (audioMgr != null)
                {
                    audioMgr.StopAudio();
                    UnityEngine.Object.DestroyImmediate(audioMgr);
                }

                if (companion.anim != null)
                {
                    companion.anim.enabled = true;
                    companion.anim.speed = 1f;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[FurnitureAnimAPI] Failed to stop animation for companion '{companion.name}': {ex.Message}");
            }
        }

        public static string GetActiveAnimationName(CharacterCustomization companion)
        {
            if (companion == null) return null;
            var animPlayer = companion.gameObject.GetComponent<FurnitureAnimationPlayer>();
            return animPlayer != null ? animPlayer.GetPlayingAnimationName() : null;
        }

        public static float GetRemainingAudioTime(CharacterCustomization companion)
        {
            if (companion == null) return 0f;
            AnimationAudioManager audioMgr = companion.GetComponent<AnimationAudioManager>();
            return audioMgr != null ? audioMgr.GetRemainingTrackTime() : 0f;
        }

        // =========================================================================
        // КОНТРАКТ ДЛЯ РЕДАКТОРА ANIMATION STUDIO
        // =========================================================================

        public static void StartPreview(CharacterCustomization character, PoseAnimationData data, float speed = 1.0f)
        {
            if (character == null || data == null) return;

            var existingPlayer = character.gameObject.GetComponent<FurnitureAnimationPlayer>();
            if (existingPlayer != null)
            {
                UnityEngine.Object.DestroyImmediate(existingPlayer);
            }

            var player = character.gameObject.AddComponent<FurnitureAnimationPlayer>();
            player.PlayPreview(character, data, speed);
        }

        public static void StopPreview(CharacterCustomization character)
        {
            if (character == null) return;

            var animPlayer = character.gameObject.GetComponent<FurnitureAnimationPlayer>();
            if (animPlayer != null)
            {
                UnityEngine.Object.DestroyImmediate(animPlayer);
            }

            // В режиме редактора Free Pose оставляем аниматор усыпленным, чтобы не сбить гизмо-позу
            if (character.anim != null)
            {
                character.anim.applyRootMotion = false;
                character.anim.speed = 0f;
                character.anim.enabled = false;
            }
        }

        public static void SnapCharacterToPose(CharacterCustomization character, Dictionary<string, BoneDelta> poseSnapshot)
        {
            if (character == null) return;
            StopPreview(character);
            FurnitureAnimationPlayer.ApplyStaticPose(character, poseSnapshot);
        }
    }
}