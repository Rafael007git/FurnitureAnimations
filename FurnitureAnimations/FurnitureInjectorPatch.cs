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

            // Защита: Если позы уже инжектированы, выходим
            if (__instance.transform.Find("posesGroup") != null) return;

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                Plugin.Log.LogWarning($"[Injector] Инжектируем справочник поз в: {furnitureName}");

                // Создаем контейнер для поз
                GameObject poseGroupObj = new GameObject("posesGroup");
                poseGroupObj.transform.SetParent(__instance.transform, false);
                __instance.posesGroup = poseGroupObj.transform;

                // Кэш всех ванильных поз игры для поиска иконок
                Pose[] allGamePoses = Resources.FindObjectsOfTypeAll<Pose>();

                foreach (PoseData poseConfig in config.InteractionPoses)
                {
                    if (poseConfig.Type.Equals("CustomJSON", System.StringComparison.OrdinalIgnoreCase)) continue;

                    // Ищем контроллер анимации
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

                    if (targetController == null)
                    {
                        Plugin.Log.LogError($"[Injector] Не найден файл анимации: {poseConfig.ControllerName}");
                        continue;
                    }

                    GameObject newPoseObj = new GameObject(poseConfig.DisplayName);
                    newPoseObj.transform.SetParent(__instance.posesGroup, false);

                    Pose newPose = newPoseObj.AddComponent<Pose>();
                    newPose.controller = targetController;
                    newPose.notshown = false;
                    newPose.locked = false;
                    newPose.crystals = 0;

                    // Поиск оригинальной позы-донора для иконки
                    Pose exactVanillaPose = null;
                    foreach (var p in allGamePoses)
                    {
                        if (p.controller != null && p.controller.name == poseConfig.ControllerName && p.icon != null)
                        {
                            exactVanillaPose = p;
                            break;
                        }
                    }

                    if (exactVanillaPose != null)
                    {
                        newPose.icon = exactVanillaPose.icon;
                        newPose.categoryName = exactVanillaPose.categoryName;
                        newPose.mood = exactVanillaPose.mood;
                    }
                    else
                    {
                        newPose.categoryName = "Dances";
                    }

                    // Настройка смещения из JSON
                    GameObject locObj = new GameObject("loc");
                    locObj.transform.SetParent(newPoseObj.transform, false);
                    locObj.transform.localPosition = new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z);
                    locObj.transform.localEulerAngles = new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z);
                    newPose.loc = locObj.transform;

                    newPoseObj.SetActive(false);
                    __instance.poses.AddItem(newPoseObj.transform);
                }

                // ИСПРАВЛЕНО: Глушим камеры без вызова методов CommonArray
                __instance.camerasGroup = null;

                // ИСПРАВЛЕНО: Безопасная проверка и добавление в список мебели через стандартный цикл
                if (Global.code != null && Global.code.interactableFurnitures != null && Global.code.interactableFurnitures.items != null)
                {
                    bool alreadyExists = false;
                    foreach (var item in Global.code.interactableFurnitures.items)
                    {
                        if (item == __instance.transform)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }

                    if (!alreadyExists)
                    {
                        Global.code.interactableFurnitures.AddItemDifferentObject(__instance.transform);
                    }
                }

                Plugin.Log.LogWarning($"[Injector] {furnitureName} успешно оживлен! Загружено поз: {__instance.poses.items.Count}");
            }
        }
    }

    [HarmonyPatch(typeof(Furniture), "DoQuitInteraction")]
    public class FurnitureQuitSafetyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Furniture __instance)
        {
            if (__instance.user == null)
            {
                Plugin.Log.LogWarning("[SafetyPatch] Безопасный выход из пустого интерактива.");

                // ИСПРАВЛЕНО: Ищем игрока через стандартный Find движка Unity, полностью минуя класс Global
                GameObject playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null)
                {
                    playerObj.SetActive(true);
                }
                else
                {
                    // Подстраховка, если тег не настроен — ищем по компоненту Player
                    var playerComp = Object.FindObjectOfType<global::Player>();
                    if (playerComp != null)
                    {
                        playerComp.gameObject.SetActive(true);
                    }
                }
                return false;
            }
            return true;
        }
    }
}
