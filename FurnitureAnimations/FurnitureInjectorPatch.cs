using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.Cameras;

namespace FurnitureAnimationsMod
{
    // === ПАТЧ 1: ИНЖЕКЦИЯ СТРУКТУРЫ ПОЗ ПРИ СТАРТЕ МЕБЕЛИ ===
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

                // Очищаем списки через РОДНЫЕ методы игры из dnSpy
                if (__instance.cameras != null) __instance.cameras.ClearItems();
                if (__instance.poses != null) __instance.poses.ClearItems();
                __instance.camerasGroup = null;

                Pose[] allGamePoses = Resources.FindObjectsOfTypeAll<Pose>();

                foreach (PoseData poseConfig in config.InteractionPoses)
                {
                    if (poseConfig.Type.Equals("CustomJSON", System.StringComparison.OrdinalIgnoreCase)) continue;

                    RuntimeAnimatorController targetController = null;
                    RuntimeAnimatorController[] allControllers = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();
                    foreach (var rc in allControllers)
                    {
                        if (rc.name == poseConfig.ControllerName) { targetController = rc; break; }
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
                            exactVanillaPose = p; break;
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
                    __instance.poses.AddItem(newPoseObj.transform); // Родной метод AddItem из dnSpy
                }

                // Безопасная регистрация в глобальном списке интерактива игры
                if (Global.code != null && Global.code.interactableFurnitures != null)
                {
                    Global.code.interactableFurnitures.AddItemDifferentObject(__instance.transform);
                }

                Plugin.Log.LogWarning($"[Injector] {furnitureName} успешно оживлен! Загружено поз: {__instance.poses.items.Count}");
            }
        }
    }

    // === ПАТЧ 2: АВТО-ПОЗА И ФИКСАЦИЯ КАМЕРЫ ПРИ ВХОДЕ ИНТЕРАКТИВА ===
    [HarmonyPatch(typeof(Furniture), "InitiateInteract")] // Точное имя метода по dnSpy!
    public class FurnitureAutoPosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Furniture __instance, CharacterCustomization customization)
        {
            if (__instance == null || customization == null) return;

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            // Если это наша кастомная мебель из справочника
            if (ConfigManager.LoadedConfigs.ContainsKey(furnitureName))
            {
                Plugin.Log.LogWarning($"[AutoInteract] Настройка бесшовного входа для: {furnitureName}");

                // 1. СИНХРОНИЗАЦИЯ СВОБОДНОЙ КАМЕРЫ (Снимаем координаты ДО отключения FreeLookCam)
                if (FreeLookCam.code != null && Global.code != null)
                {
                    // В игре объект системной свободной камеры лежит в Global.code.freeCamera
                    GameObject freeCamObj = Global.code.freeCamera;

                    if (freeCamObj != null)
                    {
                        // Принудительно включаем её и копируем ракурс, в котором стоял игрок
                        freeCamObj.SetActive(true);
                        freeCamObj.transform.position = FreeLookCam.code.transform.position;
                        freeCamObj.transform.rotation = FreeLookCam.code.transform.rotation;
                        Plugin.Log.LogInfo("[AutoInteract] Free Camera успешно зафиксирована в ракурсе игрока!");
                    }
                }

                // 2. МГНОВЕННЫЙ ЗАПУСК ПЕРВОЙ ПОЗЫ
                if (__instance.poses != null && __instance.poses.items.Count > 0)
                {
                    Transform firstPoseTransform = __instance.poses.items[0];
                    if (firstPoseTransform != null)
                    {
                        Pose firstPose = firstPoseTransform.GetComponent<Pose>();
                        if (firstPose != null)
                        {
                            Plugin.Log.LogWarning($"[AutoInteract] Авто-вызов позы: {firstPose.name}");
                            __instance.DoPose(firstPose); // Запускаем родной DoPose из dnSpy!
                        }
                    }
                }
            }
        }
    }

    // === ПАТЧ 3: БЕЗОПАСНЫЙ ВЫХОД ИЗ ИНТЕРАКТИВА ===
    [HarmonyPatch(typeof(Furniture), "DoQuitInteraction")]
    public class FurnitureQuitSafetyPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Furniture __instance)
        {
            if (__instance.user == null)
            {
                Plugin.Log.LogWarning("[SafetyPatch] Безопасный выход из пустого интерактива.");

                // Используем правильный тип CharacterCustomization из dnSpy для поиска игрока
                var playerComp = Object.FindObjectOfType<CharacterCustomization>();
                if (playerComp != null)
                {
                    playerComp.gameObject.SetActive(true);
                }
                return false;
            }
            return true;
        }
    }
}
