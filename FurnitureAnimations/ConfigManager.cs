using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using Newtonsoft.Json;

namespace FurnitureAnimationsMod
{
    public static class ConfigManager
    {
        // Пути к локальным папкам в BepInEx\config
        public static string BaseDataPath { get; private set; }
        public static string PrefabsConfigPath { get; private set; }
        public static string CustomAnimsPath { get; private set; }

        // Глобальная база данных: Ключ = GUID префаба, Значение = данные инжектора
        public static Dictionary<string, FurnitureConfig> LoadedConfigs = new Dictionary<string, FurnitureConfig>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            try
            {
                // Безопасный способ получить путь к папке BepInEx\config без использования класса Paths
                string gameDir = AppDomain.CurrentDomain.BaseDirectory;
                BaseDataPath = Path.GetFullPath(Path.Combine(gameDir, "BepInEx", "config", "FurnitureAnimationsData"));

                PrefabsConfigPath = Path.Combine(BaseDataPath, "FurnitureConfigs");
                CustomAnimsPath = Path.Combine(BaseDataPath, "CustomAnimations");

                // Проверяем и создаем структуру папок, если её нет
                if (!Directory.Exists(BaseDataPath)) Directory.CreateDirectory(BaseDataPath);
                if (!Directory.Exists(PrefabsConfigPath)) Directory.CreateDirectory(PrefabsConfigPath);
                if (!Directory.Exists(CustomAnimsPath)) Directory.CreateDirectory(CustomAnimsPath);

                Plugin.Log.LogInfo("[ConfigManager] Структура папок в BepInEx\\config успешно проверена/создана.");

                // Запускаем полное сканирование
                ReloadAllConfigs();
            }
            catch (Exception ex)
            {
                // Используем стандартный вывод Unity, если логгер BepInEx еще не готов
                UnityEngine.Debug.LogError($"[ConfigManager] Критическая ошибка инициализации: {ex.Message}");
            }
        }

        public static void ReloadAllConfigs()
        {
            LoadedConfigs.Clear();

            // 1. Сканируем локальную папку BepInEx\config\...\FurnitureConfigs
            ScanDirectoryForConfigs(PrefabsConfigPath);

            // 2. Сканируем папки Steam Workshop
            ScanWorkshopDirectory();

            Plugin.Log.LogWarning($"[ConfigManager] Сканирование завершено. Успешно загружено справочников мебели: {LoadedConfigs.Count}");
        }

        // ИСПРАВЛЕНО: Добавлен static, bool изменен на void
        private static void ScanDirectoryForConfigs(string directoryPath)
        {
            if (!Directory.Exists(directoryPath)) return;

            string[] jsonFiles = Directory.GetFiles(directoryPath, "*.json", SearchOption.AllDirectories);
            foreach (string file in jsonFiles)
            {
                try
                {
                    string jsonText = File.ReadAllText(file);
                    FurnitureConfig config = JsonConvert.DeserializeObject<FurnitureConfig>(jsonText);

                    if (config != null && !string.IsNullOrEmpty(config.FurniturePrefabName))
                    {
                        LoadedConfigs[config.FurniturePrefabName] = config;
                        Plugin.Log.LogInfo($"[ConfigManager] Загружен локальный конфиг для префаба: {config.FurniturePrefabName} (Файл: {Path.GetFileName(file)})");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ConfigManager] Ошибка чтения JSON файла {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        // ИСПРАВЛЕНО: Добавлен static, bool изменен на void
        private static void ScanWorkshopDirectory()
        {
            try
            {
                string gameDir = AppDomain.CurrentDomain.BaseDirectory;
                string workshopBaseDir = Path.GetFullPath(Path.Combine(gameDir, "..", "..", "workshop", "content", "1433420"));

                if (!Directory.Exists(workshopBaseDir))
                {
                    Plugin.Log.LogInfo("[ConfigManager] Папка Steam Workshop не найдена (возможно, пиратская версия или игра запущена не из Steam).");
                    return;
                }

                Plugin.Log.LogInfo($"[ConfigManager] Найдена папка Воркшопа: {workshopBaseDir}. Ищем конфиги...");

                string[] modDirs = Directory.GetDirectories(workshopBaseDir);
                foreach (string modDir in modDirs)
                {
                    string targetConfig = Path.Combine(modDir, "FurnitureConfig.json");
                    if (File.Exists(targetConfig))
                    {
                        try
                        {
                            string jsonText = File.ReadAllText(targetConfig);
                            FurnitureConfig config = JsonConvert.DeserializeObject<FurnitureConfig>(jsonText);

                            if (config != null && !string.IsNullOrEmpty(config.FurniturePrefabName))
                            {
                                LoadedConfigs[config.FurniturePrefabName] = config;
                                Plugin.Log.LogWarning($"[ConfigManager] [WORKSHOP] Успешно подхвачен конфиг для: {config.FurniturePrefabName} из папки мода {Path.GetFileName(modDir)}!");
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[ConfigManager] Ошибка чтения Воркшоп-конфига в {Path.GetFileName(modDir)}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ConfigManager] Ошибка при сканировании папок Воркшопа: {ex.Message}");
            }
        }
    }
}
