using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using FurnitureAnimationsMod;

namespace FurnitureAnimations
{
    // 1. Патч на открытие меню UIPose — создаем и позиционируем нашу панель
    [HarmonyPatch(typeof(UIPose), "Open")]
    public static class UIPose_Open_SpeedButtonsPatch
    {
        public static void Postfix(UIPose __instance)
        {
            try
            {
                Plugin.Log.LogInfo("[SDK_UI] Старт создания панели на основе ванильного panelTakeOffClothes...");

                Transform uiPoseRoot = __instance.transform;

                // Проверяем главную ванильную панель раздевания
                GameObject vanillaTakeoffPanel = __instance.panelTakeOffClothes;
                if (vanillaTakeoffPanel == null)
                {
                    Plugin.Log.LogError("[SDK_UI] Критическая ошибка: Ванильный 'panelTakeOffClothes' не найден в UIPose!");
                    return;
                }

                // Предотвращаем дублирование при повторном открытии интерфейса
                Transform existingPanel = uiPoseRoot.Find("Mod_FurnitureAnimationControls_BG");
                if (existingPanel != null)
                {
                    existingPanel.gameObject.SetActive(true);
                    return;
                }

                // Клонируем ванильный контейнер (это скопирует и красивый оригинальный фон с затемнением)
                GameObject modPanelBgGo = GameObject.Instantiate(vanillaTakeoffPanel, uiPoseRoot, false);
                modPanelBgGo.name = "Mod_FurnitureAnimationControls_BG";
                modPanelBgGo.SetActive(true); // Принудительно включаем наш клон

                // Настраиваем габариты под оригинальный RectTransform
                RectTransform modPanelBgRect = modPanelBgGo.GetComponent<RectTransform>();
                RectTransform vanillaBgRect = vanillaTakeoffPanel.GetComponent<RectTransform>();

                modPanelBgRect.anchorMin = vanillaBgRect.anchorMin;
                modPanelBgRect.anchorMax = vanillaBgRect.anchorMax;
                modPanelBgRect.pivot = vanillaBgRect.pivot;
                modPanelBgRect.sizeDelta = vanillaBgRect.sizeDelta;

                // Сдвигаем нашу кастомную панель ниже оригинальной
                Vector2 newPanelPos = vanillaBgRect.anchoredPosition;
                newPanelPos.y -= 220f;
                modPanelBgRect.anchoredPosition = newPanelPos;

                // Вычищаем из клона ванильные кнопки раздевания, чтобы освободить место
                Transform modButtonsContainer = modPanelBgGo.transform.Find("Takeoff Buttons") ?? modPanelBgGo.transform;
                if (modButtonsContainer != modPanelBgGo.transform)
                {
                    modButtonsContainer.name = "Mod_AnimationButtonsContainer";
                    RectTransform modBtnContainerRect = modButtonsContainer.GetComponent<RectTransform>();
                    if (modBtnContainerRect != null) modBtnContainerRect.anchoredPosition = Vector2.zero;
                }

                foreach (Transform child in modButtonsContainer)
                {
                    if (child.GetComponent<Image>() != null && child.GetComponent<Button>() == null)
                        continue; // Не удаляем плашку фона

                    GameObject.Destroy(child.gameObject);
                }

                // Берем оригинальную кнопку как префаб, чтобы сохранить ванильный стиль (шрифты, рамки, Hover-эффекты)
                Transform origBtnPrefab = vanillaTakeoffPanel.transform.Find("Takeoff Buttons/Btn takeoff highheels")
                                          ?? vanillaTakeoffPanel.GetComponentInChildren<Button>()?.transform;

                if (origBtnPrefab == null)
                {
                    Plugin.Log.LogError("[SDK_UI] Сбой: Оригинальный префаб кнопки не найден внутри panelTakeOffClothes!");
                    return;
                }

                // Строим сетку кнопок управления
                Vector3 nextBtnPos = new Vector3(0f, 0f, 0f);
                float buttonSpacing = -45f;

                // Кнопка Скорость -
                CreateModButton(modButtonsContainer, origBtnPrefab, "Mod_BtnSpeedMinus", "Speed -10%", nextBtnPos, () => {
                    if (FurnitureAnimationPlayer.Instance != null)
                        FurnitureAnimationPlayer.Instance.ChangeSpeed(-0.1f);
                });

                nextBtnPos.y += buttonSpacing;

                // Кнопка Скорость +
                CreateModButton(modButtonsContainer, origBtnPrefab, "Mod_BtnSpeedPlus", "Speed +10%", nextBtnPos, () => {
                    if (FurnitureAnimationPlayer.Instance != null)
                        FurnitureAnimationPlayer.Instance.ChangeSpeed(0.1f);
                });

                nextBtnPos.y += buttonSpacing;

                // Кнопка Интерполяции (Сглаживания)
                CreateModButton(modButtonsContainer, origBtnPrefab, "Mod_BtnEaseToggle", "Interpolation: Linear", nextBtnPos, () => {
                    if (FurnitureAnimationPlayer.Instance != null)
                    {
                        FurnitureAnimationPlayer.Instance.ToggleEaseMode();

                        GameObject btnGo = GameObject.Find("Mod_BtnEaseToggle");
                        if (btnGo != null)
                        {
                            Text textComp = btnGo.GetComponentInChildren<Text>();
                            if (textComp != null)
                            {
                                textComp.text = $"Interpolation: {FurnitureAnimationPlayer.Instance.GetEaseMode()}";
                            }
                        }
                    }
                });

                Plugin.Log.LogInfo("[SDK_UI] Панель мода успешно инжектирована на базе panelTakeOffClothes!");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[SDK_UI] Ошибка инъекции в UIPose.Open: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void CreateModButton(Transform parent, Transform baseButtonPrefab, string objName, string buttonLabel, Vector3 anchoredPos, System.Action onClickAction)
        {
            GameObject newBtnGo = GameObject.Instantiate(baseButtonPrefab.gameObject, parent);
            newBtnGo.name = objName;

            RectTransform rect = newBtnGo.GetComponent<RectTransform>();
            rect.anchoredPosition = anchoredPos;

            Button btnComp = newBtnGo.GetComponent<Button>();
            if (btnComp != null)
            {
                btnComp.onClick.RemoveAllListeners();
                btnComp.onClick.AddListener(() => onClickAction?.Invoke());
            }

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

    // 2. Патч на закрытие меню UIPose — аккуратно тушим нашу панель вместе с интерфейсом
    [HarmonyPatch(typeof(UIPose), "Close")]
    public static class UIPose_Close_SpeedButtonsPatch
    {
        public static void Postfix(UIPose __instance)
        {
            Transform modPanel = __instance.transform.Find("Mod_FurnitureAnimationControls_BG");
            if (modPanel != null)
            {
                modPanel.gameObject.SetActive(false);
            }
        }
    }
}
