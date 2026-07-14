using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace FurnitureAnimationsMod
{
    // === ПАТЧ 1: ОСНОВНАЯ ИНЖЕКЦИЯ СТРУКТУРЫ МЕБЕЛИ ===
    [HarmonyPatch(typeof(Furniture), "Start")]
    public class FurnitureInjectorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Furniture __instance)
        {
            if (__instance == null) return;

            // Защита от повторного входа
            if (__instance.transform.Find("posesGroup") != null) return;

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                Plugin.Log.LogWarning($"[Injector] Оживляем мебель: {furnitureName}");

                GameObject poseGroupObj = new GameObject("posesGroup");
                poseGroupObj.transform.SetParent(__instance.transform, false);
                __instance.posesGroup = poseGroupObj.transform;

                Pose[] allGamePoses = Resources.FindObjectsOfTypeAll<Pose>();

                foreach (PoseData poseConfig in config.InteractionPoses)
                {
                    if (poseConfig.Type.Equals("CustomJSON", System.StringComparison.OrdinalIgnoreCase)) continue;

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

                    GameObject newPoseObj = new GameObject(poseConfig.DisplayName);
                    newPoseObj.transform.SetParent(__instance.posesGroup, false);

                    Pose newPose = newPoseObj.AddComponent<Pose>();
                    newPose.controller = targetController;
                    newPose.notshown = false;
                    newPose.locked = false;
                    newPose.crystals = 0;

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

                    GameObject locObj = new GameObject("loc");
                    locObj.transform.SetParent(newPoseObj.transform, false);
                    locObj.transform.localPosition = new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z);
                    locObj.transform.localEulerAngles = new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z);
                    newPose.loc = locObj.transform;

                    newPoseObj.SetActive(false);
                    __instance.poses.AddItem(newPoseObj.transform);
                }

                // Полностью очищаем списки камер, чтобы игра отдала приоритет Free Camera
                __instance.camerasGroup = null;

                if (Global.code != null && Global.code.interactableFurnitures != null && Global.code.interactableFurnitures.items != null)
                {
                    bool alreadyExists = false;
                    foreach (var item in Global.code.interactableFurnitures.items)
                    {
                        if (item == __instance.transform) { alreadyExists = true; break; }
                    }
                    if (!alreadyExists) Global.code.interactableFurnitures.AddItemDifferentObject(__instance.transform);
                }

                Plugin.Log.LogWarning($"[Injector] {furnitureName} успешно оживлен! Загружено поз: {__instance.poses.items.Count}");
            }
        }
    }

    // === ПАТЧ 2: БЕЗОПАСНЫЙ СИНХРОНИЗАТОР И ПОДГОНКА FREE CAMERA ===
    [HarmonyPatch(typeof(Furniture), "Interact")]
    public class FurnitureInteractCameraPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Furniture __instance)
        {
            if (__instance == null) return;

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            // Если игрок садится на нашу кастомную мебель из справочника
            if (ConfigManager.LoadedConfigs.ContainsKey(furnitureName))
            {
                Plugin.Log.LogInfo($"[CameraPatch] Перехват входа в интерактив {furnitureName}. Выравниваем Free Camera...");

                // 1. Находим текущую активную главную камеру сцены
                Camera mainCam = Camera.main;
                if (mainCam == null) mainCam = Object.FindObjectOfType<Camera>();

                if (mainCam != null)
                {
                    // 2. Ищем в сцене системный объект Free Camera игры по имени
                    GameObject freeCamObj = GameObject.Find("Free Camera");
                    if (freeCamObj == null) freeCamObj = GameObject.Find("FreeCamera");

                    if (freeCamObj != null)
                    {
                        // 3. Мгновенно копируем мировые координаты основной камеры на Free Camera
                        freeCamObj.transform.position = mainCam.transform.position;
                        freeCamObj.transform.rotation = mainCam.transform.rotation;
                        Plugin.Log.LogInfo("[CameraPatch] Позиция Free Camera успешно синхронизирована с видом игрока!");
                    }
                }
            }
        }
    }

    // === ПАТЧ 3: АВТОМАТИЧЕСКИЙ ЗАПУСК ПЕРВОЙ ПОЗЫ ИЗ СПИСКА ===
    [HarmonyPatch(typeof(Furniture), "Interact")]
    public class FurnitureAutoPosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Furniture __instance)
        {
            if (__instance == null) return;

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            // Если это наша кастомная мебель и у неё успешно сгенерировались позы
            if (ConfigManager.LoadedConfigs.ContainsKey(furnitureName) && __instance.poses != null && __instance.poses.items.Count > 0)
            {
                Transform firstPoseTransform = __instance.poses.items[0];
                if (firstPoseTransform != null)
                {
                    Pose firstPose = firstPoseTransform.GetComponent<Pose>();
                    if (firstPose != null)
                    {
                        Plugin.Log.LogWarning($"[AutoPose] Принудительно активируем первую позу: {firstPose.name}");

                        // Заставляем мебель мгновенно применить эту позу к персонажу
                        __instance.DoPose(firstPose);
                    }
                }
            }
        }
    }

    // === ПАТЧ 4: БЕЗОПАСНЫЙ ВЫХОД ИЗ ИНТЕРАКТИВА ===
    [HarmonyPatch(typeof(Furniture), "DoQuitInteraction")]
    public class FurnitureQuitSafetyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Furniture __instance)
        {
            if (__instance.user == null)
            {
                Plugin.Log.LogWarning("[SafetyPatch] Безопасный выход из пустого интерактива.");
                GameObject playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null) playerObj.SetActive(true);
                else
                {
                    var playerComp = Object.FindObjectOfType<global::Player>();
                    if (playerComp != null) playerComp.gameObject.SetActive(true);
                }
                return false;
            }
            return true;
        }
    }
}
