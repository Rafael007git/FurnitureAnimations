using System;
using System.Collections.Generic;

namespace FurnitureAnimationsMod
{
    [Serializable]
    public class PoseAnimationData
    {
        public string name;
        public float rate = 0.025f;
        public bool loop;
        public bool reverse;
        public EaseMode? easeMode = null;

        // Optional props in hands (prefab/weapon names)
        public string rightHandProp = null;
        public string leftHandProp = null;

        public float[] startPos = new float[3];
        public float[] startRot = new float[3];
        public Dictionary<string, BoneDelta> boneStartDict = new Dictionary<string, BoneDelta>();
        public List<PoseAnimationDelta> deltas = new List<PoseAnimationDelta>();
    }

    [Serializable]
    public class PoseAnimationDelta
    {
        public int frames;
        public EaseMode? easeMode = null;
        public float[] bezier = null; // [x1, y1, x2, y2] - overrides easeMode if present
        public float[] endPosDelta = new float[3];
        public float[] endRotDelta = new float[3];
        public Dictionary<string, BoneDelta> boneDatas = new Dictionary<string, BoneDelta>();
    }

    [Serializable]
    public class BoneDelta
    {
        public float[] startPos = new float[3];
        public float[] endPos = new float[3];
        public float[] startRot = new float[4];
        public float[] endRot = new float[4];
        public float[] startScale = null;
        public float[] endScale = null;

        public BoneDelta() { }

        public BoneDelta(float[] endPos, float[] endRot)
        {
            this.endPos = endPos;
            this.endRot = endRot;
        }

        public BoneDelta(float[] startPos, float[] endPos, float[] startRot, float[] endRot)
        {
            this.startPos = startPos;
            this.endPos = endPos;
            this.startRot = startRot;
            this.endRot = endRot;
        }

        public BoneDelta(float[] startPos, float[] endPos, float[] startRot, float[] endRot, float[] startScale, float[] endScale)
        {
            this.startPos = startPos;
            this.endPos = endPos;
            this.startRot = startRot;
            this.endRot = endRot;
            this.startScale = startScale;
            this.endScale = endScale;
        }
    }
}
