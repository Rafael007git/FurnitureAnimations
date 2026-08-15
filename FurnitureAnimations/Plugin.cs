using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FurnitureAnimations;
using HarmonyLib;

namespace FurnitureAnimationsMod
{
    // Проверь, чтобы имя исполняемого файла игры было написано без ошибок
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("aedenthorn.PoseAnimations", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("She Will Punish Them.exe")]
    public class Plugin : BaseUnityPlugin // <-- КРИТИЧНО: Класс ОБЯЗАТЕЛЬНО должен быть public и наследоваться от BaseUnityPlugin
    {
        public const string PluginGuid = "com.lorifel007.furnitureanimations";
        public const string PluginName = "Furniture Animations Mod";
        public const string PluginVersion = "1.7.0";

        public static ManualLogSource Log;
        private Harmony harmony;

        public static bool IsAnyPoseSelected = false;
        public static bool IsCustomPoseActive = false;

        public static ConfigEntry<bool> EnableDebugRadar;
        public static ConfigEntry<bool> ForceAbsoluteSkeletalReset;

        // BepInEx ищет именно метод Awake без параметров
        private void Awake()
        {
            Log = Logger;
            Log.LogWarning("[FurnitureMod] Старт инициализации плагина...");

            EditorUiManager.Initialize();

            try
            {
                // Запускаем наш менеджер конфигураций
                ConfigManager.Initialize();

                // Применяем Harmony патчи
                harmony = new Harmony(PluginGuid);
                harmony.PatchAll();

                Log.LogWarning("[FurnitureMod] Плагин успешно загружен и применил патчи!");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"[FurnitureMod] Сбой при загрузке плагина: {ex.Message}");
            }

            EnableDebugRadar = Config.Bind(
                "Debug Settings",                             // Вкладка та же
                "Enable Debug Radar",                         // Имя параметра
                false,                                        // По умолчанию false (спрятан)
                "Show the green onscreen diagnostic radar window with frame timing and pivot matrix telemetry."
            );

            ForceAbsoluteSkeletalReset = Config.Bind(
                "Debug Settings",                             // Имя вкладки/секции
                "Force Absolute Skeletal Reset",               // Имя параметра
                true,                                         // По умолчанию true
                "True: Forces all unused bones to reset to zero (prevents pose dependency). False: Enables original additive behavior from Aedenthorn."
            );


        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
