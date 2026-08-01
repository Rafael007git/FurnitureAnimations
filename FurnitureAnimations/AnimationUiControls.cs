using System.ComponentModel;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FurnitureAnimationsMod
{
    public class AnimationUiControls : MonoBehaviour
    {
        private FurnitureAnimationPlayer _player;
        private GameObject _uiPanelInstance;

        public void Initialize(FurnitureAnimationPlayer player)
        {
            _player = player;

            try
            {
                UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
                if (uiPose == null) return;

                // 1. Проверяем, существует ли уже наша панель
                Transform existingPanel = uiPose.transform.Find("Mod_FurnitureAnimationControls_BG");
                if (existingPanel != null)
                {
                    _uiPanelInstance = existingPanel.gameObject;
                    _uiPanelInstance.SetActive(true);
                    UpdateText("Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}");
                    return;
                }

                // panelTakeOffClothes — это на самом деле дочерний 'Takeoff Buttons' с VerticalLayoutGroup
                GameObject vanillaButtonsContainerGo = uiPose.panelTakeOffClothes;
                if (vanillaButtonsContainerGo == null) return;

                // ХИРУРГИЧЕСКИЙ ХАК: Находим настоящий фоновый объект с Image (Takeoff Buttons BG)
                Transform vanillaBgTransform = vanillaButtonsContainerGo.transform.parent;
                if (vanillaBgTransform == null)
                {
                    Plugin.Log.LogError("[UI] Сбой: Не найден родительский фон для panelTakeOffClothes!");
                    return;
                }

                // 2. КЛОНИРУЕМ НАСТОЯЩИЙ ФОН ЦЕЛИКОМ (теперь у нас будет оригинальный Image!)
                _uiPanelInstance = GameObject.Instantiate(vanillaBgTransform.gameObject, uiPose.transform, false);
                _uiPanelInstance.name = "Mod_FurnitureAnimationControls_BG";
                _uiPanelInstance.SetActive(true);

                // Настраиваем RectTransform фоновой плашки
                RectTransform modRect = _uiPanelInstance.GetComponent<RectTransform>();
                RectTransform vanRect = vanillaBgTransform.GetComponent<RectTransform>();

                modRect.anchorMin = vanRect.anchorMin;
                modRect.anchorMax = vanRect.anchorMax;
                modRect.pivot = vanRect.pivot;
                modRect.sizeDelta = vanRect.sizeDelta;

                // Копируем точный локальный масштаб оригинального фона
                _uiPanelInstance.transform.localScale = vanRect.localScale;

                // Смещаем плашку-фон строго вниз по локальной оси Y относительно оригинала
                Vector3 vanLocalPos = vanRect.localPosition;
                modRect.localPosition = new Vector3(vanLocalPos.x, vanLocalPos.y - 180f, vanLocalPos.z);

                // 3. НАХОДИМ ВНУТРЕННИЙ КОНТЕЙНЕР (у него внутри нашего клона будет такое же имя, как у panelTakeOffClothes)
                Transform buttonsContainer = _uiPanelInstance.transform.Find(vanillaButtonsContainerGo.name);
                if (buttonsContainer == null && _uiPanelInstance.transform.childCount > 0)
                {
                    buttonsContainer = _uiPanelInstance.transform.GetChild(0); // Запасной вариант, берем первый дочерний
                }

                if (buttonsContainer == null)
                {
                    Plugin.Log.LogError("[UI] Критическая ошибка: Внутри нового фона не найден контейнер для кнопок!");
                    return;
                }
                buttonsContainer.name = "Mod_AnimationButtonsContainer";

                // 4. ТОТАЛЬНАЯ ЗАЧИСТКА: Удаляем абсолютно все ванильные кнопки раздевания во всей панели-клоне
                Button[] oldButtons = _uiPanelInstance.GetComponentsInChildren<Button>(true);
                foreach (Button oldBtn in oldButtons)
                {
                    GameObject.DestroyImmediate(oldBtn.gameObject);
                }

                // 5. Ищем оригинальный префаб кнопки для копирования стиля (ищем в оригинальном контейнеres)
                Transform btnPrefab = vanillaButtonsContainerGo.transform.GetComponentInChildren<Button>()?.transform;

                if (btnPrefab != null)
                {
                    Vector3 btnPos = Vector3.zero;
                    float spacing = -45f; // На случай, если LayoutGroup потребует ручного смещения, но вообще VerticalLayoutGroup расставит сам

                    // 1. Кнопка Скорость -
                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnSpeedMinus", $"Speed: {Mathf.RoundToInt(_player.GetSpeed() * 100)}% (-10)", btnPos, () => {
                        _player.ChangeSpeed(-0.1f);
                        UpdateSpeedButtonsText();
                    });
                    btnPos.y += spacing;

                    // 2. Кнопка Скорость +
                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnSpeedPlus", $"Speed: {Mathf.RoundToInt(_player.GetSpeed() * 100)}% (+10)", btnPos, () => {
                        _player.ChangeSpeed(0.1f);
                        UpdateSpeedButtonsText();
                    });
                    btnPos.y += spacing;

                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}", btnPos, () => {
                        _player.ToggleEaseMode();
                        UpdateText("Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}");
                    });
                    btnPos.y += spacing;

                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnMuteToggle", "Sound: ON", btnPos, () => {
                        if (AnimationAudioManager.Instance != null)
                        {
                            AnimationAudioManager.Instance.ToggleMute();
                            UpdateText("Mod_BtnMuteToggle", AnimationAudioManager.Instance.IsMuted() ? "Sound: MUTED" : "Sound: ON");
                        }
                    });
                }
                else
                {
                    Plugin.Log.LogError("[UI] Сбой: Не удалось обнаружить префаб ванильной кнопки для копирования стилей.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[UI] Ошибка декомпозиции иерархии плашек: {ex.Message}");
            }
        }

        private void CreateUiButton(Transform parent, Transform prefab, string objName, string label, Vector3 localPos, System.Action onClick)
        {
            GameObject btnGo = GameObject.Instantiate(prefab.gameObject, parent);
            btnGo.name = objName;

            RectTransform r = btnGo.GetComponent<RectTransform>();
            r.anchoredPosition = localPos;

            // 3. УЛЬТИМАТИВНОЕ УНИЧТОЖЕНИЕ ИНСПЕКТОРСКИХ СВЯЗЕЙ (Persistent Calls)
            Button b = btnGo.GetComponent<Button>();
            if (b != null)
            {
                // Полностью сносим старый компонент Button вместе со всей его "памятью" о лифчиках
                GameObject.DestroyImmediate(b);
            }

            // Добавляем абсолютно чистый, новый компонент Button без инспекторских связей
            b = btnGo.AddComponent<Button>();
            b.onClick.AddListener(() => onClick?.Invoke());

            Text t = btnGo.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.text = label;
                t.fontSize = 11;
                t.color = Color.white;
            }
            btnGo.SetActive(true);
        }


        private void UpdateText(string objName, string newText)
        {
            GameObject go = GameObject.Find(objName);
            Text t = go?.GetComponentInChildren<Text>();
            if (t != null) t.text = newText;
        }

        public void HidePanel()
        {
            if (_uiPanelInstance != null) _uiPanelInstance.SetActive(false);
        }

        private void OnDestroy()
        {
            HidePanel();
        }

        private void UpdateSpeedButtonsText()
        {
            int percent = Mathf.RoundToInt(_player.GetSpeed() * 100);
            UpdateText("Mod_BtnSpeedMinus", $"Speed: {percent}% (-10)");
            UpdateText("Mod_BtnSpeedPlus", $"Speed: {percent}% (+10)");
        }
    }
}
