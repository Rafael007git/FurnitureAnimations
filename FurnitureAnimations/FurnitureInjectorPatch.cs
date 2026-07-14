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

            // Защита от бесконечного цикла
            if (__instance.transform.Find("camerasGroup") != null) return;

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            // Проверяем, есть ли GUID предмета в нашей базе JSON-справочников
            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                Plugin.Log.LogWarning($"[Injector] Оживляем мебель из клуба: {furnitureName}");

                // Создаем правильную структуру контейнеров
                GameObject camGroupObj = new GameObject("camerasGroup");
                camGroupObj.transform.SetParent(__instance.transform, false);
                __instance.camerasGroup = camGroupObj.transform;

                GameObject poseGroupObj = new GameObject("posesGroup");
                poseGroupObj.transform.SetParent(__instance.transform, false);
                __instance.posesGroup = poseGroupObj.transform;

                // Нам нужен ванильный донор позы, чтобы скопировать иконку для интерфейса
                Pose vanillaPoseDonor = null;
                Pose[] allGamePoses = Resources.FindObjectsOfTypeAll<Pose>();
                foreach (var p in allGamePoses)
                {
                    // Ищем любую рабочую ванильную позу у шеста, например, где контроллер "Pole Dance4"
                    if (p.controller != null && p.controller.name == "Pole Dance4" && p.icon != null)
                    {
                        vanillaPoseDonor = p;
                        break;
                    }
                }

                foreach (PoseData poseConfig in config.InteractionPoses)
                {
                    if (poseConfig.Type.Equals("CustomJSON", System.StringComparison.OrdinalIgnoreCase)) continue;

                    // Находим контроллер анимации
                    RuntimeAnimatorController targetController = null;
                    RuntimeAnimatorController[] allControllers = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();
                    foreach (var rc in allControllers)
                    {
                        if (rc.name == poseConfig.ControllerName)
                        {
                            targetController = rc;
                            break;
                        }
                    }

                    if (targetController == null) continue;

                    // Создаем объект позы
                    GameObject newPoseObj = new GameObject(poseConfig.DisplayName);
                    newPoseObj.transform.SetParent(__instance.posesGroup, false);

                    Pose newPose = newPoseObj.AddComponent<Pose>();
                    newPose.controller = targetController;
                    newPose.notshown = false;
                    newPose.locked = false;

                    // КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: Копируем данные UI из ванильного донора
                    if (vanillaPoseDonor != null)
                    {
                        newPose.icon = vanillaPoseDonor.icon; // Теперь у позы ЕСТЬ иконка!
                        newPose.categoryName = vanillaPoseDonor.categoryName;
                        newPose.mood = vanillaPoseDonor.mood;
                    }
                    else
                    {
                        newPose.categoryName = "Dances";
                    }

                    // Настраиваем точку посадки (loc) персонажа
                    GameObject locObj = new GameObject("loc");
                    locObj.transform.SetParent(newPoseObj.transform, false);

                    // Если координаты из JSON улетают, временно ставим персонажа ровно в центр шеста (0, 0, 0)
                    // чтобы проверить саму работу меню
                    locObj.transform.localPosition = new Vector3(0f, 0f, 0f);
                    locObj.transform.localEulerAngles = new Vector3(0f, 0f, 0f);
                    newPose.loc = locObj.transform;

                    // Инжектируем базовую локальную камеру, чтобы она не улетала в другую комнату
                    GameObject camObj = new GameObject("Local_Review_Camera");
                    camObj.transform.SetParent(camGroupObj.transform, false);

                    // Ставим камеру в 3 метрах от шеста на высоте груди, направленную в центр
                    camObj.transform.localPosition = new Vector3(0f, 1.5f, -3f);
                    camObj.transform.localEulerAngles = new Vector3(10f, 0f, 0f);

                    camObj.AddComponent<Camera>().enabled = false;
                    camObj.SetActive(false);
                    __instance.cameras.AddItem(camObj.transform);

                    newPoseObj.SetActive(false);
                    __instance.poses.AddItem(newPoseObj.transform);
                }

                if (!Global.code.interactableFurnitures.items.Contains(__instance.transform))
                {
                    Global.code.interactableFurnitures.AddItemDifferentObject(__instance.transform);
                }

                Plugin.Log.LogWarning($"[Injector] Мебель {furnitureName} успешно пропатчена. Иконки восстановлены!");
            }
        }
    }
}
