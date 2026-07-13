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

            // ПРОВЕРКА-ЗАЩИТА: Если мы уже обработали эту мебель, выходим!
            // Иначе динамическое создание объектов вызовет бесконечный цикл.
            if (__instance.transform.Find("camerasGroup") != null || __instance.poses.items.Count > 0)
            {
                return;
            }

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            // Проверяем, есть ли GUID или имя этой мебели в нашей базе загруженных JSON-конфигов
            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                Plugin.Log.LogWarning($"[Injector] Найдена модовая мебель из справочника: {furnitureName}. Начинаем инжекцию поз...");

                // 1. Создаем группы камер и поз, если их нет
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

                // Временный кэш контроллеров в памяти, чтобы не искать их по кругу
                Dictionary<string, RuntimeAnimatorController> controllerCache = new Dictionary<string, RuntimeAnimatorController>();

                // 2. Инжектируем каждую позу из конфига
                foreach (PoseData poseConfig in config.InteractionPoses)
                {
                    // Пропускаем позы, которые мы еще не научились читать на Шаге 3 (кастомные JSON)
                    if (poseConfig.Type.Equals("CustomJSON", System.StringComparison.OrdinalIgnoreCase))
                    {
                        Plugin.Log.LogWarning($"[Injector] Поза '{poseConfig.DisplayName}' имеет тип CustomJSON. Поддержка будет добавлена на следующем шаге разработки.");
                        continue;
                    }

                    // Ищем оригинальный контроллер анимации в памяти игры (например, "Pole Dance4")
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
                        Plugin.Log.LogError($"[Injector] Не удалось найти в памяти игры ванильный контроллер: {poseConfig.ControllerName}");
                        continue;
                    }

                    // Создаем игровой объект позы
                    GameObject newPoseObj = new GameObject(poseConfig.DisplayName);
                    newPoseObj.transform.SetParent(__instance.posesGroup, false);

                    Pose newPose = newPoseObj.AddComponent<Pose>();
                    newPose.categoryName = "ARtClub Dances";
                    newPose.controller = targetController;

                    // Настраиваем точку посадки (loc) персонажа
                    GameObject locObj = new GameObject("loc");
                    locObj.transform.SetParent(newPoseObj.transform, false);
                    locObj.transform.localPosition = new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z);
                    locObj.transform.localEulerAngles = new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z);
                    newPose.loc = locObj.transform;

                    // 3. Инжектируем камеры облета для этой позы
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

                // Принудительно регистрируем мебель в общем списке интерактива игры
                if (!Global.code.interactableFurnitures.items.Contains(__instance.transform))
                {
                    Global.code.interactableFurnitures.AddItemDifferentObject(__instance.transform);
                }

                Plugin.Log.LogWarning($"[Injector] Мебель {furnitureName} успешно оживлена! Добавлено поз: {__instance.poses.items.Count}, камер: {__instance.cameras.items.Count}");
            }
        }
    }
}
