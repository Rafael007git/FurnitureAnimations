using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityStandardAssets.Cameras;

namespace FurnitureAnimationsMod
{
    public class AnimationUiControls : MonoBehaviour
    {
        private FurnitureAnimationPlayer _player;
        private Furniture _activeFurnitureInstance;
        private GameObject _uiPanelInstance;

        // Кэш для иконок, чтобы не читать из сборки каждый кадр
        private Texture2D _iconSpeedPlus;
        private Texture2D _iconSpeedMinus;
        private Texture2D _iconLinear;
        private Texture2D _iconEaseWhole;
        private Texture2D _iconEaseEach;
        private Texture2D _iconNextAudio;
        private Texture2D _iconSoundOn;
        private Texture2D _iconSoundOff;
        private Texture2D _iconAddCamera;
        private Texture2D _iconDeleteCamera;

        // --- ОБНОВЛЕННЫЙ МЕТОД ИНИЦИАЛИЗАЦИИ ДЛЯ СТАТИЧНЫХ ПОЗ И ДИНАМИЧЕСКИХ АНИМАЦИЙ (Пункт 6 ТЗ) ---
        public void InitializeGlobal(Furniture furniture)
        {
            // Запекаем нашу public-ссылку на мебель, полученную напрямую из UIPose.Open! 🪑⚡
            _activeFurnitureInstance = furniture;

            // Пытаемся найти живой плеер анимаций на сцене (если запущена динамическая анимация)
            // Если его нет (мы в статической позе) — _player будет null, но кнопки камер всё равно будут работать!
            _player = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>();

            // 0. Загружаем иконки из ресурсов DLL
            LoadEmbeddedResources();

            try
            {
                UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
                if (uiPose == null) return;

                // 1. ПЕРЕХВАТ СУЩЕСТВУЮЩЕЙ ПАНЕЛИ ПРИ СМЕНЕ АНИМАЦИИ
                Transform existingPanel = uiPose.transform.Find("Mod_FurnitureAnimationControls_BG");
                if (existingPanel != null)
                {
                    _uiPanelInstance = existingPanel.gameObject;
                    _uiPanelInstance.SetActive(true);

                    Transform buttonsContainer = _uiPanelInstance.transform.Find("Mod_AnimationButtonsContainer");
                    if (buttonsContainer != null)
                    {
                        // Перепривязываем события для кнопок анимаций
                        if (_player != null)
                        {
                            RebindButtonAction(buttonsContainer, "Mod_BtnSpeedPlus", () => { _player.ChangeSpeed(0.1f); UpdateSpeedButtonsText(); });
                            RebindButtonAction(buttonsContainer, "Mod_BtnSpeedMinus", () => { _player.ChangeSpeed(-0.1f); UpdateSpeedButtonsText(); });
                            RebindButtonAction(buttonsContainer, "Mod_BtnEaseToggle", () => { _player.ToggleEaseMode(); UpdateEaseButton(); });
                        }

                        RebindButtonAction(buttonsContainer, "Mod_BtnMuteToggle", () => {
                            if (AnimationAudioManager.Instance != null) { AnimationAudioManager.Instance.ToggleMute(); UpdateSoundButton(); }
                        });

                        RebindButtonAction(buttonsContainer, "Mod_BtnNextAudio", () => {
                            if (AnimationAudioManager.Instance != null) { AnimationAudioManager.Instance.PlayNextTrack(); UpdateSoundButton(); }
                        });

                        // Перепривязываем наши новые кастомные кнопки управления камерами! 🎯📸
                        RebindButtonAction(buttonsContainer, "Mod_BtnAddCamera", () => { ExecuteAddCamera(); });
                        RebindButtonAction(buttonsContainer, "Mod_BtnDeleteCamera", () => { ExecuteDeleteCamera(); });
                    }

                    UpdateInterfaceStates();
                    Plugin.Log.LogInfo("[UI] Существующая панель успешно перехвачена новым экземпляром плеера.");
                    return;
                }

                // 2. ЕСЛИ ПАНЕЛИ НЕТ — СОЗДАЕМ ЕЕ С НУЛЯ
                GameObject vanillaButtonsContainerGo = uiPose.panelTakeOffClothes;
                if (vanillaButtonsContainerGo == null) return;

                Transform vanillaBgTransform = vanillaButtonsContainerGo.transform.parent;
                if (vanillaBgTransform == null)
                {
                    Plugin.Log.LogError("[UI] Сбой: Не найден родительский фон для panelTakeOffClothes!");
                    return;
                }

                _uiPanelInstance = GameObject.Instantiate(vanillaBgTransform.gameObject, uiPose.transform, false);
                _uiPanelInstance.name = "Mod_FurnitureAnimationControls_BG";
                _uiPanelInstance.SetActive(true);

                RectTransform modRect = _uiPanelInstance.GetComponent<RectTransform>();
                RectTransform vanRect = vanillaBgTransform.GetComponent<RectTransform>();

                modRect.anchorMin = vanRect.anchorMin;
                modRect.anchorMax = vanRect.anchorMax;
                modRect.pivot = vanRect.pivot;

                // Увеличиваем высоту плашки, чтобы влезли новые кнопки камер
                Vector2 originalSize = vanRect.sizeDelta;
                modRect.sizeDelta = new Vector2(originalSize.x, originalSize.y * 1.8f); // Приподняли до 1.8, под 7 кнопок
                _uiPanelInstance.transform.localScale = vanRect.localScale;

                Vector3 vanLocalPos = vanRect.localPosition;
                modRect.localPosition = new Vector3(vanLocalPos.x, vanLocalPos.y - 250f, vanLocalPos.z);

                Transform buttonsContainerNew = _uiPanelInstance.transform.Find(vanillaButtonsContainerGo.name);
                if (buttonsContainerNew == null && _uiPanelInstance.transform.childCount > 0)
                {
                    buttonsContainerNew = _uiPanelInstance.transform.GetChild(0);
                }

                if (buttonsContainerNew == null)
                {
                    Plugin.Log.LogError("[UI] Критическая ошибка: Внутри нового фонда не найден контейнер для кнопок!");
                    return;
                }
                buttonsContainerNew.name = "Mod_AnimationButtonsContainer";

                // Очищаем оригинальный мусор
                Button[] oldButtons = _uiPanelInstance.GetComponentsInChildren<Button>(true);
                foreach (Button oldBtn in oldButtons) { GameObject.DestroyImmediate(oldBtn.gameObject); }

                Transform btnPrefab = vanillaButtonsContainerGo.transform.GetComponentInChildren<Button>()?.transform;
                if (btnPrefab != null)
                {
                    Vector3 btnPos = Vector3.zero;
                    float spacing = -45f; // Шаг разметки вниз по оси Y

                    // 1. Кнопка Скорость +
                    string speedPlusTxt = _player != null ? $"Speed: {Mathf.RoundToInt(_player.GetSpeed() * 100)}% (+10)" : "Speed: ---";
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnSpeedPlus", speedPlusTxt, btnPos, _iconSpeedPlus, () => {
                        _player?.ChangeSpeed(0.1f); UpdateSpeedButtonsText();
                    });
                    btnPos.y += spacing;

                    // 2. Кнопка Скорость -
                    string speedMinusTxt = _player != null ? $"Speed: {Mathf.RoundToInt(_player.GetSpeed() * 100)}% (-10)" : "Speed: ---";
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnSpeedMinus", speedMinusTxt, btnPos, _iconSpeedMinus, () => {
                        _player?.ChangeSpeed(-0.1f); UpdateSpeedButtonsText();
                    });
                    btnPos.y += spacing;

