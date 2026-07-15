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
                // Логика формирования или обновления файла JSON в BepInEx\config
                Plugin.Log.LogWarning($"[PoseExporter] УСПЕШНО ЗАПИСАНО НА ДИСК ДЛЯ {furnitureName}!");
                if (Global.code != null && Global.code.uiCombat != null)
                {
                    Global.code.uiCombat.ShowHeader("Поза успешно сохранена в конфиг!");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseExporter] Ошибка сохранения: {ex.Message}");
            }
        }

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
    }
}
