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

            // Точечно вытягиваем ваши приватные переменные
            int currentTransition = (int)(type.GetField("_currentTransitionIndex", flags)?.GetValue(_player) ?? 0);
            int gameFrameCounter = (int)(type.GetField("_gameFrameCounter", flags)?.GetValue(_player) ?? 0);
            int totalTargetFrames = (int)(type.GetField("_totalTargetFrames", flags)?.GetValue(_player) ?? 0);
            bool isReversing = (bool)(type.GetField("_reversing", flags)?.GetValue(_player) ?? false);

            var animDataField = type.GetField("_animData", flags)?.GetValue(_player);
            int currentDeltaFrames = 0;
            int totalAnimFrames = 0;

            if (animDataField != null)
            {
                var deltasProp = animDataField.GetType().GetField("deltas", flags)?.GetValue(animDataField) as System.Collections.IList;
                if (deltasProp != null)
                {
                    // Считаем общее число авторских кадров
                    foreach (var d in deltasProp)
                    {
                        totalAnimFrames += (int)(d.GetType().GetField("frames", flags)?.GetValue(d) ?? 0);
                    }

                    // Кадры текущего перехода
                    if (currentTransition >= 0 && currentTransition < deltasProp.Count)
                    {
                        var currentDelta = deltasProp[currentTransition];
                        currentDeltaFrames = (int)(currentDelta.GetType().GetField("frames", flags)?.GetValue(currentDelta) ?? 0);
                    }
                }
            }

            // float transitionDuration = currentDeltaFrames * baseRate;
            // float localFraction = transitionDuration > 0 ? Mathf.Clamp01(currentFrameTime / transitionDuration) : 0f;

            // Формируем строки радара
            sb.AppendLine($"[SYSTEM]");
            sb.AppendLine($"  Game FPS: {_fpsDisplay} | DeltaTime: {Time.deltaTime:F4}s");
            sb.AppendLine($"[CONFIG]");
            sb.AppendLine($"  Animation Name: {animName}");
            sb.AppendLine($"  Speed Modifier: {speedMod * 100:F0}%");
            sb.AppendLine($"  Interpolation Mode: {easeMode}");
            sb.AppendLine($"[TIMING]");
            sb.AppendLine($"  Transition Index: {currentTransition} / {totalAnimFrames}");
            sb.AppendLine($"  Direction: {(isReversing ? "REVERSE <<" : "FORWARD >>")}");
            sb.AppendLine($"  Current Transition Frames: {currentDeltaFrames} author frames");
            sb.AppendLine($"  Engine Target Frames: {totalTargetFrames} generated frames");
            sb.AppendLine($"  Progress Frame Counter: {gameFrameCounter} / {totalTargetFrames}");
            sb.AppendLine($"  Total Animation Key-Frames: {totalAnimFrames}");

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
                // Используем TextEditor, который BepInEx пропускает без ошибок безопасности
                TextEditor te = new TextEditor();
                te.text = sb.ToString();
                te.SelectAll();
                te.Copy();

                Plugin.Log.LogInfo("[Radar] Radar data successfully copied to clipboard!");
            }
        }
    }
}
