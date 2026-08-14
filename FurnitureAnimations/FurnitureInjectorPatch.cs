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

            // Ищем, есть ли для этой мебели конфигурация в нашем моде
            if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
            {
                Plugin.Log.LogWarning($"[Injector] Инжекция кастомных поз для мебели: {furnitureName} (Ванильные позы БУДУТ сохранены!)");

                // --- КРИТИЧЕСКИЙ ФИКС №1: БОЛЬШЕ НЕ УНИЧТОЖАЕМ "posesGroup" И НЕ ОЧИЩАЕМ СПИСКИ КАМЕР/ПОЗ ИГРЫ! ---
                // Если у мебели почему-то нет инициализированных списков — создаем их (защита от краша)
                if (__instance.poses == null) __instance.poses = new CommonArray();
                if (__instance.cameras == null) __instance.cameras = new CommonArray();

                // =========================================================================
                // ФИНАЛЬНЫЙ ТРОЯНСКИЙ КОНЬ: Автоматическое клонирование HDRP-донора с защитой от дублей! 🛡📸
                // =========================================================================
                if (__instance.cameras != null && config.CustomCameras != null && config.CustomCameras.Count > 0)
                {
                    try
                    {
                        // 1. Ищем или создаем корневой объект группы камер
                        Transform camGroupTrans = __instance.camerasGroup;
                        if (camGroupTrans == null)
                        {
                            GameObject camGroupObj = new GameObject("Cameras Group");
                            camGroupObj.transform.SetParent(__instance.transform, false);
                            __instance.camerasGroup = camGroupObj.transform;
                            camGroupTrans = camGroupObj.transform;
                        }

                        // 2. ИЩЕМ СВЕРХТЯЖЕЛОГО ВАНИЛЬНОГО ДОНОРА (Считываем эталон из памяти игры ровно 1 раз)
                        GameObject cameraDonor = null;
                        Furniture[] allSceneFurnitures = UnityEngine.Object.FindObjectsOfType<Furniture>();
                        foreach (var f in allSceneFurnitures)
                        {
                            // Берем мебель, которая есть в ванильной игре (у нее гарантированно правильный HDRP обвес из 8 скриптов)
                            if (f != null && f.camerasGroup != null && f.cameras?.items != null && f.cameras.items.Count > 0)
                            {
                                // Вытаскиваем самый первый Transform оригинальной камеры игры
                                Transform vanillaCamTrans = f.cameras.items[0] as Transform;
                                if (vanillaCamTrans != null && !vanillaCamTrans.name.StartsWith("[SDK]"))
                                {
                                    cameraDonor = vanillaCamTrans.gameObject;
                                    break;
                                }
                            }
                        }

                        // 3. СИНХРОНИЗИРУЕМ КАМЕРЫ ИЗ JSON С ЗАЩИТОЙ ОТ ГЕОМЕТРИЧЕСКОГО РАЗМНОЖЕНИЯ
                        foreach (var camData in config.CustomCameras)
                        {
                            if (string.IsNullOrEmpty(camData.Name)) continue;

                            // Ищем, не создавали ли мы этот тяжелый клон в прошлый раз?
                            Transform targetCamTrans = camGroupTrans.Find(camData.Name);
                            bool isNewCamera = false;

                            if (targetCamTrans == null)
                            {
                                GameObject virtualCamObj;
                                if (cameraDonor != null)
                                {
                                    // Клонируем эталон со всеми 8 нативными компонентами игры! 🎯
                                    virtualCamObj = UnityEngine.Object.Instantiate(cameraDonor);
                                    virtualCamObj.name = camData.Name;

                                    // --- ЮВЕЛИРНАЯ НАСТРОЙКА ОБЪЕКТИВА (Лечим ультра-крупный план!) --- 🎯📸
                                    try
                                    {
                                        Camera unityCamComponent = virtualCamObj.GetComponent<Camera>();
                                        if (unityCamComponent != null)
                                        {
                                            // Выставляем стандартный угол обзора (60 градусов — классический общий вид в Unity).
                                            // Если захочется сделать план еще более общим, можно поставить 70 или 75!
                                            unityCamComponent.fieldOfView = 60f;

                                            // На всякий случай сбрасываем параметры ортографии и физической линзы, 
                                            // если китайские разработчики накрутили их в префабе донора
                                            unityCamComponent.orthographic = false;

                                            Plugin.Log.LogInfo($"[SDK_Camera_Lens] Объектив камеры '{camData.Name}' успешно переведен на стандартный FOV (60).");
                                        }

                                        // Мягко гасим HDRP-оффсеты, если они заставляли камеру косить в сторону
                                        var hdData = virtualCamObj.GetComponent("HDAdditionalCameraData");
                                        if (hdData != null)
                                        {
                                            // Если в вашей версии HDRP у HDAdditionalCameraData есть открытые поля для FOV/Апертуры,
                                            // их можно сбросить здесь, но обычно изменения базового unityCamComponent.fieldOfView более чем достаточно!
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Plugin.Log.LogError($"[SDK_Camera_Lens] Сбой настройки линзы объектива: {ex.Message}");
                                    }
                                    // ------------------------------------------------------------------
                                }
                                else
                                {
                                    // Фаллбэк, если сцена абсолютно пустая
                                    virtualCamObj = new GameObject(camData.Name);
                                }

                                virtualCamObj.transform.SetParent(camGroupTrans, false);
                                virtualCamObj.SetActive(false);
                                targetCamTrans = virtualCamObj.transform;
                                isNewCamera = true;
                            }

                            // 4. ПЕРЕВОДИМ ИЗ ЛОКАЛЬНОГО В ЖЕСТКОЕ МИРОВОЕ ПРОСТРАНСТВО КОМНАТЫ
                            // Теперь точка парит на высоте 1.5м ровно у дивана, а не на полу у портала спавна!
                            if (camData.pos != null)
                            {
                                Vector3 localPos = new Vector3(camData.pos.x, camData.pos.y, camData.pos.z);
                                targetCamTrans.position = __instance.transform.TransformPoint(localPos);
                            }

                            if (camData.rot != null)
                            {
                                Quaternion localRot = Quaternion.Euler(camData.rot.x, camData.rot.y, camData.rot.z);
                                targetCamTrans.rotation = __instance.transform.rotation * localRot;
                            }

                            // Регистрируем в нативный массив игры ТОЛЬКО если это действительно новый объект!
                            if (isNewCamera)
                            {
                                __instance.cameras.AddItem(targetCamTrans);
                                Plugin.Log.LogInfo($"[SDK_Camera_Core] Успешно инжектирован тяжелый HDRP-клон для: '{camData.Name}'");
                            }
                            else
                            {
                                Plugin.Log.LogInfo($"[SDK_Camera_Core] Координаты существующего клона '{camData.Name}' обновлены на лету без дублирования сущностей.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[SDK_Camera_Core] Критический краш инжектора камер: {ex.Message}");
                    }
                }
                // =========================================================================


                // Находим или создаем кастомную подпапку для НАШИХ поз внутри мебели, чтобы не захламлять оригинальный posesGroup
                Transform modPosesGroup = __instance.transform.Find("Mod_CustomPosesGroup");
                if (modPosesGroup == null)
                {
                    GameObject modGroupObj = new GameObject("Mod_CustomPosesGroup");
                    modGroupObj.transform.SetParent(__instance.transform, false);
                    modPosesGroup = modGroupObj.transform;
                }

                Pose[] allGamePoses = Resources.FindObjectsOfTypeAll<Pose>();
                RuntimeAnimatorController[] allControllers = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>();

                // Проходим циклом по позам из нашего JSON конфига
                foreach (PoseData poseConfig in config.InteractionPoses)
                {
                    if (poseConfig == null) continue;

                    // --- КРИТИЧЕСКИЙ ФИКС №2: ИСКЛЮЧАЕМ ДУБЛИРОВАНИЕ ПРИ СЛУЧАЙНОМ ПОВТОРНОМ ВЫЗОВЕ ---
                    bool alreadyInjected = false;
                    foreach (Transform existingPose in __instance.poses.items)
                    {
                        if (existingPose != null && existingPose.name == poseConfig.DisplayName)
                        {
                            alreadyInjected = true;
                            break;
                        }
                    }
                    if (alreadyInjected) continue; // Если поза уже добавлена к этой мебели — идем дальше

                    bool isCustomPose = poseConfig.Type.Equals("CustomJSON", System.StringComparison.OrdinalIgnoreCase) || poseConfig.Type.Contains("Кастомная");
                    bool isExternalModAnim = poseConfig.Type.Equals("PoseAnimationsMod", System.StringComparison.OrdinalIgnoreCase);

                    RuntimeAnimatorController targetController = null;
                    string searchName = (isCustomPose || isExternalModAnim) ? "UnarmedController" : poseConfig.ControllerName;

                    foreach (var rc in allControllers)
                    {
                        if (rc != null && rc.name == searchName) { targetController = rc; break; }
                    }

                    if (targetController == null && !isCustomPose && !isExternalModAnim) continue;

                    // Создаем новый GameObject для НАШЕЙ кастомной позы
                    GameObject newPoseObj = new GameObject(poseConfig.DisplayName);
                    newPoseObj.transform.SetParent(modPosesGroup, false); // Кладем в нашу изолированную папку мода

                    Pose newPose = newPoseObj.AddComponent<Pose>();
                    newPose.controller = targetController;
                    newPose.notshown = false;
                    newPose.locked = false;
                    newPose.crystals = 0;

                    // Инжекция картинки-иконки с диска
                    if (isCustomPose || isExternalModAnim)
                    {
                        newPose.categoryName = "Custom";
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

                        if (exactVanillaPose != null && exactVanillaPose.icon != null)
                        {
                            try
                            {
                                // --- ГЛУБОКОЕ КОПИРОВАНИЕ ТЕКСТУРЫ ДЛЯ ЗАЩИТЫ ОТ UNLOAD --- 🌟
                                Texture2D sourceTex = exactVanillaPose.icon;

                                // Создаем чистую рантайм-текстуру точно такого же размера и формата
                                Texture2D clonedTex = new Texture2D(sourceTex.width, sourceTex.height, sourceTex.format, sourceTex.mipmapCount > 1);

                                // Программный дубликат на уровне графического чипа
                                Graphics.CopyTexture(sourceTex, clonedTex);

                                newPose.icon = clonedTex;
                            }
                            catch (Exception ex)
                            {
                                // Фаллбэк на случай, если texture защищена от чтения/записи (Read/Write Disabled)
                                newPose.icon = UnityEngine.Object.Instantiate(exactVanillaPose.icon);
                                Plugin.Log.LogInfo($"[Injector] Использован Instantiate-клон для иконки {poseConfig.DisplayName}: {ex.Message}");
                            }

                            newPose.categoryName = exactVanillaPose.categoryName;
                            newPose.mood = exactVanillaPose.mood;
                        }
                        else
                        {
                            newPose.categoryName = "Dances";
                        }
                    }

                    // Настройка координат локатора позы
                    GameObject locObj = new GameObject("loc");
                    locObj.transform.SetParent(newPoseObj.transform, false);
                    locObj.transform.localPosition = new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z);
                    locObj.transform.localEulerAngles = new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z);
                    newPose.loc = locObj.transform;

                    newPoseObj.SetActive(false);

                    // --- КРИТИЧЕСКИЙ ФИКС №3: СЛИЯНИЕ НА УРОВНЕ КОЛЛЕКЦИИ ---
                    // Аккуратно пушим нашу позу в КОНЕЦ оригинального списка игры. Ванильные позы остаются на позициях 0, 1, 2...
                    __instance.poses.AddItem(newPoseObj.transform);
                }

                // Перерегистрируем мебель в глобальном трекере интерактивов, чтобы игра обновила кэш меню взаимодействия
                if (Global.code != null && Global.code.interactableFurnitures != null)
                {
                    Global.code.interactableFurnitures.items.Remove(__instance.transform);
                    Global.code.interactableFurnitures.AddItemDifferentObject(__instance.transform);
                }

                Plugin.Log.LogWarning($"[Injector] Слияние завершено! Всего доступных поз для {furnitureName}: {__instance.poses.items.Count} (включая ванильные и кастомные).");
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

    // === ПАТЧ 3: ГЛОБАЛЬНЫЙ ПЕРЕХВАТ ОКНА UIPOSE ДЛЯ АКТИВАЦИИ ПАНЕЛИ КАМЕР (Пункт 6 ТЗ) ===
    [HarmonyPatch(typeof(UIPose), "Open")]
    public class UIPoseGlobalUiBinderPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIPose __instance, Furniture furniture)
        {
            if (__instance == null || furniture == null) return;

            try
            {
                // Наш Этап 1 (Путь А): Жестко готовим ОЗУ-карту до отрисовки кнопок
                ConfigManager.InitializeRuntimeMemoryForFurniture(furniture);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[RAM_Error] Сбой пре-инициализации памяти в UIPose.Open: {ex.Message}");
            }

            try
            {
                // Находим или вешаем наш контроллер AnimationUiControls прямо на объект самого окна UIPose игры!
                // Теперь панель родится сразу при клике "Сесть" (в позах или анимациях) и будет жить до закрытия меню.
                AnimationUiControls uiControls = __instance.gameObject.GetComponent<AnimationUiControls>();
                if (uiControls == null)
                {
                    uiControls = __instance.gameObject.AddComponent<AnimationUiControls>();
                }

                // Инициализируем наш интерфейс, передавая ему чистую public-ссылку на мебель игры!
                uiControls.InitializeGlobal(furniture);
                Plugin.Log.LogInfo($"[SDK_UI] Панель мода успешно внедрена в окно UIPose для мебели '{furniture.name}'");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[SDK_UI] Сбой глобальной инициализации панели в UIPose.Open: {ex.Message}");
            }
        }
    }

    // === ПАТЧ 4: ПЕРЕХВАТ ОБНОВЛЕНИЯ ОКНА UIPOSE ДЛЯ УТОЧНЕНИЯ СОСТОЯНИЯ КАМЕР
    [HarmonyPatch(typeof(UIPose), "Refresh")]
    public class UIPose_Refresh_Event_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(UIPose __instance)
        {
            if (__instance == null || __instance.cameraIconGroup == null) return;

            // Находим наш кастомный интерфейс AnimationUiControls на сцене
            AnimationUiControls activeControls = UnityEngine.Object.FindObjectOfType<AnimationUiControls>();
            if (activeControls == null) return;

            // Пробегаемся по всем динамически созданным кнопкам ракурсов игры
            for (int i = 0; i < __instance.cameraIconGroup.childCount; i++)
            {
                Transform buttonTrans = __instance.cameraIconGroup.GetChild(i);
                Button btnComponent = buttonTrans?.GetComponent<Button>();

                if (btnComponent != null)
                {
                    // Привязываемся напрямую к родному клику ванильной кнопки! 📸
                    btnComponent.onClick.AddListener(() =>
                    {
                        // Делаем микро-вызов обновления состояния НАШЕЙ контекстной кнопки.
                        // Небольшая задержка Mono-корутины (или Invoke) в 0.05 сек нужна, чтобы игра успела переключить SetActive(true) на выбранной камере
                        activeControls.StartCoroutine(ExecuteDelayedUiUpdate(activeControls));
                    });
                }
            }

            // Сразу принудительно обновляем стейты нашей панели при самом запуске Refresh
            activeControls.UpdateInterfaceStates();
        }

        private static System.Collections.IEnumerator ExecuteDelayedUiUpdate(AnimationUiControls controls)
        {
            yield return new UnityEngine.WaitForSeconds(0.05f); // Даем игре 1 кадр переключить камеру
            if (controls != null)
            {
                controls.UpdateInterfaceStates(); // Мгновенно пересчитываем хамелеона!
            }
        }
    }

    // === ПАТЧ 5: УЛЬТИМАТИВНЫЙ ПОСТФИКС-ПЕРЕХВАТ ДЛЯ ОКНА UIPOSE (RELEASE 0.2.0 STABLE) ===
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

                // --- КРИТИЧЕСКИЙ ФИКС СЛИЯНИЯ: ЕСЛИ ПОЗА НЕ НАЙДЕНА В КОНФИГЕ МОДА ---
                if (currentPoseData == null)
                {
                    // Это значит, что игрок кликнул по оригинальной ВАНИЛЬНОЙ позе игры!
                    // Сносим наш кастомный плеер анимаций (используем уникальное имя переменной, чтобы не было конфликта)
                    var vanillaCleanupPlayer = characterComp.gameObject.GetComponent<FurnitureAnimationPlayer>();
                    if (vanillaCleanupPlayer != null)
                    {
                        UnityEngine.Object.Destroy(vanillaCleanupPlayer);
                    }

                    // Включаем встроенный аниматор игры обратно, чтобы ванильная поза могла запуститься!
                    if (characterComp.anim != null)
                    {
                        characterComp.anim.enabled = true;
                        characterComp.anim.speed = 1f;
                    }

                    // Выключаем проигрывание аудио, если оно работало
                    if (AnimationAudioManager.Instance != null)
                    {
                        AnimationAudioManager.Instance.StopAudio();
                    }

                    Plugin.Log.LogInfo($"[SDK_Icon] Клик по ванильной позе '{uiPoseName}'. Наш плеер уничтожен, управление возвращено игре.");
                    return; // Просто выходим, позволяя игре выполнить её стандартный метод DoPose
                }

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
                    if (AnimationAudioManager.Instance != null)
                    {
                        AnimationAudioManager.Instance.StopAudio();
                    }
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

    // === ПАТЧ 6: БЕЗОПАСНЫЙ ВЫХОД ИЗ ИНТЕРАКТИВА ===
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

    // === ПАТЧ 7: РОКИРОВКА КНОПОК И ПОЛНАЯ ЗАЧИСТКА ОКНА СОХРАНЕНИЯ (RELEASE 0.2.0 STABLE) ===
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
