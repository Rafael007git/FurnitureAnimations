using System;
using System.Collections.Generic;
using UnityEngine;

namespace FurnitureAnimations
{
    public static class LoveKinematicBridge
    {
        public static event Action<CharacterCustomization, string, CharacterCustomization, string, float, float> OnProximitySample;

        internal static void RaiseSample(
            CharacterCustomization source,
            string sourceZone,
            CharacterCustomization target,
            string targetZone,
            float distance,
            float approachSpeed)
        {
            OnProximitySample?.Invoke(source, sourceZone, target, targetZone, distance, approachSpeed);
        }
    }

    public static class LoveKinematicTracker
    {
        private class CharacterRigCache
        {
            public Transform TongueTip; // tongue03 — единый эпицентр для рта/языка
            public Transform Head;      // резерв на случай нестандартного рига

            public Transform LeftHand, RightHand;
            public Transform LeftFoot, RightFoot;

            public Transform Vagina, Clitoris, Pelvis;
            public Transform LeftPectoral, RightPectoral;
            public Transform LeftThighBend, LeftShin, RightThighBend, RightShin;
        }

        private static readonly Dictionary<CharacterCustomization, CharacterRigCache> _rigCache =
            new Dictionary<CharacterCustomization, CharacterRigCache>();

        private static readonly Dictionary<string, float> _prevDistances =
            new Dictionary<string, float>();

        private static float _nextSampleTime;
        private const float SampleInterval = 0.05f; // 20 Гц
        private const float MaxScanDistance = 0.40f;

        public static void Tick(Dictionary<Furniture, List<FurnitureMultiSeatManager.SeatSlot>> furnitureSlots)
        {
            if (Time.time < _nextSampleTime) return;
            float dt = Mathf.Max(0.01f, Time.time - (_nextSampleTime - SampleInterval));
            _nextSampleTime = Time.time + SampleInterval;

            foreach (var kvp in furnitureSlots)
            {
                var slots = kvp.Value;
                if (slots == null || slots.Count < 2) continue;

                for (int i = 0; i < slots.Count; i++)
                {
                    var srcChar = slots[i]?.Character;
                    if (srcChar == null) continue;
                    var srcRig = GetOrBuildRig(srcChar);

                    for (int j = 0; j < slots.Count; j++)
                    {
                        if (i == j) continue;
                        var tgtChar = slots[j]?.Character;
                        if (tgtChar == null) continue;
                        var tgtRig = GetOrBuildRig(tgtChar);

                        EvaluatePair(srcChar, srcRig, tgtChar, tgtRig, dt);
                    }
                }
            }
        }

        private static void EvaluatePair(
            CharacterCustomization src, CharacterRigCache srcRig,
            CharacterCustomization tgt, CharacterRigCache tgtRig,
            float dt)
        {
            for (int k = 0; k < LoveSensorCatalog.ActivePairs.Count; k++)
            {
                var pair = LoveSensorCatalog.ActivePairs[k];
                Vector3? prelimTgt = ResolveZonePos(tgtRig, pair.TargetZone, null);
                Vector3? srcPos = ResolveZonePos(srcRig, pair.SourceZone, prelimTgt);
                Vector3? tgtPos = ResolveZonePos(tgtRig, pair.TargetZone, srcPos);

                if (!srcPos.HasValue || !tgtPos.HasValue) continue;

                float dist = Vector3.Distance(srcPos.Value, tgtPos.Value);
                string key = $"{src.GetInstanceID()}_{pair.SourceZone}_{tgt.GetInstanceID()}_{pair.TargetZone}";

                float prevDist = _prevDistances.TryGetValue(key, out float d) ? d : dist;
                _prevDistances[key] = dist;

                if (dist <= MaxScanDistance || prevDist <= MaxScanDistance)
                {
                    float approachSpeed = (prevDist - dist) / dt;
                    LoveKinematicBridge.RaiseSample(src, pair.SourceZone, tgt, pair.TargetZone, dist, approachSpeed);
                }
            }
        }

