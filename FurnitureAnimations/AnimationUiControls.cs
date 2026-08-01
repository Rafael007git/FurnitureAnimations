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

                _uiPanelInstance = GameObject.Instantiate(vanillaTakeoffPanel, uiPose.transform, false);
                _uiPanelInstance.name = "Mod_FurnitureAnimationControls_BG";
                _uiPanelInstance.SetActive(true);

                RectTransform modRect = _uiPanelInstance.GetComponent<RectTransform>();
                RectTransform vanRect = vanillaTakeoffPanel.GetComponent<RectTransform>();

                modRect.anchorMin = vanRect.anchorMin;
                modRect.anchorMax = vanRect.anchorMax;
                modRect.pivot = vanRect.pivot;
                modRect.sizeDelta = vanRect.sizeDelta;

                Vector2 pos = vanRect.anchoredPosition;
                pos.x += 250f;
                pos.y -= 150f;
                modRect.anchoredPosition = pos;

                Transform container = _uiPanelInstance.transform.Find("Takeoff Buttons") ?? _uiPanelInstance.transform;
                if (container != _uiPanelInstance.transform)
                {
                    container.name = "Mod_AnimationButtonsContainer";
                    var cRect = container.GetComponent<RectTransform>();
                    if (cRect != null) cRect.anchoredPosition = Vector2.zero;
                }

                foreach (Transform child in container)
                {
                    if (child.GetComponent<Image>() != null && child.GetComponent<Button>() == null) continue;
                    GameObject.Destroy(child.gameObject);
                }

                Transform btnPrefab = vanillaTakeoffPanel.transform.Find("Takeoff Buttons/Btn takeoff highheels")
                                     ?? vanillaTakeoffPanel.GetComponentInChildren<Button>()?.transform;

                if (btnPrefab != null)
                {
                    Vector3 btnPos = Vector3.zero;
                    float spacing = -45f;

                    CreateUiButton(container, btnPrefab, "Mod_BtnSpeedMinus", "Speed -10%", btnPos, () => _player.ChangeSpeed(-0.1f));
                    btnPos.y += spacing;

                    CreateUiButton(container, btnPrefab, "Mod_BtnSpeedPlus", "Speed +10%", btnPos, () => _player.ChangeSpeed(0.1f));
                    btnPos.y += spacing;

                    CreateUiButton(container, btnPrefab, "Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}", btnPos, () => {
                        _player.ToggleEaseMode();
                        UpdateText("Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}");
                    });
                    btnPos.y += spacing;

                    CreateUiButton(container, btnPrefab, "Mod_BtnMuteToggle", "Sound: ON", btnPos, () => {
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
                Plugin.Log.LogError($"[UI] Ошибка инициализации UI панели: {ex.Message}");
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
