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

            GameObject dialogPanel = uiFreePose.savePosePanel;
            dialogPanel.SetActive(true);

            // 1. Прячем поля ввода текста, как и раньше
            if (uiFreePose.poseNameText != null) uiFreePose.poseNameText.gameObject.SetActive(false);
            if (uiFreePose.creatorText != null) uiFreePose.creatorText.gameObject.SetActive(false);

            // 2. Убираем надпись "Created By" и дату
            // Ищем объект подписи создателя. Обычно он находится рядом или является родителем для creatorText
            if (uiFreePose.creatorText != null && uiFreePose.creatorText.transform.parent != null)
            {
                // Отключаем весь блок (вместе с надписью "Created By")
                uiFreePose.creatorText.transform.parent.gameObject.SetActive(false);
            }

            // Ищем текстовое поле даты. В структуре uGUI игры оно часто лежит внутри панели.
            // Пройдёмся по текстам и отключим тот, который содержит цифры даты или похож на неё
            var allTexts = dialogPanel.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            foreach (var txt in allTexts)
            {
                if (txt.text.Contains("2023") || txt.text.Contains("/") || txt.name.ToLower().Contains("date"))
                {
                    txt.gameObject.SetActive(false);
                }
            }

            // 3. Выводим наш текст в главное поле названия позы
            // Разработчики игры уже сделали его крупным (для ввода названия). Мы просто подставим туда наш текст!
            if (uiFreePose.poseNameText != null)
            {
                // Нам нужно текстовое поле, которое показывает текст внутри InputField (обычно это компонент Text на дочернем объекте Text)
                var mainTextComp = uiFreePose.poseNameText.textComponent;
                if (mainTextComp != null)
                {
                    // Включаем обратно саму область текста (но не инпут!), чтобы её было видно
                    uiFreePose.poseNameText.gameObject.SetActive(true);

                    // Отключаем фоновую картинку инпута, чтобы остался только чистый текст
                    var inputImage = uiFreePose.poseNameText.GetComponent<UnityEngine.UI.Image>();
                    if (inputImage != null) inputImage.enabled = false;

                    // Блокируем ввод, делая его чисто информационным
                    uiFreePose.poseNameText.interactable = false;

                    mainTextComp.text = messageText;
                    mainTextComp.supportRichText = true; // Чтобы работал жёлтый цвет мебели
                                                         // НЕ меняем fontSize, пусть применится родной крупный размер игры!
                }
            }

            // 4. Подставляем превью-иконку
            if (previewTexture != null && uiFreePose.iconOfPose != null)
            {
                uiFreePose.iconOfPose.texture = previewTexture;
                uiFreePose.iconOfPose.gameObject.SetActive(true);
            }

            // 5. Перенастраиваем кнопки
            var buttons = dialogPanel.GetComponentsInChildren<UnityEngine.UI.Button>(true);

            if (buttons.Length >= 2)
            {
                // Левая кнопка (Сохранить)
                var saveBtn = buttons[0];
                saveBtn.onClick.RemoveAllListeners();
                saveBtn.onClick.AddListener(() =>
                {
                    onConfirm?.Invoke();
                    RestoreInputFields(uiFreePose);
                    dialogPanel.SetActive(false);
                });

                // Меняем текст на левой кнопке
                var saveBtnText = saveBtn.GetComponentInChildren<UnityEngine.UI.Text>();
                if (saveBtnText != null)
                {
                    saveBtnText.text = "Save Furniture Pose";
                }

                // Правая кнопка (Отмена)
                var cancelBtn = buttons[1];
                cancelBtn.onClick.RemoveAllListeners();
                cancelBtn.onClick.AddListener(() =>
                {
                    RestoreInputFields(uiFreePose);
                    dialogPanel.SetActive(false);
                });
            }
        }

        // Метод восстановления при закрытии окна
        private static void RestoreInputFields(UIFreePose uiFreePose)
        {
            if (uiFreePose == null) return;

            if (uiFreePose.poseNameText != null)
            {
                uiFreePose.poseNameText.gameObject.SetActive(true);
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
