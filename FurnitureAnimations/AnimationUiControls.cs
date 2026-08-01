using System;
using UnityEngine;
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
                if (uiPose == null)
                {
                    Plugin.Log.LogError("[UI] Не найден UIPose на сцене для инъекции кнопок.");
                    return;
                }

                GameObject vanillaTakeoffPanel = uiPose.panelTakeOffClothes;
                if (vanillaTakeoffPanel == null)
                {
                    Plugin.Log.LogError("[UI] Поле 'panelTakeOffClothes' отсутствует в UIPose.");
                    return;
                }

                // Клонируем ванильный контейнер, наследуя оригинальный фон и затемнение
                _uiPanelInstance = GameObject.Instantiate(vanillaTakeoffPanel, uiPose.transform, false);
                _uiPanelInstance.name = "Mod_FurnitureAnimationControls_BG";
                _uiPanelInstance.SetActive(true);

                RectTransform modRect = _uiPanelInstance.GetComponent<RectTransform>();
                RectTransform vanRect = vanillaTakeoffPanel.GetComponent<RectTransform>();

                modRect.anchorMin = vanRect.anchorMin;
                modRect.anchorMax = vanRect.anchorMax;
                modRect.pivot = vanRect.pivot;
                modRect.sizeDelta = vanRect.sizeDelta;

                // Сдвигаем нашу кастомную панель ниже оригинальной плашки раздевания
                Vector2 pos = vanRect.anchoredPosition;
                pos.y -= 220f;
                modRect.anchoredPosition = pos;

                // Находим внутренний контейнер для кнопок
                Transform container = _uiPanelInstance.transform.Find("Takeoff Buttons") ?? _uiPanelInstance.transform;
                if (container != _uiPanelInstance.transform)
                {
                    container.name = "Mod_AnimationButtonsContainer";
                    var cRect = container.GetComponent<RectTransform>();
                    if (cRect != null) cRect.anchoredPosition = Vector2.zero;
                }

                // Вычищаем ванильные кнопки раздевания, оставляя фон
                foreach (Transform child in container)
                {
                    if (child.GetComponent<Image>() != null && child.GetComponent<Button>() == null) continue;
                    GameObject.Destroy(child.gameObject);
                }

                // Ищем оригинальную кнопку как префаб стиля (шрифты, рамки, Hover-эффекты)
                Transform btnPrefab = vanillaTakeoffPanel.transform.Find("Takeoff Buttons/Btn takeoff highheels")
                                     ?? vanillaTakeoffPanel.GetComponentInChildren<Button>()?.transform;

                if (btnPrefab != null)
                {
                    Vector3 btnPos = Vector3.zero;
                    float spacing = -45f;

                    // 1. Кнопка Скорость -
                    CreateUiButton(container, btnPrefab, "Mod_BtnSpeedMinus", "Speed -10%", btnPos, () => _player.ChangeSpeed(-0.1f));
                    btnPos.y += spacing;

                    // 2. Кнопка Скорость +
                    CreateUiButton(container, btnPrefab, "Mod_BtnSpeedPlus", "Speed +10%", btnPos, () => _player.ChangeSpeed(0.1f));
                    btnPos.y += spacing;

                    // 3. Кнопка Сглаживания
                    CreateUiButton(container, btnPrefab, "Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}", btnPos, () => {
                        _player.ToggleEaseMode();
                        UpdateText("Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}");
                    });
                    btnPos.y += spacing;

                    // 4. Кнопка Mute Звука
                    CreateUiButton(container, btnPrefab, "Mod_BtnMuteToggle", "Sound: ON", btnPos, () => {
                        if (AnimationAudioManager.Instance != null)
                        {
                            AnimationAudioManager.Instance.ToggleMute();
                            UpdateText("Mod_BtnMuteToggle", AnimationAudioManager.Instance.IsMuted() ? "Sound: MUTED" : "Sound: ON");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[UI] Критический краш при создании панели: {ex.Message}");
            }
        }

        private void CreateUiButton(Transform parent, Transform prefab, string objName, string label, Vector3 localPos, System.Action onClick)
        {
            GameObject btnGo = GameObject.Instantiate(prefab.gameObject, parent);
            btnGo.name = objName;

            RectTransform r = btnGo.GetComponent<RectTransform>();
            r.anchoredPosition = localPos;

            Button b = btnGo.GetComponent<Button>();
            if (b != null)
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() => onClick?.Invoke());
            }

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

        private void OnDestroy()
        {
            if (_uiPanelInstance != null) GameObject.Destroy(_uiPanelInstance);
        }
    }
}