                    // 3. Кнопка Сглаживания
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnEaseToggle", $"Interpolation: ", btnPos, GetCurrentEaseSprite(), () => {
                        _player?.ToggleEaseMode(); UpdateEaseButton();
                    });
                    btnPos.y += spacing;

                    // 4. Кнопка: Следующий трек
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnNextAudio", "Next Audio", btnPos, _iconNextAudio, () => {
                        if (AnimationAudioManager.Instance != null) { AnimationAudioManager.Instance.PlayNextTrack(); UpdateSoundButton(); }
                    });
                    btnPos.y += spacing;

                    // 5. Кнопка звука
                    Texture2D currentSoundIcon = (AnimationAudioManager.Instance != null && AnimationAudioManager.Instance.IsMuted()) ? _iconSoundOff : _iconSoundOn;
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnMuteToggle", "Sound: ON", btnPos, currentSoundIcon, () => {
                        if (AnimationAudioManager.Instance != null) { AnimationAudioManager.Instance.ToggleMute(); UpdateSoundButton(); }
                    });
                    btnPos.y += spacing;

                    // =========================================================================
                    // ГАРАНТИРОВАННОЕ СОЗДАНИЕ КНОПОК КАМЕР НА ФИКСИРОВАННЫХ МЕСТАХ (Пункт 5) 📸🌟
                    // =========================================================================
                    // 6. Кнопка Добавить камеру
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnAddCamera", "Add custom cam", btnPos, _iconAddCamera, () => {
                        ExecuteAddCamera();
                    });

                    // 7. Кнопка Удалить камеру — встает ТОЧНО на те же координаты (btnPos), что и кнопка ADD!
                    // Благодаря этому они идеально заменяют друг друга без сдвига сетки интерфейса!
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnDeleteCamera", "Delete custom cam", btnPos, _iconDeleteCamera, () => {
                        ExecuteDeleteCamera();
                    });
                }

                UpdateInterfaceStates();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[UI] Ошибка декомпозиции иерархии плашек: {ex.Message}");
            }
        }

        private void Update()
        {
            // Каждый кадр динамически контролируем, какие кнопки сейчас должны быть видны на панели!
            UpdateInterfaceStates();
        }

        // --- ЦЕНТРАЛЬНЫЙ КОНТРОЛЛЕР ИНДИВИДУАЛЬНОЙ ВИДИМОСТИ КНОПОК (Пункты 3, 4, 5, 6) ---
        private void UpdateInterfaceStates()
        {
            if (_uiPanelInstance == null) return;

            Transform container = _uiPanelInstance.transform.Find("Mod_AnimationButtonsContainer");
            if (container == null) return;

            // А) Управление кнопками анимации: видны только если плеер живой и анимация выбрана
            bool hasActiveAnimation = (_player != null && _player.isActiveAndEnabled);
            container.Find("Mod_BtnSpeedPlus")?.gameObject.SetActive(hasActiveAnimation);
            container.Find("Mod_BtnSpeedMinus")?.gameObject.SetActive(hasActiveAnimation);
            container.Find("Mod_BtnEaseToggle")?.gameObject.SetActive(hasActiveAnimation);

            // Б) Диагностика состояний камер игры
            bool isFreeCamActive = (FreeLookCam.code != null && FreeLookCam.code.enabled);
            bool isCustomCamSelected = false;
            string currentSelectedCamName = "";

            UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
            Furniture currentFurniture = uiPose != null ? uiPose.curFurniture : null;

            if (currentFurniture != null && currentFurniture.cameras?.items != null)
            {
                foreach (object obj in currentFurniture.cameras.items)
                {
                    Transform t = obj as Transform;
                    if (t != null && t.gameObject.activeInHierarchy)
                    {
                        currentSelectedCamName = t.name;
                        if (t.name.StartsWith("Custom camera")) isCustomCamSelected = true;
                        break;
                    }
                }
            }

            // Вытаскиваем лимиты
            FurnitureConfig currentConfig = null;
            if (currentFurniture != null)
            {
                string cleanName = currentFurniture.name.Replace("(Clone)", "").Trim();
                ConfigManager.LoadedConfigs.TryGetValue(cleanName, out currentConfig);
            }

            int nextVacantNum = (currentFurniture != null && currentConfig != null) ? ConfigManager.GetNextVacantCameraNumber(currentFurniture, currentConfig) : -1;
            bool hasVacantSlots = (nextVacantNum != -1);

            // В) ЖЕСТКАЯ УСТАНОВКА АКТИВНОСТИ КНОПОК КАМЕР (Пункты 3, 4, 5) 🔥🎯
            GameObject btnAddGo = container.Find("Mod_BtnAddCamera")?.gameObject;
            GameObject btnDeleteGo = container.Find("Mod_BtnDeleteCamera")?.gameObject;

            if (btnAddGo != null && btnDeleteGo != null)
            {
                if (isCustomCamSelected)
                {
                    btnAddGo.SetActive(false);
                    btnDeleteGo.SetActive(true); // Показываем кнопку DELETE
                    UpdateText("Mod_BtnDeleteCamera", $"Delete: {currentSelectedCamName}");
                }
                else if (isFreeCamActive && hasVacantSlots)
                {
                    btnAddGo.SetActive(true); // Показываем кнопку ADD
                    btnDeleteGo.SetActive(false);
                    UpdateText("Mod_BtnAddCamera", $"Add camera {nextVacantNum}");
                }
                else
                {
                    // Место пустует, обе кнопки выключены, но сетка остальных элементов не сдвигается!
                    btnAddGo.SetActive(false);
                    btnDeleteGo.SetActive(false);
                }
            }

            // Обновляем текстовые статусы звуков и интерполяций
            UpdateSpeedButtonsText();
            UpdateEaseButton();
            UpdateSoundButton();
        }

        // --- ИСПОЛНИТЕЛЬНАЯ КНОПКА: ДОБАВИТЬ КАМЕРУ (Пункт 3) ---
        private void ExecuteAddCamera()
        {
            UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
            Furniture furniture = uiPose?.curFurniture;
            if (furniture == null || Global.code?.freeCamera == null) return;

            string cleanName = furniture.name.Replace("(Clone)", "").Trim();
            if (ConfigManager.LoadedConfigs.TryGetValue(cleanName, out FurnitureConfig config))
            {
                int vacantNumber = ConfigManager.GetNextVacantCameraNumber(furniture, config);
                if (vacantNumber == -1) return;

                try
                {
                    Transform freeCamTrans = Global.code.freeCamera.transform;
                    Vector3 camLocalPos = furniture.transform.InverseTransformPoint(freeCamTrans.position);
                    Quaternion camLocalRot = Quaternion.Inverse(furniture.transform.rotation) * freeCamTrans.rotation;
                    Vector3 camLocalEuler = camLocalRot.eulerAngles;

                    config.CustomCameras.Add(new CameraData
                    {
                        Name = $"Custom camera {vacantNumber}",
                        pos = new Vector3Data { x = (float)Math.Round(camLocalPos.x, 4), y = (float)Math.Round(camLocalPos.y, 4), z = (float)Math.Round(camLocalPos.z, 4) },
                        rot = new Vector3Data { x = (float)Math.Round(camLocalEuler.x, 4), y = (float)Math.Round(camLocalEuler.y, 4), z = (float)Math.Round(camLocalEuler.z, 4) }
                    });

                    string fullPath = Path.Combine(ConfigManager.PrefabsConfigPath, $"{cleanName}_Config.json");
                    string finalJson = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(fullPath, finalJson);

                    FurnitureInjectorPatch.RebuildFurniturePoses(furniture);
                    uiPose.Refresh();

                    Plugin.Log.LogInfo($"[UI_SDK] Камера 'Custom camera {vacantNumber}' успешно создана!");
                }
                catch (Exception ex) { Plugin.Log.LogError($"[UI_SDK] Ошибка добавления камеры: {ex.Message}"); }
            }
        }

        // --- ИСПОЛНИТЕЛЬНАЯ КНОПКА: УДАЛИТЬ КАМЕРУ (Пункт 4) ---
        private void ExecuteDeleteCamera()
        {
            UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
            Furniture furniture = uiPose?.curFurniture;
            if (furniture == null) return;

            // Вычисляем, какая именно кастомная камера сейчас выбрана на сцене
            string targetCamName = "";
            if (furniture.cameras?.items != null)
            {
                foreach (object obj in furniture.cameras.items)
                {
                    Transform t = obj as Transform;
                    if (t != null && t.gameObject.activeInHierarchy && t.name.StartsWith("Custom camera"))
                    {
                        targetCamName = t.name; break;
                    }
                }
            }

            if (string.IsNullOrEmpty(targetCamName)) return;

            string cleanName = furniture.name.Replace("(Clone)", "").Trim();
            if (ConfigManager.LoadedConfigs.TryGetValue(cleanName, out FurnitureConfig config))
            {
                try
                {
                    config.CustomCameras.RemoveAll(c => c.Name == targetCamName);

                    string fullPath = Path.Combine(ConfigManager.PrefabsConfigPath, $"{cleanName}_Config.json");
                    string finalJson = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(fullPath, finalJson);

                    Transform camGroupTrans = furniture.camerasGroup;
                    if (camGroupTrans != null)
                    {
                        Transform targetCamObj = camGroupTrans.Find(targetCamName);
                        if (targetCamObj != null)
                        {
                            if (furniture.cameras != null) furniture.cameras.RemoveItem(targetCamObj);
                            GameObject.Destroy(targetCamObj.gameObject);
                        }
                    }

                    if (FreeLookCam.code != null) FreeLookCam.code.Reset();
                    uiPose.Refresh();

                    Plugin.Log.LogInfo($"[UI_SDK] Камера '{targetCamName}' успешно удалена!");
                }
                catch (Exception ex) { Plugin.Log.LogError($"[UI_SDK] Ошибка удаления камеры: {ex.Message}"); }
            }
        }

        #region Ванильная обвязка текстур и иконок префаба

        private void LoadEmbeddedResources()
        {
            _iconSpeedPlus = LoadTextureFromDll("FurnitureAnimations.Resources.icon_fasterPlayback.png");
            _iconSpeedMinus = LoadTextureFromDll("FurnitureAnimations.Resources.icon_slowerPlayback.png");
            _iconLinear = LoadTextureFromDll("FurnitureAnimations.Resources.icon_flowEven.png");
            _iconEaseWhole = LoadTextureFromDll("FurnitureAnimations.Resources.icon_easeInOutWhole.png");
            _iconEaseEach = LoadTextureFromDll("FurnitureAnimations.Resources.icon_easeInOutEach.png");
            _iconNextAudio = LoadTextureFromDll("FurnitureAnimations.Resources.icon_nextAudio.png");
            _iconSoundOn = LoadTextureFromDll("FurnitureAnimations.Resources.icon_soundOn.png");
            _iconSoundOff = LoadTextureFromDll("FurnitureAnimations.Resources.icon_soundOff.png");
            _iconAddCamera = LoadTextureFromDll("FurnitureAnimations.Resources.icon_addCamera.png");
            _iconDeleteCamera = LoadTextureFromDll("FurnitureAnimations.Resources.icon_deleteCamera.png");
        }

        private Texture2D LoadTextureFromDll(string resourcePath)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
                {
                    if (stream == null) return null;
                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                                        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(buffer)) return texture;
                }
            }
            catch (Exception) { }
            return null;
        }

        private void CreateUiButton(Transform parent, Transform prefab, string objName, string label, Vector3 localPos, Texture2D iconTexture, System.Action onClick)
        {
            GameObject btnGo = GameObject.Instantiate(prefab.gameObject, parent);
            btnGo.name = objName;

            RectTransform r = btnGo.GetComponent<RectTransform>();
            if (r != null) r.anchoredPosition = localPos;

            Button b = btnGo.GetComponent<Button>();
            if (b != null) GameObject.DestroyImmediate(b);

            b = btnGo.AddComponent<Button>();
            b.onClick.AddListener(() => onClick?.Invoke());

            RawImage rawImg = btnGo.GetComponent<RawImage>();
            if (rawImg != null && iconTexture != null) rawImg.texture = iconTexture;

            Text t = btnGo.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.text = label;
                t.fontSize = 10;
                t.color = Color.white;
            }
            btnGo.SetActive(true);
        }

        private void UpdateText(string objName, string newText)
        {
            if (_uiPanelInstance == null) return;
            Transform btn = _uiPanelInstance.transform.Find($"Mod_AnimationButtonsContainer/{objName}");
            Text t = btn?.GetComponentInChildren<Text>();
            if (t != null) t.text = newText;
        }

        public void HidePanel() { if (_uiPanelInstance != null) _uiPanelInstance.SetActive(false); }
        private void OnDestroy() { HidePanel(); }

        private void UpdateSpeedButtonsText()
        {
            if (_player == null) return;
            int percent = Mathf.RoundToInt(_player.GetSpeed() * 100);
            UpdateText("Mod_BtnSpeedMinus", $"Speed: {percent}% (-10)");
            UpdateText("Mod_BtnSpeedPlus", $"Speed: {percent}% (+10)");
        }

        private Texture2D GetCurrentEaseSprite()
        {
            if (_player == null) return _iconLinear;
            switch (_player.GetEaseMode())
            {
                case EaseMode.Global: return _iconEaseWhole;
                case EaseMode.PerFrame: return _iconEaseEach;
                case EaseMode.Linear:
                default: return _iconLinear;
            }
        }

        private void UpdateEaseButton()
        {
            if (_player == null) return;
            UpdateText("Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}");
            UpdateImageSprite("Mod_BtnEaseToggle", GetCurrentEaseSprite());
        }

        private void UpdateSoundButton()
        {
            bool isMuted = AnimationAudioManager.Instance != null && AnimationAudioManager.Instance.IsMuted();
            UpdateText("Mod_BtnMuteToggle", isMuted ? "Sound: MUTED" : "Sound: ON");
            UpdateImageSprite("Mod_BtnMuteToggle", isMuted ? _iconSoundOff : _iconSoundOn);
        }

        private void UpdateImageSprite(string objName, Texture2D newTexture)
        {
            if (_uiPanelInstance == null || newTexture == null) return;
            Transform btn = _uiPanelInstance.transform.Find($"Mod_AnimationButtonsContainer/{objName}");
            RawImage rawImg = btn?.GetComponent<RawImage>();
            if (rawImg != null) { rawImg.texture = newTexture; return; }
            Image img = btn?.GetComponent<Image>();
            if (img != null) img.sprite = Sprite.Create(newTexture, new Rect(0, 0, newTexture.width, newTexture.height), new Vector2(0.5f, 0.5f));
        }

        private void RebindButtonAction(Transform container, string buttonName, System.Action onClick)
        {
            Transform btnTransform = container.Find(buttonName);
            Button b = btnTransform?.GetComponent<Button>();
            if (b != null)
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() => onClick?.Invoke());
            }
        }
        #endregion
    }
}
