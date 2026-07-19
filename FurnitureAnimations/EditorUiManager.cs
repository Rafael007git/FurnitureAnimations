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
        public static void ShowNativeStyleDialog(UIFreePose uiFreePose, string messageText, Texture2D previewTexture, System.Action onConfirm)
        {
            if (uiFreePose == null || uiFreePose.savePosePanel == null) return;

            // Включаем панель игры точно так же, как в том самом рабочем шаге
            GameObject dialogPanel = uiFreePose.savePosePanel;
            dialogPanel.SetActive(true);

            // --- ФИКС ТЕКСТА И КРУПНОГО ШРИФТА ---
            // Чтобы игра не сходила с ума и не прятала окно, мы ОСТАВЛЯЕМ поля ввода активными,
            // но просто стираем из них текст, чтобы они не перекрывали наше сообщение.
            if (uiFreePose.poseNameText != null) uiFreePose.poseNameText.text = "";
            if (uiFreePose.creatorText != null) uiFreePose.creatorText.text = "";

            // Находим ВСЕ тексты на панели, чтобы точечно управлять ими, а не портить всё скопом
            var allTexts = dialogPanel.GetComponentsInChildren<UnityEngine.UI.Text>(true);

            foreach (var txt in allTexts)
            {
                if (txt == null) continue;

                // 1. Ищем главный заголовок панели (обычно там написано "Save Pose" или "Saving...")
                // и заменяем его на ваше кастомное трехстрочное сообщение
                if (txt.name.ToLower().Contains("title") || txt.text.Contains("Save") || txt.text.Contains("Поза"))
                {
                    txt.text = messageText;
                    txt.fontSize = 20; // Делаем его КРУПНЫМ, как вы и хотели!
                    txt.supportRichText = true; // Чтобы работал желтый цвет <color=yellow>
                    txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    txt.verticalOverflow = VerticalWrapMode.Overflow;
                }

                // 2. Скрываем текст даты "2023/06/12", просто сделав его полностью прозрачным
                if (txt.text.Contains("2023") || txt.text.Contains("/") || txt.name.ToLower().Contains("date"))
                {
                    txt.color = new Color(0, 0, 0, 0); // Прозрачный цвет
                }

                // 3. Скрываем надпись "Created By"
                if (txt.text.ToLower().Contains("created") || txt.text.ToLower().Contains("author") || txt.text.Contains("Автор"))
                {
                    txt.color = new Color(0, 0, 0, 0); // Прозрачный цвет
                }
            }

            // --- ПОДСТАНОВКА ИКОНКИ ---
            if (previewTexture != null && uiFreePose.iconOfPose != null)
            {
                uiFreePose.iconOfPose.texture = previewTexture;
                uiFreePose.iconOfPose.gameObject.SetActive(true);
            }

            // --- НАСТРОЙКА КНОПОК ---
            var buttons = dialogPanel.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            if (buttons != null && buttons.Length >= 2)
            {
                // Левая кнопка (Сохранить)
                var saveBtn = buttons[0];
                saveBtn.onClick.RemoveAllListeners();
                saveBtn.onClick.AddListener(() =>
                {
                    onConfirm?.Invoke();
                    dialogPanel.SetActive(false);
                });

                // Меняем текст на левой кнопке
                var saveBtnText = saveBtn.GetComponentInChildren<UnityEngine.UI.Text>();
                if (saveBtnText != null)
                {
                    saveBtnText.text = "Save Furniture Pose";
                    saveBtnText.fontSize = 14; // Комфортный размер для кнопки
                }

                // Правая кнопка (Отмена)
                var cancelBtn = buttons[1];
                cancelBtn.onClick.RemoveAllListeners();
                cancelBtn.onClick.AddListener(() =>
                {
                    dialogPanel.SetActive(false);
                });

                var cancelBtnText = cancelBtn.GetComponentInChildren<UnityEngine.UI.Text>();
                if (cancelBtnText != null)
                {
                    cancelBtnText.fontSize = 14;
                }
            }
        }


        private static void RestoreInputFields(UIFreePose uiFreePose)
        {
            if (uiFreePose == null) return;

            if (uiFreePose.poseNameText != null)
            {
                uiFreePose.poseNameText.text = ""; // Очищаем наш текст перед возвратом игре
                uiFreePose.poseNameText.interactable = true;
                var inputImage = uiFreePose.poseNameText.GetComponent<UnityEngine.UI.Image>();
                if (inputImage != null) inputImage.enabled = true;
            }

            if (uiFreePose.creatorText != null)
            {
                uiFreePose.creatorText.gameObject.SetActive(true);
                if (uiFreePose.creatorText.transform.parent != null)
                {
                    uiFreePose.creatorText.transform.parent.gameObject.SetActive(true);
                }
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
