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
}
