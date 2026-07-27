using FurnitureAnimationsMod;
using HarmonyLib;
using UnityEngine;

namespace FurnitureAnimations
{
    // 🕵️ УЛЬТИМАТИВНЫЙ ПЕРЕХВАТ КЛИКА В ЯДРЕ ANIMATED POSE
    // Патчим метод, который выводит строчку "Setting Player to animation..."
    [HarmonyPatch("PoseAnimations.BepInExPlugin", "SetToAnimation")]
    [HarmonyPatch(new System.Type[] { typeof(string), typeof(CharacterCustomization) })]

    public static class AnimatedPose_Method_Overtake
    {
        public static void Postfix(string name, CharacterCustomization character)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || character == null) return;

                Plugin.Log.LogWarning($"[HARMONY_OVERTAKE] Поймали активацию танца: '{name}' на персонаже {character.name}");

                // 1. Находим объект позы в каталоге игры, чтобы забрать легальную иконку мода
                if (RM.code != null && RM.code.allFreePoses != null)
                {
                    foreach (Transform t in RM.code.allFreePoses.items)
                    {
                        if (t != null && t.name == name)
                        {
                            var p = t.GetComponent<global::Pose>();
                            if (p != null && p.icon != null)
                            {
                                // Записываем иконку в наше скрытое поле через рефлексию
                                var field = typeof(PoseExporter).GetField("_lastCapturedIcon", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                                field?.SetValue(null, p.icon);
                                Plugin.Log.LogInfo($"[HARMONY_OVERTAKE] Легальная иконка для '{name}' успешно перехвачена.");
                            }
                            break;
                        }
                    }
                }

                // 2. Мгновенно меняем текст и цвет нашей кнопки интерактива на сцене
                var uiFreePose = GameObject.FindObjectOfType<UIFreePose>();
                if (uiFreePose != null)
                {
                    Transform sdkBtnTrans = uiFreePose.transform.Find("Button_SaveInteract");
                    var buttonTextComp = sdkBtnTrans?.GetComponentInChildren<UnityEngine.UI.Text>();
                    var buttonImageComp = sdkBtnTrans?.GetComponent<UnityEngine.UI.Image>();

                    if (buttonTextComp != null)
                    {
                        buttonTextComp.text = "Link Animated Pose for Furniture";
                    }
                    if (buttonImageComp != null)
                    {
                        // Вспыхиваем красивым фиолетовым цветом мода в рантайме!
                        buttonImageComp.color = new Color(0.6f, 0.2f, 0.8f, 1f);
                    }
                    Plugin.Log.LogInfo("[HARMONY_OVERTAKE] Кнопка на сцене мгновенно переведена в режим 'Link Animated Pose'.");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[HARMONY_OVERTAKE] Ошибка перехвата в SetToAnimation: {ex}");
            }
        }
    }
}
