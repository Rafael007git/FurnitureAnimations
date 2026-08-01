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

                // 1. Проверяем, существует ли уже наша панель
                Transform existingPanel = uiPose.transform.Find("Mod_FurnitureAnimationControls_BG");
                if (existingPanel != null)
                {
                    _uiPanelInstance = existingPanel.gameObject;
                    _uiPanelInstance.SetActive(true);
                    UpdateText("Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}");
                    return;
                }

                GameObject vanillaTakeoffPanel = uiPose.panelTakeOffClothes;
                if (vanillaTakeoffPanel == null) return;

                // 2. Клонируем панель
                _uiPanelInstance = GameObject.Instantiate(vanillaTakeoffPanel, uiPose.transform, false);
                _uiPanelInstance.name = "Mod_FurnitureAnimationControls_BG";
                _uiPanelInstance.SetActive(true);

                // 3. ЖЕСТКОЕ ИСПРАВЛЕНИЕ МАСШТАБА И ПОЗИЦИИ ФОНА
                RectTransform modRect = _uiPanelInstance.GetComponent<RectTransform>();
                RectTransform vanRect = vanillaTakeoffPanel.GetComponent<RectTransform>();

                // Копируем базовую структуру якорей оригинала
                modRect.anchorMin = vanRect.anchorMin;
                modRect.anchorMax = vanRect.anchorMax;
                modRect.pivot = vanRect.pivot;
                modRect.sizeDelta = vanRect.sizeDelta;

                // Выравниваем локальный масштаб в точности как у оригинала (0.9, а не 1.0)
                _uiPanelInstance.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);

                // Позиционируем на основе оригинальных локальных координат, смещаясь строго вниз по Y
                Vector3 vanLocalPos = vanRect.localPosition;
                modRect.localPosition = new Vector3(vanLocalPos.x, vanLocalPos.y - 180f, vanLocalPos.z);

                // 4. НАХОДИМ И ВЫЧИЩАЕМ АБСОЛЮТНО ВСЕ КНОПКИ (включая дубликаты в корне)
                // Сначала находим контейнер для наших будущих кнопок
                Transform buttonsContainer = _uiPanelInstance.transform.Find("Takeoff Buttons");
                if (buttonsContainer == null && _uiPanelInstance.transform.childCount > 0)
                {
                    buttonsContainer = _uiPanelInstance.transform.GetChild(0);
                }

                if (buttonsContainer == null) return;
                buttonsContainer.name = "Mod_AnimationButtonsContainer";

                // Сбрасываем его локальные координаты, чтобы он сидел ровно
                RectTransform containerRect = buttonsContainer.GetComponent<RectTransform>();
                if (containerRect != null) containerRect.anchoredPosition = Vector2.zero;

                // Рекурсивно уничтожаем все старые ванильные кнопки раздевания во ВСЕЙ панели-клоне
                Button[] oldButtons = _uiPanelInstance.GetComponentsInChildren<Button>(true);
                foreach (Button oldBtn in oldButtons)
                {
                    GameObject.DestroyImmediate(oldBtn.gameObject);
                }

                // 5. Ищем оригинальный префаб кнопки для копирования стиля
                Transform btnPrefab = vanillaTakeoffPanel.transform.Find("Takeoff Buttons/Btn takeoff highheels")
                                     ?? vanillaTakeoffPanel.GetComponentInChildren<Button>()?.transform;

                if (btnPrefab != null)
                {
                    Vector3 btnPos = Vector3.zero;
                    float spacing = -45f;

                    // Складываем кастомные кнопки строго в очищенный buttonsContainer
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
                Plugin.Log.LogError($"[UI] Ошибка калибровки RectTransform: {ex.Message}");
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
