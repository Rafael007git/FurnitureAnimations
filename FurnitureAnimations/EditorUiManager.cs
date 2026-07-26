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

        public static void ShowNativeStyleDialog(string title, Texture2D previewTexture, System.Action onConfirm)
        {
            var uiFreePose = GameObject.FindObjectOfType<UIFreePose>();
            if (uiFreePose == null || uiFreePose.savePosePanel == null) return;

            // Клонируем донорскую панель
            _myCustomDialogInstance = GameObject.Instantiate(uiFreePose.savePosePanel, uiFreePose.savePosePanel.transform.parent);
            if (_myCustomDialogInstance == null) return;

            _myCustomDialogInstance.name = "Mod_CustomFurnitureSaveDialog";
            _myCustomDialogInstance.SetActive(true);

            // ==========================================================
            // 🛠️ ЖЕСТКАЯ ЧИСТКА ВЁРСТКИ ОТ ДЕФОЛТНЫХ ЭЛЕМЕНТОВ ИГРЫ
            // ==========================================================
            var allTransforms = _myCustomDialogInstance.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                if (t == null) continue;
                string name = t.name.Trim();

                // Скрываем инпуты, ванильные тексты подсказок и плашку с датой
                if (name.Contains("InputField Pose") ||
                    name.Contains("InputField creator") ||
                    name.Contains("Pose Name") ||
                    name.Contains("Created By") ||
                    name.Contains("2023/06/12") || // Скрываем строку даты
                    name == "date" || // Защита, если объект называется просто date
                    name.Contains("Text Date"))
                {
                    t.gameObject.SetActive(false);
                }
            }

            // Устанавливаем кастомный заголовок ("Do you want to save this pose...")
            var titleText = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (titleText != null)
            {
                titleText.text = title;
            }

            // ==========================================================
            // 🖼️ ВОССТАНОВЛЕНИЕ ИКОНКИ ПРЕВЬЮ
            // ==========================================================
            if (previewTexture != null)
            {
                var rawImageComp = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                if (rawImageComp != null)
                {
                    rawImageComp.gameObject.SetActive(true);
                    rawImageComp.texture = previewTexture; // Заменяем дефолт на нашу текстуру мебели!
                }
            }

            // ==========================================================
            // 🎛️ НАСТРОЙКА КНОПОК И ТЕКСТА НА КНОПКЕ
            // ==========================================================
            UnityEngine.UI.Button nativeSaveBtn = null;
            UnityEngine.UI.Button nativeCloseBtn = null;

            var allButtonsInClone = _myCustomDialogInstance.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            foreach (var btn in allButtonsInClone)
            {
                if (btn == null) continue;
                string btnName = btn.name.Trim();
                if (btnName == "Button Save") nativeSaveBtn = btn;
                if (btnName == "Btn Close (4)") nativeCloseBtn = btn;
            }

            // Меняем надпись и действие на главной кнопке
            if (nativeSaveBtn != null)
            {
                nativeSaveBtn.onClick.RemoveAllListeners();
                nativeSaveBtn.onClick.AddListener(() =>
                {
                    onConfirm?.Invoke();
                    GameObject.Destroy(_myCustomDialogInstance);
                });

                nativeSaveBtn.gameObject.SetActive(true);
                nativeSaveBtn.interactable = true;

                // Перекрашиваем текст на кнопке на наш лад!
                var btnText = nativeSaveBtn.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (btnText != null)
                {
                    btnText.gameObject.SetActive(true);
                    btnText.text = "Save Furniture Pose"; // Наша надпись
                    btnText.fontSize = 11;
                    btnText.color = Color.white;
                    btnText.alignment = TextAnchor.MiddleCenter;
                }
            }

            // Настройка крестика закрытия
            if (nativeCloseBtn != null)
            {
                nativeCloseBtn.onClick.RemoveAllListeners();
                nativeCloseBtn.onClick.AddListener(() =>
                {
                    GameObject.Destroy(_myCustomDialogInstance);
                });
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
