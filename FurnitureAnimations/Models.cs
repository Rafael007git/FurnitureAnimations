using System;
using System.Collections.Generic;

namespace FurnitureAnimationsMod
{
    [Serializable]
    public class Vector3Data
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public class CameraData
    {
        public string Name;
        public Vector3Data pos;
        public Vector3Data rot;
    }

    [Serializable]
    public class PoseData
    {
        public string DisplayName;
        public string Type;
        public string ControllerName;
        public string JsonFileName;
        public Vector3Data LocPosition;
        public Vector3Data LocRotation;
        public List<CameraData> Cameras;
    }

    // --- ЭТАП 1: КОМПАКТНЫЙ КЛАСС ХРАНЕНИЯ НАСТРОЕК ТЕМПА И СГЛАЖИВАНИЯ (По ТЗ: 150% и Linear) --- 🎵🔥
    [Serializable]
    public class PlaybackSettingsData
    {
        public float Speed = 1.5f;                      // По вашему ТЗ по умолчанию speed = 150%
        public EaseMode EaseMode = EaseMode.Linear;     // По вашему ТЗ по умолчанию сглаживание = linear
    }

    [Serializable]
    public class FurnitureConfig
    {
        public string FurniturePrefabName;
        public List<PoseData> InteractionPoses;
        public List<CameraData> CustomCameras = new List<CameraData>();

        // --- НАШЕ НОВОЕ ДОПОЛНЕНИЕ ДЛЯ ОЗУ (ЭТАП 1) --- 🧠⚡
        // Ключ строки будет иметь формат: "ИмяАнимации_ИмяАудио" (например, "danceBachata_danceBachata-01")
        // [Newtonsoft.Json.JsonIgnore] гарантирует, что на Этапе 1 мы работаем СТРОГО в памяти и диск не трогаем!
        [Newtonsoft.Json.JsonIgnore]
        public Dictionary<string, PlaybackSettingsData> RuntimePlaybackMemory = new Dictionary<string, PlaybackSettingsData>(StringComparer.OrdinalIgnoreCase);
    }

    // Модель для распаковки бинарного слепка кости/света из CustomAnimations
    [Serializable]
    public class BakedElementData
    {
        public string type;
        public bool enabled;
        public float intensity;
        public float range;
        public Vector3Data rot;
        public Vector3Data pos;
        public ColorData color;
    }

    [Serializable]
    public class ColorData
    {
        public float r;
        public float g;
        public float b;
    }
}
