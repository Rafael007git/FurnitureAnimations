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
            // При старте мебели просто вызываем наш универсальный метод сборки!
            RebuildFurniturePoses(__instance);
        }

        // Наш новый публичный метод, который можно вызвать из ЛЮБОЙ точки мода для мгновенного рефреша!
        public static void RebuildFurniturePoses(Furniture __instance)
        {
            if (__instance == null) return;

            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                Plugin.Log.LogWarning($"[Injector] Сборка/Рефреш списка поз для мебели: {furnitureName}");

                // 1. Если группа поз уже существовала — полностью уничтожаем её старые дочерние объекты!
                Transform oldGroup = __instance.transform.Find("posesGroup");
                if (oldGroup != null)
                {
                    UnityEngine.Object.Destroy(oldGroup.gameObject);
                }

                // 2. Создаем чистый, свежий контейнер для обновленного списка поз
                GameObject poseGroupObj = new GameObject("posesGroup");
                poseGroupObj.transform.SetParent(__instance.transform, false);
                __instance.posesGroup = poseGroupObj.transform;

                // 3. Очищаем рантайм-списки через РОДНЫЕ методы игры из dnSpy
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
                    RuntimeAnimatorController targetController = null;
                    string searchName = isCustomPose ? "UnarmedController" : poseConfig.ControllerName;

                    foreach (var rc in allControllers)
                    {
                        if (rc != null && rc.name == searchName) { targetController = rc; break; }
                    }

                    if (targetController == null && !isCustomPose) continue;

                    GameObject newPoseObj = new GameObject(poseConfig.DisplayName);
                    newPoseObj.transform.SetParent(__instance.posesGroup, false);

                    Pose newPose = newPoseObj.AddComponent<Pose>();
                    newPose.controller = targetController;
                    newPose.notshown = false;
                    newPose.locked = false;
                    newPose.crystals = 0;

                    // Инжекция картинки-иконки с диска
                    if (isCustomPose)
                    {
                        newPose.categoryName = "Custom";
                        string iconName = poseConfig.JsonFileName.Replace(".json", ".png");
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
                    __instance.poses.AddItem(newPoseObj.transform); // Родной метод AddItem из dnSpy
                }

                // Безопасная регистрация в глобальном списке интерактива игры
                if (Global.code != null && Global.code.interactableFurnitures != null)
                {
                    // Чтобы не дублировать ссылки, удаляем старую перед добавлением свежей
                    Global.code.interactableFurnitures.items.Remove(__instance.transform);
                    Global.code.interactableFurnitures.AddItemDifferentObject(__instance.transform);
                }

                Plugin.Log.LogWarning($"[Injector] Рефреш завершен! Всего актуальных поз в памяти мебели: {__instance.poses.items.Count}");
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

    // === ПАТЧ 4: УЛЬТИМАТИВНЫЙ ПОСТФИКС-ПЕРЕХВАТ ДЛЯ ОКНА UIPOSE (RELEASE 0.2.0 STABLE) ===
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

            // Ищем объект мебели через иерархию Canvas
            Furniture parentFurniture = __instance.GetComponentInParent<Furniture>() ??
                                         __instance.pose.GetComponentInParent<Furniture>();

            if (parentFurniture == null)
            {
                Furniture[] allFurniture = UnityEngine.Object.FindObjectsOfType<Furniture>();
                if (allFurniture != null && allFurniture.Length > 0)
                {
                    parentFurniture = allFurniture[0]; // Берем первый активный проп
                }
            }

            if (parentFurniture == null || parentFurniture.user == null) return;

            CharacterCustomization activeChar = parentFurniture.user;

            // =========================================================================
            // КРИТИЧЕСКИЙ ВЫПРЯМИТЕЛЬ ВАНИЛИ: Оживляем аниматор при абсолютно ЛЮБОМ клике!
            // Это мгновенно выведет куклу из комы, если до этого была включена Диорама.
            if (activeChar != null && activeChar.anim != null)
            {
                activeChar.anim.enabled = true;  // Пробуждаем компонент!
                activeChar.anim.speed = 1f;      // Возвращаем стандартную скорость ванильным танцам!
            }
            // =========================================================================

            // Передаем персонажа на эластичную фильтрацию типов повадок
            ProcessPoseClick(activeChar, uiPoseName, uiCtrlName);
        }

        private static void ProcessPoseClick(CharacterCustomization characterComp, string uiPoseName, string uiCtrlName)
        {
            if (characterComp == null || characterComp.interactingObject == null) return;

            string furnitureName = characterComp.interactingObject.name.Replace("(Clone)", "").Trim();

            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                // Приводим имена к эластичному виду для обхода багов тире/дефисов
                string cleanUiName = uiPoseName.ToLower().Replace(" ", "").Replace("-", "").Replace("—", "").Replace("–", "").Trim();

                PoseData currentPoseData = config.InteractionPoses.Find(p =>
                    p != null &&
                    p.DisplayName.ToLower().Replace(" ", "").Replace("-", "").Replace("—", "").Replace("–", "").Trim() == cleanUiName
                );

                // Подстраховка по типу контроллера Unarmed/Custom
                if (currentPoseData == null && (cleanUiName.Contains("custom") || uiCtrlName.ToLower().Contains("unarmed")))
                {
                    currentPoseData = config.InteractionPoses.Find(p => p != null && p.Type.Equals("CustomJSON", StringComparison.OrdinalIgnoreCase));
                }

                if (currentPoseData == null || !currentPoseData.Type.Equals("CustomJSON", StringComparison.OrdinalIgnoreCase)) return;

                Plugin.Log.LogWarning($"[SDK_Icon] Целевой файл Диорамы найден: {currentPoseData.JsonFileName}. Запускаем отложенный поток...");

                // Запускаем корутину ожидания кадров прямо на глобальном менеджере Global.code, так как он активен ВСЕГДА!
                if (Global.code != null)
                {
                    Global.code.StartCoroutine(ExecuteBonesInjectionDelayed(characterComp, currentPoseData.JsonFileName));
                }
            }
        }

        private static System.Collections.IEnumerator ExecuteBonesInjectionDelayed(CharacterCustomization characterComp, string jsonFileName)
        {
            // Пережидаем 3 кадра рантайм-сбросов аниматора игрой и другими модами
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

                    // ВОССТАНОВЛЕНИЕ СКЕЛЕТА, АНАТОМИИ И СВЕТА ДИОРАМЫ
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

                // Намертво усыпляем аниматор, блокируя А-позу куклы
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

    // === ПАТЧ 5: ПОДСЕЛЕНИЕ КНОПКИ SDK В ПРАВЫЙ НИЖНИЙ УГОЛ UI (RELEASE 0.2.0 STABLE) ===
    [HarmonyPatch(typeof(UIFreePose), "Open")]
    public class UIFreePoseButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIFreePose __instance)
        {
            if (__instance == null) return;

            Transform existingBtn = __instance.transform.Find("Button_SaveInteract");
            if (existingBtn != null) return;

            Plugin.Log.LogWarning("[UI_Patch] Меню открыто. Находим родную кнопку 'Button Save' для клонирования масштаба...");

            // 1. НАХОДИМ КОМПАКТНУЮ РОДНУЮ КНОПКУ ДИОРАМЫ КАK ЭТАЛОН РАЗМЕРА
            Transform targetTemplateBtn = __instance.transform.Find("Button Save") ??
                                          __instance.transform.Find("Button Load") ??
                                          __instance.GetComponentInChildren<UnityEngine.UI.Button>()?.transform;

            if (targetTemplateBtn == null)
            {
                Plugin.Log.LogError("[UI_Patch] Ошибка: Шаблон кнопки 'Button Save' не найден на Canvas!");
                return;
            }

            // 2. КЛОНИРУЕМ ЕЁ НА CANVAS
            GameObject newButtonObj = UnityEngine.Object.Instantiate(targetTemplateBtn.gameObject, __instance.transform);
            newButtonObj.name = "Button_SaveInteract";
            newButtonObj.transform.localScale = Vector3.one;

            // 3. ЮВЕЛИРНАЯ ПОСАДКА НА ВТОРУЮ ЛИНИЮ В ПРАВЫЙ УГОЛ
            RectTransform templateRect = targetTemplateBtn.GetComponent<RectTransform>();
            RectTransform rect = newButtonObj.GetComponent<RectTransform>();

            if (rect != null && templateRect != null)
            {
                rect.anchorMin = templateRect.anchorMin;
                rect.anchorMax = templateRect.anchorMax;
                rect.pivot = templateRect.pivot;

                // Смещаем нашу кнопку строго по вертикали ровно на 45 пикселей вверх (+45f).
                // Она встанет аккуратным вторым этажом над стандартными кнопками Save/Load!
                rect.anchoredPosition = new Vector2(templateRect.anchoredPosition.x, templateRect.anchoredPosition.y + 45f);
                rect.sizeDelta = templateRect.sizeDelta; // Копируем компактный фабричный размер!
            }

            // Вычищаем скрипт локализации, чтобы он не перезаписал наш текст
            LocalizationText locText = newButtonObj.GetComponentInChildren<LocalizationText>();
            if (locText != null) UnityEngine.Object.Destroy(locText);

            // Настраиваем компактный шрифт под новый масштаб кнопки
            UnityEngine.UI.Text buttonText = newButtonObj.GetComponentInChildren<UnityEngine.UI.Text>();
            if (buttonText != null)
            {
                buttonText.text = "Initializing SDK...";
                buttonText.color = Color.white;
                buttonText.fontSize = 12; // Уменьшаем до 12, чтобы текст идеально влез в компактную кнопку
                buttonText.alignment = TextAnchor.MiddleCenter;
            }

            // Навешиваем событие клика
            UnityEngine.UI.Button buttonComp = newButtonObj.GetComponent<UnityEngine.UI.Button>();
            if (buttonComp != null)
            {
                buttonComp.onClick.RemoveAllListeners();
                buttonComp.onClick.AddListener(new UnityEngine.Events.UnityAction(() =>
                {
                    PoseExporter.OnSaveInteractClicked(__instance);
                }));
            }

            // Подселяем наш оригинальный MonoBehaviour-контроллер, его поведение НЕ меняется!
            newButtonObj.AddComponent<FurnitureSdkButtonController>().Setup(__instance);

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

    // --- ДИГНОСТИЧЕСКИЙ ШПИОН №1: СЛЕДИМ ЗА МЕТОДОМ POSE.WARP ---
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

    // --- ДИГНОСТИЧЕСКИЙ ШПИОН №2: СЛЕДИМ ЗА МЕТОДОМ FURNITURE.WARPCHARACTER ---
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
