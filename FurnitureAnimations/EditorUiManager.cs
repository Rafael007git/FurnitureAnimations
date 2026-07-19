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

                // 1. Вызываем родной метод игры для обновления внутренних данных и скриншота
                uiFreePose.OpenSaveFreePosePanel();
                uiFreePose.savePosePanel.SetActive(false); // Прячем оригинал от греха подальше

                // 2. Создаем независимый клон панели
                _myCustomDialogInstance = GameObject.Instantiate(uiFreePose.savePosePanel, uiFreePose.savePosePanel.transform.parent);
                _myCustomDialogInstance.name = "Mod_CustomFurnitureSaveDialog";

                // Удаляем скрипт игры с клона, лишая игру контроля над ним
                var duplicatedGameScript = _myCustomDialogInstance.GetComponent<UIFreePose>();
                if (duplicatedGameScript != null) GameObject.Destroy(duplicatedGameScript);

                // 3. ПОЛНАЯ НЕЙТРАЛИЗАЦИЯ ИГРОВЫХ ТЕКСТОВ И ИНПУТОВ
                // Вместо редактирования мы просто ГАСИМ старый заголовок, чтобы он нам не мешал
                Transform nameTrans = _myCustomDialogInstance.transform.Find("name");
                if (nameTrans != null) nameTrans.gameObject.SetActive(false);

                // Прячем "Created By" и дату
                Transform createdByTrans = _myCustomDialogInstance.transform.Find("txt created by (1)");
                if (createdByTrans != null) createdByTrans.gameObject.SetActive(false);

                Transform dateTextTrans = _myCustomDialogInstance.transform.Find("name (1)");
                if (dateTextTrans != null) dateTextTrans.gameObject.SetActive(false);

                Transform dateFramTrans = _myCustomDialogInstance.transform.Find("fram (3)");
                if (dateFramTrans != null) dateFramTrans.gameObject.SetActive(false);

                // Гасим поля ввода (InputField), чтобы убрать пустые белые рамки
                Transform inputPoseTrans = _myCustomDialogInstance.transform.Find("[3] InputField Pose");
                if (inputPoseTrans != null) inputPoseTrans.gameObject.SetActive(false);

                Transform inputCreatorTrans = _myCustomDialogInstance.transform.Find("[3] InputField creator");
                if (inputCreatorTrans != null) inputCreatorTrans.gameObject.SetActive(false);

                // 4. СОЗДАЕМ НАШ СОБСТВЕННЫЙ ТЕКСТ С НУЛЯ (ИГРА ЕГО НЕ ТРОНЕТ!)
                GameObject myTextGo = new GameObject("Mod_CustomMessageText", typeof(RectTransform), typeof(UnityEngine.UI.Text));
                myTextGo.transform.SetParent(_myCustomDialogInstance.transform, false);

                UnityEngine.UI.Text myTextComp = myTextGo.GetComponent<UnityEngine.UI.Text>();
                myTextComp.text = messageText;
                myTextComp.fontSize = 16; // Идеальный средний размер
                myTextComp.color = Color.white;
                myTextComp.supportRichText = true; // Наша желтая мебель будет работать на 100%!
                myTextComp.alignment = TextAnchor.UpperLeft;
                myTextComp.horizontalOverflow = HorizontalWrapMode.Overflow;
                myTextComp.verticalOverflow = VerticalWrapMode.Overflow;

                // Настраиваем координаты нашего текста. Мы вешаем его ровно туда, где была правая панель
                RectTransform textRect = myTextGo.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0.42f, 0.35f); // Смещение вправо и вверх относительно рамки иконки
                textRect.anchorMax = new Vector2(0.95f, 0.90f);
                textRect.offsetMin = textRect.offsetMax = Vector2.zero;

                // Если в игре используется кастомный шрифт, попробуем стянуть его у оригинальной кнопки, чтобы сохранить стиль
                var sampleText = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.Text>(true);
                if (sampleText != null && sampleText.font != null) myTextComp.font = sampleText.font;

                // 5. НАСТРОЙКА КНОПКИ " Button Save"
                Transform saveBtnTrans = _myCustomDialogInstance.transform.Find(" Button Save");
                if (saveBtnTrans != null)
                {
                    var saveBtn = saveBtnTrans.GetComponent<UnityEngine.UI.Button>();
                    if (saveBtn != null)
                    {
                        saveBtn.onClick.RemoveAllListeners();
                        saveBtn.onClick.AddListener(() =>
                        {
                            Plugin.Log.LogInfo("[EditorUiManager] Клон: Сохранение подтверждено.");
                            onConfirm?.Invoke();
                            GameObject.Destroy(_myCustomDialogInstance);
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
                Transform closeBtnTrans = _myCustomDialogInstance.transform.Find("Btn Close (4)");
                if (closeBtnTrans != null)
                {
                    var closeBtn = closeBtnTrans.GetComponent<UnityEngine.UI.Button>();
                    if (closeBtn != null)
                    {
                        closeBtn.onClick.AddListener(() =>
                        {
                            Plugin.Log.LogInfo("[EditorUiManager] Клон: Окно закрыто.");
                            GameObject.Destroy(_myCustomDialogInstance);
                        });
                    }
                }

                // ПОДСТАНОВКА ИКОНКИ (Слева)
                if (previewTexture != null)
                {
                    var rawImageComp = _myCustomDialogInstance.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                    if (rawImageComp != null) rawImageComp.texture = previewTexture;
                }

                _myCustomDialogInstance.SetActive(true);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[EditorUiManager] Ошибка тотальной изоляции окна: {ex}");
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
