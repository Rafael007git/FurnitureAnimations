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
                if (uiPose == null) return;

                // 1. Ищем существующую панель, чтобы не плодить копии
                Transform existingPanel = uiPose.transform.Find("Mod_FurnitureAnimationControls_BG");
                if (existingPanel != null)
                {
                    _uiPanelInstance = existingPanel.gameObject;
                    _uiPanelInstance.SetActive(true);
                    UpdateText("Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}");
                    return;
                }

                // 2. Создаем клон из ванильного panelTakeOffClothes
                GameObject vanillaTakeoffPanel = uiPose.panelTakeOffClothes;
                if (vanillaTakeoffPanel == null) return;

                _uiPanelInstance = GameObject.Instantiate(vanillaTakeoffPanel, uiPose.transform, false);
                _uiPanelInstance.name = "Mod_FurnitureAnimationControls_BG";
                _uiPanelInstance.SetActive(true);

                // Копируем параметры RectTransform для фона
                RectTransform modRect = _uiPanelInstance.GetComponent<RectTransform>();
                RectTransform vanRect = vanillaTakeoffPanel.GetComponent<RectTransform>();

                modRect.anchorMin = vanRect.anchorMin;
                modRect.anchorMax = vanRect.anchorMax;
                modRect.pivot = vanRect.pivot;
                modRect.sizeDelta = vanRect.sizeDelta;

                // Временный дефолтный сдвиг (позже настроим точнее)
                Vector2 pos = vanRect.anchoredPosition;
                pos.x = vanRect.anchoredPosition.x; // Полностью убираем сторонний сдвиг по X, фиксируем ванильную позицию
                pos.y -= 210f;                      // Смещаем панель строго вниз (210-220 пикселей 
                modRect.anchoredPosition = pos;

                // 3. СТРУКТУРНОЕ ИСПРАВЛЕНИЕ: Находим оригинальный промежуточный контейнер кнопок
                Transform buttonsContainer = _uiPanelInstance.transform.Find("Takeoff Buttons");
                if (buttonsContainer == null)
                {
                    Plugin.Log.LogError("[UI] Критическая ошибка: Внутри клона не найден дочерний контейнер 'Takeoff Buttons'!");
                    // Если Find не сработал напрямую, ищем по первому дочернему объекту
                    if (_uiPanelInstance.transform.childCount > 0)
                        buttonsContainer = _uiPanelInstance.transform.GetChild(0);
                }

                if (buttonsContainer == null) return; // Защита от краша

                buttonsContainer.name = "Mod_AnimationButtonsContainer";

                // Сбрасываем локальные координаты контейнера, чтобы он сидел ровно внутри фона
                RectTransform containerRect = buttonsContainer.GetComponent<RectTransform>();
                if (containerRect != null)
                {
                    containerRect.anchoredPosition = Vector2.zero;
                }

                // 4. ЗАЧИСТКА: Удаляем ванильные кнопки строго ИЗ КОНТЕЙНЕРА, не трогая сам фон панели
                foreach (Transform child in buttonsContainer)
                {
                    GameObject.Destroy(child.gameObject);
                }

                // 5. Ищем оригинальный префаб кнопки для копирования стиля
                Transform btnPrefab = vanillaTakeoffPanel.transform.Find("Takeoff Buttons/Btn takeoff highheels")
                                     ?? vanillaTakeoffPanel.GetComponentInChildren<Button>()?.transform;

                if (btnPrefab != null)
                {
                    Vector3 btnPos = Vector3.zero;
                    float spacing = -45f; // Если в контейнере нет AutoLayout, этот шаг расставит их вертикально

                    // Складываем кнопки строго в buttonsContainer
                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnSpeedMinus", "Speed -10%", btnPos, () => _player.ChangeSpeed(-0.1f));
                    btnPos.y += spacing;

                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnSpeedPlus", "Speed +10%", btnPos, () => _player.ChangeSpeed(0.1f));
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
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[UI] Ошибка построения правильной иерархии: {ex.Message}");
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

        public void HidePanel()
        {
            if (_uiPanelInstance != null) _uiPanelInstance.SetActive(false);
        }

        private void OnDestroy()
        {
            HidePanel();
        }
    }
}
