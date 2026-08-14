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

                // 2. Базовая именная папка мода
                PluginDirectory = Path.Combine(pluginsFolder, "FurnitureAnimations");

                // 3. АРХИТЕКТУРНЫЙ ФИКС ПУТЕЙ СЕЙВА:
                PrefabsConfigPath = Path.Combine(PluginDirectory, "FurnitureConfigs");
                CustomAnimsPath = Path.Combine(PluginDirectory, "CustomAnimations");
                IconsPath = Path.Combine(CustomAnimsPath, "Icons"); // <-- Папка уехала в CustomAnimations\Icons

                // 4. Создаем директории на диске, если их нет
                if (!Directory.Exists(PrefabsConfigPath)) Directory.CreateDirectory(PrefabsConfigPath);
                if (!Directory.Exists(CustomAnimsPath)) Directory.CreateDirectory(CustomAnimsPath);
                if (!Directory.Exists(IconsPath)) Directory.CreateDirectory(IconsPath);

                // Запускаем диагностику: смотрим, какие моды внедрились в мебель!
                CheckDoPoseInterceptors();

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
                        // --- НАШ ХИРУРГИЧЕСКИЙ ПРЕДОХРАНИТЕЛЬ ДЛЯ СТАРЫХ JSON ---
                        if (config.CustomCameras == null)
                        {
                            config.CustomCameras = new List<CameraData>();
                        }
                        // --------------------------------------------------------

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

        public static void CheckDoPoseInterceptors()
        {
            try
            {
                var originalMethod = typeof(Furniture).GetMethod("DoPose", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (originalMethod == null) return;

                var patchInfo = HarmonyLib.Harmony.GetPatchInfo(originalMethod);
                if (patchInfo == null)
                {
                    Plugin.Log.LogWarning("[SDK_Registry] Метод Furniture.DoPose абсолютно чист от чужих патчей.");
                    return;
                }

                Plugin.Log.LogError($"[SDK_Registry] === КАРТА ВОЙНЫ МОДОВ ДЛЯ Furniture.DoPose ===");

                // ИСПРАВЛЕНО: Используем правильные свойства PatchMethod и owner из актуального HarmonyЛиб
                if (patchInfo.Prefixes != null)
                {
                    foreach (var p in patchInfo.Prefixes)
                    {
                        string ownerName = p.owner ?? "UnknownMod";
                        string methodName = p.PatchMethod != null ? p.PatchMethod.Name : "UnknownMethod";
                        Plugin.Log.LogError($" -> КРИТИЧЕСКИЙ ПЕРЕХВАТ (Prefix): Мод: {ownerName} | Метод: {methodName}");
                    }
                }
                if (patchInfo.Postfixes != null)
                {
                    foreach (var p in patchInfo.Postfixes)
                    {
                        string ownerName = p.owner ?? "UnknownMod";
                        string methodName = p.PatchMethod != null ? p.PatchMethod.Name : "UnknownMethod";
                        Plugin.Log.LogWarning($" -> Постфикс мода: {ownerName} | Метод: {methodName}");
                    }
                }
                Plugin.Log.LogError("=================================================");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Ошибка сканера: {ex.Message}");
            }
        }

        public static int GetNextVacantCameraNumber(Furniture furniture, FurnitureConfig config)
        {
            if (furniture == null || config == null) return -1;

            // 1. ОПРЕДЕЛЯЕМ ЛИМИТ (Пункт 1 ТЗ)
            // Очищаем имя от рантайм-суффиксов игры
            string furnitureName = furniture.name.Replace("(Clone)", "").Trim();

            // Мягкий и надежный способ определить ваниль: если имя совпадает со стандартными префабами игры 
            // (например, "Chair", "Bed", "Sofa") или в названии конфига нет тегов кастома.
            // Вы можете дополнить это условие под ваш Code Style проекта.
            bool isVanilla = furnitureName.Equals("Chair", StringComparison.OrdinalIgnoreCase) ||
                             furnitureName.Equals("Bed", StringComparison.OrdinalIgnoreCase) ||
                             furnitureName.Equals("Sofa", StringComparison.OrdinalIgnoreCase) ||
                             !furnitureName.Contains(" "); // Ванильные префабы обычно идут одним словом

            int maxLimit = isVanilla ? 2 : 5;

            // Страховка от NullReferenceException
            if (config.CustomCameras == null) config.CustomCameras = new List<CameraData>();

            // Если текущее количество камер уже уперлось в потолок лимита — вакантных мест нет
            if (config.CustomCameras.Count >= maxLimit)
            {
                Plugin.Log.LogInfo($"[SDK_Camera] Мебель '{furnitureName}' достигла лимита камер ({maxLimit}). Добавление заблокировано.");
                return -1;
            }

            // 2. ИЩЕМ НАИМЕНЬШИЙ ВАКАНТНЫЙ НОМЕР (Пункт 1 ТЗ)
            // Идем циклом от 1 до максимального лимита
            for (int i = 1; i <= maxLimit; i++)
            {
                string expectedName = $"Custom camera {i}";
                bool isNameTaken = false;

                // Проверяем, не занято ли это имя кем-то в списке
                foreach (var cam in config.CustomCameras)
                {
                    if (cam != null && cam.Name == expectedName)
                    {
                        isNameTaken = true;
                        break;
                    }
                }

                // Если имя свободно — это и есть наш наименьший свободный индекс!
                if (!isNameTaken)
                {
                    return i;
                }
            }

            return -1; // На всякий случай, если что-то пошло не так
        }

        // =========================================================================
        // ЭТАП 1: LAZY-ИНЪЕКЦИЯ ДЛЯ УПРАВЛЕНИЯ СВЯЗКАМИ АНИМАЦИЯ+АУДИО В ОЗУ 🧠⚡ 
        // =========================================================================
        public static void UpdateRuntimePlaybackMemory(string furnitureName, string animationName, string trackName, float speed, EaseMode easeMode)
        {
            if (string.IsNullOrEmpty(furnitureName) || string.IsNullOrEmpty(animationName)) return;

            string cleanFurnName = furnitureName.Replace("(Clone)", "").Trim();

            // Находим нужный конфиг мебели в ОЗУ-словаре мода
            if (LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config) && config != null)
            {
                // На всякий случай страхуем словарь от NullReferenceException
                if (config.RuntimePlaybackMemory == null)
                {
                    config.RuntimePlaybackMemory = new Dictionary<string, PlaybackSettingsData>(StringComparer.OrdinalIgnoreCase);
                }

                // Ключ трека по ТЗ: если пусто — кейс "noAudio", иначе — чистое имя файла
                string trackKey = string.IsNullOrEmpty(trackName) ? "noAudio" : Path.GetFileName(trackName);

                // Собираем наш монолитный составной ключ сессии
                string sessionKey = $"{animationName}_{trackKey}";

                // Ленивая инициализация: если такой пары в ОЗУ еще нет — создаем её с дефолтами
                if (!config.RuntimePlaybackMemory.TryGetValue(sessionKey, out PlaybackSettingsData settings) || settings == null)
                {
                    settings = new PlaybackSettingsData();
                    config.RuntimePlaybackMemory[sessionKey] = settings;
                }

                // Мгновенно перезаписываем параметры в ОЗУ сессии интерактива!
                settings.Speed = speed;
                settings.EaseMode = easeMode;

                Plugin.Log.LogInfo($"[ОЗУ_МЕНЕДЖЕР] Зафиксировано в ОЗУ для [{sessionKey}]: Скорость={speed * 100}%, Сглаживание={easeMode}");
            }
        }

        public static void InitializeRuntimeMemoryForFurniture(Furniture furniture)
        {
            if (furniture == null) return;

            string cleanFurnName = furniture.name.Replace("(Clone)", "").Trim();

            // Находим конфиг мебели в глобальном ОЗУ-реестре
            if (!LoadedConfigs.TryGetValue(cleanFurnName, out FurnitureConfig config) || config == null)
            {
                Plugin.Log.LogWarning($"[RAM_Init] Конфиг для мебели {cleanFurnName} не найден в LoadedConfigs. Пропуск.");
                return;
            }

            // Лениво создаем словарь памяти, если его еще нет
            if (config.RuntimePlaybackMemory == null)
            {
                config.RuntimePlaybackMemory = new Dictionary<string, PlaybackSettingsData>(StringComparer.OrdinalIgnoreCase);
            }

            // Шаг 1. Сканируем вообще все аудиофайлы в папке Audio (как в методе ScanAndPlay)
            List<string> allAudioFiles = new List<string> { "noAudio" }; // noAudio доступен всегда по ТЗ
            string audioFolder = Path.Combine(PluginDirectory, "Audio");

            if (Directory.Exists(audioFolder))
            {
                string[] files = Directory.GetFiles(audioFolder, "*.*");
                foreach (string file in files)
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (ext == ".wav" || ext == ".mp3" || ext == ".ogg")
                    {
                        string pureName = Path.GetFileName(file);
                        if (!allAudioFiles.Contains(pureName))
                        {
                            allAudioFiles.Add(pureName);
                        }
                    }
                }
            }

            // Шаг 2. Перебираем позы мебели и отбираем ТОЛЬКО динамические анимации
            if (config.InteractionPoses == null || config.InteractionPoses.Count == 0) return;

            int newlyCreatedPairs = 0;

            foreach (var pose in config.InteractionPoses)
            {
                if (pose == null) continue;

                // Фикс Бага 2: Жестко отсекаем статичные позы по полю Type!
                if (pose.Type != "PoseAnimationsMod")
                {
                    continue;
                }

                string animName = pose.ControllerName;
                if (string.IsNullOrEmpty(animName)) continue;

                // Шаг 3. Строим пары Анимация -> Аудио по правилам ТЗ
                foreach (string audioTrack in allAudioFiles)
                {
                    bool isIdle = animName.ToLower().Contains("idle");

                    // Правило ТЗ: Для "idle" пишем только связку с "noAudio"
                    if (isIdle && audioTrack != "noAudio")
                    {
                        continue;
                    }

                    // Правило ТЗ: Танцевальные анимации сочетаются с noAudio и ТОЛЬКО со своими треками
                    if (!isIdle && audioTrack != "noAudio" && !audioTrack.StartsWith(animName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Формируем монолитный сессионный ключ
                    string sessionKey = $"{animName}_{audioTrack}";

                    // Если пара еще не была создана в памяти, инициализируем ее дефолтами по ТЗ (150%, Linear)
                    if (!config.RuntimePlaybackMemory.ContainsKey(sessionKey))
                    {
                        config.RuntimePlaybackMemory[sessionKey] = new PlaybackSettingsData
                        {
                            Speed = 1.5f,       // 150% по умолчанию
                            EaseMode = EaseMode.Linear // Linear по умолчанию
                        };
                        newlyCreatedPairs++;

                        Plugin.Log.LogInfo($"[RAM_State_New] -> Сгенерирована пара для '{cleanFurnName}': {sessionKey} (Дефолт: 150%, Linear)");
                    }
                }
            }

            Plugin.Log.LogInfo($"[RAM_Init] Успешная подготовка ОЗУ для '{cleanFurnName}'. " +
                               $"Всего пар в памяти: {config.RuntimePlaybackMemory.Count} (Создано новых: {newlyCreatedPairs})");
        }
    }
}
