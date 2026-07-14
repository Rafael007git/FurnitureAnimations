using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace FurnitureAnimationsMod
{
    // Проверь, чтобы имя исполняемого файла игры было написано без ошибок
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("She Will Punish Them.exe")]
    public class Plugin : BaseUnityPlugin // <-- КРИТИЧНО: Класс ОБЯЗАТЕЛЬНО должен быть public и наследоваться от BaseUnityPlugin
    {
        public const string PluginGuid = "com.lorifel007.furnitureposefix";
        public const string PluginName = "Furniture Animations Mod";
        public const string PluginVersion = "0.0.3";

        public static ManualLogSource Log;
        private Harmony harmony;

        // BepInEx ищет именно метод Awake без параметров
        private void Awake()
        {
            Log = Logger;
            Log.LogWarning("[FurnitureMod] Старт инициализации плагина...");

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
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
