using System;
using UnityEngine;

namespace FurnitureAnimationsMod
{
    public class EditorUiManager : MonoBehaviour
    {
        private static EditorUiManager _instance;

        // Переменные для состояния диалогового окна
        private bool _isShowingDialog = false;
        private string _dialogText = string.Empty;
        private Texture2D _previewIcon = null;
        private Action _onConfirmAction = null;

        // Размеры окна подтверждения
        private Rect _windowRect = new Rect(0, 0, 400, 250);

        public static void Initialize()
        {
            if (_instance != null) return;

            // Создаем невидимый игровой объект в памяти Unity для отрисовки GUI
            GameObject guiObj = new GameObject("FurnitureEditorUI");
            DontDestroyOnLoad(guiObj); // Чтобы объект не удалялся при перезагрузке комнат
            _instance = guiObj.AddComponent<EditorUiManager>();

            Plugin.Log.LogInfo("[EditorUiManager] Графический интерфейс OnGUI успешно инициализирован.");
        }

        // Публичный метод вызова окна подтверждения из экспортера
        public static void ShowConfirmationDialog(string text, Texture2D icon, Action onConfirm)
        {
            if (_instance == null) Initialize();

            _instance._dialogText = text;
            _instance._previewIcon = icon;
            _instance._onConfirmAction = onConfirm;

            // Центрируем окно на экране в зависимости от текущего разрешения игрока
            _instance._windowRect = new Rect(
                (Screen.width / 2) - 200,
                (Screen.height / 2) - 160,
                400,
                320
            );

            _instance._isShowingDialog = true;
        }

        // Встроенный метод Unity для отрисовки классического GUI
        private void OnGUI()
        {
            if (!_isShowingDialog) return;

            // Отрисовываем окно с заголовком
            _windowRect = GUILayout.Window(99, _windowRect, DrawDialogWindow, "Saving Furniture Pose/Animation");
        }


        // Сделайте метод статическим, чтобы вызывать его напрямую через имя класса
        private static GameObject _myCustomDialogInstance = null;

        public static void ShowNativeStyleDialog(UIFreePose uiFreePose, string messageText, Texture2D previewTexture, System.Action onConfirm)
        {
            try
            {
                if (uiFreePose == null || uiFreePose.savePosePanel == null) return;

                if (_myCustomDialogInstance != null) GameObject.Destroy(_myCustomDialogInstance);

                // 1. Вызываем родной метод игры
                uiFreePose.OpenSaveFreePosePanel();
                uiFreePose.savePosePanel.SetActive(false); // Прячем оригинал

                // 2. Создаем независимый клон панели
                _myCustomDialogInstance = GameObject.Instantiate(uiFreePose.savePosePanel, uiFreePose.savePosePanel.transform.parent);
                _myCustomDialogInstance.name = "Mod_CustomFurnitureSaveDialog";

                // Удаляем скрипт игры с клона
                var duplicatedGameScript = _myCustomDialogInstance.GetComponent<UIFreePose>();
                if (duplicatedGameScript != null) GameObject.Destroy(duplicatedGameScript);

                Color gameTextColor = new Color(0.8f, 0.8f, 0.8f, 1f); // Бежево-серый дефолт

                // 3. БЕЗОПАСНЫЙ ПОИСК И НАСТРОЙКА ОБЪЕКТОВ
                foreach (Transform child in _myCustomDialogInstance.transform)
                {
                    string childNameClean = child.name.Trim();

                    // Извлекаем цвет игры и гасим старый заголовок "name"
                    if (childNameClean == "name")
                    {
                        var originalText = child.GetComponent<UnityEngine.UI.Text>();
                        if (originalText != null) gameTextColor = originalText.color;
                        child.gameObject.SetActive(false);
                    }

                    // Прячем "Created By"
                    if (childNameClean == "txt created by (1)") child.gameObject.SetActive(false);

                    // Прячем нижнюю дату "name (1)"
                    if (childNameClean == "name (1)") child.gameObject.SetActive(false);

                    // ОСТАВЛЯЕМ красивую общую рамку панели
                    if (childNameClean == "fram (3)") child.gameObject.SetActive(true);

                    // ПОЛНОСТЬЮ ВЫКЛЮЧАЕМ ПОЛЯ ВВОДА (Это уберет их ненужные контуры, как в первом варианте!)
                    if (childNameClean == "InputField Pose" || childNameClean == "InputField creator")
                    {
                        child.gameObject.SetActive(false);
                    }
                }

                // 4. НАСТРОЙКА НАШЕГО КАСТОМНОГО ТЕКСТА
                GameObject myTextGo = new GameObject("Mod_CustomMessageText", typeof(RectTransform), typeof(UnityEngine.UI.Text));
                myTextGo.transform.SetParent(_myCustomDialogInstance.transform, false);

                UnityEngine.UI.Text myTextComp = myTextGo.GetComponent<UnityEngine.UI.Text>();
                myTextComp.text = messageText;
                myTextComp.fontSize = 11;
                myTextComp.color = gameTextColor;
                myTextComp.supportRichText = true;
                myTextComp.alignment = TextAnchor.UpperLeft;
                myTextComp.horizontalOverflow = HorizontalWrapMode.Wrap;
                myTextComp.verticalOverflow = VerticalWrapMode.Overflow;

                // Позиционируем текст на правой панели
                RectTransform textRect = myTextGo.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0.35f, 0.35f);
                textRect.anchorMax = new Vector2(0.96f, 0.96f);
                textRect.offsetMin = textRect.offsetMax = Vector2.zero;

                // Подтягиваем шрифт игры
                var fontSample = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (fontSample != null && fontSample.font != null) myTextComp.font = fontSample.font;

                // 5. ИСПРАВЛЕННАЯ НАСТРОЙКА КНОПОК
                var buttons = _myCustomDialogInstance.GetComponentsInChildren<UnityEngine.UI.Button>(true);

                if (buttons != null && buttons.Length >= 2)
                {
                    // ЛЕВАЯ КНОПКА (Темно-красная Save)
                    var saveBtn = buttons[0];
                    if (saveBtn != null)
                    {
                        saveBtn.onClick.RemoveAllListeners();
                        saveBtn.onClick.AddListener(() =>
                        {
                            Plugin.Log.LogInfo("[EditorUiManager] Клон: Сохранение подтверждено.");
                            onConfirm?.Invoke();
                            GameObject.Destroy(_myCustomDialogInstance);
                        });

                        // Возвращаем кнопку на её РОДНОЕ ванильное место (убираем некорректное расширение влево)
                        // Оставляем цвет по умолчанию (темно-красный)
                        var btnImg = saveBtn.GetComponent<UnityEngine.UI.Image>();
                        if (btnImg != null) btnImg.color = Color.white; // Белый восстановит оригинальный спрайт кнопки игры

                        var btnText = saveBtn.GetComponentInChildren<UnityEngine.UI.Text>(true);
                        if (btnText != null)
                        {
                            btnText.gameObject.SetActive(true);
                            btnText.text = "Save Furniture Pose";
                            btnText.fontSize = 11;
                            btnText.color = Color.white; // Белый текст идеально читается на темно-красном фоне

                            // ЖЕЛЕЗНЫЙ ХАК ДЛЯ ОТОБРАЖЕНИЯ: Разрешаем тексту выходить за границы кнопки, 
                            // чтобы он гарантированно отрендерился и не исчезал, если кнопка слишком узкая!
                            btnText.horizontalOverflow = HorizontalWrapMode.Overflow;
                            btnText.verticalOverflow = VerticalWrapMode.Overflow;
                            btnText.alignment = TextAnchor.MiddleCenter; // Центрируем текст на кнопке
                        }
                    }

                    // ПРАВАЯ КНОПКА (Крестик закрытия)
                    var cancelBtn = buttons[1];
                    if (cancelBtn != null)
                    {
                        cancelBtn.onClick.RemoveAllListeners();
                        cancelBtn.onClick.AddListener(() =>
                        {
                            Plugin.Log.LogInfo("[EditorUiManager] Клон: Окно закрыто.");
                            GameObject.Destroy(_myCustomDialogInstance);
                        });
                    }
                }

                // 6. ПОДСТАНОВКА ИКОНКИ
                if (previewTexture != null)
                {
                    var rawImageComp = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                    if (rawImageComp != null) rawImageComp.texture = previewTexture;
                }

                _myCustomDialogInstance.SetActive(true);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[EditorUiManager] Ошибка финальной очистки: {ex}");
            }
        }

