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
        private bool _lastHadPlayerState = false;

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

        // --- МЕТОД ИНИЦИАЛИЗАЦИИ ДЛЯ СТАТИЧНЫХ ПОЗ И ДИНАМИЧЕСКИХ АНИМАЦИЙ ---
        public void InitializeGlobal(Furniture furniture)
        {
            // Запекаем нашу public-ссылку на мебель, полученную напрямую из UIPose.Open! 🪑⚡
            _activeFurnitureInstance = furniture;

            // Пытаемся найти живой плеер анимаций на сцене (если запущена динамическая анимация)
            // Если его нет (мы в статической позе) — _player будет null, но кнопка камеры всё равно будет работать!
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
                            RebindButtonAction(buttonsContainer, "Mod_BtnSpeedPlus", () => {
                                var p = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>();
                                if (p != null) { p.ChangeSpeed(0.1f); UpdateSpeedButtonsText(); }
                            });
                            RebindButtonAction(buttonsContainer, "Mod_BtnSpeedMinus", () => {
                                var p = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>();
                                if (p != null) { p.ChangeSpeed(-0.1f); UpdateSpeedButtonsText(); }
                            });
                            RebindButtonAction(buttonsContainer, "Mod_BtnEaseToggle", () => {
                                var p = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>();
                                if (p != null) { p.ToggleEaseMode(); UpdateEaseButton(); }
                            });
                        }

                        RebindButtonAction(buttonsContainer, "Mod_BtnMuteToggle", () =>
                        {
                            if (AnimationAudioManager.Instance != null) { AnimationAudioManager.Instance.ToggleMute(); UpdateSoundButton(); }
                        });

                        RebindButtonAction(buttonsContainer, "Mod_BtnNextAudio", () =>
                        {
                            if (AnimationAudioManager.Instance != null) { AnimationAudioManager.Instance.PlayNextTrack(); UpdateSoundButton(); }
                        });

                        // Перепривязываем нашу единую контекстную кнопку управления камерами! 🎯📸
                        RebindButtonAction(buttonsContainer, "Mod_BtnContextCamera", () =>
                        {
                            UIPose up = GameObject.FindObjectOfType<UIPose>();
                            Furniture cf = up != null ? up.curFurniture : null;
                            bool isCustom = false;
                            if (cf != null && cf.cameras?.items != null)
                            {
                                foreach (object o in cf.cameras.items)
                                {
                                    Transform t = o as Transform;
                                    if (t != null && t.gameObject.activeInHierarchy && t.name.StartsWith("Custom camera"))
                                    {
                                        isCustom = true;
                                        break;
                                    }
                                }
                            }
                            if (isCustom) ExecuteDeleteCamera();
                            else if (FreeLookCam.code != null && FreeLookCam.code.enabled) ExecuteAddCamera();
                        });
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
                    Plugin.Log.LogError("[UI] Сбой: Не найден родительский фoнд для panelTakeOffClothes!");
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

                // Настраиваем базовый размер плашки
                Vector2 originalSize = vanRect.sizeDelta;
                modRect.sizeDelta = new Vector2(originalSize.x, originalSize.y * 1.8f);
                _uiPanelInstance.transform.localScale = vanRect.localScale;

                Vector3 vanLocalPos = vanRect.localPosition;
                modRect.localPosition = new Vector3(vanLocalPos.x, vanLocalPos.y - 173f, vanLocalPos.z);

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

                // Очищаем оригинальный мусор (кнопки раздевания)
                Button[] oldButtons = _uiPanelInstance.GetComponentsInChildren<Button>(true);
                foreach (Button oldBtn in oldButtons) { GameObject.DestroyImmediate(oldBtn.gameObject); }

                Transform btnPrefab = vanillaButtonsContainerGo.transform.GetComponentInChildren<Button>()?.transform;
                if (btnPrefab != null)
                {
                    Vector3 btnPos = Vector3.zero;
                    float spacing = -45f; // Шаг разметки вниз по оси Y

                    // ==========================================================
                    // А) ЕДИНСТВЕННАЯ КОНТЕКСТНАЯ КНОПКА КАМЕРЫ (ТЕПЕРЬ ПЕРВАЯ) 📸🌟
                    // ==========================================================
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnContextCamera", "Camera Control", btnPos, _iconAddCamera, () =>
                    {
                        UIPose up = GameObject.FindObjectOfType<UIPose>();
                        Furniture cf = up != null ? up.curFurniture : null;

                        bool isCustom = false;
                        if (cf != null && cf.cameras?.items != null)
                        {
                            foreach (object o in cf.cameras.items)
                            {
                                Transform t = o as Transform;
                                if (t != null && t.gameObject.activeInHierarchy && t.name.StartsWith("Custom camera"))
                                {
                                    isCustom = true;
                                    break;
                                }
                            }
                        }

                        if (isCustom) ExecuteDeleteCamera();
                        else if (FreeLookCam.code != null && FreeLookCam.code.enabled) ExecuteAddCamera();
                    });
                    btnPos.y += spacing;

                    // ==========================================================
                    // Б) БЛОК АНИМАЦИЙ И ЗВУКА (ИДЕТ СЛЕДОМ) 🎬🎵
                    // ==========================================================

                    // --- КНОПКА СКОРОСТЬ + ---
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnSpeedPlus", "Speed: ---", btnPos, _iconSpeedPlus, () => {
                        var p = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>();
                        if (p != null) { p.ChangeSpeed(0.1f); UpdateSpeedButtonsText(); }
                    });
                    btnPos.y += spacing;

                    // --- КНОПКА СКОРОСТЬ - ---
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnSpeedMinus", "Speed: ---", btnPos, _iconSpeedMinus, () => {
                        var p = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>();
                        if (p != null) { p.ChangeSpeed(-0.1f); UpdateSpeedButtonsText(); }
                    });
                    btnPos.y += spacing;

                    // Кнопка Сглаживания
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnEaseToggle", "Interpolation: ", btnPos, GetCurrentEaseSprite(), () => {
                        var p = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>();
                        if (p != null) { p.ToggleEaseMode(); UpdateEaseButton(); }
                    });
                    btnPos.y += spacing;

                    // Кнопка: Следующий трек
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnNextAudio", "Next Audio", btnPos, _iconNextAudio, () => { if (AnimationAudioManager.Instance != null) { AnimationAudioManager.Instance.PlayNextTrack(); UpdateSoundButton(); } });
                    btnPos.y += spacing;

                    // Кнопка звука
                    Texture2D currentSoundIcon = (AnimationAudioManager.Instance != null && AnimationAudioManager.Instance.IsMuted()) ? _iconSoundOff : _iconSoundOn;
                    CreateUiButton(buttonsContainerNew, btnPrefab, "Mod_BtnMuteToggle", "Sound: ON", btnPos, currentSoundIcon, () => { if (AnimationAudioManager.Instance != null) { AnimationAudioManager.Instance.ToggleMute(); UpdateSoundButton(); } });
                    btnPos.y += spacing;
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
            // Быстрая проверка живости плеера на сцене движка Unity
            bool currentHasPlayer = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>() != null;

            // Интерфейс просыпается и пересчитывает uGUI-кнопки ТОЛЬКО в ту миллисекунду,
            // когда плеер физически появился на сцене (или исчез при возврате в позу)
            if (currentHasPlayer != _lastHadPlayerState)
            {
                _lastHadPlayerState = currentHasPlayer;

                if (currentHasPlayer)
                {
                    // Если плеер родился — запускаем мягкую корутину ожидания конца кадра.
                    // Это даст ванильной игре UIPose время завершить свой внутренний цикл,
                    // и мы со 100% гарантией включим кнопки на актуальной плашке Canvas! 🔥
                    StartCoroutine(DelayedInterfaceRefresh());
                }
                else
                {
                    // Если плеер уничтожен (вернулись в позу) — мгновенно гасим кнопки скорости
                    UpdateInterfaceStates();
                }
            }
        }

        private System.Collections.IEnumerator DelayedInterfaceRefresh()
        {
            yield return new WaitForEndOfFrame(); // Пропускаем рендер текущего кадра
            yield return new WaitForEndOfFrame(); // Пропускаем второй кадр для железной гарантии

            UpdateInterfaceStates(); // Пересчитываем хамелеона и зажигаем кнопки анимации! 🎯🚀
        }

        // --- ЦЕНТРАЛЬНЫЙ КОНТРОЛЛЕР ИНДИВИДУАЛЬНОЙ ВИДИМОСТИ КНОПОК ---
        public void UpdateInterfaceStates()
        {
            if (_uiPanelInstance == null) return;

            Transform container = _uiPanelInstance.transform.Find("Mod_AnimationButtonsContainer");
            if (container == null) return;

            // --- ИСПРАВЛЕНИЕ БАГА АНИМАЦИИ: Динамический перехват плеера --- ⚡
            // Использованием .Equals(null) или жестким FindObjectOfType гарантируем, 
            // что стертый плеер Латины уступит место новому жильцу!
            if (_player == null || _player.Equals(null))
            {
                _player = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>();
            }

            // А) Управление кнопками анимации И ЗВУКА: видны только если плеер живой и анимация выбрана
            bool hasActiveAnimation = (_player != null && _player.isActiveAndEnabled);
            container.Find("Mod_BtnSpeedPlus")?.gameObject.SetActive(hasActiveAnimation);
            container.Find("Mod_BtnSpeedMinus")?.gameObject.SetActive(hasActiveAnimation);
            container.Find("Mod_BtnEaseToggle")?.gameObject.SetActive(hasActiveAnimation);
            container.Find("Mod_BtnNextAudio")?.gameObject.SetActive(hasActiveAnimation);
            container.Find("Mod_BtnMuteToggle")?.gameObject.SetActive(hasActiveAnimation);

            // --- ОБНОВЛЕНИЕ uGUI СТЕЙТОВ ПОД ПАРАМЕТРЫ СВЕЖЕГО ПЛЕЕРА ИЗ ОЗУ --- 🧠🌟
            if (hasActiveAnimation)
            {
                UpdateSpeedButtonsText();
                UpdateEaseButton();
                UpdateSoundButton();
            }

            // Б) Диагностика состояний камер игры
            bool isFreeCamActive = (FreeLookCam.code != null && FreeLookCam.code.enabled);
            bool isCustomCamSelected = false;
            string currentSelectedCamName = "";

            UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
            Furniture currentFurniture = uiPose != null ? uiPose.curFurniture : null;

            int vanillaCamsCount = 0;   // Счётчик встроенных ванильных камер игры
            int customCamsCount = 0;    // Счётчик наших кастомных SDK-камер

            if (currentFurniture != null && currentFurniture.cameras?.items != null)
            {
                foreach (object obj in currentFurniture.cameras.items)
                {
                    Transform t = obj as Transform;
                    if (t != null)
                    {
                        // Раздельно считаем типы камер на сцене мебели
                        if (t.name.StartsWith("Custom camera"))
                        {
                            customCamsCount++;
                        }
                        else
                        {
                            vanillaCamsCount++;
                        }

                        // Фиксируем имя той, которая активна прямо сейчас
                        if (t.gameObject.activeInHierarchy)
                        {
                            currentSelectedCamName = t.name;
                            if (t.name.StartsWith("Custom camera")) isCustomCamSelected = true;
                        }
                    }
                }
            }

            // Вытаскиваем лимиты свободных слотов для добавления
            FurnitureConfig currentConfig = null;
            if (currentFurniture != null)
            {
                string cleanName = currentFurniture.name.Replace("(Clone)", "").Trim();
                ConfigManager.LoadedConfigs.TryGetValue(cleanName, out currentConfig);
            }

            int nextVacantNum = (currentFurniture != null && currentConfig != null) ? ConfigManager.GetNextVacantCameraNumber(currentFurniture, currentConfig) : -1;
            bool hasVacantSlots = (nextVacantNum != -1);

            // Условие блокировки удаления единственной физической камеры (Защита от фантомных ракурсов)
            bool isTheOnlyPhysicalCameraLeft = isCustomCamSelected && (vanillaCamsCount == 0) && (customCamsCount <= 1);

            // В) УПРАВЛЕНИЕ ЕДИНОЙ КОНТЕКСТНОЙ КНОПКОЙ КАМЕРЫ 🔥🎯
            Transform contextBtnTrans = container.Find("Mod_BtnContextCamera");
            if (contextBtnTrans != null)
            {
                GameObject btnGo = contextBtnTrans.gameObject;
                Button b = btnGo.GetComponent<Button>();
                RawImage ri = btnGo.GetComponent<RawImage>(); // Хватаем компонент нашей текстуры иконки!

                if (isCustomCamSelected)
                {
                    btnGo.SetActive(true);

                    if (isTheOnlyPhysicalCameraLeft)
                    {
                        // Жесткий запрет: ванильных камер нет, кастомная — последняя на сцене.
                        UpdateText("Mod_BtnContextCamera", "Cannot delete last cam");
                        UpdateImageSprite("Mod_BtnContextCamera", _iconDeleteCamera);
                        if (b != null) b.interactable = false;
                        // ДЕЛАЕМ ТЕКСТУРУ ИКОНКИ ПОЛУПРОЗРАЧНОЙ (30%) 🎨
                        if (ri != null) ri.color = new Color(1f, 1f, 1f, 0.3f);
                    }
                    else
                    {
                        // Кастомную камеру можно спокойно удалять
                        UpdateText("Mod_BtnContextCamera", $"Delete: {currentSelectedCamName}");
                        UpdateImageSprite("Mod_BtnContextCamera", _iconDeleteCamera);
                        if (b != null) b.interactable = true;
                        // ВОЗВРАЩАЕМ 100% ЯРКОСТЬ
                        if (ri != null) ri.color = Color.white;
                    }
                }
                else if (isFreeCamActive && hasVacantSlots)
                {
                    btnGo.SetActive(true);
                    UpdateText("Mod_BtnContextCamera", $"Add camera {nextVacantNum}");
                    UpdateImageSprite("Mod_BtnContextCamera", _iconAddCamera);
                    if (b != null) b.interactable = true;
                    // ВОЗВРАЩАЕМ 100% ЯРКОСТЬ
                    if (ri != null) ri.color = Color.white;
                }
                else
                {
                    // Если активна встроенная ванильная камера игры — блокируем кнопку, защищая её от изменений
                    btnGo.SetActive(true);
                    UpdateText("Mod_BtnContextCamera", "Vanilla Camera");
                    UpdateImageSprite("Mod_BtnContextCamera", _iconAddCamera);
                    if (b != null) b.interactable = false;
                    // ДЕЛАЕМ ТЕКСТУРУ ИКОНКИ ПОЛУПРОЗРАЧНОЙ (30%) 🎨
                    if (ri != null) ri.color = new Color(1f, 1f, 1f, 0.3f);
                }
            }

            // Обновляем текстовые статусы звуков и интерполяций
            UpdateSpeedButtonsText();
            UpdateEaseButton();
            UpdateSoundButton();

            // Если плеер на сцене умер (анимация выключена), очищаем ссылку, чтобы при следующем запуске анимации мод перехватил её заново
            if (_player != null && !_player.isActiveAndEnabled)
            {
                _player = null;
            }
        }

        // --- ИСПОЛНИТЕЛЬНАЯ КНОПКА: ДОБАВИТЬ КАМЕРУ ---
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

        // --- ИСПОЛНИТЕЛЬНАЯ КНОПКА: УДАЛИТЬ КАМЕРУ ---
        private void ExecuteDeleteCamera()
        {
            UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
            Furniture furniture = uiPose?.curFurniture;
            if (furniture == null) return;

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

        private void ExecuteEaseToggleAction()
        {
            // 1. Хватаем строго живой синглтон плеера напрямую из движка в момент клика
            var livePlayer = FurnitureAnimationPlayer.Instance;

            // Резервный поиск на случай, если синглтон еще не успел обновиться
            if (livePlayer == null || livePlayer.Equals(null) || !livePlayer.isActiveAndEnabled)
            {
                livePlayer = UnityEngine.Object.FindObjectOfType<FurnitureAnimationPlayer>();
            }

            // Синхронизируем наше локальное поле класса, чтобы другие методы (например, UpdateEaseButton) не упали
            _player = livePlayer;

            if (livePlayer != null && _activeFurnitureInstance != null)
            {
                // 2. Меняем режим в ЖИВОМ плеере и обновляем иконку/текст кнопки
                livePlayer.ToggleEaseMode();
                UpdateEaseButton();

                // 3. Безопасно вычисляем компоненты ключа
                string cleanFurnName = _activeFurnitureInstance.name.Replace("(Clone)", "").Trim();
                string activeAnimName = livePlayer.GetPlayingAnimationName();

                // Фикс ошибок 404: если имя трека сомнительное, принудительно шлем "noAudio" по ТЗ
                string currentTrackName = "noAudio";
                if (AnimationAudioManager.Instance != null)
                {
                    string rawTrack = AnimationAudioManager.Instance.GetCurrentTrackName();
                    if (!string.IsNullOrEmpty(rawTrack) && !rawTrack.Contains("404") && rawTrack != "unknown")
                    {
                        currentTrackName = rawTrack;
                    }
                }

                // 4. Отправляем на валидацию и запись в ОЗУ-карту
                ConfigManager.UpdateRuntimePlaybackMemory(
                    cleanFurnName,
                    activeAnimName,
                    currentTrackName,
                    livePlayer.GetSpeed(),
                    livePlayer.GetEaseMode()
                );

                Plugin.Log.LogInfo($"[UI_Action] Сглаживание успешно изменено для живой пары: {activeAnimName} + {currentTrackName}");
            }
            else
            {
                Plugin.Log.LogError("[UI_Action] Не удалось переключить сглаживание: активный плеер анимаций физически отсутствует на сцене!");
            }
        }
        #endregion
    }
}
