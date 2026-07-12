using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System.Text;

namespace FurnitureAnimationsMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lorifel007.furnitureposefix";
        public const string PluginName = "Furniture Pose Fix (Editor and Injector)";
        public const string PluginVersion = "0.0.1";

        public static ManualLogSource Log;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;

            // Инициализируем и применяем патчи строго для этого проекта
            harmony = new Harmony(PluginGuid);
            harmony.PatchAll();

            Log.LogInfo($"{PluginName} успешно запущен! Сканер анимаций мебели активирован.");
        }

        private void OnDestroy()
        {
            // Корректно снимаем патчи при выгрузке (если используешь ScriptEngine для тестов)
            harmony?.UnpatchSelf();
        }
    }

    // === ШАГ 1: ИНСПЕКТОР ВАНИЛЬНЫХ АНИМАЦИЙ МЕБЕЛИ ===
    [HarmonyPatch(typeof(Furniture), "DoPose")]
    public class FurnitureAnimationInspector
    {
        [HarmonyPostfix]
        public static void Postfix(Furniture __instance, global::Pose code)
        {
            if (code == null || code.controller == null)
            {
                Plugin.Log.LogWarning("[AnimInspector] Персонаж взаимодействует с мебелью, но у позы нет контроллера анимаций.");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"\n==================================================");
            sb.AppendLine($"[AnimInspector] ДИАГНОСТИКА АНИМАЦИИ МЕБЕЛИ: {__instance.name}");
            sb.AppendLine($"Название позы (GameObject): {code.name}");
            sb.AppendLine($"Имя контроллера: {code.controller.name}");
            sb.AppendLine($"Категория в UI игры: {code.categoryName}");
            sb.AppendLine($"--------------------------------------------------");

            // Извлекаем все анимационные клипы из контроллера в памяти
            AnimationClip[] clips = code.controller.animationClips;
            sb.AppendLine($"Всего клипов в контроллере: {clips.Length}");

            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (clip != null)
                {
                    sb.AppendLine($"  [{i + 1}] Название клипа: \"{clip.name}\"");
                    sb.AppendLine($"      Длительность: {clip.length:F2} сек.");
                    sb.AppendLine($"      Зациклен (Loop): {clip.isLooping}");
                    sb.AppendLine($"      Частота кадров (FPS): {clip.frameRate}");
                    sb.AppendLine($"      Тип анимации (Humanoid): {clip.isHumanMotion}");
                }
            }
            sb.AppendLine($"==================================================");

            // Пишем в лог BepInEx
            Plugin.Log.LogWarning(sb.ToString());
        }
    }
}
