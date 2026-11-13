using System;
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
                    int randomIdx = UnityEngine.Random.Range(0, furniture.poses.items.Count);
                    Transform poseTrans = furniture.poses.items[randomIdx] as Transform;
                    Pose chosenPose = poseTrans != null ? poseTrans.GetComponent<Pose>() : null;

                    if (chosenPose != null)
                    {
                        furniture.DoPose(chosenPose);

                        string ctrlName = chosenPose.controller != null ? chosenPose.controller.name : "UnarmedController";
                        PoseIconClickDioramaPatch.ProcessPoseClick(companion, furniture, chosenPose.name, ctrlName);
                        return;
                    }
                }

                string furnitureName = furniture.name.Replace("(Clone)", "").Trim();
                if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config) &&
                    config.InteractionPoses != null && config.InteractionPoses.Count > 0)
                {
                    PoseData poseData = config.InteractionPoses[UnityEngine.Random.Range(0, config.InteractionPoses.Count)];
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
    }
}
