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
            if (code == null) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"\n==================================================");
            sb.AppendLine($"[PoleInspector] ДАННЫЕ ИНТЕРАКТИВА: {__instance.name}");
            sb.AppendLine($"Имя контроллера анимации: {code.controller?.name ?? "NULL"}");

            // 1. Вычисляем эталонное смещение (координаты точки loc)
            if (code.loc != null)
            {
                Vector3 pos = code.loc.localPosition;
                Vector3 rot = code.loc.localEulerAngles;
                sb.AppendLine($"--------------------------------------------------");
                sb.AppendLine($"ЭТАЛОННОЕ СМЕЩЕНИЕ (loc):");
                sb.AppendLine($"  localPosition: new Vector3({pos.x}f, {pos.y}f, {pos.z}f)");
                sb.AppendLine($"  localRotation (Euler): new Vector3({rot.x}f, {rot.y}f, {rot.z}f)");
            }

            // 2. Собираем данные обо всех камерах
            sb.AppendLine($"--------------------------------------------------");
            if (__instance.camerasGroup != null)
            {
                sb.AppendLine($"Найдено камер в карусели: {__instance.camerasGroup.childCount}");
                for (int i = 0; i < __instance.camerasGroup.childCount; i++)
                {
                    Transform cam = __instance.camerasGroup.GetChild(i);
                    Vector3 cPos = cam.localPosition;
                    Vector3 cRot = cam.localEulerAngles;
                    sb.AppendLine($"  Камера [{i}] Название: \"{cam.name}\"");
                    sb.AppendLine($"    pos: new Vector3({cPos.x}f, {cPos.y}f, {cPos.z}f)");
                    sb.AppendLine($"    rot: new Vector3({cRot.x}f, {cRot.y}f, {cRot.z}f)");
                }
            }
            else
            {
                sb.AppendLine($"[Предупреждение] В объекте {__instance.name} отсутствует camerasGroup!");
            }
            sb.AppendLine($"==================================================");

            // Выводим в лог BepInEx
            Plugin.Log.LogWarning(sb.ToString());
        }
    }
}
