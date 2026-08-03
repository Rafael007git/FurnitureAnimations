using System;
using System.Text;
using UnityEngine;

namespace FurnitureAnimationsMod
{
    public class AnimationRadar : MonoBehaviour
    {
        private FurnitureAnimationPlayer _player;
        private Rect _windowRect = new Rect(Screen.width - 360f, 10f, 350f, 320f);
        private float _fpsCounter = 0f;
        private float _fpsTimer = 0f;
        private int _fpsDisplay = 0;

        private void Awake()
        {
            _player = GetComponent<FurnitureAnimationPlayer>();
        }

        private void Update()
        {
            // Простой расчет FPS
            _fpsCounter++;
            _fpsTimer += Time.deltaTime;
            if (_fpsTimer >= 1.0f)
            {
                _fpsDisplay = Mathf.RoundToInt(_fpsCounter / _fpsTimer);
                _fpsCounter = 0f;
                _fpsTimer = 0f;
            }
        }

        private void OnGUI()
        {
            if (_player == null) return;

            // Задаем стильный темный полупрозрачный фон для радара
            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            _windowRect = GUI.Window(999, _windowRect, DrawRadarWindow, "ANIMATION ENGINE RADAR");
        }

        private void DrawRadarWindow(int windowID)
        {
            GUI.DragWindow();

            // Собираем текст для отображения и копирования
            StringBuilder sb = new StringBuilder();

            // Используем рефлексию или открытые геттеры для вытягивания приватных полей из плеера
            // Если поля приватные в вашем классе, мы можем получить их через Reflection:
            var type = _player.GetType();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            string animName = _player.GetPlayingAnimationName();
            float speedMod = _player.GetSpeed();
            string easeMode = _player.GetEaseMode().ToString();

            int currentTransition = (int)(type.GetField("_currentTransitionIndex", flags)?.GetValue(_player) ?? 0);
            float currentFrameTime = (float)(type.GetField("_currentFrameTime", flags)?.GetValue(_player) ?? 0f);
            bool isReversing = (bool)(type.GetField("_reversing", flags)?.GetValue(_player) ?? false);

            // Безопасно достаем PoseAnimationData и данные текущей дельты
            var animDataField = type.GetField("_animData", flags)?.GetValue(_player);
            int currentDeltaFrames = 0;
            int totalAnimFrames = 0;
            float baseRate = 0.0333f;

            if (animDataField != null)
            {
                var deltasProp = animDataField.GetType().GetField("deltas", flags)?.GetValue(animDataField) as System.Collections.IList;
                baseRate = (float)(animDataField.GetType().GetField("rate", flags)?.GetValue(animDataField) ?? 0.0333f);
                if (baseRate <= 0f) baseRate = 0.0333f;

                if (deltasProp != null)
                {
                    // Считаем общее число авторских кадров
                    foreach (var d in deltasProp)
                    {
                        totalAnimFrames += (int)(d.GetType().GetField("frames", flags)?.GetValue(d) ?? 0);
                    }

                    // Кадры текущей дельты
                    int arrayIdx = currentTransition - 1;
                    if (arrayIdx >= 0 && arrayIdx < deltasProp.Count)
                    {
                        var currentDelta = deltasProp[arrayIdx];
                        currentDeltaFrames = (int)(currentDelta.GetType().GetField("frames", flags)?.GetValue(currentDelta) ?? 0);
                    }
                }
            }

            float transitionDuration = currentDeltaFrames * baseRate;
            float localFraction = transitionDuration > 0 ? Mathf.Clamp01(currentFrameTime / transitionDuration) : 0f;

            // Формируем строки радара
            sb.AppendLine($"[SYSTEM]");
            sb.AppendLine($"  Game FPS: {_fpsDisplay} | DeltaTime: {Time.deltaTime:F4}s");
            sb.AppendLine($"[CONFIG]");
            sb.AppendLine($"  Animation Name: {animName}");
            sb.AppendLine($"  Speed Modifier: {speedMod * 100:F0}%");
            sb.AppendLine($"  Interpolation Mode: {easeMode}");
            sb.AppendLine($"[TIMING]");
            sb.AppendLine($"  Current Transition: idx {currentTransition} (Array: {currentTransition - 1})");
            sb.AppendLine($"  Direction: {(isReversing ? "REVERSE <<" : "FORWARD >>")}");
            sb.AppendLine($"  Delta Duration: {transitionDuration:F3}s ({currentDeltaFrames} author frames)");
            sb.AppendLine($"  Local Time Progress: {currentFrameTime:F3}s / {transitionDuration:F3}s");
            sb.AppendLine($"  Calculated Frame: {localFraction * currentDeltaFrames:F1} / {currentDeltaFrames}");
            sb.AppendLine($"  Total Animation Frames: {totalAnimFrames}");

            // Отрисовка в окне GUI
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.textColor = Color.green;

            GUILayout.BeginArea(new Rect(10, 25, 330, 250));
            GUILayout.Label(sb.ToString(), labelStyle);
            GUILayout.EndArea();

            // Кнопка копирования в буфер обмена
            if (GUI.Button(new Rect(10, 280, 330, 30), "COPY TO CLIPBOARD"))
            {
                GUIUtility.systemCopyBuffer = sb.ToString();
                Plugin.Log.LogInfo("[Radar] Radar data successfully copied to clipboard!");
            }
        }
    }
}
