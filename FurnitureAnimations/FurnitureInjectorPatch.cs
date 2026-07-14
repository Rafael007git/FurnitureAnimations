using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace FurnitureAnimationsMod
{
    [HarmonyPatch(typeof(Furniture), "Start")]
    public class FurnitureInjectorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Furniture __instance)
        {
            if (__instance == null) return;

            // Защита от бесконечного цикла рекурсии Unity
            if (__instance.transform.Find("camerasGroup") != null) return;

            // Отрезаем "(Clone)", чтобы получить чистый GUID префаба
            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            // === ВАЖНАЯ ДИАГНОСТИКА ===
            // Если объект не имеет поз, выводим его реальное имя в консоль, чтобы поймать нужный GUID
            if (__instance.poses.items.Count == 0)
            {
                Plugin.Log.LogWarning($"[GUID_Finder] Обнаружена пустая мебель на сцене! Имя/GUID в памяти: \"{furnitureName}\"");
            }

            // Проверяем, есть ли этот GUID в нашей базе LoadedConfigs
            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                Plugin.Log.LogWarning($"[Injector] ИДЕАЛЬНОЕ СОВПАДЕНИЕ! Оживляем префаб: {furnitureName}");

                if (__instance.camerasGroup == null)
                {
                    __instance.camerasGroup = new GameObject("camerasGroup").transform;
                    __instance.camerasGroup.SetParent(__instance.transform, false);
                }
                if (__instance.posesGroup == null)
                {
                    __instance.posesGroup = new GameObject("posesGroup").transform;
                    __instance.posesGroup.SetParent(__instance.transform, false);
                }

                Dictionary<string, RuntimeAnimatorController> controllerCache = new Dictionary<string, RuntimeAnimatorController>();

                foreach (PoseData poseConfig in config.InteractionPoses)
                {
                    if (poseConfig.Type.Equals("CustomJSON", System.StringComparison.OrdinalIgnoreCase)) continue;

                    if (!controllerCache.TryGetValue(poseConfig.ControllerName, out RuntimeAnimatorController targetController))
                    {
                        RuntimeAnimatorController[] allControllers = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();
                        foreach (var rc in allControllers)
                        {
                            if (rc.name == poseConfig.ControllerName)
                            {
                                targetController = rc;
                                controllerCache[poseConfig.ControllerName] = rc;
                                break;
                            }
                        }
                    }

                    if (targetController == null)
                    {
                        Plugin.Log.LogError($"[Injector] Не найден контроллер: {poseConfig.ControllerName}");
                        continue;
                    }

                    GameObject newPoseObj = new GameObject(poseConfig.DisplayName);
                    newPoseObj.transform.SetParent(__instance.posesGroup, false);

                    Pose newPose = newPoseObj.AddComponent<Pose>();
                    newPose.categoryName = "ARtClub Dances";
                    newPose.controller = targetController;

                    GameObject locObj = new GameObject("loc");
                    locObj.transform.SetParent(newPoseObj.transform, false);
                    locObj.transform.localPosition = new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z);
                    locObj.transform.localEulerAngles = new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z);
                    newPose.loc = locObj.transform;

                    if (poseConfig.Cameras != null)
                    {
                        foreach (CameraData camConfig in poseConfig.Cameras)
                        {
                            GameObject camObj = new GameObject(camConfig.Name);
                            camObj.transform.SetParent(__instance.camerasGroup, false);
                            camObj.transform.localPosition = new Vector3(camConfig.pos.x, camConfig.pos.y, camConfig.pos.z);
                            camObj.transform.localEulerAngles = new Vector3(camConfig.rot.x, camConfig.rot.y, camConfig.rot.z);

                            camObj.SetActive(false);
                            __instance.cameras.AddItem(camObj.transform);
                        }
                    }

                    newPoseObj.SetActive(false);
                    __instance.poses.AddItem(newPoseObj.transform);
                }

                if (!Global.code.interactableFurnitures.items.Contains(__instance.transform))
                {
                    Global.code.interactableFurnitures.AddItemDifferentObject(__instance.transform);
                }

                Plugin.Log.LogWarning($"[Injector] Мебель {furnitureName} успешно оживлена! Добавлено поз: {__instance.poses.items.Count}");
            }
        }
    }
}
