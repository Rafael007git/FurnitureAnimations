using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FurnitureAnimationsMod
{
    public class AnimationUiControls : MonoBehaviour
    {
        private FurnitureAnimationPlayer _player;
        private GameObject _uiPanelInstance;

        // Кэш для иконок, чтобы не читать из сборки каждый кадр
        private Texture2D _iconSpeedMinus;
        private Texture2D _iconSpeedPlus;
        private Texture2D _iconNextAudio;
        private Texture2D _iconSoundOn;
        private Texture2D _iconSoundOff;
        private Texture2D _iconLinear;
        private Texture2D _iconEaseWhole;
        private Texture2D _iconEaseEach;
        public void Initialize(FurnitureAnimationPlayer player)
        {
            _player = player;

            // 0. Загружаем иконки из ресурсов DLL
            LoadEmbeddedResources();

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
                
                // --- АРХИТЕКТУРНОЕ УВЕЛИЧЕНИЕ В 1.5 РАЗА ---
                Vector2 originalSize = vanRect.sizeDelta;
                modRect.sizeDelta = new Vector2(originalSize.x, originalSize.y * 1.5f);

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


                    // 1. Кнопка Скорость +
                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnSpeedPlus",
                        $"Speed: {Mathf.RoundToInt(_player.GetSpeed() * 100)}% (+10)", btnPos, _iconSpeedPlus, () => {
                            _player.ChangeSpeed(0.1f);
                            UpdateSpeedButtonsText();
                        });
                    btnPos.y += spacing;

                    // 2. Кнопка Скорость -
                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnSpeedMinus",
                        $"Speed: {Mathf.RoundToInt(_player.GetSpeed() * 100)}% (-10)", btnPos, _iconSpeedMinus, () => {
                            _player.ChangeSpeed(-0.1f);
                            UpdateSpeedButtonsText();
                        });
                    btnPos.y += spacing;

                    // 3. Кнопка Сглаживания (Интерполяции)
                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnEaseToggle",
                        $"Interpolation: {_player.GetEaseMode()}", btnPos, GetCurrentEaseSprite(), () => {
                            _player.ToggleEaseMode();
                            UpdateEaseButton(); // Используем централизованный метод обновления текста и иконки
                        });
                    btnPos.y += spacing;

                    // 4. НОВАЯ КНОПКА: СЛЕДУЮЩИЙ ТРЕК
                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnNextAudio", "Next Audio", btnPos, _iconNextAudio, () => {
                        if (AnimationAudioManager.Instance != null)
                        {
                            AnimationAudioManager.Instance.PlayNextTrack();
                            UpdateSoundButton(); // <-- ПРАВКА: Синхронизируем иконку звука, если Mute сбросился!
                        }
                        else
                        {
                            Plugin.Log.LogWarning("[UI] Невозможно переключить трек: AnimationAudioManager не инициализирован.");
                        }
                    });
                    btnPos.y += spacing;

                    // 5. Кнопка MuteToggle
                    Texture2D currentSoundIcon = (AnimationAudioManager.Instance != null && AnimationAudioManager.Instance.IsMuted()) ? _iconSoundOff : _iconSoundOn;
                    CreateUiButton(buttonsContainer, btnPrefab, "Mod_BtnMuteToggle",
                        "Sound: ON", btnPos, currentSoundIcon, () => {
                            if (AnimationAudioManager.Instance != null)
                            {
                                AnimationAudioManager.Instance.ToggleMute();
                                UpdateSoundButton(); // Используем централизованный метод обновления текста и иконки
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

        #region Исправленная работа с текстурами (RawImage)

        private void LoadEmbeddedResources()
        {
            _iconSpeedMinus = LoadTextureFromDll("FurnitureAnimations.Resources.icon_slowerPlayback.png");
            _iconSpeedPlus = LoadTextureFromDll("FurnitureAnimations.Resources.icon_fasterPlayback.png");
            _iconNextAudio = LoadTextureFromDll("FurnitureAnimations.Resources.icon_nextAudio.png");
            _iconSoundOn = LoadTextureFromDll("FurnitureAnimations.Resources.icon_soundOn.png");
            _iconSoundOff = LoadTextureFromDll("FurnitureAnimations.Resources.icon_soundOff.png");

            _iconLinear = LoadTextureFromDll("FurnitureAnimations.Resources.icon_flowEven.png");
            _iconEaseWhole = LoadTextureFromDll("FurnitureAnimations.Resources.icon_easeInOutWhole.png");
            _iconEaseEach = LoadTextureFromDll("FurnitureAnimations.Resources.icon_easeInOutEach.png");
        }

        private Texture2D LoadTextureFromDll(string resourcePath)
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
                {
                    if (stream == null)
                    {
                        Plugin.Log.LogError($"[UI_Resources] Не найден встроенный ресурс: {resourcePath}");
                        return null;
                    }

                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);

                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(buffer))
                    {
                        return texture; // Возвращаем чистую текстуру для RawImage
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[UI_Resources] Ошибка загрузки иконки {resourcePath}: {ex.Message}");
            }
            return null;
        }

        #endregion

        // Изменен тип аргумента с Sprite на Texture2D
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

            // --- ЖЕСТКОЕ ПЕРЕОПРЕДЕЛЕНИЕ RAWIMAGE ---
            RawImage rawImg = btnGo.GetComponent<RawImage>();
            if (rawImg != null && iconTexture != null)
            {
                rawImg.texture = iconTexture; // Меняем текстуру лифчика на нашу иконку!
            }
            else if (rawImg == null && iconTexture != null)
            {
                // На случай если там обычный Image, подстрахуемся
                Image img = btnGo.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = Sprite.Create(iconTexture, new Rect(0, 0, iconTexture.width, iconTexture.height), new Vector2(0.5f, 0.5f));
                }
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
            if (_uiPanelInstance == null) return;
            Transform btn = _uiPanelInstance.transform.Find($"Mod_AnimationButtonsContainer/{objName}");
            Text t = btn?.GetComponentInChildren<Text>();
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
            UpdateText("Mod_BtnEaseToggle", $"Interpolation: {_player.GetEaseMode()}");
            UpdateImageSprite("Mod_BtnEaseToggle", GetCurrentEaseSprite());
        }

        private void UpdateSoundButton()
        {
            bool isMuted = AnimationAudioManager.Instance != null && AnimationAudioManager.Instance.IsMuted();
            UpdateText("Mod_BtnMuteToggle", isMuted ? "Sound: MUTED" : "Sound: ON");
            UpdateImageSprite("Mod_BtnMuteToggle", isMuted ? _iconSoundOff : _iconSoundOn);
        }

        private void UpdateInterfaceStates()
        {
            UpdateSpeedButtonsText();
            UpdateEaseButton();
            UpdateSoundButton();
        }

        private void UpdateImageSprite(string objName, Texture2D newTexture)
        {
            if (_uiPanelInstance == null || newTexture == null) return;
            Transform btn = _uiPanelInstance.transform.Find($"Mod_AnimationButtonsContainer/{objName}");

            RawImage rawImg = btn?.GetComponent<RawImage>();
            if (rawImg != null)
            {
                rawImg.texture = newTexture;
                return;
            }

            Image img = btn?.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = Sprite.Create(newTexture, new Rect(0, 0, newTexture.width, newTexture.height), new Vector2(0.5f, 0.5f));
            }
        }

    }
}
