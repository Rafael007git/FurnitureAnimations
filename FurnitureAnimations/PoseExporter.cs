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

            Vector3 playerPos = uiInstance.selectedCharacter.position;
            Furniture closestFurniture = FindClosestFurniture(playerPos);

            if (closestFurniture == null)
            {
                if (Global.code != null && Global.code.uiCombat != null)
                    Global.code.uiCombat.AddPrompt("Рядом нет мебели в радиусе 10 метров!");
                return;
            }

            string furnitureName = closestFurniture.name.Replace("(Clone)", "").Trim();
            CharacterCustomization characterComp = uiInstance.selectedCharacter.GetComponent<CharacterCustomization>();

            string controllerName = "None";
            _lastCapturedIcon = null;

            // Умное распознавание типа позы
            bool hasActiveAnimator = characterComp != null && characterComp.anim != null && characterComp.anim.runtimeAnimatorController != null;
            bool isPresetPose = hasActiveAnimator && !string.IsNullOrEmpty(characterComp.anim.runtimeAnimatorController.name) && characterComp.anim.runtimeAnimatorController.name != "CustomJSON";

            if (isPresetPose)
            {
                controllerName = characterComp.anim.runtimeAnimatorController.name;
                Plugin.Log.LogInfo($"[PoseExporter] Распознана предустановленная поза: {controllerName}");

                // Ищем родную иконку этой позы в реестре игры
                if (RM.code != null && RM.code.allFreePoses != null)
                {
                    foreach (Transform t in RM.code.allFreePoses.items)
                    {
                        var p = t.GetComponent<global::Pose>();
                        if (p != null && p.controller != null && p.controller.name == controllerName)
                        {
                            _lastCapturedIcon = p.icon;
                            break;
                        }
                    }
                }
            }
            else
            {
                // Сценарий Б: Полностью кастомная поза скелета
                controllerName = "CustomJSON";
                Plugin.Log.LogInfo("[PoseExporter] Распознана полностью ручная поза. Делаем снимок экрана...");

                var photoComp = uiInstance.GetComponent<TakePhotos>();
                if (photoComp != null && Global.code != null && Global.code.freeCamera != null)
                {
                    Camera cam = Global.code.freeCamera.GetComponent<Camera>();
                    _lastCapturedIcon = photoComp.CameraCapture(cam, new Rect(0f, 0f, 300f, 300f), "");
                }
            }

            Vector3 exactLocPos = closestFurniture.transform.InverseTransformPoint(playerPos);
            Quaternion localQuaternion = Quaternion.Inverse(closestFurniture.transform.rotation) * uiInstance.selectedCharacter.rotation;
            Vector3 exactLocRot = localQuaternion.eulerAngles;

            string promptText = $"Вы хотите сохранить эту позу для {furnitureName}?\n" +
                                $"Тип: {(isPresetPose ? "Предустановленная" : "Кастомная")}\n" +
                                $"Контроллер: {controllerName}";

            EditorUiManager.ShowConfirmationDialog(promptText, _lastCapturedIcon, () =>
            {
                SavePoseToDataFolder(furnitureName, controllerName, exactLocPos, exactLocRot, !isPresetPose, characterComp);
            });
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
                string generatedPoseName = isCustom ? $"Кастомная — {DateTime.Now:dd.MM HH:mm}" : $"Анимация — {controller}";
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

                if (Global.code != null && Global.code.uiCombat != null)
                    Global.code.uiCombat.ShowHeader("Поза успешно сохранена!");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseExporter] Критическая ошибка записи: {ex.Message}");
            }
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
                if (distance < maxDistance) { maxDistance = distance; closestFurniture = f; }
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

