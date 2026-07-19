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
                (Screen.height / 2) - 125,
                400,
                260
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

        private void DrawDialogWindow(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(10);

            // 1. Выводим текст сообщения (Название мебели, тип позы, координаты)
            GUILayout.Label(_dialogText, GUILayout.ExpandWidth(true));
            GUILayout.Space(10);

            // 2. Отрисовываем превью-иконку (скриншот или родную картинку), если она есть
            if (_previewIcon != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace(); // Центрируем картинку по горизонтали

                // Рисуем квадратную иконку 80x80 пикселей
                Rect textureRect = GUILayoutUtility.GetRect(80, 80);
                GUI.DrawTexture(textureRect, _previewIcon, ScaleMode.ScaleToFit);

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(15);
            }

            GUILayout.FlexibleSpace();

            // 3. Строка с кнопками управления
            GUILayout.BeginHorizontal();

            // Кнопка ДА
            GUI.backgroundColor = Color.green;
            if (GUILayout.Button("Save", GUILayout.Height(35)))
            {
                _isShowingDialog = false;
                _onConfirmAction?.Invoke(); // Вызываем метод записи в JSON
            }

            // Кнопка ОТМЕНА
            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("Cancel", GUILayout.Height(35)))
            {
                _isShowingDialog = false;
                Plugin.Log.LogInfo("[EditorUiManager] Сохранение позы отменено пользователем.");
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // Разрешаем игроку перетаскивать окошко мышкой за заголовок
            GUI.DragWindow(new Rect(0, 0, 400, 20));
        }
    }
}
