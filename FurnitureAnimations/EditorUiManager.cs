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

        public void ShowNativeStyleDialog(string title, System.Action onConfirm)
        {
            Plugin.Log.LogWarning("[TRACE] 1. Метод ShowNativeStyleDialog запущен.");

            var uiFreePose = GameObject.FindObjectOfType<UIFreePose>();
            if (uiFreePose == null)
            {
                Plugin.Log.LogError("[TRACE] КРИТИЧЕСКАЯ ОШИБКА: Компонент UIFreePose не найден на сцене!");
                return;
            }
            Plugin.Log.LogInfo("[TRACE] 2. Компонент UIFreePose успешно найден.");

            if (uiFreePose.savePosePanel == null)
            {
                Plugin.Log.LogError("[TRACE] КРИТИЧЕСКАЯ ОШИБКА: uiFreePose.savePosePanel равен null!");
                return;
            }
            Plugin.Log.LogInfo($"[TRACE] 3. Донорская панель определена: {uiFreePose.savePosePanel.name}");

            // Создаем клон панели
            _myCustomDialogInstance = GameObject.Instantiate(uiFreePose.savePosePanel, uiFreePose.savePosePanel.transform.parent);
            if (_myCustomDialogInstance == null)
            {
                Plugin.Log.LogError("[TRACE] КРИТИЧЕСКАЯ ОШИБКА: Не удалось склонировать панель!");
                return;
            }
            _myCustomDialogInstance.name = "Mod_CustomFurnitureSaveDialog";
            _myCustomDialogInstance.SetActive(true);
            Plugin.Log.LogInfo("[TRACE] 4. Клон панели успешно создан, переименован и активирован.");

            // Дампим все дочерние объекты для проверки иерархии
            Plugin.Log.LogWarning("=== НАЧАЛО ДАМПА ИЕРАРХИИ КЛОНА ===");
            var allTransforms = _myCustomDialogInstance.GetComponentsInChildren<Transform>(true);
            foreach (var t in allTransforms)
            {
                Plugin.Log.LogInfo($" -> Элемент: {t.name} | Активен: {t.gameObject.activeSelf}");
            }
            Plugin.Log.LogWarning("=== КОНЕЦ ДАМПА ИЕРАРХИИ КЛОНА ===");

            // Скрываем ненужные оригинальные инпуты
            int hiddenCount = 0;
            foreach (Transform child in _myCustomDialogInstance.transform)
            {
                if (child.name.Contains("InputField Pose") || child.name.Contains("InputField creator"))
                {
                    child.gameObject.SetActive(false);
                    hiddenCount++;
                }
            }
            Plugin.Log.LogInfo($"[TRACE] 5. Скрыто оригинальных полей ввода: {hiddenCount}");

            // Меняем заголовок окна
            var titleText = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (titleText != null)
            {
                titleText.text = title;
                Plugin.Log.LogInfo($"[TRACE] 6. Изменен заголовок окна на: '{title}'");
            }
            else
            {
                Plugin.Log.LogWarning("[TRACE] Предупреждение: Компонент Text для заголовка не найден в корне.");
            }

            // Ищем и настраиваем кнопки
            Plugin.Log.LogInfo("[TRACE] 7. Начинаем поиск кнопок...");
            UnityEngine.UI.Button nativeSaveBtn = null;
            UnityEngine.UI.Button nativeCloseBtn = null;

            var allButtonsInClone = _myCustomDialogInstance.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            Plugin.Log.LogInfo($"[TRACE] Всего найдено компонентов Button в клоне: {allButtonsInClone.Length}");

            foreach (var btn in allButtonsInClone)
            {
                if (btn == null) continue;
                string btnName = btn.name.Trim();
                Plugin.Log.LogInfo($"    * Найдена кнопка с именем: '{btnName}'");

                if (btnName == "Button Save") nativeSaveBtn = btn;
                if (btnName == "Btn Close (4)") nativeCloseBtn = btn;
            }

            // Настройка кнопки Save
            if (nativeSaveBtn != null)
            {
                Plugin.Log.LogWarning($"[TRACE] 8А. Кнопка 'Button Save' успешно привязана.");
                nativeSaveBtn.onClick.RemoveAllListeners();
                nativeSaveBtn.onClick.AddListener(() =>
                {
                    Plugin.Log.LogWarning("[КЛИК] Кнопка Save нажата!");
                    onConfirm?.Invoke();
                    GameObject.Destroy(_myCustomDialogInstance);
                });

                nativeSaveBtn.gameObject.SetActive(true);
                nativeSaveBtn.interactable = true;

                var btnText = nativeSaveBtn.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (btnText != null)
                {
                    btnText.gameObject.SetActive(true);
                    btnText.text = "Save Furniture Pose";
                    Plugin.Log.LogInfo("[TRACE] Текст на кнопке изменен на 'Save Furniture Pose'.");
                }
                else
                {
                    Plugin.Log.LogError("[TRACE] ОШИБКА: Внутри 'Button Save' не найден дочерний компонент Text!");
                }
            }
            else
            {
                Plugin.Log.LogError("[TRACE] ОШИБКА: 'Button Save' НЕ НАЙДЕНА в цикле перебора!");
            }

            // Настройка кнопки Close
            if (nativeCloseBtn != null)
            {
                Plugin.Log.LogInfo("[TRACE] 8Б. Крестик 'Btn Close (4)' успешно привязан.");
                nativeCloseBtn.onClick.RemoveAllListeners();
                nativeCloseBtn.onClick.AddListener(() =>
                {
                    Plugin.Log.LogWarning("[КЛИК] Крестик Close нажат!");
                    GameObject.Destroy(_myCustomDialogInstance);
                });
            }
            else
            {
                Plugin.Log.LogError("[TRACE] ОШИБКА: 'Btn Close (4)' НЕ НАЙДЕНА в цикле перебора!");
            }

            Plugin.Log.LogWarning("[TRACE] Метод ShowNativeStyleDialog успешно завершил работу.");
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
