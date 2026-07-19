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
            try
            {
                if (uiFreePose == null || uiFreePose.savePosePanel == null) return;

                // 1. Вызываем родной метод игры (он сам включит панель, настроит слои и сделает скриншот)
                uiFreePose.OpenSaveFreePosePanel();
                Plugin.Log.LogInfo("[EditorUiManager] Вызван родной метод OpenSaveFreePosePanel().");

                GameObject dialogPanel = uiFreePose.savePosePanel;

                // ЖЕСТКИЙ ХАК ДЛЯ ПРОВЕРКИ ИГРЫ:
                // В оригинальном методе ButtonSaveCharacterPreset() стоит проверка if (this.poseName != "" && this.creatorName != "")
                // Заполняем эти внутренние переменные игры заглушками, чтобы проверка железно проходила!
                uiFreePose.poseName = "Furniture_Custom_Pose";
                uiFreePose.creatorName = "ModUser";

                // 2. РАБОТАЕМ С ОБЪЕКТАМИ ПО ИХ ТОЧНЫМ ИМЕНАМ

                // Меняем верхний текст "Pose name" на ваше крупное сообщение
                Transform nameTrans = dialogPanel.transform.Find("name");
                if (nameTrans != null)
                {
                    var titleText = nameTrans.GetComponent<UnityEngine.UI.Text>();
                    if (titleText != null)
                    {
                        titleText.text = messageText; // Ваше трехстрочное сообщение с желтой мебелью
                        titleText.fontSize = 18;      // Крупный читаемый размер
                        titleText.supportRichText = true;
                        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;
                        titleText.verticalOverflow = VerticalWrapMode.Overflow;
                    }
                }

                // Прячем надпись "Created By" через прозрачность
                Transform createdByTrans = dialogPanel.transform.Find("txt created by (1)");
                if (createdByTrans != null)
                {
                    var createdText = createdByTrans.GetComponent<UnityEngine.UI.Text>();
                    if (createdText != null) createdText.color = new Color(0, 0, 0, 0);
                }

                // Прячем нижнюю дату/текст name (1) и её рамку fram (3)
                Transform dateTextTrans = dialogPanel.transform.Find("name (1)");
                if (dateTextTrans != null)
                {
                    var dateText = dateTextTrans.GetComponent<UnityEngine.UI.Text>();
                    if (dateText != null) dateText.color = new Color(0, 0, 0, 0);
                }
                Transform dateFramTrans = dialogPanel.transform.Find("fram (3)");
                if (dateFramTrans != null) dateFramTrans.gameObject.SetActive(false); // Рамку даты можно выключить безопасно

                // 3. СКРЫВАЕМ ПОЛЯ ВВОДА (InputField) ЧЕРЕЗ ПРОЗРАЧНОСТЬ
                // Оставляем объекты активными для скриптов игры, но невидимыми для игрока
                if (uiFreePose.poseNameText != null)
                {
                    var img = uiFreePose.poseNameText.GetComponent<UnityEngine.UI.Image>();
                    if (img != null) img.color = new Color(0, 0, 0, 0); // Прозрачный фон инпута
                    if (uiFreePose.poseNameText.placeholder != null) uiFreePose.poseNameText.placeholder.gameObject.SetActive(false);
                }

                if (uiFreePose.creatorText != null)
                {
                    var img = uiFreePose.creatorText.GetComponent<UnityEngine.UI.Image>();
                    if (img != null) img.color = new Color(0, 0, 0, 0); // Прозрачный фон инпута
                    if (uiFreePose.creatorText.placeholder != null) uiFreePose.creatorText.placeholder.gameObject.SetActive(false);
                }

                // 4. ПОДМЕНЯЕМ ИКОНКУ (Слева, если плагин передал свою текстуру)
                if (previewTexture != null && uiFreePose.iconOfPose != null)
                {
                    uiFreePose.iconOfPose.texture = previewTexture;
                }

                // 5. НАСТРАИВАЕМ КНОПКУ СОХРАНЕНИЯ "[1] Button Save"
                Transform saveBtnTrans = dialogPanel.transform.Find("[1] Button Save");
                if (saveBtnTrans != null)
                {
                    var saveBtn = saveBtnTrans.GetComponent<UnityEngine.UI.Button>();
                    if (saveBtn != null)
                    {
                        saveBtn.onClick.RemoveAllListeners(); // Отвязываем сохранение игры
                        saveBtn.onClick.AddListener(() =>
                        {
                            Plugin.Log.LogInfo("[EditorUiManager] Нажата кнопка 'Save Furniture Pose'. Запуск записи файлов...");
                            onConfirm?.Invoke(); // Вызываем ваше сохранение плагина
                            dialogPanel.SetActive(false); // Закрываем окно
                        });

                        // Возвращаем кнопке нормальный цвет (убираем красный сжатый вид)
                        var btnImg = saveBtn.GetComponent<UnityEngine.UI.Image>();
                        if (btnImg != null) btnImg.color = Color.white;

                        // Настраиваем текст на кнопке
                        var btnText = saveBtnTrans.GetComponentInChildren<UnityEngine.UI.Text>(true);
                        if (btnText != null)
                        {
                            btnText.gameObject.SetActive(true);
                            btnText.text = "Save Furniture Pose";
                            btnText.fontSize = 14;
                            btnText.color = Color.black; // Или Color.white в зависимости от стиля игры
                        }
                    }
                }

                // Настраиваем кнопку закрытия "Btn Close (4)" на обычный выход без сохранения
                Transform closeBtnTrans = dialogPanel.transform.Find("Btn Close (4)");
                if (closeBtnTrans != null)
                {
                    var closeBtn = closeBtnTrans.GetComponent<UnityEngine.UI.Button>();
                    if (closeBtn != null)
                    {
                        closeBtn.onClick.RemoveAllListeners();
                        closeBtn.onClick.AddListener(() =>
                        {
                            Plugin.Log.LogInfo("[EditorUiManager] Диалог закрыт без сохранения.");
                            dialogPanel.SetActive(false);
                        });
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[EditorUiManager] Ошибка в точечном ShowNativeStyleDialog: {ex}");
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
