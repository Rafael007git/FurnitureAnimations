using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PoseAnimations; // Прямая ссылка

namespace FurnitureAnimationsMod
{
    public static class PoseExporter
    {
        public static Texture2D _lastCapturedIcon = null;

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

            _lastCapturedIcon = null;

            // 2. ИСПОЛЬЗУЕМ НАШ СТРОГИЙ ДЕТЕКТОР ДЛЯ ОПРЕДЕЛЕНИЯ ТРОЙНОГО СОСТОЯНИЯ
            CharacterPoseState currentState = CharacterStateHelper.GetCurrentState(characterComp);

            string controllerName = "None";
            string buttonText = "Link Preset Pose for Furniture";
            string typeText = "Pose/Animation from the game";
            bool isCustomBakeMode = false;

            Plugin.Log.LogWarning($"[DEBUG_ICON] === СТАРТ ТРАССИРОВКИ ИКОНКИ (ОБНОВЛЕННЫЙ) ===");
            Plugin.Log.LogInfo($"[DEBUG_ICON] Вычисленное состояние куклы: {currentState}");

            switch (currentState)
            {
                case CharacterPoseState.PoseAnimationsModActive:
                    // ==========================================================
                    // ТИП 3: Внешняя JSON-анимация (мод aedenthorn) 💃
                    // ==========================================================
                    isCustomBakeMode = false;
                    controllerName = CharacterStateHelper.GetActiveModAnimationName(characterComp);
                    buttonText = "Link Animated Pose for Furniture";
                    typeText = "External Mod Animation (AnimatedPose)";

                    Plugin.Log.LogWarning($"[PoseExporter] Детектор: Поймали JSON-анимацию '{controllerName}'! Делаем скриншот с наложением иконки.");

                    // 1. Делаем базовый скisting скриншот куклы в движении
                    Texture2D rawScreenshot = null;
                    var photoCompAnim = uiInstance.GetComponent<TakePhotos>();
                    if (photoCompAnim != null && Global.code != null && Global.code.freeCamera != null)
                    {
                        Camera cam = Global.code.freeCamera.GetComponent<Camera>();
                        rawScreenshot = photoCompAnim.CameraCapture(cam, new Rect(0f, 0f, 300f, 300f), "");
                    }

                    if (rawScreenshot != null)
                    {
                        // 2. Достаем нашу иконку icon_animation из ресурсов проекта
                        Texture2D watermarkIcon = LoadWatermarkFromResources();

                        if (watermarkIcon != null)
                        {
                            // 3. Запекаем её в левый нижний угол скриншота
                            _lastCapturedIcon = ApplyWatermarkToBottomLeft(rawScreenshot, watermarkIcon);

                            // Очищаем временную текстуру водяного знака из памяти, чтобы не плодить утечки
                            UnityEngine.Object.Destroy(watermarkIcon);

                            Plugin.Log.LogInfo("[PoseExporter] Водяной знак 'icon_animation' успешно внедрен на скриншот анимации.");
                        }
                        else
                        {
                            // Фаллбэк: если ресурс не прочитался, оставляем чистый скриншот
                            _lastCapturedIcon = rawScreenshot;
                        }
                    }
                    break;

                case CharacterPoseState.GameAnimatorActive:
                    // ==========================================================
                    // ТИП 1: Иконка -> Unity (Ванильная поза/пресет игры) 🎮
                    // ==========================================================
                    isCustomBakeMode = false;
                    controllerName = characterComp?.anim?.runtimeAnimatorController?.name ?? "None";

                    // Убираем рантайм-суффикс Юнити, если он прицепился
                    if (controllerName.EndsWith("(Instance)"))
                        controllerName = controllerName.Replace("(Instance)", "").Trim();

                    buttonText = "Link Preset Pose for Furniture";
                    typeText = "Pose/Animation from the game";

                    Plugin.Log.LogWarning($"[DEBUG_ICON] Запуск Сценария А (Preset Link). Целевое имя контроллера: '{controllerName}'");

                    if (RM.code == null) Plugin.Log.LogError("[DEBUG_ICON] Ошибка: RM.code равен null!");
                    else if (RM.code.allFreePoses == null) Plugin.Log.LogError("[DEBUG_ICON] Ошибка: RM.code.allFreePoses равен null!");
                    else
                    {
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

                            if (checkedPosesCount <= 5 || currentPoseCtrlName.ToLower() == controllerName.ToLower())
                            {
                                Plugin.Log.LogInfo($" -> Проверка позы №{checkedPosesCount}: Имя='{currentPoseName}' | Контроллер в игре='{currentPoseCtrlName}'");
                            }

                            if (p.controller != null && p.controller.name == controllerName)
                            {
                                Plugin.Log.LogWarning($"[DEBUG_ICON] СОВПАДЕНИЕ 🎉 НАЙДЕНО! Поза: '{currentPoseName}'. Извлекаем родную иконку...");
                                _lastCapturedIcon = p.icon;
                                foundMatch = true;
                                break;
                            }
                        }

                        if (!foundMatch)
                        {
                            Plugin.Log.LogError($"[DEBUG_ICON] Сбой: Ни один контроллер в игре не совпал с целевым '{controllerName}'!");
                        }
                    }
                    break;