        private void DrawDialogWindow(int windowID)
        {
            // 1. Сохраняем исходные настройки, чтобы не испортить другие окна Unity
            int defaultLabelSize = GUI.skin.label.fontSize;
            int defaultButtonSize = GUI.skin.button.fontSize;
            int defaultWindowSize = GUI.skin.window.fontSize;

            // 2. Выставляем увеличенный размер (+20% от стандартного)
            GUI.skin.label.fontSize = 16;
            GUI.skin.button.fontSize = 16;
            GUI.skin.window.fontSize = 16; // Это увеличит заголовок "Saving Furniture Pose..."

            GUILayout.BeginVertical();
            GUILayout.Space(10);

            // Выводим текст сообщения
            GUILayout.Label(_dialogText, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Space(10);

            // Отрисовываем превью-иконку
            if (_previewIcon != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                Rect textureRect = GUILayoutUtility.GetRect(120, 120);
                GUI.DrawTexture(textureRect, _previewIcon, ScaleMode.ScaleToFit);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(15);
            }

            GUILayout.FlexibleSpace();

            // Строка с кнопками управления (высоту увеличили с 35 до 42 под новый шрифт)
            GUILayout.BeginHorizontal();

            // Кнопка ДА
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Save", GUILayout.Height(42)))
            {
                _isShowingDialog = false;
                _onConfirmAction?.Invoke();
            }

            // Кнопка ОТМЕНА
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Cancel", GUILayout.Height(42)))
            {
                _isShowingDialog = false;
                Plugin.Log.LogInfo("[EditorUiManager] Сохранение позы отменено пользователем.");
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // Перетаскивание окна за заголовок (зону клика увеличили до 25 из-за крупного шрифта)
            GUI.DragWindow(new Rect(0, 0, 480, 25));

            // 3. ОБЯЗАТЕЛЬНО возвращаем всё как было
            GUI.skin.label.fontSize = defaultLabelSize;
            GUI.skin.button.fontSize = defaultButtonSize;
            GUI.skin.window.fontSize = defaultWindowSize;
        }

    }
}
