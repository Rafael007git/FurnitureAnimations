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
        private Rect _windowRect = new Rect(Screen.width - 360f, 10f, 350f, 520f);
        private float _fpsCounter = 0f;
        private float _fpsTimer = 0f;
        private int _fpsDisplay = 0;

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
            // Если галочка в меню выключена (по умолчанию), радар полностью засыпает и ничего не рисует!
            if (Plugin.EnableDebugRadar == null || !Plugin.EnableDebugRadar.Value)
            {
                return;
            }

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

            // --- УЛЬТРА-АВТОНОМНЫЙ ВЫВОД ДЛЯ СЛОЖНЫХ СИСТЕМ ---
            if (GUI.Button(new Rect(10, 25, 330, 30), "DUMP TELEMETRY TO ALL LOGS"))
            {
                string logContent = sb.ToString();

                // 1. Прямая запись в корень игры (ВСЕГДА РАБОТАЕТ, НЕЗАВИСИМО ОТ ONEDRIVE)
                try
                {
                    string rootPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "furniture_pivot_debug.txt");
                    System.IO.File.WriteAllText(rootPath, logContent);

                    // Вместо капризного Plugin.Log пишем в стандартную системную консоль
                    System.Console.WriteLine("[RADAR SUCCESS] Лог успешно сохранен в корень игры: " + rootPath);
                }
                catch (System.Exception)
                {
                    // Игнорируем молча, чтобы не уронить поток кнопки
                }

                // 2. Мягкая попытка дублирования на рабочий стол
                try
                {
                    string desktopPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
                    string filePath = System.IO.Path.Combine(desktopPath, "animation_pivot_debug.txt");
                    System.IO.File.WriteAllText(filePath, logContent);
                    System.Console.WriteLine("[RADAR SUCCESS] Лог продублирован на десктоп.");
                }
                catch (System.Exception)
                {
                    // Ошибки доступа OneDrive теперь просто проглатываются, не ломая выполнение!
                }
            }

            // -----------------------------------------------------------------------------

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
            // =========================================================================
            // АБСОЛЮТНО БЕЗОПАСНАЯ ТЕЛЕМЕТРИЯ ВСЕХ ПИВОТОВ И ЯКОРЕЙ 📐✨
            // =========================================================================
            sb.AppendLine($"[PIVOTS & ANCHORS]");

            if (_player != null)
            {
                // 1. Координаты мебели в мире
                var furnitureField = _player.GetType().GetField("_targetFurniture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var furniture = furnitureField?.GetValue(_player) as Furniture;
                sb.AppendLine(furniture != null
                    ? $"  Furniture World Pos : {furniture.transform.position.x:F3}, {furniture.transform.position.y:F3}, {furniture.transform.position.z:F3}"
                    : "  Furniture World Pos : NOT FOUND");

                // 2. Локальный якорь посадки
                var localBasePosField = _player.GetType().GetField("_localBasePos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var lBaseVal = localBasePosField?.GetValue(_player);
                sb.AppendLine(lBaseVal is Vector3 lBase
                    ? $"  Furniture Local Anchor: {lBase.x:F3}, {lBase.y:F3}, {lBase.z:F3}"
                    : "  Furniture Local Anchor: NOT FOUND");

                // 3. Текущая рассчитываемая локальная позиция движения тела (Проверяем оба регистра имени)
                var targetLocalPosField = _player.GetType().GetField("targetLocalPos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                       ?? _player.GetType().GetField("_targetLocalPos", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var tLocalVal = targetLocalPosField?.GetValue(_player);
                sb.AppendLine(tLocalVal is Vector3 tLocal
                    ? $"  Target Motion Root   : {tLocal.x:F3}, {tLocal.y:F3}, {tLocal.z:F3}"
                    : "  Target Motion Root   : NOT FOUND");

                // 4. Физическая мировая позиция персонажа в Unity
                var characterField = _player.GetType().GetField("_character", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var characterComp = characterField?.GetValue(_player) as UnityEngine.Component;
                sb.AppendLine(characterComp != null
                    ? $"  Character World Root : {characterComp.transform.position.x:F3}, {characterComp.transform.position.y:F3}, {characterComp.transform.position.z:F3}"
                    : "  Character World Root : NOT FOUND");

                // 5. Движение кости таза (hip)
                var boneCacheField = _player.GetType().GetField("_boneCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var cache = boneCacheField?.GetValue(_player) as Dictionary<string, Transform>;
                if (cache != null && cache.TryGetValue("hip", out Transform hipTrans) && hipTrans != null)
                {
                    sb.AppendLine($"  Anatomy Bone 'hip'   : {hipTrans.localPosition.x:F3}, {hipTrans.localPosition.y:F3}, {hipTrans.localPosition.z:F3}");
                    sb.AppendLine($"  Hip World Pivot      : {hipTrans.position.x:F3}, {hipTrans.position.y:F3}, {hipTrans.position.z:F3}");
                }
                else
                {
                    sb.AppendLine("  Anatomy Bone 'hip'   : NOT FOUND IN CACHE");
                    sb.AppendLine("  Hip World Pivot      : NOT FOUND IN CACHE");
                }
            }

            // Рендерим блок текста на экране
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            labelStyle.normal.textColor = Color.green;

            GUILayout.BeginArea(new Rect(10, 60, 330, 450));
            GUILayout.Label(sb.ToString(), labelStyle);
            GUILayout.EndArea();

        }


    }
}
