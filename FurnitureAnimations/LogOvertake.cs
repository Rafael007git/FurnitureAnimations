using BepInEx.Logging;
using FurnitureAnimationsMod;
using System.Linq;
using UnityEngine;

namespace FurnitureAnimations
{
    // 🕵️ ГЛОБАЛЬНЫЙ ПЕРЕХВАТЧИК ЛОГОВ БЕЗ НАГРУЗКИ НА UPDATE
    public class AnimatedPoseLogListener : ILogListener
    {
        public void LogEvent(object sender, LogEventArgs eventArgs)
        {
            try
            {
                // Нам интересны только информационные или предупреждающие сообщения
                if (eventArgs.Level != LogLevel.Info && eventArgs.Level != LogLevel.Warning && eventArgs.Level != LogLevel.Message) return;

                string logMessage = eventArgs.Data?.ToString();
                if (string.IsNullOrEmpty(logMessage)) return;

                // 🎯 ЛОВИМ ТЕКСТ ЗЛОДЕЯ ИЗ ВАШЕГО ЛОГА!
                if (logMessage.Contains("Setting Player to animation"))
                {
                    // Вытаскиваем имя анимации. Из строки "Setting Player to animation DanceAround01" 
                    // мы забираем всё, что идет после слова "animation "
                    string keyword = "animation ";
                    int index = logMessage.IndexOf(keyword);
                    if (index == -1) return;

                    string animationName = logMessage.Substring(index + keyword.Length).Trim();
                    Plugin.Log.LogWarning($"[LOG_OVERTAKE] 🎉 Успешно перехвачен лог AnimatedPose! Включен танец: '{animationName}'");

                    // 1. Ищем легальную иконку этого танца в каталоге игры
                    if (RM.code != null && RM.code.allFreePoses != null)
                    {
                        foreach (Transform t in RM.code.allFreePoses.items)
                        {
                            if (t != null && t.name == animationName)
                            {
                                var p = t.GetComponent<global::Pose>();
                                if (p != null && p.icon != null)
                                {
                                    // Записываем иконку в скрытое поле PoseExporter через рефлексию
                                    var field = typeof(PoseExporter).GetField("_lastCapturedIcon", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                    field?.SetValue(null, p.icon);
                                    Plugin.Log.LogInfo($"[LOG_OVERTAKE] Легальная иконка для '{animationName}' успешно сохранена в память.");
                                }
                                break;
                            }
                        }
                    }

                    // 2. Мгновенно перекрашиваем кнопку интерактива на сцене
                    var uiFreePose = GameObject.FindObjectOfType<UIFreePose>();
                    if (uiFreePose != null)
                    {
                        Transform sdkBtnTrans = uiFreePose.transform.Find("Button_SaveInteract");
                        var buttonTextComp = sdkBtnTrans?.GetComponentInChildren<UnityEngine.UI.Text>();
                        var buttonImageComp = sdkBtnTrans?.GetComponent<UnityEngine.UI.Image>();

                        if (buttonTextComp != null) buttonTextComp.text = "Link Animated Pose for Furniture";
                        if (buttonImageComp != null) buttonImageComp.color = new Color(0.6f, 0.2f, 0.8f, 1f); // Фиолетовый

                        Plugin.Log.LogInfo("[LOG_OVERTAKE] Кнопка интерактива успешно переведена в режим танца.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[LOG_OVERTAKE] Ошибка разбора строки лога: {ex}");
            }
        }

        // Обязательные методы интерфейса ILogListener (просто оставляем пустыми)
        public void Dispose() { }
    }
}