                case CharacterPoseState.CustomPoseJSON:
                    // ==========================================================
                    // ТИП 2: Гизмо (Ручное запекание костей Диорамы) 🛠
                    // ==========================================================
                    isCustomBakeMode = true;
                    controllerName = "CustomJSON";
                    buttonText = "Save Custom Pose for Furniture";
                    typeText = "User-made Custom Pose";

                    Plugin.Log.LogWarning("[PoseExporter] Запуск Сценария Б (Режим Гизмо). Сейчас будет скриншот!");

                    var photoComp = uiInstance.GetComponent<TakePhotos>();
                    if (photoComp != null && Global.code != null && Global.code.freeCamera != null)
                    {
                        Camera cam = Global.code.freeCamera.GetComponent<Camera>();
                        _lastCapturedIcon = photoComp.CameraCapture(cam, new Rect(0f, 0f, 300f, 300f), "");
                    }
                    break;
            }

            Texture2D finalPreview = _lastCapturedIcon;

            // Динамически меняем текст нашей кнопки интерактива на сцене игры
            Transform sdkBtnTrans = uiInstance.transform.Find("Button_SaveInteract");
            UnityEngine.UI.Text buttonTextComp = sdkBtnTrans?.GetComponentInChildren<UnityEngine.UI.Text>();
            if (buttonTextComp != null)
            {
                buttonTextComp.text = buttonText;
            }

            // Расчет локального смещения относительно мебели
            Vector3 exactLocPos = closestFurniture.transform.InverseTransformPoint(playerPos);
            Quaternion localQuaternion = Quaternion.Inverse(closestFurniture.transform.rotation) * uiInstance.selectedCharacter.rotation;
            Vector3 exactLocRot = localQuaternion.eulerAngles;

            // Формируем текст сообщения для нашего идеального диалога
            string promptText = $"Do you want to save this pose for <color=yellow>{furnitureName}</color>?\n" +
                                $"Type: {typeText}\n" +
                                $"Identifier: {controllerName}";

            // Шаг A: Обманываем скрытую валидацию игры, чтобы пропустить сохранение
            if (uiInstance != null)
            {
                uiInstance.poseName = "FurniturePose";
                uiInstance.creatorName = "ModAuthor";
            }

