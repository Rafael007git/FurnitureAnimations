using HarmonyLib;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityStandardAssets.Cameras;
using static System.Net.Mime.MediaTypeNames;

namespace FurnitureAnimationsMod
{
    // === ПАТЧ 1: ИНЖЕКЦИЯ СТРУКТУРЫ ПОЗ ПРИ СТАРТЕ МЕБЕЛИ (RELEASE 0.2.0 С АВТО-РЕФРЕШЕМ) ===
    [HarmonyPatch(typeof(Furniture), "Start")]
    public class FurnitureInjectorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Furniture __instance)
        {
            if (__instance == null) return;
            RebuildFurniturePoses(__instance);
        }

        public static void RebuildFurniturePoses(Furniture __instance)
        {
            if (__instance == null) return;

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                Plugin.Log.LogWarning($"[Injector] Сборка/Рефреш списка поз для мебели: {furnitureName}");

                Transform oldGroup = __instance.transform.Find("posesGroup");
                if (oldGroup != null)
                {
                    UnityEngine.Object.Destroy(oldGroup.gameObject);
                }

                GameObject poseGroupObj = new GameObject("posesGroup");
                poseGroupObj.transform.SetParent(__instance.transform, false);
                __instance.posesGroup = poseGroupObj.transform;

                if (__instance.cameras != null) __instance.cameras.ClearItems();
                if (__instance.poses != null) __instance.poses.ClearItems();
                __instance.camerasGroup = null;

                Pose[] allGamePoses = Resources.FindObjectsOfTypeAll<Pose>();
                RuntimeAnimatorController[] allControllers = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();

                foreach (PoseData poseConfig in config.InteractionPoses)
                {
                    if (poseConfig == null) continue;

                    bool isCustomPose = poseConfig.Type.Equals("CustomJSON", System.StringComparison.OrdinalIgnoreCase) ||
                                        poseConfig.Type.Contains("Кастомная");

                    // ХИРУРГИЧЕСКАЯ ПРАВКА №1: Проверяем, является ли поза внешней JSON-анимацией
                    bool isExternalModAnim = poseConfig.Type.Equals("PoseAnimationsMod", System.StringComparison.OrdinalIgnoreCase);

                    RuntimeAnimatorController targetController = null;

                    // Если это кастомные кости или внешний JSON мода — подсовываем Unarmed, чтобы не ломать логику игры
                    string searchName = (isCustomPose || isExternalModAnim) ? "UnarmedController" : poseConfig.ControllerName;

                    foreach (var rc in allControllers)
                    {
                        if (rc != null && rc.name == searchName) { targetController = rc; break; }
                    }

                    // Если контроллер не нашелся, но это не кастом и не мод — пропускаем (защита от краша ванили)
                    if (targetController == null && !isCustomPose && !isExternalModAnim) continue;

                    GameObject newPoseObj = new GameObject(poseConfig.DisplayName);
                    newPoseObj.transform.SetParent(__instance.posesGroup, false);

                    Pose newPose = newPoseObj.AddComponent<Pose>();
                    newPose.controller = targetController;
                    newPose.notshown = false;
                    newPose.locked = false;
                    newPose.crystals = 0;

                    // Инжекция картинки-иконки с диска
                    if (isCustomPose || isExternalModAnim)
                    {
                        newPose.categoryName = "Custom";

                        // Пытаемся считать сохраненную PNG иконку (для мода имя файла равно controllerName.png)
                        string iconName = isCustomPose ? poseConfig.JsonFileName.Replace(".json", ".png") : $"{poseConfig.ControllerName}.png";
                        string iconFullPath = Path.Combine(ConfigManager.IconsPath, iconName);

                        if (File.Exists(iconFullPath))
                        {
                            try
                            {
                                byte[] imgBytes = File.ReadAllBytes(iconFullPath);
                                Texture2D customTex = new Texture2D(2, 2);
                                customTex.LoadImage(imgBytes);
                                newPose.icon = customTex;
                            }
                            catch (Exception ex)
                            {
                                Plugin.Log.LogError($"[Injector] Сбой загрузки иконки {iconName}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        Pose exactVanillaPose = null;
                        foreach (var p in allGamePoses)
                        {
                            if (p != null && p.controller != null && p.controller.name == poseConfig.ControllerName && p.icon != null)
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
                    }

                    // Координаты локатора позы
                    GameObject locObj = new GameObject("loc");
                    locObj.transform.SetParent(newPoseObj.transform, false);
                    locObj.transform.localPosition = new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z);
                    locObj.transform.localEulerAngles = new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z);
                    newPose.loc = locObj.transform;

                    newPoseObj.SetActive(false);
                    __instance.poses.AddItem(newPoseObj.transform);
                }

                if (Global.code != null && Global.code.interactableFurnitures != null)
                {
                    Global.code.interactableFurnitures.items.Remove(__instance.transform);
                    Global.code.interactableFurnitures.AddItemDifferentObject(__instance.transform);
                }

                Plugin.Log.LogWarning($"[Injector] Рефреш завершен! Всего актуальных поз в памяти мебели: {__instance.poses.items.Count}");
            }
        }
    }

    // === ПАТЧ 2: АВТО-ПОЗА И ФИКСАЦИЯ КАМЕРЫ ПРИ ВХОДЕ ИНТЕРАКТИВА ===
    [HarmonyPatch(typeof(Furniture), "InitiateInteract")]
    public class FurnitureAutoPosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Furniture __instance, CharacterCustomization customization)
        {
            if (__instance == null || customization == null) return;

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            if (ConfigManager.LoadedConfigs.ContainsKey(furnitureName))
            {
                Plugin.Log.LogWarning($"[AutoInteract] Настройка бесшовного входа для: {furnitureName}");

                if (FreeLookCam.code != null && Global.code != null)
                {
                    GameObject freeCamObj = Global.code.freeCamera;
                    if (freeCamObj != null)
                    {
                        freeCamObj.SetActive(true);
                        freeCamObj.transform.position = FreeLookCam.code.transform.position;
                        freeCamObj.transform.rotation = FreeLookCam.code.transform.rotation;
                        Plugin.Log.LogInfo("[AutoInteract] Free Camera успешно зафиксирована в ракурсе игрока!");
                    }
                }

                if (__instance.poses != null && __instance.poses.items.Count > 0)
                {
                    Transform firstPoseTransform = __instance.poses.items[0];
                    if (firstPoseTransform != null)
                    {
                        Pose firstPose = firstPoseTransform.GetComponent<Pose>();
                        if (firstPose != null)
                        {
                            Plugin.Log.LogWarning($"[AutoInteract] Авто-вызов позы: {firstPose.name}");
                            __instance.DoPose(firstPose);
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

    // === П ПАТЧ 4: УЛЬТИМАТИВНЫЙ ПОСТФИКС-ПЕРЕХВАТ ДЛЯ ОКНА UIPOSE (RELEASE 0.2.0 STABLE) ===
    [HarmonyPatch(typeof(PoseIcon), "Click")]
    public class PoseIconClickDioramaPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PoseIcon __instance)
        {
            if (__instance == null || __instance.pose == null) return;

            string uiPoseName = __instance.pose.name ?? "NULL";
            string uiCtrlName = __instance.pose.controller != null ? __instance.pose.controller.name : "NULL";

            Plugin.Log.LogWarning($"[SDK_Icon] Клик зафиксирован! Поза: '{uiPoseName}' | Контроллер: '{uiCtrlName}'");

            Furniture parentFurniture = __instance.GetComponentInParent<Furniture>() ?? __instance.pose.GetComponentInParent<Furniture>();

            if (parentFurniture == null)
            {
                Furniture[] allFurniture = UnityEngine.Object.FindObjectsOfType<Furniture>();
                if (allFurniture != null && allFurniture.Length > 0)
                {
                    parentFurniture = allFurniture[0];
                }
            }

            if (parentFurniture == null || parentFurniture.user == null) return;

            CharacterCustomization activeChar = parentFurniture.user;

            if (activeChar != null && activeChar.anim != null)
            {
                activeChar.anim.enabled = true;
                activeChar.anim.speed = 1f;
            }

            ProcessPoseClick(activeChar, parentFurniture, uiPoseName, uiCtrlName);
        }


        private static void ProcessPoseClick(CharacterCustomization characterComp, Furniture furniture, string uiPoseName, string uiCtrlName)
        {
            if (characterComp == null || furniture == null) return;

            string furnitureName = furniture.name.Replace("(Clone)", "").Trim();

            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                string cleanUiName = uiPoseName.ToLower().Replace(" ", "").Replace("-", "").Replace("—", "").Replace("–", "").Trim();

                PoseData currentPoseData = config.InteractionPoses.Find(p =>
                    p != null &&
                    p.DisplayName.ToLower().Replace(" ", "").Replace("-", "").Replace("—", "").Replace("–", "").Trim() == cleanUiName
                );

                if (currentPoseData == null && (cleanUiName.Contains("custom") || uiCtrlName.ToLower().Contains("unarmed")))
                {
                    currentPoseData = config.InteractionPoses.Find(p => p != null && p.Type.Equals("CustomJSON", StringComparison.OrdinalIgnoreCase));
                }

                if (currentPoseData == null) return;

                // =========================================================================
                // УМНЫЙ ПЕРЕХВАТ ДЛЯ СМЕНЫ И ОСТАНОВКИ АНИМАЦИЙ 🛑💃
                // =========================================================================

                // 1. Проверяем, запущен ли наш встроенный плеер на персонаже прямо сейчас
                var activePlayer = characterComp.gameObject.GetComponent<FurnitureAnimationPlayer>();

                if (activePlayer != null)
                {
                    // Узнаем имя анимации, которая крутится в данный момент
                    string currentlyPlayingAnim = activePlayer.GetPlayingAnimationName();

                    // ЕСЛИ КЛИКНУЛИ ПО ТОЙ ЖЕ ИКОНКЕ -> Просто останавливаем «горшочек»
                    if (currentPoseData.Type.Equals("PoseAnimationsMod", StringComparison.OrdinalIgnoreCase) &&
                        currentPoseData.ControllerName == currentlyPlayingAnim)
                    {
                        Plugin.Log.LogWarning($"[SDK_Icon] Клик по той же анимации '{currentlyPlayingAnim}'. Останавливаем плеер.");
                        UnityEngine.Object.Destroy(activePlayer as UnityEngine.Component);
                        return; // Мгновенный выход, ничего нового не запускаем
                    }

                    // ЕСЛИ КЛИКНУЛИ ПО ЛЮБОЙ ДРУГОЙ ИКОНКЕ -> Сносим старый плеер и даем коду идти дальше
                    Plugin.Log.LogWarning($"[SDK_Icon] Переключение! Удаляем старую анимацию '{currentlyPlayingAnim}' перед запуском нового режима.");
                    UnityEngine.Object.Destroy(activePlayer as UnityEngine.Component);
                }

                // =========================================================================

                // Логика запуска внешней JSON-анимации мода
                if (currentPoseData.Type.Equals("PoseAnimationsMod", StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogWarning($"[SDK_Icon] Активирован локальный встроенный плеер для анимации: {currentPoseData.ControllerName}");

                    var newPlayer = characterComp.gameObject.AddComponent<FurnitureAnimationPlayer>();
                    newPlayer.Play(characterComp, currentPoseData.ControllerName, furniture, currentPoseData);
                    return;
                }

                // Логика Диорамы (Сценарий Б)
                if (currentPoseData.Type.Equals("CustomJSON", StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogWarning($"[SDK_Icon] Целевой файл Диорамы найден: {currentPoseData.JsonFileName}. Запускаем отложенный поток...");
                    if (Global.code != null)
                    {
                        Global.code.StartCoroutine(ExecuteBonesInjectionDelayed(characterComp, currentPoseData.JsonFileName));
                    }
                }
            }
        }


        private static System.Collections.IEnumerator ExecuteBonesInjectionDelayed(CharacterCustomization characterComp, string jsonFileName)
        {
            for (int i = 0; i < 3; i++)
            {
                yield return null;
            }

            if (characterComp == null || characterComp.anim == null) yield break;

            string customAnimFullPath = Path.Combine(ConfigManager.CustomAnimsPath, jsonFileName);
            if (!File.Exists(customAnimFullPath))
            {
                Plugin.Log.LogError($"[SDK_Coroutine] Ошибка: Файл слепка костей отсутствует: {customAnimFullPath}");
                yield break;
            }

            try
            {
                Transform character = characterComp.transform;
                string jsonContent = File.ReadAllText(customAnimFullPath);
                var rawBonesData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, BakedElementData>>(jsonContent);

                if (rawBonesData != null)
                {
                    Plugin.Log.LogWarning($"[SDK_Coroutine] Кадры ожидания прошли! Раскатываем {rawBonesData.Count} элементов Диорамы на скелет...");

                    Transform FindChildRecursive(Transform parent, string name)
                    {
                        if (parent == null) return null;
                        if (parent.name == name) return parent;
                        for (int i = 0; i < parent.childCount; i++)
                        {
                            Transform found = FindChildRecursive(parent.GetChild(i), name);
                            if (found != null) return found;
                        }
                        return null;
                    }

                    foreach (var kp in rawBonesData)
                    {
                        Transform boneTrans = FindChildRecursive(character, kp.Key);
                        if (boneTrans == null || kp.Value == null) continue;

                        if ((kp.Value.type ?? "").Equals("Light", StringComparison.OrdinalIgnoreCase))
                        {
                            Light light = boneTrans.GetComponent<Light>();
                            if (light != null)
                            {
                                light.enabled = kp.Value.enabled;
                                light.intensity = kp.Value.intensity;
                                light.range = kp.Value.range;
                                if (kp.Value.color != null) light.color = new Color(kp.Value.color.r, kp.Value.color.g, kp.Value.color.b);
                            }
                        }
                        else
                        {
                            if (kp.Value.rot != null) boneTrans.localEulerAngles = new Vector3(kp.Value.rot.x, kp.Value.rot.y, kp.Value.rot.z);
                            if (DioramaConstants.PositionalObjectsRegistry.Contains(kp.Key) && kp.Value.pos != null)
                            {
                                boneTrans.localPosition = new Vector3(kp.Value.pos.x, kp.Value.pos.y, kp.Value.pos.z);
                            }
                        }
                    }
                }

                characterComp.anim.applyRootMotion = false;
                characterComp.anim.speed = 0f;
                characterComp.anim.enabled = false;

                Plugin.Log.LogWarning($"[SDK_Coroutine] Скелет Диорамы из файла {jsonFileName} успешно зафиксирован поверх А-позы!");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SDK_Coroutine] Краш в потоке раскатки костей: {ex.Message}");
            }
        }
    }

    // === ПАТЧ 5: РОКИРОВКА КНОПОК И ПОЛНАЯ ЗАЧИСТКА ОКНА СОХРАНЕНИЯ (RELEASE 0.2.0 STABLE) ===
    [HarmonyPatch(typeof(UIFreePose), "Open")]
    public class UIFreePoseButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIFreePose __instance)
        {
            if (__instance == null) return;

            Transform existingBtn = __instance.transform.Find("Button_SaveInteract");
            if (existingBtn != null) return;

            Plugin.Log.LogWarning("[UI_Patch] ... Меню открыто. Запуск рокировки ...");

            Transform vanillaSaveBtn = __instance.transform.Find("Button Save");
            Transform vanillaLoadBtn = __instance.transform.Find("Button Load");

            Transform targetTemplateBtn = vanillaSaveBtn ?? vanillaLoadBtn ?? __instance.GetComponentInChildren<UnityEngine.UI.Button>()?.transform;
            if (targetTemplateBtn == null) return;

            GameObject newButtonObj = UnityEngine.Object.Instantiate(targetTemplateBtn.gameObject, __instance.transform);
            newButtonObj.name = "Button_SaveInteract";
            newButtonObj.transform.localScale = Vector3.one;

            RectTransform rectSDK = newButtonObj.GetComponent<RectTransform>();
            RectTransform rectVanillaSave = vanillaSaveBtn != null ? vanillaSaveBtn.GetComponent<RectTransform>() : null;
            RectTransform rectVanillaLoad = vanillaLoadBtn != null ? vanillaLoadBtn.GetComponent<RectTransform>() : null;

            if (rectSDK != null && rectVanillaSave != null && rectVanillaLoad != null)
            {
                Vector2 originalSavePos = rectVanillaSave.anchoredPosition;
                Vector2 originalLoadPos = rectVanillaLoad.anchoredPosition;

                rectSDK.anchorMin = rectVanillaSave.anchorMin; rectSDK.anchorMax = rectVanillaSave.anchorMax; rectSDK.pivot = rectVanillaSave.pivot;
                rectSDK.anchoredPosition = originalSavePos;
                rectSDK.sizeDelta = rectVanillaSave.sizeDelta;
                // Б. Родную кнопку Save Pose смещаем вправо на оригинальное место кнопки Load!
                rectVanillaSave.anchoredPosition = originalLoadPos;

                // В. Родную кнопку Load Pose поднимаем вторым этажом ровно НАД кнопкой Save Pose!
                // Смещаем её по вертикали (ось Y) на 27 пикселей вверх от новой позиции Save
                rectVanillaLoad.anchoredPosition = new Vector2(originalLoadPos.x, originalLoadPos.y + 27f);
            }

            LocalizationText locText = newButtonObj.GetComponentInChildren<LocalizationText>();
            if (locText != null) UnityEngine.Object.Destroy(locText);

            UnityEngine.UI.Text buttonText = newButtonObj.GetComponentInChildren<UnityEngine.UI.Text>();
            if (buttonText != null)
            {
                buttonText.text = "Initializing SDK...";
                buttonText.color = Color.white;
                buttonText.fontSize = 12;
                buttonText.alignment = TextAnchor.MiddleCenter;
            }

            UnityEngine.UI.Button buttonComp = newButtonObj.GetComponent<UnityEngine.UI.Button>();
            if (buttonComp != null)
            {
                buttonComp.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                buttonComp.onClick.AddListener(new UnityEngine.Events.UnityAction(() =>
                {
                    PoseExporter.OnSaveInteractClicked(__instance);
                }));
            }

            newButtonObj.AddComponent<FurnitureSdkButtonController>().Setup(__instance);
            newButtonObj.SetActive(true);
        }
    }
    
    [HarmonyPatch(typeof(UIFreePose), "SelectPose")]
    public class UIFreePoseSelectPoseTracker
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Plugin.IsAnyPoseSelected = true;
            Plugin.IsCustomPoseActive = false;
            Plugin.Log.LogInfo("[SDK_Tracker] Выбрана готовая ванильная поза из списка.");
        }
    }

    [HarmonyPatch(typeof(UIFreePose), "ToggleCustomPoseMode")]
    public class UIFreePoseToggleModeTracker
    {
        [HarmonyPostfix]
        public static void Postfix(UIFreePose __instance)
        {
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
            Plugin.Log.LogInfo("[SDK_Tracker] ... Меню закрыто, флаги сброшены ...");
        }
    }

    [HarmonyPatch(typeof(global::Pose), "Warp")]
    public class DebugPoseWarpSpy
    {
        [HarmonyPostfix]
        public static void Postfix(global::Pose __instance, Transform character)
        {
            if (__instance == null || character == null) return;

            Animator anim = character.GetComponent<Animator>();
            string ctrlName = anim?.runtimeAnimatorController?.name ?? "None";
            bool isEnabled = anim != null && anim.enabled;

            Plugin.Log.LogWarning(
                $"[TIMING_DEBUG] -> 1. Сработал метод Pose.Warp для позы '{__instance.name}'\n" +
                $"Персонаж: {character.name}\n" +
                $"Animator Enabled: {isEnabled}\n" +
                $"Controller Name: {ctrlName}"
            );
        }
    }

    [HarmonyPatch(typeof(Furniture), "WarpCharacter")]
    public class DebugWarpCharacterSpy
    {
        [HarmonyPostfix]
        public static void Postfix(Furniture __instance, Transform character, Transform pose)
        {
            if (__instance == null || character == null || pose == null) return;

            Animator anim = character.GetComponent<Animator>();
            string ctrlName = anim?.runtimeAnimatorController?.name ?? "None";
            bool isEnabled = anim != null && anim.enabled;

            Plugin.Log.LogError(
                $"[TIMING_DEBUG] -> 2. Сработал метод Furniture.WarpCharacter для мебели '{__instance.name}'\n" +
                $"Выбранный локатор позы: {pose.name}\n" +
                $"Animator Enabled: {isEnabled}\n" +
                $"Controller Name: {ctrlName}"
            );
        }
    }
}
