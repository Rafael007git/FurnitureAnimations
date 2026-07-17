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

                    bool isCustomPose = poseConfig.Type.Equals("CustomJSON", System.StringComparison.OrdinalIgnoreCase);
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

    // === ПАТЧ 5: УЛЬТИМАТИВНАЯ ИНЖЕКЦИЯ АВТОНОМНОЙ КНОПКИ ЧЕРЕЗ MONOBEHAVIOUR ===
    [HarmonyPatch(typeof(UIFreePose), "Open")] // Перешли на железный метод Open!
    public class UIFreePoseButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIFreePose __instance)
        {
            if (__instance == null) return;

            // Защита от дублирования кнопок при повторных открытиях меню
            Transform existingBtn = __instance.transform.Find("Button_SaveInteract");
            if (existingBtn != null) return;

            Plugin.Log.LogWarning("[UI_Patch] Меню открыто. Начинаем физическую сборку автономной кнопки SDK...");

            // Ищем оригинальную кнопку "FreePose" на левой панели в качестве донора компонентов (Image, Button)
            Transform templateBtn = __instance.transform.Find("FreePose");
            if (templateBtn == null)
            {
                // Подстраховка: берем первую попавшуюся кнопку в иерархии окна
                templateBtn = __instance.GetComponentInChildren<UnityEngine.UI.Button>()?.transform;
            }

            if (templateBtn == null)
            {
                Plugin.Log.LogError("[UI_Patch] Критическая ошибка: Не найден шаблон кнопки на Canvas!");
                return;
            }

            // 1. Клонируем кнопку строго в корень __instance.transform, защищая от удаления другими модами
            GameObject newButtonObj = UnityEngine.Object.Instantiate(templateBtn.gameObject, __instance.transform);
            newButtonObj.name = "Button_SaveInteract";

            // Жестко сбрасываем масштаб клона до нормы, убирая баг раздувания в два раза!
            newButtonObj.transform.localScale = Vector3.one;

            // 2. Выставляем фиксированные координаты в правом нижнем углу экрана монитора
            RectTransform rect = newButtonObj.GetComponent<RectTransform>();
            if (rect != null)
            {
                // Прижимаем якоря к правому нижнему углу экрана (x=1, y=0)
                rect.anchorMin = new Vector2(1f, 0f); rect.anchorMax = new Vector2(1f, 0f); rect.pivot = new Vector2(1f, 0f);

                // Фиксированная позиция: 40 пикселей от правого края, 230 пикселей от низа монитора
                rect.anchoredPosition = new Vector2(-40f, 230f);
                rect.sizeDelta = new Vector2(210f, 40f); // Красивый стандартный размер
            }

            // 3. Уничтожаем ванильные скрипты локализации игры, чтобы они не затерли наш текст
            LocalizationText locText = newButtonObj.GetComponentInChildren<LocalizationText>();
            if (locText != null) UnityEngine.Object.Destroy(locText);

            // 4. Первичная настройка шрифта и текста
            UnityEngine.UI.Text buttonText = newButtonObj.GetComponentInChildren<UnityEngine.UI.Text>();
            if (buttonText != null)
            {
                buttonText.text = "Initializing SDK...";
                buttonText.color = Color.white;
                buttonText.fontSize = 13;
                buttonText.alignment = TextAnchor.MiddleCenter;
            }

            // 5. Очищаем старые листенеры шаблона и вешаем наш метод экспорта
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

            // 6. ХИРУРГИЧЕСКИЙ ШАГ: Подселяем наш MonoBehaviour скрипт прямо на созданную кнопку!
            FurnitureSdkButtonController controller = newButtonObj.AddComponent<FurnitureSdkButtonController>();
            controller.Setup(__instance);

            newButtonObj.SetActive(true);
            Plugin.Log.LogWarning("[UI_Patch] Автономная кнопка SDK успешно создана и переведена на MonoBehaviour-контроль!");
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

    // === ТРОЙНОЙ ДИАГНОСТИЧЕСКИЙ ПАТЧ ДЛЯ ПОИСКА А-ПОЗЫ (RELEASE 0.2.0 DEBUG) ===
    [HarmonyPatch(typeof(Furniture), "DoPose")]
    public class FurnitureDoPoseDioramaPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Furniture __instance, Pose pose)
        {
            if (__instance == null || pose == null) return;

            // ТОЧКА 1: Проверяем сам факт вызова метода DoPose игрой
            Plugin.Log.LogWarning($"[TRACKER_T1] Метод DoPose ВЫЗВАН! Мебель: {__instance.name}, Объект позы на сцене: {pose.name}");

            if (__instance.user == null)
            {
                Plugin.Log.LogError("[TRACKER_T1] Ошибка: У мебели отсутствует пользователь (user == null)!");
                return;
            }

            Transform character = __instance.user.transform;
            string furnitureName = __instance.name.Replace("(Clone)", "").Trim();

            if (!ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                Plugin.Log.LogError($"[TRACKER_T1] Ошибка: Для мебели '{furnitureName}' вообще не загружен JSON-конфиг!");
                return;
            }

            // Ищем позу в конфиге мебели
            PoseData currentPoseData = config.InteractionPoses.Find(p => p != null && p.DisplayName == pose.name);

            if (currentPoseData == null)
            {
                Plugin.Log.LogError($"[TRACKER_T1] Ошибка: Поза с DisplayName '{pose.name}' не найдена в InteractionPoses конфига {furnitureName}!");
                // Выведем список того, что вообще есть в памяти для этой мебели, чтобы найти опечатку
                foreach (var p in config.InteractionPoses)
                {
                    if (p != null) Plugin.Log.LogInfo($" -> Доступно в памяти конфига: '{p.DisplayName}' (Тип: {p.Type})");
                }
                return;
            }

            if (!currentPoseData.Type.Equals("CustomJSON", StringComparison.OrdinalIgnoreCase))
            {
                Plugin.Log.LogInfo($"[TRACKER_T1] Это ванильная поза '{pose.name}' (Тип: Vanilla). Накат Диорамы пропускаем.");
                return;
            }

            // ТОЧКА 2: Мы успешно вошли в Сценарий Б (Кастомная поза)!
            Plugin.Log.LogWarning($"[TRACKER_T2] Шаг пройден! Начинаем чтение файла: {currentPoseData.JsonFileName}");
            string customAnimFullPath = Path.Combine(ConfigManager.CustomAnimsPath, currentPoseData.JsonFileName);

            if (!File.Exists(customAnimFullPath))
            {
                Plugin.Log.LogError($"[TRACKER_T2] Критическая ошибка: Файл слепка костей физически отсутствует по пути: {customAnimFullPath}");
                return;
            }

            try
            {
                string jsonBones = File.ReadAllText(customAnimFullPath);
                var rawBonesData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, BakedElementData>>(jsonBones);

                if (rawBonesData == null || rawBonesData.Count == 0)
                {
                    Plugin.Log.LogError("[TRACKER_T2] Ошибка: Файл JSON пустой или поврежден!");
                    return;
                }

                Plugin.Log.LogInfo($"[TRACKER_T2] Файл прочитан успешно. Раскатываем {rawBonesData.Count} элементов Диорамы на скелет...");

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

                int successBonesCount = 0;
                foreach (var kp in rawBonesData)
                {
                    string targetName = kp.Key;
                    BakedElementData elementData = kp.Value;
                    if (elementData == null) continue;

                    Transform boneTrans = FindChildRecursive(character, targetName);
                    if (boneTrans == null) continue;

                    string type = elementData.type ?? "Bone";

                    if (type.Equals("Light", StringComparison.OrdinalIgnoreCase))
                    {
                        Light lightComponent = boneTrans.GetComponent<Light>();
                        if (lightComponent != null)
                        {
                            lightComponent.enabled = elementData.enabled;
                            lightComponent.intensity = elementData.intensity;
                            lightComponent.range = elementData.range;
                            if (elementData.color != null) lightComponent.color = new Color(elementData.color.r, elementData.color.g, elementData.color.b);
                        }
                        if (elementData.pos != null) boneTrans.localPosition = new Vector3(elementData.pos.x, elementData.pos.y, elementData.pos.z);
                    }
                    else
                    {
                        if (elementData.rot != null) boneTrans.localEulerAngles = new Vector3(elementData.rot.x, elementData.rot.y, elementData.rot.z);
                        if (DioramaConstants.PositionalObjectsRegistry.Contains(targetName) && elementData.pos != null)
                        {
                            boneTrans.localPosition = new Vector3(elementData.pos.x, elementData.pos.y, elementData.pos.z);
                        }
                    }
                    successBonesCount++;
                }

                // ТОЧКА 3: Применение завершено. Проверяем, устоит ли поза против движка!
                Plugin.Log.LogWarning($"[TRACKER_T3] Раскатка костей завершена! Успешно изменено {successBonesCount} узлов скелета.");

                Animator anim = character.GetComponent<Animator>();
                if (anim != null)
                {
                    anim.applyRootMotion = false;
                    anim.speed = 0f;
                    anim.enabled = false; // Замораживаем аниматор
                    Plugin.Log.LogInfo($"[TRACKER_T3] Компонент Animator ПРИНУДИТЕЛЬНО ВЫКЛЮЧЕН (enabled = false).");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TRACKER_T2] Критический краш в процессе раскатки костей: {ex.Message}\n{ex.StackTrace}");
            }
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
