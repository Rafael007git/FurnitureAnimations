using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace FurnitureAnimationsMod
{
    public static class ConfigManager
    {
        public static string PluginDirectory { get; private set; }
        public static string PrefabsConfigPath { get; private set; }
        public static string CustomAnimsPath { get; private set; }
        public static string IconsPath { get; private set; }

        public static Dictionary<string, FurnitureConfig> LoadedConfigs = new Dictionary<string, FurnitureConfig>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            try
            {
                // 1. Получаем путь к папке BepInEx\plugins\, где лежит наш .dll
                string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string pluginsFolder = Path.GetDirectoryName(dllPath);

                // 2. ЖЕСТКИЙ ФИКС: Устанавливаем базовую именную папку мода
                PluginDirectory = Path.Combine(pluginsFolder, "FurnitureAnimations");

                // 3. Формируем структуру подпапок строго по твоей спецификации
                PrefabsConfigPath = Path.Combine(PluginDirectory, "FurnitureConfigs");
                CustomAnimsPath = Path.Combine(PluginDirectory, "CustomAnimations");
                IconsPath = Path.Combine(PrefabsConfigPath, "Icons"); // Папка Icons переехала внутрь FurnitureConfigs!

                // 4. Создаем директории на диске, если их нет
                if (!Directory.Exists(PrefabsConfigPath)) Directory.CreateDirectory(PrefabsConfigPath);
                if (!Directory.Exists(CustomAnimsPath)) Directory.CreateDirectory(CustomAnimsPath);
                if (!Directory.Exists(IconsPath)) Directory.CreateDirectory(IconsPath);

                Plugin.Log.LogWarning($"[ConfigManager] Истинные пути SDK мода установлены: {PluginDirectory}");

                LoadAllConfigs();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ConfigManager] Критическая ошибка инициализации путей: {ex.Message}");
            }
        }

        private static void LoadAllConfigs()
        {
            LoadedConfigs.Clear();
            // Считываем ВСЕ файлы .json в папке конфигураций мебели интерактивов
            string[] files = Directory.GetFiles(PrefabsConfigPath, "*.json");

            foreach (string file in files)
            {
                try
                {
                    string jsonContent = File.ReadAllText(file);
                    FurnitureConfig config = Newtonsoft.Json.JsonConvert.DeserializeObject<FurnitureConfig>(jsonContent);

                    if (config != null && !string.IsNullOrEmpty(config.FurniturePrefabName))
                    {
                        LoadedConfigs[config.FurniturePrefabName] = config;
                        Plugin.Log.LogInfo($"[ConfigManager] Загружен конфиг мебели: {config.FurniturePrefabName} ({config.InteractionPoses?.Count ?? 0} поз)");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ConfigManager] Ошибка чтения файла {Path.GetFileName(file)}: {ex.Message}");
                }
            }
            Plugin.Log.LogWarning($"[ConfigManager] Всего проиндексировано префабов мебели в памяти: {LoadedConfigs.Count}");
        }
    }
}
