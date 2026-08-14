using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace FurnitureAnimationsMod
{
    public class AnimationRadar : MonoBehaviour
    {
        private FurnitureAnimationPlayer _player;
        private Rect _windowRect = new Rect(Screen.width - 410f, 10f, 400f, 650f); // Немного расширили окно под таблицу
        private float _fpsCounter = 0f;
        private float _fpsTimer = 0f;
        private int _fpsDisplay = 0;
        private Vector2 _scrollPosition = Vector2.zero; // Добавили скролл для длинных списков

        private void Update()
        {
            // Расчет FPS
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
            // Если отладка выключена в меню F1, радар полностью спит
            if (Plugin.EnableDebugRadar == null || !Plugin.EnableDebugRadar.Value)
            {
                return;
            }

            // Задаем стильный темный полупрозрачный фон для радара
            GUI.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.92f);
            _windowRect = GUI.Window(999, _windowRect, DrawRadarWindow, "ANIMATION ENGINE TELEMETRY & RAM MAP");
        }

        private void DrawRadarWindow(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, 400, 20));

            // Защита и автоматическое связывание с активным плеером
            if (_player == null || _player.Equals(null))
            {
                _player = FurnitureAnimationPlayer.Instance;
            }

            // Пытаемся определить текущую мебель интерактива через UIPose или плеер
            UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
            Furniture currentFurniture = null;

            if (uiPose != null && uiPose.gameObject.activeInHierarchy)
            {
                currentFurniture = uiPose.curFurniture;
            }
            else if (_player != null)
            {
                var furnitureField = _player.GetType().GetField("_targetFurniture",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                currentFurniture = furnitureField?.GetValue(_player) as Furniture;
            }

            if (currentFurniture == null)
            {
                GUIStyle centerLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                GUILayout.Label("\n\n🤖 NO ACTIVE FURNITURE INTERACTION DETECTED\n(Approach furniture and enter Pose Menu)", centerLabelStyle);
                return;
            }

            string cleanFurnitureName = currentFurniture.name.Replace("(Clone)", "").Trim();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"<b>[SYSTEM]</b>");
            sb.AppendLine($" Game FPS: {_fpsDisplay} | DeltaTime: {Time.deltaTime:F4}s");
            sb.AppendLine($" Target Furniture: <color=yellow>{cleanFurnitureName}</color>");

            // Секция активного состояния плеера
            sb.AppendLine($"\n<b>[ACTIVE PLAYER STATE]</b>");
            if (_player != null && _player._animData != null)
            {
                string curAnim = _player.GetPlayingAnimationName();
                string curTrack = (AnimationAudioManager.Instance != null) ? AnimationAudioManager.Instance.GetCurrentTrackName() : "noAudio";
                sb.AppendLine($" Playing Animation : {curAnim}");
                sb.AppendLine($" Playing Audio Track: <color=cyan>{curTrack}</color>");
                sb.AppendLine($" Engine Frame State : {_player._gameFrameCounter} / {_player._totalTargetFrames} (Index: {_player._currentTransitionIndex})");
                sb.AppendLine($" Current Direction  : {(_player._reversing ? "REVERSE <<" : "FORWARD >>")}");
            }
            else
            {
                sb.AppendLine($" Player Engine Status: <color=gray>IDLE / VANILLA PRESET ACTIVE</color>");
            }

            // Рендерим верхнюю текстовую телеметрию
            GUIStyle textStyle = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Normal };
            textStyle.normal.textColor = Color.white;
            GUILayout.Label(sb.ToString(), textStyle);

            // =========================================================================
            // КАРТА ОЗУ СОСТОЯНИЙ (МЕБЕЛЬ -> АНИМАЦИЯ -> АУДИО)
            // =========================================================================
            GUILayout.Label("<b>[RAM CONFIGURATION MAP FOR THIS FURNITURE]</b>", textStyle);

            // Начало области прокрутки таблицы ОЗУ-карт
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(320));

            GUIStyle tableHeaderStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            tableHeaderStyle.normal.textColor = Color.yellow;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Animation & Audio Pair Key", tableHeaderStyle, GUILayout.Width(220));
            GUILayout.Label("Speed", tableHeaderStyle, GUILayout.Width(60));
            GUILayout.Label("EaseMode", tableHeaderStyle, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            // Отрисовываем разделительную черту
            GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));

            int totalPairsCalculated = 0;

            // ФИКС ОШИБКИ CS0103: Извлекаем config из LoadedConfigs прямо перед чтением словаря ОЗУ
            if (ConfigManager.LoadedConfigs.TryGetValue(cleanFurnitureName, out FurnitureConfig config) && config != null && config.RuntimePlaybackMemory != null && config.RuntimePlaybackMemory.Count > 0)
            {
                // Получаем имя текущей играющей анимации и трека для подсветки строки
                string truePlayingAnimName = (_player != null && _player._animData != null) ? _player.GetPlayingAnimationName() : "";
                string activeTrack = (AnimationAudioManager.Instance != null) ? AnimationAudioManager.Instance.GetCurrentTrackName() : "noAudio";

                foreach (KeyValuePair<string, PlaybackSettingsData> entry in config.RuntimePlaybackMemory)
                {
                    string sessionKey = entry.Key; // Формат: "DanceKneelingA_noAudio"
                    PlaybackSettingsData settings = entry.Value;

                    if (settings == null) continue;

                    totalPairsCalculated++;

                    // Красиво разделяем ключ для вывода на экран по первому символу подчеркивания
                    int underscoreIndex = sessionKey.IndexOf('_');
                    string animName = underscoreIndex > 0 ? sessionKey.Substring(0, underscoreIndex) : sessionKey;
                    string audioTrack = underscoreIndex > 0 ? sessionKey.Substring(underscoreIndex + 1) : "unknown";

                    string speedText = $"{Mathf.RoundToInt(settings.Speed * 100)}%";
                    string easeText = settings.EaseMode.ToString();

                    // Выделяем цветом строчку, которая проигрывается прямо сейчас
                    string rowPrefix = "";
                    Color rowColor = Color.white;

                    if (!string.IsNullOrEmpty(truePlayingAnimName) && animName.Equals(truePlayingAnimName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (audioTrack.Equals(activeTrack, StringComparison.OrdinalIgnoreCase))
                        {
                            rowPrefix = "▶ ";
                            rowColor = Color.green; // Зеленый для активной пары
                        }
                    }

                    GUIStyle rowStyle = new GUIStyle(GUI.skin.label) { richText = true };
                    rowStyle.normal.textColor = rowColor;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{rowPrefix}{animName}\n   └─ {audioTrack}", rowStyle, GUILayout.Width(220));
                    GUILayout.Label(speedText, rowStyle, GUILayout.Width(60));
                    GUILayout.Label(easeText, rowStyle, GUILayout.Width(80));
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4);
                }
            }
            else
            {
                GUILayout.Label("<color=gray>No pairs initialized in Runtime Memory.</color>", textStyle);
            }

            GUILayout.EndScrollView();

            // Подвальная статистика радара
            GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));
            int ramCount = (config != null && config.RuntimePlaybackMemory != null) ? config.RuntimePlaybackMemory.Count : 0;
            GUILayout.Label($"Total Combinations in UI Matrix: <b>{totalPairsCalculated}</b>", textStyle);
            GUILayout.Label($"Total Pairs Initialized in RAM: <b><color=lime>{ramCount}</color></b>", textStyle);

            // Кнопка принудительного дампа
            if (GUILayout.Button("DUMP MAP TO LOG FILE"))
            {
                Plugin.Log.LogWarning($"[RADAR_DUMP] Ручной отчет по ОЗУ для {cleanFurnitureName}. Записано пар: {ramCount}");
                if (config != null && config.RuntimePlaybackMemory != null)
                {
                    foreach (var kp in config.RuntimePlaybackMemory)
                    {
                        Plugin.Log.LogInfo($"   Key: {kp.Key} | Speed: {kp.Value.Speed} | Ease: {kp.Value.EaseMode}");
                    }
                }
            }
        }
    }
}
