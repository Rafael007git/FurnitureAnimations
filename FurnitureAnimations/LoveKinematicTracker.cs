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
            public Transform TongueTip; // tongue03
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

        private static readonly List<CharacterCustomization> _freePoseBuffer =
            new List<CharacterCustomization>();

        private static float _nextFurnitureSampleTime;
        private static float _nextFreePoseSampleTime;
        private const float SampleInterval = 0.05f; // 20 Гц
        private const float MaxScanDistance = 0.40f;

        public static void Tick(Dictionary<Furniture, List<FurnitureMultiSeatManager.SeatSlot>> furnitureSlots)
        {
            if (Time.time < _nextFurnitureSampleTime) return;
            float dt = Mathf.Max(0.01f, Time.time - (_nextFurnitureSampleTime - SampleInterval));
            _nextFurnitureSampleTime = Time.time + SampleInterval;

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

        public static void TickFreePose()
        {
            if (Global.code == null || Global.code.uiFreePose == null || !Global.code.uiFreePose.gameObject.activeInHierarchy)
                return;

            if (Time.time < _nextFreePoseSampleTime) return;
            float dt = Mathf.Max(0.01f, Time.time - (_nextFreePoseSampleTime - SampleInterval));
            _nextFreePoseSampleTime = Time.time + SampleInterval;

            _freePoseBuffer.Clear();
            var uiFreePose = Global.code.uiFreePose;

            if (uiFreePose.characters != null && uiFreePose.characters.items != null)
            {
                for (int i = 0; i < uiFreePose.characters.items.Count; i++)
                {
                    var tr = uiFreePose.characters.items[i] as Transform;
                    if (tr == null || !tr.gameObject.activeInHierarchy) continue;
                    var cc = tr.GetComponent<CharacterCustomization>();
                    if (cc != null && !_freePoseBuffer.Contains(cc))
                        _freePoseBuffer.Add(cc);
                }
            }

            if (uiFreePose.selectedCharacter != null)
            {
                var selCc = uiFreePose.selectedCharacter.GetComponent<CharacterCustomization>();
                if (selCc != null && !_freePoseBuffer.Contains(selCc))
                    _freePoseBuffer.Add(selCc);
            }

            if (_freePoseBuffer.Count < 2) return;

            for (int i = 0; i < _freePoseBuffer.Count; i++)
            {
                var srcChar = _freePoseBuffer[i];
                var srcRig = GetOrBuildRig(srcChar);

                for (int j = 0; j < _freePoseBuffer.Count; j++)
                {
                    if (i == j) continue;
                    var tgtChar = _freePoseBuffer[j];
                    var tgtRig = GetOrBuildRig(tgtChar);

                    EvaluatePair(srcChar, srcRig, tgtChar, tgtRig, dt);
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
                    return r.TongueTip != null ? (Vector3?)r.TongueTip.position : null;

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
                    return r.Vagina != null ? (Vector3?)r.Vagina.position : null;

                case "Clitoris":
                    return r.Clitoris != null ? (Vector3?)r.Clitoris.position : null;

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
            if (a == null || b == null) return null;
            if (!p.HasValue) return (a.position + b.position) * 0.5f;

            Vector3 ab = b.position - a.position;
            float sqrLen = ab.sqrMagnitude;
            if (sqrLen < 1e-6f) return a.position;

            float t = Mathf.Clamp01(Vector3.Dot(p.Value - a.position, ab) / sqrLen);
            return a.position + ab * t;
        }

        private static CharacterRigCache GetOrBuildRig(CharacterCustomization cc)
        {
            if (_rigCache.TryGetValue(cc, out var cached) && cached.Pelvis != null)
                return cached;

            var rig = new CharacterRigCache();
            Transform liveHip = null;

            // 1. Берём живые кости исключительно из нативного массива cc.bonesNeedSave
            if (cc.bonesNeedSave != null)
            {
                foreach (Transform t in cc.bonesNeedSave)
                {
                    if (t == null) continue;
                    string n = t.name;
                    if (n.EndsWith("hip")) liveHip = t;
                    else if (n.EndsWith("tongue03")) rig.TongueTip = t;
                    else if (n.EndsWith("lMid2")) rig.LeftHand = t;
                    else if (n.EndsWith("rMid2")) rig.RightHand = t;
                    else if (n.EndsWith("lToe")) rig.LeftFoot = t;
                    else if (n.EndsWith("rToe")) rig.RightFoot = t;
                    else if (n.EndsWith("pelvis")) rig.Pelvis = t;
                    else if (n.EndsWith("lPectoral")) rig.LeftPectoral = t;
                    else if (n.EndsWith("rPectoral")) rig.RightPectoral = t;
                    else if (n.EndsWith("lThighBend")) rig.LeftThighBend = t;
                    else if (n.EndsWith("lShin")) rig.LeftShin = t;
                    else if (n.EndsWith("rThighBend")) rig.RightThighBend = t;
                    else if (n.EndsWith("rShin")) rig.RightShin = t;
                }
            }
            // 2. Интимные кости (Vagina / Clitoris) ищем строго внутри живого liveHip
            Transform searchRoot = liveHip != null ? liveHip : (cc.transform.childCount > 0 ? cc.transform.GetChild(0) : cc.transform);
            foreach (Transform t in searchRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string n = t.name;
                if (rig.TongueTip == null && n.EndsWith("tongue03")) rig.TongueTip = t;
                else if (rig.Vagina == null && n.EndsWith("Vagina")) rig.Vagina = t;
                else if (rig.Clitoris == null && n.EndsWith("Clitoris")) rig.Clitoris = t;
                else if (rig.Pelvis == null && n.EndsWith("pelvis")) rig.Pelvis = t;
                else if (rig.LeftHand == null && n.EndsWith("lMid2")) rig.LeftHand = t;
                else if (rig.RightHand == null && n.EndsWith("rMid2")) rig.RightHand = t;
                else if (rig.LeftPectoral == null && n.EndsWith("lPectoral")) rig.LeftPectoral = t;
                else if (rig.RightPectoral == null && n.EndsWith("rPectoral")) rig.RightPectoral = t;
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