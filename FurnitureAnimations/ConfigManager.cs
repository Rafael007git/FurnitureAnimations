using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace FurnitureAnimationsMod
{
    public static class ConfigManager
    {
        // Глобальные пути, перенесенные внутрь папки плагина в plugins!
        public static string PluginDirectory { get; private set; }
        public static string PrefabsConfigPath { get; private set; }
        public static string CustomAnimsPath { get; private set; }
        public static string IconsPath { get; private set; }

        public static Dictionary<string, FurnitureConfig> LoadedConfigs = new Dictionary<string, FurnitureConfig>(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            try
            {
                // 1. Автоматически определяем, где лежит наша скомпилированная .dll
                string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                PluginDirectory = Path.GetDirectoryName(dllPath);

                // 2. Формируем чистую структуру локальных папок мода
                PrefabsConfigPath = Path.Combine(PluginDirectory, "FurnitureConfigs");
                CustomAnimsPath = Path.Combine(PluginDirectory, "CustomAnimations");
                IconsPath = Path.Combine(PluginDirectory, "Icons");

                // 3. Создаем директории на диске, если их еще нет
                if (!Directory.Exists(PrefabsConfigPath)) Directory.CreateDirectory(PrefabsConfigPath);
                if (!Directory.Exists(CustomAnimsPath)) Directory.CreateDirectory(CustomAnimsPath);
                if (!Directory.Exists(IconsPath)) Directory.CreateDirectory(IconsPath);

                Plugin.Log.LogInfo($"[ConfigManager] Базовая директория мода установлена: {PluginDirectory}");

                // 4. Запускаем сканирование папки с конфигами мебели
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
            // Считываем абсолютно все файлы .json в папке конфигураций мебели
            string[] files = Directory.GetFiles(PrefabsConfigPath, "*.json");

            foreach (string file in files)
            {
                try
                {
                    string jsonContent = File.ReadAllText(file);
                    FurnitureConfig config = Newtonsoft.Json.JsonConvert.DeserializeObject<FurnitureConfig>(jsonContent);

                    // Проверяем, что это действительно наш конфиг мебели, а не случайный файл
                    if (config != null && !string.IsNullOrEmpty(config.FurniturePrefabName))
                    {
                        LoadedConfigs[config.FurniturePrefabName] = config;
                        Plugin.Log.LogInfo($"[ConfigManager] Успешно загружен рантайм-конфиг мебели: {config.FurniturePrefabName} ({config.InteractionPoses?.Count ?? 0} поз)");
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
