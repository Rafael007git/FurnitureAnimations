using System;
using System.IO;
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

            // Если ссылка пустая или указывает на уничтоженный Unity-объект,
            // принудительно переключаемся на глобальный живой синглтон!
            if (_player == null || _player.Equals(null))
            {
                _player = FurnitureAnimationPlayer.Instance;
            }

            if (_player == null || _player._animData == null || _player._animData.deltas == null)
            {
                GUILayout.Label("LOADING ENGINE DATA...", GUI.skin.label);
                return;
            }

            StringBuilder sb = new StringBuilder();

            string animName = _player.GetPlayingAnimationName();
            float speedMod = _player.GetSpeed();
            string easeMode = _player.GetEaseMode().ToString();

            // Читаем переменные через internal-доступ (убедитесь, что в плеере у них стоит internal)
            int currentTransition = _player._currentTransitionIndex;
            int gameFrameCounter = _player._gameFrameCounter;
            int totalTargetFrames = _player._totalTargetFrames;
            bool isReversing = _player._reversing;

            int currentDeltaFrames = 0;
            int totalAnimFrames = 0;
            int totalTransitions = _player._animData.deltas.Count;

            // Безопасный подсчет кадров автора
            for (int i = 0; i < totalTransitions; i++)
            {
                if (_player._animData.deltas[i] != null)
                {
                    totalAnimFrames += _player._animData.deltas[i].frames;
                }
            }

            if (currentTransition >= 0 && currentTransition < totalTransitions)
            {
                currentDeltaFrames = _player._animData.deltas[currentTransition].frames;
            }

            sb.AppendLine($"[SYSTEM]");
            sb.AppendLine($"  Game FPS: {_fpsDisplay} | DeltaTime: {Time.deltaTime:F4}s");
            sb.AppendLine($"[CONFIG]");
            sb.AppendLine($"  Animation Name: {animName}");
            sb.AppendLine($"  Speed Modifier: {speedMod * 100:F0}%");
            sb.AppendLine($"  Interpolation Mode: {easeMode}");
            sb.AppendLine($"[TIMING]");
            sb.AppendLine($"  Transition Index: {currentTransition} / {totalTransitions}");
            sb.AppendLine($"  Direction: {(isReversing ? "REVERSE <<" : "FORWARD >>")}");
            sb.AppendLine($"  Current Transition Frames: {currentDeltaFrames} author frames");
            sb.AppendLine($"  Engine Target Frames: {totalTargetFrames} generated frames");
            sb.AppendLine($"  Progress Frame Counter: {gameFrameCounter} / {totalTargetFrames}");
            sb.AppendLine($"  Total Animation Key-Frames: {totalAnimFrames}");

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.normal.textColor = Color.green;

            GUILayout.BeginArea(new Rect(10, 25, 330, 250));
            GUILayout.Label(sb.ToString(), labelStyle);
            GUILayout.EndArea();

            if (GUI.Button(new Rect(10, 280, 330, 30), "SAVE DEBUG TO DESKTOP"))
            {
                try
                {
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string filePath = Path.Combine(desktopPath, "animation_debug.txt");
                    File.WriteAllText(filePath, sb.ToString());
                    Plugin.Log.LogWarning($"[Radar] Лог успешно сохранен на рабочий стол!");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[Radar] Ошибка записи файла: {ex.Message}");
                }
            }
        }

    }
}