            // Шаг Б: Вызываем наше отлаженное диалоговое окно
            EditorUiManager.ShowNativeStyleDialog(
                uiInstance,
                promptText,
                finalPreview,
                () => {
                    SavePoseToDataFolder(furnitureName, controllerName, exactLocPos, exactLocRot, currentState, characterComp);
                },
                currentState // <-- ПРОСТО ДОПИШИТЕ ЭТУ ПЕРЕМЕННУЮ СЮДА ЧЕРЕЗ ЗАПЯТУЮ! 🌟
            );
        }

        private static void SavePoseToDataFolder(string furnitureName, string controller, Vector3 pos, Vector3 rot, CharacterPoseState poseState, CharacterCustomization character)
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

                bool isCustom = (poseState == CharacterPoseState.CustomPoseJSON);
                // Красивое имя в списке
                string generatedPoseName = isCustom ? $"Custom Pose — {DateTime.Now:dd.MM HH:mm}" : $"Animation — {controller}";
                string customAnimFileName = isCustom ? $"{furnitureName}_{timestamp}.json" : "";

                // ВЫЧИСЛЯЕМ СТРОКОВЫЙ ТИП НА ОСНОВЕ НАШЕГО ENUM
                string savedType = "Vanilla";
                switch (poseState)
                {
                    case CharacterPoseState.CustomPoseJSON:
                        savedType = "CustomJSON";
                        break;
                    case CharacterPoseState.PoseAnimationsModActive:
                        savedType = "PoseAnimationsMod"; // <--- Теперь в файл запишется этот тип!
                        break;
                    case CharacterPoseState.GameAnimatorActive:
                        savedType = "Vanilla";
                        break;
                }

                PoseData newPoseData = new PoseData
                {
                    DisplayName = generatedPoseName,
                    Type = savedType, // <--- Передаем вычисленное строковое значение
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

                // Сохранение иконки строго в \FurnitureConfigs\Icons\ с защитой от ванильного краша
                if (_lastCapturedIcon != null)
                {
                    string iconName = isCustom ? $"{furnitureName}_{timestamp}.png" : $"{controller}.png";
                    string iconFullPath = Path.Combine(ConfigManager.IconsPath, iconName);

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
                Furniture currentPropOnScene = FindClosestFurniture(character.transform.position, 5f);
                if (currentPropOnScene != null)
                {
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
                } // <--- Функция поиска правильно закрывается

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

                // Этот return честно возвращает JSON из самого метода ExportBonesToCustomJson!
                return Newtonsoft.Json.JsonConvert.SerializeObject(bakedPoseData, Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[BonesBake] Ошибка: {ex.Message}");
                return "{}";
            }
        }

        private static Texture2D LoadWatermarkFromResources()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                // Полное имя ресурса: ПространствоИмен.Папка.ИмяФайла
                using (var stream = assembly.GetManifestResourceStream("FurnitureAnimations.Resources.icon_animation.png"))
                {
                    if (stream == null)
                    {
                        Plugin.Log.LogError("[PoseExporter] Встроенный ресурс icon_animation.png не найден!");
                        return null;
                    }

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);

                    // Создаем временную текстуру, LoadImage сама изменит её размер под формат PNG
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(buffer))
                    {
                        return texture;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseExporter] Ошибка загрузки водяного знака из ресурсов: {ex.Message}");
            }
            return null;
        }

        private static Texture2D ApplyWatermarkToBottomLeft(Texture2D baseTexture, Texture2D watermark)
        {
            if (baseTexture == null) return null;
            if (watermark == null) return baseTexture;

            try
            {
                // Создаем новую незащищенную текстуру для чтения/записи, чтобы избежать ошибок RenderTextures
                Texture2D readableBase = new Texture2D(baseTexture.width, baseTexture.height, TextureFormat.RGBA32, false);

                // Переносим пиксели из скриншота (через GetPixels/SetPixels или Graphics.CopyTexture)
                // Так как скриншот из CameraCapture обычно доступен для чтения, берем пиксели напрямую:
                Color[] basePixels = baseTexture.GetPixels();
                readableBase.SetPixels(basePixels);

                int baseWidth = readableBase.width;
                int baseHeight = readableBase.height;

                // Определяем размер водяного знака. Сделаем его, например, 20% от размера скриншота (или фиксированно 48x48)
                // Давай сделаем пропорциональный размер, например 1/5 от ширины скриншота
                int wmWidth = baseWidth / 5;
                int wmHeight = (watermark.height * wmWidth) / watermark.width; // Сохраняем пропорции

                // Ресэмплим водяной знак под нужный размер (простейший Bilinear / Point рескейл)
                // Для этого временно воспользуемся RenderTexture или стандартным методом, но проще сделать попиксельно:
                // Чтобы не усложнять, если иконка в ресурсах изначально маленькая (например, 32x32 или 64x64), можно брать её оригинальный размер:
                int targetWmWidth = Mathf.Min(watermark.width, baseWidth / 6);
                int targetWmHeight = Mathf.Min(watermark.height, baseHeight / 6);

                // Отступ от левого нижнего угла (в пикселях)
                int paddingX = 0;
                int paddingY = 0;

                // Попиксельное наложение с учетом альфа-канала (Alpha Blending)
                for (int y = 0; y < targetWmHeight; y++)
                {
                    for (int x = 0; x < targetWmWidth; x++)
                    {
                        int targetX = paddingX + x;
                        int targetY = paddingY + y;

                        if (targetX >= baseWidth || targetY >= baseHeight) continue;

                        // Вычисляем интерполяцию для масштабирования оригинальной иконки
                        float u = (float)x / targetWmWidth;
                        float v = (float)y / targetWmHeight;
                        Color wmPixel = watermark.GetPixelBilinear(u, v);

                        if (wmPixel.a > 0.01f) // Если пиксель не прозрачный
                        {
                            Color bgPixel = readableBase.GetPixel(targetX, targetY);
                            // Классическая формула линейного смешивания цветов по альфе
                            Color blendedPixel = Color.Lerp(bgPixel, wmPixel, wmPixel.a);
                            blendedPixel.a = bgPixel.a; // сохраняем альфу фона

                            readableBase.SetPixel(targetX, targetY, blendedPixel);
                        }
                    }
                }

                readableBase.Apply();
                return readableBase;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseExporter] Ошибка запекания водяного знака: {ex.Message}");
                return baseTexture;
            }
        }

    }
}
