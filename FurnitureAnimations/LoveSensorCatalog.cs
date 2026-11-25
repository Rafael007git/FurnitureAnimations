using System.Collections.Generic;

namespace FurnitureAnimations
{
    public struct KinematicZonePair
    {
        public string SourceZone;
        public string TargetZone;

        public KinematicZonePair(string sourceZone, string targetZone)
        {
            SourceZone = sourceZone;
            TargetZone = targetZone;
        }
    }

    public static class LoveSensorCatalog
    {
        // Тестовый набор пар (легко дополнять новыми строками без правки трекера)
        public static readonly List<KinematicZonePair> ActivePairs = new List<KinematicZonePair>
        {
            new KinematicZonePair("Mouth", "Mouth"),
            new KinematicZonePair("Mouth", "Vagina"),
            new KinematicZonePair("Hands", "Pectorals"),
            new KinematicZonePair("Hands", "Thighs")
        };
    }
}
