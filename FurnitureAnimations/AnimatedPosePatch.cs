using FurnitureAnimationsMod;
using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace FurnitureAnimations
{
    // 🕵️ ХАРМОНИ-ПЕРЕХВАТ ЯДРА ANIMATED POSE (РАБОТАЕТ ПРИ ЛЮБОМ ВКЛЮЧЕНИИ АНИМАЦИИ)
    // Патчим класс BepInExPlugin и метод LateUpdate (или метод установки анимации),
    // Но так как имя метода установки может отличаться, мы внедримся в Update или LateUpdate, 
    // где проверяется словарь currentlyPosing!

    [HarmonyPatch("PoseAnimations.BepInExPlugin", "LateUpdate")]
    public static class AnimatedPose_Core_Tracker
    {
        private static string _lastDetectedAnim = "";

        public static void Postfix()
        {
            try
            {
                // Проверяем, открыт ли вообще режим Free Pose
                if (Global.code == null || Global.code.uiFreePose == null || !Global.code.uiFreePose.enabled) return;

                var selectedChar = Global.code.uiFreePose.selectedCharacter;
                if (selectedChar == null) return;

                var characterComp = selectedChar.GetComponent<CharacterCustomization>();
                if (characterComp == null || characterComp.anim == null || characterComp.anim.runtimeAnimatorController == null) return;

                // Получаем текущее имя контроллера (имя JSON-файла танца)
                string currentAnimName = characterComp.anim.runtimeAnimatorController.name;

                // Проверяем: это JSON-анимация мода?
                bool isJsonAnim = currentAnimName.EndsWith(".json") || currentAnimName.Contains("JSON") || currentAnimName.StartsWith("Dance") || currentAnimName.StartsWith("A_");

                if (isJsonAnim)
                {
                    // Если анимация сменилась, обновляем данные один раз, чтобы не спамить в Update
                    if (_lastDetectedAnim != currentAnimName)
                    {
                        _lastDetectedAnim = currentAnimName;
                        Plugin.Log.LogWarning($"[CORE_OVERTAKE] Детектор: Обнаружена активная JSON-анимация '{currentAnimName}' на персонаже!");

                        // 1. Пытаемся вытащить легальную иконку этой позы из общего реестра игры
                        if (RM.code != null && RM.code.allFreePoses != null)
                        {
                            foreach (Transform t in RM.code.allFreePoses.items)
                            {
                                if (t == null) continue;
                                var p = t.GetComponent<global::Pose>();
                                if (p != null && p.name == currentAnimName)
                                {
                                    // Записываем её в наше поле превью (через рефлексию для безопасности доступа)
                                    var field = typeof(PoseExporter).GetField("_lastCapturedIcon", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                    if (field != null)
                                    {
                                        field.SetValue(null, p.icon);
                                        Plugin.Log.LogInfo($"[CORE_OVERTAKE] Легальная иконка для '{currentAnimName}' успешно перехвачена.");
                                    }
                                    break;
                                }
                            }
                        }

                        // 2. Железно переключаем текст нашей ультимативной кнопки на сцене
                        Transform sdkBtnTrans = Global.code.uiFreePose.transform.Find("Button_SaveInteract");
                        var buttonTextComp = sdkBtnTrans?.GetComponentInChildren<UnityEngine.UI.Text>();
                        if (buttonTextComp != null)
                        {
                            buttonTextComp.text = "Link Animated Pose for Furniture";
                            Plugin.Log.LogInfo("[CORE_OVERTAKE] Кнопка интерактива переведена в режим 'Link Animated Pose'.");
                        }
                    }
                }
                else
                {
                    // Если персонаж переключился обратно на обычную позу
                    if (_lastDetectedAnim != "" && !string.IsNullOrEmpty(currentAnimName) && !currentAnimName.Contains("CustomJSON"))
                    {
                        _lastDetectedAnim = "";
                    }
                }
            }
            catch (System.Exception ex)
            {
                // Подавляем спам в логах, если что-то не инициализировалось при старте сцены
            }
        }
    }
}
