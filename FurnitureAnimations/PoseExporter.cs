using System;
using System.IO;
using System.Collections;
using UnityEngine;

namespace FurnitureAnimationsMod
{
    public static class PoseExporter
    {
        // Метод А: Автоматический поиск ближайшей мебели в радиусе 10 метров
        public static Furniture FindClosestFurniture(Vector3 playerPosition)
        {
            Furniture closestFurniture = null;
            float maxDistance = 10f; // Ограничение сферы поиска в 10 метров

            // Находим вообще всю мебель, которая сейчас инициализирована на сцене
            Furniture[] allFurnitures = UnityEngine.Object.FindObjectsOfType<Furniture>();

            foreach (Furniture f in allFurnitures)
            {
                if (f == null) continue;

                // Считаем расстояние от пивота персонажа до пивота мебели
                float distance = Vector3.Distance(playerPosition, f.transform.position);

                if (distance < maxDistance)
                {
                    maxDistance = distance; // Сужаем круг поиска до самого близкого объекта
                    closestFurniture = f;
                }
            }

            if (closestFurniture != null)
            {
                Plugin.Log.LogInfo($"[PoseExporter] Авто-поиск: Найдена ближайшая мебель \"{closestFurniture.name}\" на расстоянии {maxDistance:F2}м.");
            }
            else
            {
                Plugin.Log.LogError("[PoseExporter] Авто-поиск: В радиусе 10 метров не обнаружено ни одного объекта мебели!");
            }

            return closestFurniture;
        }

        // Метод Б: Расчет точного локального смещения и разворота относительно мебели
        public static void CalculateExactOffsets(Furniture targetFurniture, CharacterCustomization user, out Vector3 localPos, out Vector3 localRot)
        {
            if (targetFurniture == null || user == null)
            {
                localPos = Vector3.zero;
                localRot = Vector3.zero;
                return;
            }

            // Магия Unity: перевод мировых координат персонажа в локальное пространство мебели
            localPos = targetFurniture.transform.InverseTransformPoint(user.transform.position);

            // Расчет локального разворота куклы относительно разворота мебели
            Quaternion localQuaternion = Quaternion.Inverse(targetFurniture.transform.rotation) * user.transform.rotation;
            localRot = localQuaternion.eulerAngles;
        }

        // Метод В: Корутина для создания скриншота-иконки с сокрытием интерфейса
        // Используем IEnumerator, так как чтение пикселей в Unity требует завершения кадра (WaitForEndOfFrame)
        public static IEnumerator CaptureIconCoroutine(Action<Texture2D> onIconCaptured)
        {
            Plugin.Log.LogInfo("[PoseExporter] Прячем интерфейс игры для создания чистого скриншота...");

            // 1. Скрываем весь игровой интерфейс и худ, чтобы они не попали на иконку позы
            bool hudWasActive = false;
            if (Global.code != null && Global.code.uiCombat != null && Global.code.uiCombat.hud != null)
            {
                hudWasActive = Global.code.uiCombat.hud.activeSelf;
                Global.code.uiCombat.hud.SetActive(false);
            }

            // Скрываем меню выбора поз, если оно открыто
            // (В будущем здесь обратимся к конкретному окну UI_FurnitureMenu)

            // Ждем, пока Unity полностью отрендерит текущий чистый кадр без UI
            yield return new WaitForEndOfFrame();

            Texture2D iconTexture = null;
            try
            {
                // 2. Делаем снимок центральной области экрана (например, квадрат 256x256 пикселей)
                int width = 256;
                int height = 256;
                int startX = (Screen.width / 2) - (width / 2);
                int startY = (Screen.height / 2) - (height / 2);

                iconTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
                iconTexture.ReadPixels(new Rect(startX, startY, width, height), 0, 0);
                iconTexture.Apply();

                Plugin.Log.LogWarning("[PoseExporter] Скриншот-иконка успешно сгенерирована в памяти!");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[PoseExporter] Сбой при чтении пикселей экрана: {ex.Message}");
            }

            // 3. Возвращаем интерфейс игры в исходное состояние
            if (Global.code != null && Global.code.uiCombat != null && Global.code.uiCombat.hud != null)
            {
                Global.code.uiCombat.hud.SetActive(hudWasActive);
            }

            // Передаем созданную текстуру обратно через колбэк для отображения в диалоговом окне
            onIconCaptured?.Invoke(iconTexture);
        }

        // Метод Г: Экспорт костей скелета в кастомный JSON (Для сценария Б — Свободная поза)
        public static string ExportBonesToCustomJson(CharacterCustomization user)
        {
            if (user == null) return string.Empty;

            Plugin.Log.LogInfo("[PoseExporter] Запуск сканирования иерархии костей Genesis8Female...");

            // Здесь будет цикл пробежки по всем дочерним Transform объекта user (hip, abdomen, lHand и т.д.)
            // Сериализуем их локальные позиции и повороты по аналогии с модом Диорамы.

            return "{}"; // Временная пустышка для черновика
        }
    }
}
