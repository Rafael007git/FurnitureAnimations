using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FurnitureAnimationsMod
{
    public static class PoseExporter
    {
        private static Texture2D _lastCapturedIcon = null;

        public static void OnSaveInteractClicked(UIFreePose uiInstance)
        {
            if (uiInstance == null || uiInstance.selectedCharacter == null) return;

            // 1. Проверяем дистанцию до мебели (5 метров)
            Vector3 playerPos = uiInstance.selectedCharacter.position;
            Furniture closestFurniture = FindClosestFurniture(playerPos, 5f);

            if (closestFurniture == null)
            {
                if (Global.code != null && Global.code.uiCombat != null)
                    Global.code.uiCombat.AddPrompt("No interactive furniture found within 5 meters!");
                return;
            }

            string furnitureName = closestFurniture.name.Replace("(Clone)", "").Trim();
            CharacterCustomization characterComp = uiInstance.selectedCharacter.GetComponent<CharacterCustomization>();

            // 2. ИЩЕМ НАШУ УЛЬТИМАТИВНУЮ КНОПКУ НА СЦЕНЕ ДЛЯ ОПРЕДЕЛЕНИЯ РЕЖИМА
            Transform sdkBtnTrans = uiInstance.transform.Find("Button_SaveInteract");
            UnityEngine.UI.Text buttonTextComp = sdkBtnTrans?.GetComponentInChildren<UnityEngine.UI.Text>();
            string currentButtonText = buttonTextComp != null ? buttonTextComp.text : "";

            // Железно определяем режим на основе текста кнопки, который выбрал пользователь!
            bool isCustomBakeMode = currentButtonText == "Save Custom Pose for Furniture";

            string controllerName = "None";
            _lastCapturedIcon = null;

            Plugin.Log.LogWarning($"[DEBUG_ICON] === СТАРТ ТРАССИРОВКИ ИКОНКИ ===");
            Plugin.Log.LogInfo($"[DEBUG_ICON] Выбранный режим: isCustomBakeMode = {isCustomBakeMode}");
            Plugin.Log.LogInfo($"[DEBUG_ICON] Текст кнопки на сцене: '{currentButtonText}'");

            if (!isCustomBakeMode)
            {
                // Сценарий А: Link Preset Pose (Зеленый режим кнопки)
                controllerName = (characterComp?.anim?.runtimeAnimatorController?.name ?? "None");
                Plugin.Log.LogWarning($"[DEBUG_ICON] Запуск Сценария А (Preset Link). Целевое имя контроллера: '{controllerName}'");

                // Проверяем доступность игровых реестров
                if (RM.code == null) Plugin.Log.LogError("[DEBUG_ICON] Ошибка: RM.code равен null!");
                else if (RM.code.allFreePoses == null) Plugin.Log.LogError("[DEBUG_ICON] Ошибка: RM.code.allFreePoses равен null!");
                else
                {
                    Plugin.Log.LogInfo($"[DEBUG_ICON] Успешно зашли в RM.allFreePoses. Всего элементов для перебора: {RM.code.allFreePoses.items.Count}");

                    int checkedPosesCount = 0;
                    bool foundMatch = false;

                    foreach (Transform t in RM.code.allFreePoses.items)
                    {
                        if (t == null) continue;
                        checkedPosesCount++;

                        var p = t.GetComponent<global::Pose>();
                        if (p == null) continue;

                        string currentPoseName = p.name ?? "NULL";
                        string currentPoseCtrlName = p.controller != null ? p.controller.name : "NULL";

                        // Спамим в лог каждые несколько поз, чтобы увидеть реальные имена контроллеров в игре
                        if (checkedPosesCount <= 5 || currentPoseCtrlName.ToLower() == controllerName.ToLower())
                        {
                            Plugin.Log.LogInfo($"    -> Проверка позы №{checkedPosesCount}: Имя='{currentPoseName}' | Контроллер в игре='{currentPoseCtrlName}'");
                        }

                        if (p.controller != null && p.controller.name == controllerName)
                        {
                            Plugin.Log.LogWarning($"[DEBUG_ICON] 🎉 СОВПАДЕНИЕ НАЙДЕНО! Поза: '{currentPoseName}'. Извлекаем родную иконку...");
                            _lastCapturedIcon = p.icon;

                            if (_lastCapturedIcon == null) Plugin.Log.LogError("[DEBUG_ICON] Критично: p.icon у этой позы равен null!");
                            else Plugin.Log.LogInfo($"[DEBUG_ICON] Успешно записали p.icon в _lastCapturedIcon. Размеры: {_lastCapturedIcon.width}x{_lastCapturedIcon.height}");

                            foundMatch = true;
                            break;
                        }
                    }

                    if (!foundMatch)
                    {
                        Plugin.Log.LogError($"[DEBUG_ICON] ❌ Сбой: Цикл завершился, но ни один контроллер в игре не совпал с целевым '{controllerName}'!");
                    }
                }
            }
            else
            {
                // Сценарий Б: Save Custom Pose (Бирюзовый режим кнопки)
                controllerName = "CustomJSON";
                Plugin.Log.LogWarning("[DEBUG_ICON] Запуск Сценария Б (Кастомная поза). Сейчас будет скриншот!");

                var photoComp = uiInstance.GetComponent<TakePhotos>();
                if (photoComp == null) Plugin.Log.LogError("[DEBUG_ICON] Ошибка: Компонент TakePhotos не найден на uiInstance!");

                if (photoComp != null && Global.code != null && Global.code.freeCamera != null)
                {
                    Camera cam = Global.code.freeCamera.GetComponent<Camera>();
                    _lastCapturedIcon = photoComp.CameraCapture(cam, new Rect(0f, 0f, 300f, 300f), "");

                    if (_lastCapturedIcon == null) Plugin.Log.LogError("[DEBUG_ICON] Ошибка: Метод CameraCapture вернул null!");
                    else Plugin.Log.LogInfo($"[DEBUG_ICON] Скриншот успешно сгенерирован. Размеры: {_lastCapturedIcon.width}x{_lastCapturedIcon.height}");
                }
            }

            Plugin.Log.LogWarning($"[DEBUG_ICON] Финальный статус _lastCapturedIcon перед отправкой в UI: {(_lastCapturedIcon != null ? "НЕ NULL (Есть картинка)" : "NULL (Пусто)")}");
            Plugin.Log.LogWarning($"[DEBUG_ICON] === КОНЕЦ ТРАССИРОВКИ ИКОНКИ ===");


            // Расчет локального смещения относительно мебели (Как на стр 19)
            Vector3 exactLocPos = closestFurniture.transform.InverseTransformPoint(playerPos);
            Quaternion localQuaternion = Quaternion.Inverse(closestFurniture.transform.rotation) * uiInstance.selectedCharacter.rotation;
            Vector3 exactLocRot = localQuaternion.eulerAngles;

            // Формируем текст сообщения
            string promptText = $"Do you want to save this pose for <color=yellow>{furnitureName}</color>?\n" +
                                $"Type: {(isCustomBakeMode ? "User-made Custom Pose" : "Pose/Animation from the game")}\n" +
                                $"Identifier: {controllerName}";

            // ==========================================================
            // 🔀 ВОЗВРАТ РАЗДЕЛЕНИЯ ЛОГИКИ ИКОНОК
            // ==========================================================
            Texture2D finalPreview = null;

            if (!isCustomBakeMode)
            {
                // Сценарий А: Готовая поза — вытаскиваем сохранённую иконку из игры
                finalPreview = _lastCapturedIcon;
                Plugin.Log.LogInfo("[PoseExporter] В диалог уходит оригинальная иконка ванильной позы.");
            }
            else
            {
                // Сценарий Б: Кастомная поза — генерируем свежий скриншот-иконку нашей камерой
                var photoComp = uiInstance.GetComponent<TakePhotos>();
                if (photoComp != null && Global.code != null && Global.code.freeCamera != null)
                {
                    Camera cam = Global.code.freeCamera.GetComponent<Camera>();
                    finalPreview = photoComp.CameraCapture(cam, new Rect(0f, 0f, 300f, 300f), "");
                    Plugin.Log.LogInfo("[PoseExporter] В диалог уходит свежий скриншот кастомной позы.");
                }
            }

            // Шаг A: Заполняем переменные оригинального скрипта, чтобы пробить валидацию игры
            if (uiInstance != null)
            {
                uiInstance.poseName = "FurniturePose";
                uiInstance.creatorName = "ModAuthor";
            }

            // Шаг Б: Вызываем диалог, передавая ИМЕННО КОРРЕКТНУЮ finalPreview
            EditorUiManager.ShowNativeStyleDialog(
                uiInstance,
                promptText,
                finalPreview, // Наш исправленный выбор!
                () => {
                    // При нажатии запускаем физическую запись файлов конфига мода
                    SavePoseToDataFolder(furnitureName, controllerName, exactLocPos, exactLocRot, isCustomBakeMode, characterComp);
                }
            );
        }


        private static void SavePoseToDataFolder(string furnitureName, string controller, Vector3 pos, Vector3 rot, bool isCustom, CharacterCustomization character)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{furnitureName}_Config.json";
                string fullPath = Path.Combine(ConfigManager.PrefabsConfigPath, fileName);

                FurnitureConfig configToSave;

                if (File.Exists(fullPath))
                {
                    string existingJson = File.ReadAllText(fullPath);
                    configToSave = Newtonsoft.Json.JsonConvert.DeserializeObject<FurnitureConfig>(existingJson) ?? new FurnitureConfig();
                }
                else
                {
                    configToSave = new FurnitureConfig { FurniturePrefabName = furnitureName, InteractionPoses = new List<PoseData>() };
                }

                if (configToSave.InteractionPoses == null) configToSave.InteractionPoses = new List<PoseData>();

                // Исправлено: Красивое имя в списке
                string generatedPoseName = isCustom ? $"Custom Pose — {DateTime.Now:dd.MM HH:mm}" : $"Animation — {controller}";
                string customAnimFileName = isCustom ? $"{furnitureName}_{timestamp}.json" : "";

                PoseData newPoseData = new PoseData
                {
                    DisplayName = generatedPoseName,
                    Type = isCustom ? "CustomJSON" : "Vanilla",
                    ControllerName = controller,
                    JsonFileName = customAnimFileName,
                    LocPosition = new Vector3Data { x = (float)Math.Round(pos.x, 4), y = (float)Math.Round(pos.y, 4), z = (float)Math.Round(pos.z, 4) },
                    LocRotation = new Vector3Data { x = (float)Math.Round(rot.x, 4), y = (float)Math.Round(rot.y, 4), z = (float)Math.Round(rot.z, 4) },
                    Cameras = new List<CameraData>()
                };

                // Сохранение бинарного слепка костей (Сценарий Б)
                if (isCustom && character != null)
                {
                    string customAnimFullPath = Path.Combine(ConfigManager.CustomAnimsPath, customAnimFileName);
                    string bonesJson = ExportBonesToCustomJson(character);
                    File.WriteAllText(customAnimFullPath, bonesJson);
                }

                // ИСПРАВЛЕНО: Сохранение иконки строго в \FurnitureConfigs\Icons\ с защитой от ванильного краша
                if (_lastCapturedIcon != null)
                {
                    string iconName = isCustom ? $"{furnitureName}_{timestamp}.png" : $"{controller}.png";
                    string iconFullPath = Path.Combine(ConfigManager.IconsPath, iconName); // Путь автоматически подхватит FurnitureConfigs\Icons!

                    bool canWriteTexture = true;
                    try
                    {
                        _lastCapturedIcon.GetPixels();
                    }
                    catch (Exception)
                    {
                        canWriteTexture = false;
                        Plugin.Log.LogInfo($"[PoseExporter] Текстура '{_lastCapturedIcon.name}' защищена от чтения. Пропускаем физическую запись PNG, игра подтянет её из памяти.");
                    }

                    if (canWriteTexture)
                    {
                        byte[] pngBytes = UnityEngine.ImageConversion.EncodeToPNG(_lastCapturedIcon);
                        File.WriteAllBytes(iconFullPath, pngBytes);
                        Plugin.Log.LogWarning($"[PoseExporter] Иконка успешно сохранена на диск: {iconFullPath}");
                    }
                }

                // Теперь сохранение гарантированно ДОЙДЕТ до конца списка без краша!
                configToSave.InteractionPoses.Add(newPoseData);
                string finalJson = Newtonsoft.Json.JsonConvert.SerializeObject(configToSave, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(fullPath, finalJson);

                ConfigManager.LoadedConfigs[furnitureName] = configToSave;

                // ХИРУРГИЧЕСКИЙ ВЫЗОВ МГНОВЕННОГО РЕФРЕША:
                // Ищем объект мебели, который мы сейчас редактировали на сцене Unity
                Furniture currentPropOnScene = FindClosestFurniture(character.transform.position, 5f);
                if (currentPropOnScene != null)
                {
                    // Вызываем наш метод пересборки кнопок интерактива!
                    FurnitureInjectorPatch.RebuildFurniturePoses(currentPropOnScene);
                    Plugin.Log.LogWarning($"[PoseExporter] Рантайм-рефреш меню интерактива для {furnitureName} выполнен успешно!");
                }

                if (Global.code != null && Global.code.uiCombat != null)
                    Global.code.uiCombat.ShowHeader("Поза успешно сохранена!");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseExporter] Критическая ошибка записи: {ex.Message}");
            }
        }

        // Обновленный метод поиска: теперь принимает максимальную дистанцию (радиус)
        public static Furniture FindClosestFurniture(Vector3 playerPosition, float maxDistance = 5f)
        {
            Furniture closestFurniture = null;
            Furniture[] allFurnitures = UnityEngine.Object.FindObjectsOfType<Furniture>();

            foreach (Furniture f in allFurnitures)
            {
                if (f == null) continue;
                float distance = Vector3.Distance(playerPosition, f.transform.position);
                if (distance < maxDistance)
                {
                    maxDistance = distance;
                    closestFurniture = f;
                }
            }
            return closestFurniture;
        }

        public static string ExportBonesToCustomJson(CharacterCustomization user)
        {
            if (user == null) return "{}";
            try
            {
                var bakedPoseData = new Dictionary<string, object>();

                // Локальная функция поиска дочерних объектов
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
                } // <--- ЗДЕСЬ ФУНКЦИЯ ПОИСКА ПРАВИЛЬНО ЗАКРЫВАЕТСЯ!

                // Пробегаемся строго по нашему эталонному списку имен из реестра Диорамы
                foreach (string targetName in DioramaConstants.AnatomyBoneRegistry)
                {
                    Transform element = FindChildRecursive(user.transform, targetName);
                    if (element == null) continue;

                    Light lightComponent = element.GetComponent<Light>();
                    if (lightComponent != null)
                    {
                        bakedPoseData[targetName] = new
                        {
                            type = "Light",
                            enabled = lightComponent.enabled,
                            intensity = lightComponent.intensity,
                            range = lightComponent.range,
                            pos = new { x = (float)Math.Round(element.localPosition.x, 4), y = (float)Math.Round(element.localPosition.y, 4), z = (float)Math.Round(element.localPosition.z, 4) },
                            color = new { r = lightComponent.color.r, g = lightComponent.color.g, b = lightComponent.color.b }
                        };
                        continue;
                    }

                    if (DioramaConstants.PositionalObjectsRegistry.Contains(targetName))
                    {
                        bakedPoseData[targetName] = new
                        {
                            type = "Bone",
                            rot = new { x = (float)Math.Round(element.localEulerAngles.x, 4), y = (float)Math.Round(element.localEulerAngles.y, 4), z = (float)Math.Round(element.localEulerAngles.z, 4) },
                            pos = new { x = (float)Math.Round(element.localPosition.x, 4), y = (float)Math.Round(element.localPosition.y, 4), z = (float)Math.Round(element.localPosition.z, 4) }
                        };
                    }
                    else
                    {
                        bakedPoseData[targetName] = new
                        {
                            type = "Bone",
                            rot = new { x = (float)Math.Round(element.localEulerAngles.x, 4), y = (float)Math.Round(element.localEulerAngles.y, 4), z = (float)Math.Round(element.localEulerAngles.z, 4) }
                        };
                    }
                }

                // Теперь этот return честно возвращает JSON из самого метода ExportBonesToCustomJson!
                return Newtonsoft.Json.JsonConvert.SerializeObject(bakedPoseData, Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[BonesBake] Ошибка: {ex.Message}");
                return "{}";
            }

            return "{}";
        }

    }
}

