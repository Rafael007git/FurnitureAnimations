using System.Collections.Generic;

namespace FurnitureAnimationsMod
{
    public class PoseAnimationData
    {
        public string name;
        public float rate = 0.0333f; // Скорость смены дельт в секундах
        public bool loop;
        public bool reverse;
        public float[] startPos = new float[3];
        public float[] startRot = new float[3];
        public Dictionary<string, BoneDelta> boneStartDict = new Dictionary<string, BoneDelta>();
        public List<PoseAnimationDelta> deltas = new List<PoseAnimationDelta>();
    }

    public class PoseAnimationDelta
    {
        public int frames; // Общее число кадров интерполяции внутри дельты
        public float[] endPosDelta = new float[3];
        public float[] endRotDelta = new float[3];
        public Dictionary<string, BoneDelta> boneDatas = new Dictionary<string, BoneDelta>();
    }

    public class BoneDelta
    {
        public float[] startPos = new float[3];
        public float[] endPos = new float[3];
        public float[] startRot = new float[3];
        public float[] endRot = new float[3];

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
    }
}