        private static Vector3? ResolveZonePos(CharacterRigCache r, string zone, Vector3? refPos)
        {
            switch (zone)
            {
                case "Mouth":
                    if (r.TongueTip != null) return r.TongueTip.position;
                    return r.Head != null ? (Vector3?)(r.Head.position + r.Head.forward * 0.09f - r.Head.up * 0.04f) : null;

                case "Hands":
                    return PickNearest(
                        r.LeftHand != null ? (Vector3?)r.LeftHand.position : null,
                        r.RightHand != null ? (Vector3?)r.RightHand.position : null,
                        refPos);

                case "Feet":
                    return PickNearest(
                        r.LeftFoot != null ? (Vector3?)r.LeftFoot.position : null,
                        r.RightFoot != null ? (Vector3?)r.RightFoot.position : null,
                        refPos);

                case "Pectorals":
                    return PickNearest(
                        r.LeftPectoral != null ? (Vector3?)(r.LeftPectoral.position + r.LeftPectoral.forward * 0.05f) : null,
                        r.RightPectoral != null ? (Vector3?)(r.RightPectoral.position + r.RightPectoral.forward * 0.05f) : null,
                        refPos);

                case "Thighs":
                    return GetNearestThighPoint(r, refPos);

                case "Vagina":
                    if (r.Vagina != null) return r.Vagina.position;
                    return r.Pelvis != null ? (Vector3?)(r.Pelvis.position - r.Pelvis.up * 0.07f) : null;

                case "Clitoris":
                    if (r.Clitoris != null) return r.Clitoris.position;
                    goto case "Vagina";

                case "Pelvis":
                    return r.Pelvis != null ? (Vector3?)r.Pelvis.position : null;

                default:
                    return null;
            }
        }

        private static Vector3? GetNearestThighPoint(CharacterRigCache r, Vector3? refPos)
        {
            Vector3? l = ClosestPointOnSegment(r.LeftThighBend, r.LeftShin, refPos);
            Vector3? re = ClosestPointOnSegment(r.RightThighBend, r.RightShin, refPos);
            return PickNearest(l, re, refPos);
        }

        private static Vector3? ClosestPointOnSegment(Transform a, Transform b, Vector3? p)
        {
            if (a == null) return b != null ? (Vector3?)b.position : null;
            if (b == null || !p.HasValue) return a.position;

            Vector3 ab = b.position - a.position;
            float sqrLen = ab.sqrMagnitude;
            if (sqrLen < 1e-6f) return a.position;

            float t = Mathf.Clamp01(Vector3.Dot(p.Value - a.position, ab) / sqrLen);
            return a.position + ab * t;
        }

        private static CharacterRigCache GetOrBuildRig(CharacterCustomization cc)
        {
            if (_rigCache.TryGetValue(cc, out var cached) && (cached.TongueTip != null || cached.Head != null))
                return cached;

            var rig = new CharacterRigCache();
            foreach (var t in cc.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                switch (t.name)
                {
                    case "tongue03": rig.TongueTip = t; break;
                    case "head": rig.Head = t; break;

                    case "lMid2":
                    case "lHand": if (rig.LeftHand == null || t.name == "lMid2") rig.LeftHand = t; break;
                    case "rMid2":
                    case "rHand": if (rig.RightHand == null || t.name == "rMid2") rig.RightHand = t; break;

                    case "lToe":
                    case "lFoot": if (rig.LeftFoot == null || t.name == "lToe") rig.LeftFoot = t; break;
                    case "rToe":
                    case "rFoot": if (rig.RightFoot == null || t.name == "rToe") rig.RightFoot = t; break;

                    case "Vagina": rig.Vagina = t; break;
                    case "Clitoris": rig.Clitoris = t; break;
                    case "pelvis": rig.Pelvis = t; break;
                    case "lPectoral": rig.LeftPectoral = t; break;
                    case "rPectoral": rig.RightPectoral = t; break;

                    case "lThighBend": rig.LeftThighBend = t; break;
                    case "lShin": rig.LeftShin = t; break;
                    case "rThighBend": rig.RightThighBend = t; break;
                    case "rShin": rig.RightShin = t; break;
                }
            }

            _rigCache[cc] = rig;
            return rig;
        }

        private static Vector3? PickNearest(Vector3? a, Vector3? b, Vector3? refPos)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue || !refPos.HasValue) return a;
            return Vector3.SqrMagnitude(a.Value - refPos.Value) <= Vector3.SqrMagnitude(b.Value - refPos.Value) ? a : b;
        }
    }
}
