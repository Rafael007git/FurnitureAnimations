using HarmonyLib;
using System;
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
                var playerComp = UnityEngine.Object.FindObjectOfType<CharacterCustomization>();
                if (playerComp != null)
                {
                    playerComp.gameObject.SetActive(true);
                }
                return false;
            }
            return true;
        }
    }

    // === ПАТЧ 5: СТАБИЛЬНАЯ ДИНАМИЧЕСКАЯ КНОПКА SDK (ФИНАЛЬНЫЙ РЕЛИЗ 0.2.0) ===
    [HarmonyPatch(typeof(UIFreePose), "Refresh")]
    public class UIFreePoseButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIFreePose __instance)
        {
            if (__instance == null || __instance.selectedCharacter == null) return;

            CharacterCustomization characterComp = __instance.selectedCharacter.GetComponent<CharacterCustomization>();
            if (characterComp == null || characterComp.anim == null) return;

            // 1. Создание или удержание нашей кнопки SDK в корне Canvas окна
            Transform saveBtnTrans = __instance.transform.Find("Button_SaveInteract");
            GameObject newButtonObj = null;

            if (saveBtnTrans == null)
            {
                Transform templateBtn = __instance.transform.Find("FreePose");
                if (templateBtn == null) templateBtn = __instance.GetComponentInChildren<UnityEngine.UI.Button>()?.transform;
                if (templateBtn == null) return;

                newButtonObj = UnityEngine.Object.Instantiate(templateBtn.gameObject, __instance.transform);
                newButtonObj.name = "Button_SaveInteract";
                newButtonObj.transform.localScale = Vector3.one; // Сброс масштаба, чтобы не раздувало!

                RectTransform rect = newButtonObj.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(1f, 0f); rect.anchorMax = new Vector2(1f, 0f); rect.pivot = new Vector2(1f, 0f);
                    rect.anchoredPosition = new Vector2(-40f, 230f); // Позиция справа внизу
                    rect.sizeDelta = new Vector2(210f, 40f); // Расширили под длинный английский текст
                }

                if (newButtonObj.GetComponentInChildren<LocalizationText>() != null)
                    UnityEngine.Object.Destroy(newButtonObj.GetComponentInChildren<LocalizationText>());

                UnityEngine.UI.Button buttonComp = newButtonObj.GetComponent<UnityEngine.UI.Button>();
                if (buttonComp != null)
                {
                    buttonComp.onClick.RemoveAllListeners();
                    buttonComp.onClick.AddListener(new UnityEngine.Events.UnityAction(() =>
                    {
                        Plugin.Log.LogWarning("[UI_Patch] Клик по автономной кнопке 'Сохранить интерактив' зафиксирован!");
                        PoseExporter.OnSaveInteractClicked(__instance);
                    }));
                }
                saveBtnTrans = newButtonObj.transform;
            }

            newButtonObj = saveBtnTrans.gameObject;
            UnityEngine.UI.Button mainButtonComp = newButtonObj.GetComponent<UnityEngine.UI.Button>();
            UnityEngine.UI.Text buttonText = newButtonObj.GetComponentInChildren<UnityEngine.UI.Text>();

            if (mainButtonComp == null || buttonText == null) return;

            // 2. ХИРУРГИЧЕСКИЙ АНАЛИЗ СОСТОЯНИЙ НА ОСНОВЕ ФАКТОВ ДВИЖКА
            string currentCtrlName = (characterComp.anim.runtimeAnimatorController?.name ?? "").ToLower();

            // Проверяем, активен ли ручной режим кручения костей скелета ( Advanced Free Pose )
            bool isHandEditingActive = GameObject.Find("TransformGizmo") != null || currentCtrlName == "customjson";

            // Проверяем, находится ли персонаж в базовом состоянии покоя (Idle или Unarmed стойка)
            bool isDefaultIdleActive = currentCtrlName.Contains("idle") || currentCtrlName.Contains("unarmed") || string.IsNullOrEmpty(currentCtrlName);

            // УМНАЯ ПРОВЕРКА НА ЛЮБУЮ ВАНИЛЬНУЮ ПОЗУ (И статика, и динамика):
            // Поза считается выбранной, ЕСЛИ аниматор заморожен (для позы-статуи) 
            // ИЛИ если имя текущего контроллера сменилось с дефолтного Idle на имя конкретной позы мебели!
            bool isAnyPresetPoseActive = characterComp.anim.enabled == false || !isDefaultIdleActive;

            // 3. АВТО-ПЕРЕКЛЮЧЕНИЕ ТЕКСТА И ЦВЕТА КНОПКИ
            if (isHandEditingActive)
            {
                // СОСТОЯНИЕ 3: Крутим кости вручную (Гизмо на экране)
                buttonText.text = "Save Custom Pose for Furniture";
                buttonText.color = Color.cyan; // Бирюзовый SDK
                mainButtonComp.interactable = true;
            }
            else if (isAnyPresetPoseActive)
            {
                // СОСТОЯНИЕ 2 и 4: Выбрана любая предустановленная поза из списка (статичная или анимированная!)
                buttonText.text = "Link Preset Pose for Furniture";
                buttonText.color = Color.green; // Зеленый цвет связи
                mainButtonComp.interactable = true;
            }
            else
            {
                // СОСТОЯНИЕ 1: Поза мебели еще не выбрана (персонаж просто стоит в дефолтном Idle)
                buttonText.text = "No Furniture Pose";
                buttonText.color = Color.gray; // Серый цвет
                mainButtonComp.interactable = false; // Блокируем клик
            }

            newButtonObj.SetActive(true);
        }
    }



    // ПАТЧ А: Перехватываем клик по ГОТОВОЙ позе из ванильного списка игры
    [HarmonyPatch(typeof(UIFreePose), "SelectPose")] // Метод игры при выборе иконки позы
    public class UIFreePoseSelectPoseTracker
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.IsAnyPoseSelected = true;    // Поза выбрана!
            Plugin.IsCustomPoseActive = false;  // Режим ручного гизмо автоматически гасится игрой
            Plugin.Log.LogInfo("[SDK_Tracker] Выбрана готовая ванильная поза из списка.");
        }
    }

    // ПАТЧ Б: Перехватываем клик по кнопке "FreePose" (Включение гизмо мода bugerry)
    [HarmonyPatch(typeof(UIFreePose), "ToggleCustomPoseMode")] // Или метод bugerry/игры, включающий FreePose
    public class UIFreePoseToggleModeTracker
    {
        [HarmonyPostfix]
        public static void Postfix(UIFreePose __instance)
        {
            // Смотрим на реальное состояние окна игры после клика
            Plugin.IsCustomPoseActive = __instance.isCustomPoseMode;
            Plugin.Log.LogInfo($"[SDK_Tracker] Переключение режима ручной правки костей: {Plugin.IsCustomPoseActive}");
        }
    }

    [HarmonyPatch(typeof(UIFreePose), "Close")]
    public class UIFreePoseCloseTracker
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.IsAnyPoseSelected = false;
            Plugin.IsCustomPoseActive = false;
            Plugin.Log.LogInfo("[SDK_Tracker] Меню FreePose закрыто, флаги полностью очищены.");
        }
    }

}
