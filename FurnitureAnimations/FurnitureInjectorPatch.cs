using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityStandardAssets.Cameras;
using static System.Net.Mime.MediaTypeNames;

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

    // === ПАТЧ 5: УЛЬТИМАТИВНАЯ ДИНАМИЧЕСКАЯ КНОПКА SDK С АВТО-БЛОКИРОВКОЙ ПО РАДИУСУ (ENG) ===
    [HarmonyPatch(typeof(UIFreePose), "Refresh")]
    public class UIFreePoseButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIFreePose __instance)
        {
            if (__instance == null || __instance.selectedCharacter == null) return;

            CharacterCustomization characterComp = __instance.selectedCharacter.GetComponent<CharacterCustomization>();
            if (characterComp == null || characterComp.anim == null) return;

            // 1. Ищем, создана ли уже наша кнопка в корне окна
            Transform saveBtnTrans = __instance.transform.Find("Button_SaveInteract");
            GameObject newButtonObj = null;

            if (saveBtnTrans == null)
            {
                // Если кнопки нет — клонируем её из шаблона один раз строго в корень __instance.transform
                Transform templateBtn = __instance.transform.Find("FreePose");
                if (templateBtn == null) templateBtn = __instance.GetComponentInChildren<UnityEngine.UI.Button>()?.transform;
                if (templateBtn == null) return;

                newButtonObj = Object.Instantiate(templateBtn.gameObject, __instance.transform);
                newButtonObj.name = "Button_SaveInteract";

                // Сброс масштаба, чтобы не раздувало
                newButtonObj.transform.localScale = Vector3.one;

                RectTransform rect = newButtonObj.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(1f, 0f); rect.anchorMax = new Vector2(1f, 0f); rect.pivot = new Vector2(1f, 0f);
                    rect.anchoredPosition = new Vector2(-40f, 230f); // Фиксированная позиция справа
                    rect.sizeDelta = new Vector2(180f, 40f);
                }

                // Стираем ванильную локализацию игры
                LocalizationText locText = newButtonObj.GetComponentInChildren<LocalizationText>();
                if (locText != null) Object.Destroy(locText);

                // Навешиваем клик
                UnityEngine.UI.Button buttonComp = newButtonObj.GetComponent<UnityEngine.UI.Button>();
                if (buttonComp != null)
                {
                    buttonComp.onClick.RemoveAllListeners();
                    buttonComp.onClick.AddListener(new UnityEngine.Events.UnityAction(() =>
                    {
                        PoseExporter.OnSaveInteractClicked(__instance);
                    }));
                }

                saveBtnTrans = newButtonObj.transform;
            }

            newButtonObj = saveBtnTrans.gameObject;
            UnityEngine.UI.Button mainButtonComp = newButtonObj.GetComponent<UnityEngine.UI.Button>();
            UnityEngine.UI.Text buttonText = newButtonObj.GetComponentInChildren<UnityEngine.UI.Text>();

            if (mainButtonComp == null || buttonText == null) return;

            // 2. УМНЫЙ АНАЛИЗ ОКРУЖЕНИЯ: Ищем мебель в радиусе 5 метров
            Furniture nearbyProp = PoseExporter.FindClosestFurniture(characterComp.transform.position, 5f);
            bool isFurnitureNearby = nearbyProp != null;

            // 3. ДИНАМИЧЕСКИЙ АНАЛИЗ СОСТОЯНИЯ АНИМАЦИЙ
            string currentCtrlName = characterComp.anim.runtimeAnimatorController?.name ?? "";

            bool isMenuEmpty = string.IsNullOrEmpty(currentCtrlName) || currentCtrlName == "Combat_Idle" || currentCtrlName == "Common_Idle";
            bool isCustomModeActive = __instance.isCustomPoseMode;

            // 4. ТВОЯ ИСПРАВЛЕННАЯ ЛОГИКА НАЗВАНИЙ С ДИСТАНЦИОННЫМ БЛОКОМ
            if (!isFurnitureNearby)
            {
                // Если мебели нет ближе 5 метров — жестко блокируем кнопку
                buttonText.text = "No Furniture Nearby";
                buttonText.color = Color.gray;
                mainButtonComp.interactable = false;
            }
            else if (isMenuEmpty && !isCustomModeActive)
            {
                // СОСТОЯНИЕ 1: Мебель рядом есть, но поза еще не выбрана
                buttonText.text = "No Furniture Pose";
                buttonText.color = Color.gray;
                mainButtonComp.interactable = false;
            }
            else if (isCustomModeActive)
            {
                // СОСТОЯНИЕ 3: Активирован ручной режим FreePose (Гизмо на экране)
                buttonText.text = "Save Custom Pose for Furniture";
                buttonText.color = Color.cyan;
                mainButtonComp.interactable = true;
            }
            else
            {
                // СОСТОЯНИЕ 2 и 4: Выбрана предустановленная ванильная анимация из списка
                buttonText.text = "Link Preset Pose for Furniture";
                buttonText.color = Color.green;
                mainButtonComp.interactable = true;
            }

            newButtonObj.SetActive(true);
        }
    }



}
