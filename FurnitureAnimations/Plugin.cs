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
            if (code == null || code.controller == null) return;

            // Нам нужен сам компонент Animator персонажа, чтобы увидеть слои в рантайме
            Animator animator = __instance.user.anim;
            if (animator == null) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"\n==================================================");
            sb.AppendLine($"[AnimInspector] СТРУКТУРА СЛОЕВ ДЛЯ: {code.controller.name}");
            sb.AppendLine($"Всего слоев в Аниматоре: {animator.layerCount}");

            for (int i = 0; i < animator.layerCount; i++)
            {
                string layerName = animator.GetLayerName(i);
                float layerWeight = animator.GetLayerWeight(i);
                sb.AppendLine($"  Слой [{i}]: \"{layerName}\" | Вес (Weight): {layerWeight:F2}");

                // Проверим текущее состояние слоя
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(i);
                sb.AppendLine($"      Текущий стейт (Хэш): {stateInfo.fullPathHash}");
                sb.AppendLine($"      Скорость стейта: {stateInfo.speed}");
            }
            sb.AppendLine($"==================================================");

            Plugin.Log.LogWarning(sb.ToString());
        }
    }
}
