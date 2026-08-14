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
                // Вытаскиваем приватное поле мебели из плеера рефлексией, если UI закрыт, но анимация идет
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

            // Пытаемся достать список всех аудиотреков, которые отсканировал менеджер звука
            List<string> scannedAudioFiles = new List<string> { "noAudio" }; // "noAudio" присутствует всегда по ТЗ

            // Вытягиваем приватный плейлист из синглтона аудио-менеджера через рефлексию
            if (AnimationAudioManager.Instance != null)
            {
                var playlistField = typeof(AnimationAudioManager).GetField("_currentPlaylist",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                List<string> rawPlaylist = playlistField?.GetValue(AnimationAudioManager.Instance) as List<string>;

                if (rawPlaylist != null && rawPlaylist.Count > 0)
                {
                    foreach (string fullPath in rawPlaylist)
                    {
                        string pureName = Path.GetFileName(fullPath);
                        if (!scannedAudioFiles.Contains(pureName))
                        {
                            scannedAudioFiles.Add(pureName);
                        }
                    }
                }
            }

            // Достаем позы из конфига мода для этой мебели
            List<string> availableAnimations = new List<string>();
            if (ConfigManager.LoadedConfigs.TryGetValue(cleanFurnitureName, out FurnitureConfig config) && config != null)
            {
                if (config.InteractionPoses != null)
                {
                    foreach (var pose in config.InteractionPoses)
                    {
                        // Берем только анимации нашего мода (внешние позы), у которых есть контроллеры/имена
                        if (pose != null && !string.IsNullOrEmpty(pose.ControllerName))
                        {
                            if (!availableAnimations.Contains(pose.ControllerName))
                                availableAnimations.Add(pose.ControllerName);
                        }
                    }
                }
            }

            // Фаллбэк на случай, если в JSON еще нет кастомных поз, но плеер играет какую-то анимацию прямо сейчас
            if (_player != null && _player._animData != null && !availableAnimations.Contains(_player._animData.name))
            {
                availableAnimations.Add(_player._animData.name);
            }

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

            if (availableAnimations.Count == 0)
            {
                GUILayout.Label("<color=gray>No custom animations indexed for this asset.</color>", textStyle);
            }
            else
            {
                foreach (string animName in availableAnimations)
                {
                    foreach (string audioTrack in scannedAudioFiles)
                    {
                        // ТЗ: Для "idle" выводим только "noAudio", а для танцев — "noAudio" + их треки
                        bool isIdle = animName.ToLower().Contains("idle");
                        if (isIdle && audioTrack != "noAudio")
                        {
                            continue; // Пропускаем комбинации типа idle + танцевальный трек
                        }

                        // Если это танец (например danceBachata), проверяем префикс файла, чтобы не выводить кашу
                        // (danceBachata должен сочетаться только с noAudio и файлами, начинающимися на danceBachata)
                        if (!isIdle && audioTrack != "noAudio" && !audioTrack.StartsWith(animName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // Пропускаем перекрестные пары (например danceBachata + danceSalsa.mp3)
                        }

                        totalPairsCalculated++;

                        // Сборка монолитного составного ключа
                        string sessionKey = $"{animName}_{audioTrack}";

                        // Проверяем, существует ли пара в ОЗУ памяти
                        string speedText = "150%"; // Наш дефолт по ТЗ
                        string easeText = "Linear"; // Наш дефолт по ТЗ
                        bool isInitializedInRam = false;


                        if (config != null && config.RuntimePlaybackMemory != null)
                        {
                            if (config.RuntimePlaybackMemory.TryGetValue(sessionKey, out PlaybackSettingsData settings) && settings != null)
                            {
                                speedText = $"{Mathf.RoundToInt(settings.Speed * 100)}%";
                                easeText = settings.EaseMode.ToString();
                                isInitializedInRam = true;
                            }
                        }

                        // Выделяем цветом строчку, которая проигрывается прямо сейчас
                        string rowPrefix = "";
                        Color rowColor = Color.gray;

                        if (_player != null && _player._animData != null && _player._animData.name == animName)
                        {
                            string activeTrack = (AnimationAudioManager.Instance != null) ? AnimationAudioManager.Instance.GetCurrentTrackName() : "noAudio";
                            if (activeTrack == audioTrack)
                            {
                                rowPrefix = "▶ ";
                                rowColor = Color.green;
                            }
                            else if (isInitializedInRam)
                            {
                                rowColor = Color.white; // Пара инициализирована, но не играет сейчас
                            }
                        }
                        else if (isInitializedInRam)
                        {
                            rowColor = Color.white;
                        }

                        GUIStyle rowStyle = new GUIStyle(GUI.skin.label);
                        rowStyle.normal.textColor = rowColor;

                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{rowPrefix}{animName}\n   └─ {audioTrack}", rowStyle, GUILayout.Width(220));
                        GUILayout.Label(speedText, rowStyle, GUILayout.Width(60));
                        GUILayout.Label(easeText, rowStyle, GUILayout.Width(80));
                        GUILayout.EndHorizontal();

                        // Легкий отступ между парами
                        GUILayout.Space(4);
                    }
                }
            }

            GUILayout.EndScrollView();

            // Подвальная статистика радара
            GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));
            int ramCount = (config != null && config.RuntimePlaybackMemory != null) ? config.RuntimePlaybackMemory.Count : 0;
            GUILayout.Label($"Total Combinations in UI Matrix: <b>{totalPairsCalculated}</b>", textStyle);
            GUILayout.Label($"Total Pairs Initialized in RAM: <b><color=lime>{ramCount}</color></b>", textStyle);

            // Кнопка принудительного дампа (сохранена из оригинального класса)
            if (GUILayout.Button("DUMP MAP TO LOG FILE"))
            {
                // Логика записи лога на диск...
                Plugin.Log.LogWarning($"[RADAR_DUMP] Игрок запросил ручной отчет по ОЗУ для {cleanFurnitureName}. Записано пар: {ramCount}");
            }
        }
    }
}