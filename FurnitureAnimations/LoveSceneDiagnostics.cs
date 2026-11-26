using FurnitureAnimationsMod;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FurnitureAnimations
{
    public static class LoveSceneDiagnostics
    {
        public static void CheckHotkey()
        {
            if (BepInEx.UnityInput.Current.GetKeyDown(KeyCode.F9))
            {
                DumpSceneCharactersSnapshot();
            }
            if (BepInEx.UnityInput.Current.GetKeyDown(KeyCode.F10))
            {
                DumpContactDistances();
            }
        }

        public static void DumpContactDistances()
        {
            var uiFreePose = Global.code != null ? Global.code.uiFreePose : null;
            if (uiFreePose == null || !uiFreePose.gameObject.activeInHierarchy) return;

            var chars = new List<CharacterCustomization>();
            if (uiFreePose.characters != null && uiFreePose.characters.items != null)
            {
                for (int i = 0; i < uiFreePose.characters.items.Count; i++)
                {
                    var tr = uiFreePose.characters.items[i] as Transform;
                    if (tr == null || !tr.gameObject.activeInHierarchy) continue;
                    var cc = tr.GetComponent<CharacterCustomization>();
                    if (cc != null && !chars.Contains(cc)) chars.Add(cc);
                }
            }

            Plugin.Log?.LogInfo($"=== [LoveCalibrate F10] Active chars: {chars.Count} ===");

            for (int i = 0; i < chars.Count; i++)
            {
                var src = chars[i];
                GetBones(src, out var sTongue, out var sLHand, out var sRHand, out var sLPec, out var sRPec, out var sVag, out var sPelvis);

                // Проверим заодно, есть ли дочерние трансформы у lPectoral (например, кость соска)
                if (sLPec != null && sLPec.childCount > 0)
                {
                    Plugin.Log?.LogInfo($"[LoveCalibrate] {src.characterName}.lPectoral child[0] = '{sLPec.GetChild(0).name}' (offset={Vector3.Distance(sLPec.position, sLPec.GetChild(0).position):F3}m)");
                }

                for (int j = 0; j < chars.Count; j++)
                {
                    if (i == j) continue;
                    var tgt = chars[j];
                    GetBones(tgt, out var tTongue, out _, out _, out var tLPec, out var tRPec, out var tVag, out var tPelvis);

                    float dMouthMouth = Dist(sTongue, tTongue);
                    float dMouthVag = Dist(sTongue, tVag);
                    float dLHandLPec = Dist(sLHand, tLPec);
                    float dLHandRPec = Dist(sLHand, tRPec);
                    float dRHandLPec = Dist(sRHand, tLPec);
                    float dRHandRPec = Dist(sRHand, tRPec);
                    float dLHandVag = Dist(sLHand, tVag);
                    float dRHandVag = Dist(sRHand, tVag);

                    Plugin.Log?.LogInfo(
                        $"[LoveCalibrate] {src.characterName} -> {tgt.characterName} | " +
                        $"Mouth->Mouth={dMouthMouth:F3}m | Mouth->Vag={dMouthVag:F3}m | " +
                        $"LHand->(LPec={dLHandLPec:F3}m, RPec={dLHandRPec:F3}m, Vag={dLHandVag:F3}m) | " +
                        $"RHand->(LPec={dRHandLPec:F3}m, RPec={dRHandRPec:F3}m, Vag={dRHandVag:F3}m)"
                    );
                }
            }
            Plugin.Log?.LogInfo("=== [LoveCalibrate F10] End ===");
        }

        private static void GetBones(
            CharacterCustomization cc,
            out Transform tongue, out Transform lHand, out Transform rHand,
            out Transform lPec, out Transform rPec, out Transform vag, out Transform pelvis)
        {
            tongue = lHand = rHand = lPec = rPec = vag = pelvis = null;
            Transform liveHip = null;

            if (cc.bonesNeedSave != null)
            {
                foreach (Transform t in cc.bonesNeedSave)
                {
                    if (t == null) continue;
                    string n = t.name;
                    if (n.EndsWith("hip")) liveHip = t;
                    else if (n.EndsWith("tongue03")) tongue = t;
                    else if (n.EndsWith("lMid2")) lHand = t;
                    else if (n.EndsWith("rMid2")) rHand = t;
                    else if (n.EndsWith("lPectoral")) lPec = t;
                    else if (n.EndsWith("rPectoral")) rPec = t;
                    else if (n.EndsWith("pelvis")) pelvis = t;
                }
            }

            Transform root = liveHip != null ? liveHip : (cc.transform.childCount > 0 ? cc.transform.GetChild(0) : cc.transform);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                string n = t.name;
                if (vag == null && n.EndsWith("Vagina")) vag = t;
                else if (tongue == null && n.EndsWith("tongue03")) tongue = t;
            }
        }

        private static float Dist(Transform a, Transform b) => (a != null && b != null) ? Vector3.Distance(a.position, b.position) : -1f;

        public static void DumpSceneCharactersSnapshot()
        {
            var cam = Global.code != null && Global.code.freeCamera != null
                ? Global.code.freeCamera.transform
                : (Camera.main != null ? Camera.main.transform : null);

            Vector3 camPos = cam != null ? cam.position : Vector3.zero;
            Plugin.Log?.LogInfo($"=== [LoveSnapshot] Camera Pos: {camPos:F2} ===");

            var allChars = UnityEngine.Object.FindObjectsOfType<CharacterCustomization>();
            Plugin.Log?.LogInfo($"[LoveSnapshot] Active CharacterCustomization count: {allChars.Length}");

            foreach (var cc in allChars)
            {
                if (cc == null) continue;

                Transform child0 = cc.transform.childCount > 0 ? cc.transform.GetChild(0) : null;
                Transform hip = null, tongue = null, head = null, lHand = null, lPec = null, pelvis = null, vagina = null;
                foreach (var t in cc.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;
                    string n = t.name;
                    if (n.EndsWith("hip")) hip = t;
                    else if (n.EndsWith("tongue03")) tongue = t;
                    else if (n.EndsWith("head")) head = t;
                    else if (n.EndsWith("lMid2")) lHand = t;
                    else if (n.EndsWith("lPectoral")) lPec = t;
                    else if (n.EndsWith("pelvis")) pelvis = t;
                    else if (n.EndsWith("Vagina")) vagina = t;
                }

                float distToCam = Vector3.Distance(cc.transform.position, camPos);
                string parentName = cc.transform.parent != null ? cc.transform.parent.name : "ROOT";
                string child0Str = child0 != null ? $"{child0.name}@{child0.position:F2}" : "NULL";

                Plugin.Log?.LogInfo(
                    $"[LoveSnapshot] Obj='{cc.name}' | Char='{cc.characterName}' | ID={cc.GetInstanceID()} | Parent='{parentName}' | " +
                    $"RootPos={cc.transform.position:F2} (camDist={distToCam:F2}m) | Child0={child0Str} | " +
                    $"Hip={Fmt(hip)}, Head={Fmt(head)}, Tongue={Fmt(tongue)}, LHand={Fmt(lHand)}, LPec={Fmt(lPec)}, Pelvis={Fmt(pelvis)}, Vag={Fmt(vagina)}"
                );
            }

            Plugin.Log?.LogInfo("=== [LoveSnapshot] End ===");
        }

        private static string Fmt(Transform t) => t != null ? t.position.ToString("F2") : "NULL";
    }
}
