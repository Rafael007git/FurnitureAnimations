using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FurnitureAnimationsMod
{
    public static class PoseExporter
    {
        // Метод, который вызывается при клике на нашу новую UI кнопку
        public static void OnSaveInteractClicked(UIFreePose uiInstance)
        {
            if (uiInstance == null || uiInstance.selectedCharacter == null)
            {
                Plugin.Log.LogError("[PoseExporter] Ошибка: Нет активного персонажа в меню FreePose!");
                return;
            }

            // 1. АВТО-ПОИСК МЕБЕЛИ ПО СФЕРЕ (10 метров)
            Vector3 playerPos = uiInstance.selectedCharacter.position;
            Furniture closestFurniture = FindClosestFurniture(playerPos);

            if (closestFurniture == null)
            {
                // Если мебели нет в радиусе 10м, выводим предупреждение на экран игры
                if (Global.code != null && Global.code.uiCombat != null)
                {
                    Global.code.uiCombat.AddPrompt("Рядом нет мебели в радиусе 10 метров!");
                }
                return;
            }

            string furnitureName = closestFurniture.name.Replace("(Clone)", "").Trim();

            // 2. ВЕТВЛЕНИЕ ЛОГИКИ: АНИМАЦИЯ ИЛИ КАСТАМНАЯ ПОЗА
            string controllerName = "None";
            Texture2D finalIcon = null;
            bool isCustom = uiInstance.isCustomPoseMode;

            CharacterCustomization characterComp = uiInstance.selectedCharacter.GetComponent<CharacterCustomization>();

            if (!isCustom)
            {
                // Сценарий А: Активна ванильная анимация движка игры
                if (characterComp != null && characterComp.anim != null && characterComp.anim.runtimeAnimatorController != null)
                {
                    controllerName = characterComp.anim.runtimeAnimatorController.name;
                    Plugin.Log.LogInfo($"[PoseExporter] Обнаружена ванильная анимация: {controllerName}");
                }

                // Пытаемся вытащить родную иконку из текущей активной позы UI игры
                // Для этого ищем запущенную позу в RM
                if (RM.code != null && RM.code.allFreePoses != null)
                {
                    foreach (Transform t in RM.code.allFreePoses.items)
                    {
                        var p = t.GetComponent<global::Pose>();
                        if (p != null && p.controller != null && p.controller.name == controllerName)
                        {
                            finalIcon = p.icon;
                            break;
                        }
                    }
                }
            }
            else
            {
                // Сценарий Б: Кастомная поза ( Advanced Free Pose )
                controllerName = "CustomJSON";
                Plugin.Log.LogInfo("[PoseExporter] Обнаружена кастомная поза скелета. Генерируем скриншот-иконку...");

                // Используем встроенный в UIFreePose фотокомпонент игры из dnSpy!
                var photoComp = uiInstance.GetComponent<TakePhotos>();
                if (photoComp != null && Global.code != null && Global.code.freeCamera != null)
                {
                    Camera cam = Global.code.freeCamera.GetComponent<Camera>();
                    // Снимаем квадратное превью 300x300, как это делает сама игра в методе OpenSaveFreePosePanel
                    finalIcon = photoComp.CameraCapture(cam, new Rect(0f, 0f, 300f, 300f), "");
                }
            }

            // 3. РАСЧЕТ ТОЧНЫХ СМЕЩЕНИЙ ОТНОСИТЕЛЬНО НАЙДЕННОЙ МЕБЕЛИ
            Vector3 exactLocPos = closestFurniture.transform.InverseTransformPoint(playerPos);
            Quaternion localQuaternion = Quaternion.Inverse(closestFurniture.transform.rotation) * uiInstance.selectedCharacter.rotation;
            Vector3 exactLocRot = localQuaternion.eulerAngles;

            // 4. ВЫЗОВ ДИАЛОГОВОГО ОКНА ПОДТВЕРЖДЕНИЯ
            string promptText = $"Вы хотите сохранить эту позу для {furnitureName}?\n" +
                                $"Тип: {(isCustom ? "Кастомная" : "Ванильная")}\n" +
                                $"Координаты: {exactLocPos.ToString("F2")}";

            Plugin.Log.LogWarning($"[PoseExporter] Расчет завершен для {furnitureName}. Выводим окно...");

            // Временно выводим в лог BepInEx готовый результат, пока пишем GUI менеджер
            PrintDebugJson(furnitureName, controllerName, exactLocPos, exactLocRot);

            // Инициируем диалоговое окно
            EditorUiManager.ShowConfirmationDialog(promptText, finalIcon, () =>
            {
                // Колбэк при нажатии кнопки "ДА" - Запись в JSON
                SavePoseToDataFolder(furnitureName, controllerName, exactLocPos, exactLocRot, isCustom, characterComp);
            });
        }

        private static Furniture FindClosestFurniture(Vector3 playerPosition)
        {
            Furniture closestFurniture = null;
            float maxDistance = 10f;
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

        private static void SavePoseToDataFolder(string furnitureName, string controller, Vector3 pos, Vector3 rot, bool isCustom, CharacterCustomization character)
        {
            try
            {
                // 1. Формируем пути к файлу конфигурации мебели
                string fileName = $"{furnitureName}_Config.json";
                string fullPath = Path.Combine(ConfigManager.PrefabsConfigPath, fileName);

                FurnitureConfig configToSave;

                // 2. Если файл уже существует, считываем его, чтобы не затереть старые позы
                if (File.Exists(fullPath))
                {
                    Plugin.Log.LogInfo($"[PoseExporter] Файл конфигурации {fileName} найден. Читаем существующие позы...");
                    string existingJson = File.ReadAllText(fullPath);
                    configToSave = Newtonsoft.Json.JsonConvert.DeserializeObject<FurnitureConfig>(existingJson);

                    if (configToSave == null) configToSave = new FurnitureConfig { FurniturePrefabName = furnitureName, InteractionPoses = new List<PoseData>() };
                    if (configToSave.InteractionPoses == null) configToSave.InteractionPoses = new List<PoseData>();
                }
                else
                {
                    // Если файла нет, инициализируем новую чистую структуру
                    Plugin.Log.LogInfo($"[PoseExporter] Создаем новый конфигурационный файл для: {furnitureName}");
                    configToSave = new FurnitureConfig
                    {
                        FurniturePrefabName = furnitureName,
                        InteractionPoses = new List<PoseData>()
                    };
                }

                // 3. Формируем объект новой позы
                string generatedPoseName = $"Интерактив — {DateTime.Now:dd.MM HH:mm:ss}";

                PoseData newPoseData = new PoseData
                {
                    DisplayName = generatedPoseName,
                    Type = "Vanilla",
                    ControllerName = controller,
                    JsonFileName = isCustom ? $"{furnitureName}_{DateTime.Now:yyyyMMdd_HHmmss}.json" : "",
                    LocPosition = new Vector3Data { x = (float)Math.Round(pos.x, 4), y = (float)Math.Round(pos.y, 4), z = (float)Math.Round(pos.z, 4) },
                    LocRotation = new Vector3Data { x = (float)Math.Round(rot.x, 4), y = (float)Math.Round(rot.y, 4), z = (float)Math.Round(rot.z, 4) },
                    Cameras = new List<CameraData>() // Список камер оставляем пустым по нашей спецификации 0.1.0 Stable
                };

                // 4. Если Сценарий Б (Кастомная поза) — вызываем экспорт костей в отдельный файл
                if (isCustom && character != null)
                {
                    string customAnimFileName = newPoseData.JsonFileName;
                    string customAnimFullPath = Path.Combine(ConfigManager.CustomAnimsPath, customAnimFileName);

                    // Запрашиваем экспорт иерархии костей (заглушка или вызов метода Диорамы)
                    string bonesJson = ExportBonesToCustomJson(character);
                    File.WriteAllText(customAnimFullPath, bonesJson);
                    Plugin.Log.LogWarning($"[PoseExporter] Бинарный слепок скелета сохранен в: {customAnimFileName}");
                }

                // 5. Добавляем позу в общий список
                configToSave.InteractionPoses.Add(newPoseData);

                // 6. Сериализуем и перезаписываем файл конфигурации мебели на диск с красивыми отступами
                string finalJson = Newtonsoft.Json.JsonConvert.SerializeObject(configToSave, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(fullPath, finalJson);

                Plugin.Log.LogWarning($"[PoseExporter] УСПЕШНО ЗАПИСАНО НА ДИСК! Конфиг обновлен: {fullPath}");

                // 7. Мгновенно обновляем рантайм-базу данных мода, чтобы поза сразу же появилась в игре без перезапуска!
                ConfigManager.LoadedConfigs[furnitureName] = configToSave;

                // Выводим красивую плашку в интерфейс самой игры
                if (Global.code != null && Global.code.uiCombat != null)
                {
                    Global.code.uiCombat.ShowHeader("Поза успешно сохранена в конфиг!");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseExporter] Критическая ошибка сохранения: {ex.Message}");
            }
        }


        // === ВСПОМОГАТЕЛЬНЫЙ МЕТОД ДЛЯ ВЫВОДА В КОНСОЛЬ ===
        private static void PrintDebugJson(string furnitureName, string controller, Vector3 pos, Vector3 rot)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"\n==================================================");
            sb.AppendLine($"\"DisplayName\": \"Новая поза\",");
            sb.AppendLine($"\"Type\": \"Vanilla\",");
            sb.AppendLine($"\"ControllerName\": \"{controller}\",");
            sb.AppendLine($"\"LocPosition\": {{ \"x\": {pos.x.ToString("F4").Replace(",", ".")}, \"y\": {pos.y.ToString("F4").Replace(",", ".")}, \"z\": {pos.z.ToString("F4").Replace(",", ".")} }},");
            sb.AppendLine($"\"LocRotation\": {{ \"x\": {rot.x.ToString("F4").Replace(",", ".")}, \"y\": {rot.y.ToString("F4").Replace(",", ".")}, \"z\": {rot.z.ToString("F4").Replace(",", ".")} }}");
            sb.AppendLine($"==================================================");
            Plugin.Log.LogWarning(sb.ToString());
        }

        // === МЕТОД Г: ЭКСПОРТ КОСТЕЙ И ОГОНЬКОВ ПО СПРАВОЧНИКУ ДИОРАМЫ ===
        public static string ExportBonesToCustomJson(CharacterCustomization user)
        {
            if (user == null) return "{}";

            Plugin.Log.LogInfo("[PoseExporter] Запуск запекания скелета по справочнику DioramaConstants...");

            try
            {
                var bakedPoseData = new Dictionary<string, object>();

                Transform FindChildRecursive(Transform parent, string name)
                {
                    if (parent.name == name) return parent;
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        Transform found = FindChildRecursive(parent.GetChild(i), name);
                        if (found != null) return found;
                    }
                    return null;
                }

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
                            pos = new { x = (float)System.Math.Round(element.localPosition.x, 4), y = (float)System.Math.Round(element.localPosition.y, 4), z = (float)System.Math.Round(element.localPosition.z, 4) },
                            color = new { r = lightComponent.color.r, g = lightComponent.color.g, b = lightComponent.color.b }
                        };
                        continue;
                    }

                    if (DioramaConstants.PositionalObjectsRegistry.Contains(targetName))
                    {
                        bakedPoseData[targetName] = new
                        {
                            type = "Bone",
                            rot = new
                            {
                                x = (float)System.Math.Round(element.localEulerAngles.x, 4),
                                y = (float)System.Math.Round(element.localEulerAngles.y, 4),
                                z = (float)System.Math.Round(element.localEulerAngles.z, 4)
                            },
                            pos = new
                            {
                                x = (float)System.Math.Round(element.localPosition.x, 4),
                                y = (float)System.Math.Round(element.localPosition.y, 4),
                                z = (float)System.Math.Round(element.localPosition.z, 4)
                            }
                        };
                    }
                    else
                    {
                        bakedPoseData[targetName] = new
                        {
                            type = "Bone",
                            rot = new
                            {
                                x = (float)System.Math.Round(element.localEulerAngles.x, 4),
                                y = (float)System.Math.Round(element.localEulerAngles.y, 4),
                                z = (float)System.Math.Round(element.localEulerAngles.z, 4)
                            }
                        };
                    }
                }

                string jsonResult = Newtonsoft.Json.JsonConvert.SerializeObject(bakedPoseData, Newtonsoft.Json.Formatting.Indented);
                Plugin.Log.LogWarning($"[PoseExporter] Скелет успешно запечен! Обработано элементов: {bakedPoseData.Count}");

                return jsonResult;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseExporter] Критическая ошибка запекания справочника: {ex.Message}");
                return "{}";
            }
        }
    } // Конец класса PoseExporter
} // Конец пространства имен FurnitureAnimationsMod

