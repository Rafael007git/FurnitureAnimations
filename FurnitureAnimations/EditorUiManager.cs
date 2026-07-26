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

                if (_myCustomDialogInstance != null)
                    GameObject.Destroy(_myCustomDialogInstance);

                // 1. Открываем оригинальное окно
                uiFreePose.OpenSaveFreePosePanel();
                uiFreePose.savePosePanel.SetActive(false);

                // 2. Создаем клон панели
                _myCustomDialogInstance = GameObject.Instantiate(uiFreePose.savePosePanel, uiFreePose.savePosePanel.transform.parent);
                _myCustomDialogInstance.name = "Mod_CustomFurnitureSaveDialog";

                // Отрезаем скрипт игры от клона
                var duplicatedGameScript = _myCustomDialogInstance.GetComponent<UIFreePose>();
                if (duplicatedGameScript != null)
                    GameObject.Destroy(duplicatedGameScript);

                Color gameTextColor = new Color(0.8f, 0.8f, 0.8f, 1f);

                // 3. Чистим ванильный мусор (Ваш оригинальный цикл)
                foreach (Transform child in _myCustomDialogInstance.transform)
                {
                    string childNameClean = child.name.Trim();
                    if (childNameClean == "name")
                    {
                        var originalText = child.GetComponent<UnityEngine.UI.Text>();
                        if (originalText != null) gameTextColor = originalText.color;
                        child.gameObject.SetActive(false);
                    }
                    if (childNameClean == "txt created by (1)") child.gameObject.SetActive(false);
                    if (childNameClean == "name (1)") child.gameObject.SetActive(false);
                    if (childNameClean == "fram (3)") child.gameObject.SetActive(true);

                    if (childNameClean == "InputField Pose" || childNameClean == "InputField creator")
                    {
                        child.gameObject.SetActive(false);
                    }
                }

                // 4. Верстка нашего кастомного текста (Ваш идеальный блок)
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

                RectTransform textRect = myTextGo.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0.35f, 0.35f);
                textRect.anchorMax = new Vector2(0.96f, 0.96f);
                textRect.offsetMin = textRect.offsetMax = Vector2.zero;

                var fontSample = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (fontSample != null && fontSample.font != null) myTextComp.font = fontSample.font;


                // ==========================================================
                // 💥 СОЗДАНИЕ КНОПКИ ПОВЕРХ ВАНИЛЬНОЙ С ТОЧНЫМ ПОЗИЦИОНИРОВАНИЕМ
                // ==========================================================

                // Находим оригинальную кнопку, чтобы считать её координаты
                var vanillaButtons = _myCustomDialogInstance.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                UnityEngine.UI.Button oldGameSaveBtn = null;

                foreach (var b in vanillaButtons)
                {
                    if (b != null && b.name.Trim() == "Button Save")
                    {
                        oldGameSaveBtn = b;
                        break;
                    }
                }

                // Создаем абсолютно чистую кнопку-пустышку (без картинки, прозрачную)
                GameObject customBtnGo = new GameObject("Mod_UltimateSaveBtnOverlay", typeof(RectTransform), typeof(UnityEngine.UI.Button));

                // Вешаем её на того же родителя, где лежит старая кнопка
                if (oldGameSaveBtn != null)
                {
                    customBtnGo.transform.SetParent(oldGameSaveBtn.transform.parent, false);

                    // Копируем геометрию один в один, чтобы лечь ровно поверх
                    RectTransform targetRect = customBtnGo.GetComponent<RectTransform>();
                    RectTransform sourceRect = oldGameSaveBtn.GetComponent<RectTransform>();

                    targetRect.anchorMin = sourceRect.anchorMin;
                    targetRect.anchorMax = sourceRect.anchorMax;
                    targetRect.pivot = sourceRect.pivot;
                    targetRect.anchoredPosition = sourceRect.anchoredPosition;
                    targetRect.sizeDelta = sourceRect.sizeDelta;
                }
                else
                {
                    // Подстраховка, если вдруг не нашли старую кнопку
                    customBtnGo.transform.SetParent(_myCustomDialogInstance.transform, false);
                    RectTransform targetRect = customBtnGo.GetComponent<RectTransform>();
                    targetRect.anchorMin = new Vector2(0.35f, 0.12f);
                    targetRect.anchorMax = new Vector2(0.65f, 0.25f);
                    targetRect.offsetMin = targetRect.offsetMax = Vector2.zero;
                }

                // Привязываем наше сохранение мода
                UnityEngine.UI.Button myButtonComp = customBtnGo.GetComponent<UnityEngine.UI.Button>();
                myButtonComp.onClick.AddListener(() =>
                {
                    Plugin.Log.LogWarning("[EditorUiManager] Клик по наложенной кнопке зафиксирован!");
                    onConfirm?.Invoke(); // Физическая запись JSON в файлы мода
                    GameObject.Destroy(_myCustomDialogInstance);
                });

                // Рисуем текст на нашей наложенной кнопке
                GameObject btnTextGo = new GameObject("Text", typeof(RectTransform), typeof(UnityEngine.UI.Text));
                btnTextGo.transform.SetParent(customBtnGo.transform, false);

                UnityEngine.UI.Text btnTextComp = btnTextGo.GetComponent<UnityEngine.UI.Text>();
                btnTextComp.text = "Save Furniture Pose";
                btnTextComp.fontSize = 11;
                btnTextComp.color = Color.white;
                btnTextComp.alignment = TextAnchor.MiddleCenter;
                btnTextComp.horizontalOverflow = HorizontalWrapMode.Overflow;
                btnTextComp.verticalOverflow = VerticalWrapMode.Overflow;
                if (fontSample != null && fontSample.font != null) btnTextComp.font = fontSample.font;

                RectTransform btnTextRect = btnTextGo.GetComponent<RectTransform>();
                btnTextRect.anchorMin = Vector2.zero;
                btnTextRect.anchorMax = Vector2.one;
                btnTextRect.offsetMin = btnTextRect.offsetMax = Vector2.zero;

                // 6. Подстановка иконки превью
                if (previewTexture != null)
                {
                    var rawImageComp = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                    if (rawImageComp != null)
                        rawImageComp.texture = previewTexture;
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
