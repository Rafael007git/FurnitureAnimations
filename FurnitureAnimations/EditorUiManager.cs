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
        // Храним ссылку на созданный нами клон панели
        private static GameObject _myCustomDialogInstance = null;

        public static void ShowNativeStyleDialog(UIFreePose uiFreePose, string messageText, Texture2D previewTexture, System.Action onConfirm)
        {
            try
            {
                if (uiFreePose == null || uiFreePose.savePosePanel == null) return;

                // Если вдруг предыдущее наше окно не закрылось, уничтожаем его перед созданием нового
                if (_myCustomDialogInstance != null) GameObject.Destroy(_myCustomDialogInstance);

                // 1. СНАЧАЛА вызываем родной метод игры. 
                // Он сделает скриншот и обновит внутренние переменные оригинала, но само окно мы тут же перехватим.
                uiFreePose.OpenSaveFreePosePanel();

                // Сразу ВЫКЛЮЧАЕМ оригинал игры, чтобы он не маячил на экране и не конфликтовал с нами
                uiFreePose.savePosePanel.SetActive(false);

                // 2. СОЗДАЕМ ГЛУБОКИЙ КЛОН ОРИГИНАЛЬНОГО ОКНА ИГРЫ
                // Наш клон получит все те же текстуры, рамки и красивый визуал, но станет полностью НЕЗАВИСИМЫМ.
                _myCustomDialogInstance = GameObject.Instantiate(uiFreePose.savePosePanel, uiFreePose.savePosePanel.transform.parent);
                _myCustomDialogInstance.name = "Mod_CustomFurnitureSaveDialog";

                // Удаляем оригинальный скрипт UIFreePose с НАШЕГО КЛОНА, чтобы игра не могла им управлять и сбрасывать тексты!
                var duplicatedGameScript = _myCustomDialogInstance.GetComponent<UIFreePose>();
                if (duplicatedGameScript != null) GameObject.Destroy(duplicatedGameScript);

                // 3. МОДИФИЦИРУЕМ НАШ КЛОН (Оригинал игры теперь в полной безопасности!)

                // Меняем текст заголовка "name" на наше трехстрочное сообщение
                Transform nameTrans = _myCustomDialogInstance.transform.Find("name");
                if (nameTrans != null)
                {
                    var titleText = nameTrans.GetComponent<UnityEngine.UI.Text>();
                    if (titleText != null)
                    {
                        titleText.text = messageText;
                        titleText.fontSize = 16; // Сделали чуть меньше (16 вместо 18/20), чтобы не был огромным
                        titleText.supportRichText = true;
                        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
                        titleText.verticalOverflow = VerticalWrapMode.Overflow;
                    }
                }

                // Прячем надпись "txt created by (1)" на клоне
                Transform createdByTrans = _myCustomDialogInstance.transform.Find("txt created by (1)");
                if (createdByTrans != null)
                {
                    var txt = createdByTrans.GetComponent<UnityEngine.UI.Text>();
                    if (txt != null) txt.color = new Color(0, 0, 0, 0);
                }

                // Прячем нижнюю дату "name (1)" и рамку "fram (3)" на клоне
                Transform dateTextTrans = _myCustomDialogInstance.transform.Find("name (1)");
                if (dateTextTrans != null)
                {
                    var txt = dateTextTrans.GetComponent<UnityEngine.UI.Text>();
                    if (txt != null) txt.color = new Color(0, 0, 0, 0);
                }
                Transform dateFramTrans = _myCustomDialogInstance.transform.Find("fram (3)");
                if (dateFramTrans != null) dateFramTrans.gameObject.SetActive(false);

                // Ищем и очищаем поля ввода на клоне (убираем белые рамки InputField)
                var inputFields = _myCustomDialogInstance.GetComponentsInChildren<UnityEngine.UI.InputField>(true);
                foreach (var input in inputFields)
                {
                    if (input == null) continue;
                    input.text = "";
                    if (input.placeholder != null) input.placeholder.gameObject.SetActive(false);
                    var img = input.GetComponent<UnityEngine.UI.Image>();
                    if (img != null) img.color = new Color(0, 0, 0, 0); // Прозрачность
                    input.interactable = false; // Блокируем клики по ним
                }

                // 4. ПОДМЕНЯЕМ ИКОНКУ НА КЛОНЕ (Если передали текстуру из плагина)
                if (previewTexture != null)
                {
                    // Ищем RawImage на клоне панели
                    var rawImageComp = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                    if (rawImageComp != null) rawImageComp.texture = previewTexture;
                }

                // 5. НАСТРАИВАЕМ КНОПКИ НА КЛОНЕ

                // Кнопка сохранения " Button Save"
                Transform saveBtnTrans = _myCustomDialogInstance.transform.Find(" Button Save");
                if (saveBtnTrans != null)
                {
                    var saveBtn = saveBtnTrans.GetComponent<UnityEngine.UI.Button>();
                    if (saveBtn != null)
                    {
                        saveBtn.onClick.RemoveAllListeners();
                        saveBtn.onClick.AddListener(() =>
                        {
                            Plugin.Log.LogInfo("[EditorUiManager] Клон: Нажата кнопка 'Save'.");
                            onConfirm?.Invoke(); // Выполняем ваше сохранение плагина
                            GameObject.Destroy(_myCustomDialogInstance); // Полностью уничтожаем наше окно
                        });

                        var btnImg = saveBtn.GetComponent<UnityEngine.UI.Image>();
                        if (btnImg != null) btnImg.color = Color.white;

                        var btnText = saveBtnTrans.GetComponentInChildren<UnityEngine.UI.Text>(true);
                        if (btnText != null)
                        {
                            btnText.text = "Save Furniture Pose";
                            btnText.fontSize = 13;
                            btnText.color = Color.black;
                        }
                    }
                }

                // Кнопка закрытия "Btn Close (4)"
                // Мы ВООБЩЕ НЕ ТРОГАЕМ её Listeners! Мы просто вешаем НАШЕ зарытие поверх, 
                // чтобы при клике наш созданный клон уничтожался и не зависал.
                Transform closeBtnTrans = _myCustomDialogInstance.transform.Find("Btn Close (4)");
                if (closeBtnTrans != null)
                {
                    var closeBtn = closeBtnTrans.GetComponent<UnityEngine.UI.Button>();
                    if (closeBtn != null)
                    {
                        closeBtn.onClick.AddListener(() =>
                        {
                            Plugin.Log.LogInfo("[EditorUiManager] Клон: Окно закрыто пользователем.");
                            GameObject.Destroy(_myCustomDialogInstance);
                        });
                    }
                }

                // Включаем наше красивое кастомное окно на экране
                _myCustomDialogInstance.SetActive(true);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[EditorUiManager] Критическая ошибка клонирования: {ex}");
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
