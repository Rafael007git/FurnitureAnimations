using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using FurnitureAnimationsMod;

namespace FurnitureAnimations
{
    [HarmonyPatch(typeof(UIFreePose), "Start")]
    public static class UIFreePose_Start_SpeedButtonsPatch
    {
        public static void Postfix(UIFreePose __instance)
        {
            try
            {
                Plugin.Log.LogInfo("[SDK_UI] Старт инициализации кастомной UI-панели мода...");

                // 1. Находим корневой родительский UI-элемент, где лежат панели позы
                Transform uiPoseRoot = __instance.transform;

                // Ищем оригинальный фоновый контейнер ванильной панели
                Transform vanillaBgContainer = uiPoseRoot.Find("Takeoff Buttons BG");
                if (vanillaBgContainer == null)
                {
                    Plugin.Log.LogError("[SDK_UI] Критическая ошибка: Контейнер 'Takeoff Buttons BG' не найден в структуре UIPose!");
                    return;
                }

                // 2. КЛОНИРУЕМ ВСЮ ПАНЕЛЬ ЦЕЛИКОМ под наши нужды
                GameObject modPanelBgGo = GameObject.Instantiate(vanillaBgContainer.gameObject, uiPoseRoot);
                modPanelBgGo.name = "Mod_FurnitureAnimationControls_BG";

                // Принудительно ВКЛЮЧАЕМ нашу кастомную панель, исправляя ванильное затухание
                modPanelBgGo.SetActive(true);

                // 3. Корректируем позиционирование новой панели, чтобы она встала ровно ПОД ванильной
                RectTransform modPanelBgRect = modPanelBgGo.GetComponent<RectTransform>();
                RectTransform vanillaBgRect = vanillaBgContainer.GetComponent<RectTransform>();

                // Смещаем панель-клон вниз (оффсет подбирается под высоту ванильной панели, обычно около -200f или -220f)
                Vector3 newPanelPos = vanillaBgRect.anchoredPosition;
                newPanelPos.y -= 220f;
                modPanelBgRect.anchoredPosition = newPanelPos;

                // 4. Находим внутренний контейнер для кнопок внутри нашего клона
                Transform modButtonsContainer = modPanelBgGo.transform.Find("Takeoff Buttons");
                if (modButtonsContainer == null)
                {
                    Plugin.Log.LogError("[SDK_UI] Сбой: Внутри клона панели не найден контейнер 'Takeoff Buttons'!");
                    return;
                }
                modButtonsContainer.name = "Mod_AnimationButtonsContainer";

                // 5. ПОЛНАЯ ЗАЧИСТКА ВАНИЛЬНОГО ХЛАМА внутри нашего контейнера
                // Удаляем старые кнопки раздевания персонажа, чтобы они не двоились
                foreach (Transform child in modButtonsContainer)
                {
                    GameObject.Destroy(child.gameObject);
                }

                // 6. ИСПОЛЬЗУЕМ КНОПКУ-ПРЕФАБ ИЗ ОРИГИНАЛЬНОЙ ПАНЕЛИ ДЛЯ СОХРАНЕНИЯ СТИЛЯ
                Transform origBtnPrefab = vanillaBgContainer.Find("Takeoff Buttons/Btn takeoff highheels");
                if (origBtnPrefab == null)
                {
                    Plugin.Log.LogError("[SDK_UI] Сбой: Оригинальный префаб кнопки 'Btn takeoff highheels' не найден для копирования стилей!");
                    return;
                }

                // 7. НАПОЛНЯЕМ НАШУ ПАНЕЛЬ НОВЫМИ КНОПКАМИ
                Vector3 nextBtnPos = new Vector3(0f, 0f, 0f); // Первая кнопка встанет в самый верх панели
                float buttonSpacing = -45f;                   // Вертикальный шаг сетки интерфейса игры

                // Кнопка Уменьшения скорости
                CreateModButton(modButtonsContainer, origBtnPrefab, "Mod_BtnSpeedMinus", "Speed -10%", nextBtnPos, () => {
                    if (FurnitureAnimationPlayer.Instance != null)
                        FurnitureAnimationPlayer.Instance.ChangeSpeed(-0.1f);
                });

                // Смещаемся ниже для следующей кнопки
                nextBtnPos.y += buttonSpacing;

                // Кнопка Увеличения скорости
                CreateModButton(modButtonsContainer, origBtnPrefab, "Mod_BtnSpeedPlus", "Speed +10%", nextBtnPos, () => {
                    if (FurnitureAnimationPlayer.Instance != null)
                        FurnitureAnimationPlayer.Instance.ChangeSpeed(0.1f);
                });

                Plugin.Log.LogWarning("[SDK_UI] Независимая кастомная панель мода успешно создана и активирована!");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[SDK_UI] Критический краш при изоляции кастомной панели: {ex.Message}");
            }
        }

        private static void CreateModButton(Transform parent, Transform baseButtonPrefab, string objName, string buttonLabel, Vector3 anchoredPos, System.Action onClickAction)
        {
            // Клонируем оригинальный префаб кнопки со всеми нативными Hover/Press эффектами игры
            GameObject newBtnGo = GameObject.Instantiate(baseButtonPrefab.gameObject, parent);
            newBtnGo.name = objName;

            RectTransform rect = newBtnGo.GetComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;

            // Настраиваем логику клика
            Button btnComp = newBtnGo.GetComponent<Button>();
            if (btnComp != null)
            {
                btnComp.onClick.RemoveAllListeners();
                btnComp.onClick.AddListener(() => onClickAction?.Invoke());
            }

            // Настраиваем текст
            Text txtComp = newBtnGo.GetComponentInChildren<Text>();
            if (txtComp != null)
            {
                txtComp.text = buttonLabel;
                txtComp.fontSize = 11;
                txtComp.color = Color.white;
            }

            newBtnGo.SetActive(true);
        }
    }
}
