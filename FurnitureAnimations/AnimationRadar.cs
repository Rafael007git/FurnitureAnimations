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
        private Rect _windowRect = new Rect(Screen.width - 410f, 10f, 420f, 950f); // Немного расширили окно под таблицу
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

            // --- FOCUS CHECK: Сверяем фокус интерфейса перед отрисовкой HUD ---
            UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
            if (uiPose != null && uiPose.gameObject.activeInHierarchy)
            {
                // Если меню открыто, но текущий пользователь мебели НЕ совпадает с объектом,
                // на котором висит этот радар — мягко уходим в спячку и уступаем дорогу фокусу!
                if (uiPose.curFurniture != null && uiPose.curFurniture.user != null)
                {
                    if (uiPose.curFurniture.user.gameObject != this.gameObject)
                    {
                        return; // Уступаем отрисовку активному персонажу интерфейса
                    }
                }
            }
            else
            {
                // Если общее меню UIPose вообще закрыто, показывать радар для компаньонок нет смысла.
                // Оставляем радар только на Главной Героине (Player), чтобы не захламлять пустой экран.
                bool isMainHero = this.gameObject.name.Contains("Player") ||
                                  this.gameObject.GetComponent("PlayerControl") != null;
                if (!isMainHero) return;
            }

            // Задаем стильный темный полупрозрачный фон для радара
            GUI.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.92f);
            _windowRect = GUI.Window(999, _windowRect, DrawRadarWindow, "ANIMATION ENGINE TELEMETRY & RAM MAP");
        }

        private void DrawRadarWindow(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, 400, 20));

            // Strongly bind to the local character instance this radar script is attached to
            if (_player == null || _player.Equals(null))
            {
                _player = gameObject.GetComponent<FurnitureAnimationPlayer>();
            }

            // CRITICAL VERIFICATION:
            // Extract the active furniture instance directly from our own localized player runtime context.
            // This prevents companions sitting on other furniture from hijacking the Main Hero's telemetry window!
            Furniture currentFurniture = null;
            if (_player != null)
            {
                var furnitureField = _player.GetType().GetField("_targetFurniture",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                currentFurniture = furnitureField?.GetValue(_player) as Furniture;
            }
            else
            {
                // Fallback to active UI context only if the local custom player loop hasn't initialized yet
                UIPose uiPose = GameObject.FindObjectOfType<UIPose>();
                if (uiPose != null && uiPose.gameObject.activeInHierarchy)
                {
                    currentFurniture = uiPose.curFurniture;
                }
            }

            if (currentFurniture == null)
            {
                GUIStyle centerLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                GUILayout.Label("\n\n🤖 NO ACTIVE FURNITURE INTERACTION DETECTED\n(Approach furniture and enter Pose Menu)", centerLabelStyle);
                return;
            }

            if (currentFurniture == null)
            {
                GUIStyle centerLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                GUILayout.Label("\n\n🤖 NO ACTIVE FURNITURE INTERACTION DETECTED\n(Approach furniture and enter Pose Menu)", centerLabelStyle);
                return;
            }

            string cleanFurnitureName = currentFurniture.name.Replace("(Clone)", "").Trim();
            var localAudio = gameObject.GetComponent<AnimationAudioManager>();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"<b>[SYSTEM]</b>");
            sb.AppendLine($" Game FPS: {_fpsDisplay} | DeltaTime: {Time.deltaTime:F4}s");
            sb.AppendLine($" Target Furniture: <color=yellow>{cleanFurnitureName}</color>");

            sb.AppendLine($"\n<b>[ACTIVE PLAYER STATE]</b>");
            string curAnim = (_player != null && _player._animData != null) ? _player.GetPlayingAnimationName() : "";
            string curTrack = (localAudio != null) ? localAudio.GetCurrentTrackName() : "noAudio";

            if (_player != null && _player._animData != null)
            {
                sb.AppendLine($" Playing Animation : {curAnim}");
                sb.AppendLine($" Playing Audio Track: <color=cyan>{curTrack}</color>");
                sb.AppendLine($" Engine Frame State : {_player._gameFrameCounter} / {_player._totalTargetFrames} (Index: {_player._currentTransitionIndex})");
                sb.AppendLine($" Current Direction  : {(_player._reversing ? "REVERSE <<" : "FORWARD >>")}");
            }
            else
            {
                sb.AppendLine($" Player Engine Status: <color=gray>IDLE / VANILLA PRESET ACTIVE</color>");
            }

            GUIStyle textStyle = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Normal };
            textStyle.normal.textColor = Color.white;
            GUILayout.Label(sb.ToString(), textStyle);

            // =========================================================================
            // НОВАЯ ИЗОЛИРОВАННАЯ КАРТА ОЗУ ДЛЯ ТЕКУЩЕЙ АНИМАЦИИ 🧠⚙️
            // =========================================================================
            if (ConfigManager.LoadedConfigs.TryGetValue(cleanFurnitureName, out FurnitureConfig config) && config != null && config.RuntimePlaybackMemory != null)
            {
                if (!string.IsNullOrEmpty(curAnim))
                {
                    // 1. СЕКЦИЯ СГЛАЖИВАНИЯ ТЕКУЩЕЙ АНИМАЦИИ
                    GUILayout.Label("<b>[GEOMETRY & INTERPOLATION ANCHOR]</b>", textStyle);
                    string easeKey = $"{curAnim}_globalEase";
                    EaseMode cachedEase = EaseMode.Linear;

                    if (config.RuntimePlaybackMemory.TryGetValue(easeKey, out PlaybackSettingsData easeData) && easeData != null)
                    {
                        cachedEase = easeData.EaseMode;
                    }

                    EaseMode liveEase = (_player != null) ? _player.GetEaseMode() : cachedEase;
                    GUILayout.Label($" Active Animation Target: <color=yellow>{curAnim}</color>");
                    GUILayout.Label($" Global Ease Profile (RAM): <color=lime>{cachedEase}</color> | Hardware State: <color=cyan>{liveEase}</color>");
                    GUILayout.Space(8);

                    // 2. СЕКЦИЯ СКОРОСТЕЙ И ТРЕКОВ (ТОЛЬКО ДЛЯ ТЕКУЩЕЙ АНИМАЦИИ!)
                    GUILayout.Label($"<b>[AUDIO TRACK & SPEED FOR: {curAnim}]</b>", textStyle);
                    _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(480));

                    GUIStyle tableHeaderStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                    tableHeaderStyle.normal.textColor = Color.yellow;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Available Audio Track Name", tableHeaderStyle, GUILayout.Width(280));
                    GUILayout.Label("Speed", tableHeaderStyle, GUILayout.Width(80));
                    GUILayout.EndHorizontal();
                    GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));

                    int activeAnimationTracksCount = 0;
                    foreach (KeyValuePair<string, PlaybackSettingsData> entry in config.RuntimePlaybackMemory)
                    {
                        string sessionKey = entry.Key;
                        PlaybackSettingsData settings = entry.Value;

                        if (settings == null) continue;

                        // КРИТИЧЕСКИЙ ФИЛЬТР КОНТЕКСТА: 
                        // Выводим трек только если он принадлежит текущей активной анимации и не является сервисным ключом сглаживания!
                        if (!sessionKey.StartsWith(curAnim, StringComparison.OrdinalIgnoreCase) ||
                            sessionKey.EndsWith("_globalEase", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        activeAnimationTracksCount++;
                        int underscoreIndex = sessionKey.IndexOf('_');
                        string audioTrack = underscoreIndex > 0 ? sessionKey.Substring(underscoreIndex + 1) : "unknown";

                        string speedText = $"{Mathf.RoundToInt(settings.Speed * 100)}%";

                        string rowPrefix = "";
                        Color rowColor = Color.white;

                        if (audioTrack.Equals(curTrack, StringComparison.OrdinalIgnoreCase))
                        {
                            rowPrefix = "▶ ";
                            rowColor = Color.green;
                        }

                        GUIStyle rowStyle = new GUIStyle(GUI.skin.label) { richText = true };
                        rowStyle.normal.textColor = rowColor;

                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{rowPrefix}{audioTrack}", rowStyle, GUILayout.Width(280));
                        GUILayout.Label(speedText, rowStyle, GUILayout.Width(80));
                        GUILayout.EndHorizontal();
                        GUILayout.Space(2);
                    }

                    GUILayout.EndScrollView();

                    // Подвальная статистика
                    GUILayout.Box("", GUILayout.Height(2), GUILayout.ExpandWidth(true));
                    GUILayout.Label($"Total Audio Tracks for Current Animation: <b>{activeAnimationTracksCount}</b>", textStyle);
                }
                else
                {
                    // Фаллбэк-панель на случай, если игрок просто зашел в меню мебели, но еще не нажал ни на одну кастомную иконку
                    GUIStyle idleStateStyle = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleCenter };
                    GUILayout.Label("\n\n<color=gray><i>[WAITING FOR PROFILE SELECT]\nClick on any Custom Animation Icon to display matrix telemetry map</i></color>", idleStateStyle);
                }

                int ramCount = config.RuntimePlaybackMemory.Count;
                GUILayout.Label($"Total Memory Nodes Baked for Asset: <b><color=lime>{ramCount}</color></b>", textStyle);
            }
            else
            {
                GUILayout.Label("<color=gray>No profiles allocated inside Runtime Memory map.</color>", textStyle);
            }

            if (GUILayout.Button("DUMP MAP TO LOG FILE"))
            {
                Plugin.Log.LogWarning($"[RADAR_DUMP] Manual RAM diagnostic report requested for: {cleanFurnitureName}");
                if (config != null && config.RuntimePlaybackMemory != null)
                {
                    foreach (var kp in config.RuntimePlaybackMemory)
                    {
                        Plugin.Log.LogInfo($"   Profile Node -> Key: {kp.Key} | Speed: {kp.Value.Speed} | Ease: {kp.Value.EaseMode}");
                    }
                }
            }
        }

    }
}
