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

    [Serializable]
    public class FurnitureConfig
    {
        public string FurniturePrefabName;
        public List<PoseData> InteractionPoses;
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
