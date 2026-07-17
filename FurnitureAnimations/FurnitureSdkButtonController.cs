using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RuntimeGizmos;

namespace FurnitureAnimationsMod
{
    public class FurnitureSdkButtonController : MonoBehaviour
    {
        private UIFreePose _uiInstance;
        private UnityEngine.UI.Button _mainButton;
        private UnityEngine.UI.Text _buttonText;
        private float _updateTimer = 0f;

        public void Setup(UIFreePose ui)
        {
            _uiInstance = ui;
            _mainButton = GetComponent<UnityEngine.UI.Button>();
            _buttonText = GetComponentInChildren<UnityEngine.UI.Text>();
            Plugin.Log.LogInfo("[SDK_Controller] Нативный Update-трекер запущен в дипломатическом режиме.");
        }

        private void Update()
        {
            if (_uiInstance == null || _mainButton == null || _buttonText == null) return;

            // Оптимизация: опрашиваем движок 5 раз в секунду
            _updateTimer += Time.deltaTime;
            if (_updateTimer < 0.2f) return;
            _updateTimer = 0f;

            if (_uiInstance.selectedCharacter == null) return;

            CharacterCustomization characterComp = _uiInstance.selectedCharacter.GetComponent<CharacterCustomization>();
            if (characterComp == null || characterComp.anim == null) return;

            try
            {
                string currentCtrlName = (characterComp.anim.runtimeAnimatorController?.name ?? "").ToLower();

                // 1. КОНТРОЛЬ ГИЗМО BUGERRY
                bool isGizmoActiveOnScene = false;
                bool isUserActivelyRotating = false;

                if (TransformGizmo.transformGizmo_ != null)
                {
                    isGizmoActiveOnScene = TransformGizmo.transformGizmo_.runTransformGizmo;
                    isUserActivelyRotating = TransformGizmo.transformGizmo_.isTransforming;
                }

                // 2. ДИПЛОМАТИЧЕСКИЙ РАНТАЙМ-ПЕРЕХВАТ А-ПОЗЫ В ОБХОД ВСЕХ МОДОВ
                // Если персонаж взаимодействует с мебелью, и на экране ЕЩЕ открыто наше меню...
                if (characterComp.interactingObject != null)
                {
                    string furnitureName = characterComp.interactingObject.name.Replace("(Clone)", "").Trim();

                    if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
                    {
                        // Смотрим, какая именно иконка позы сейчас выбрана в интерфейсе игры (через uiPose.curpose)
                        if (Global.code != null && Global.code.uiPose != null && Global.code.uiPose.curpose != null)
                        {
                            string activePoseNameInUi = Global.code.uiPose.curpose.name;

                            // Ищем данные этой позы в нашем JSON
                            PoseData currentPoseData = config.InteractionPoses.Find(p => p != null && p.DisplayName == activePoseNameInUi);

                            // СРАБАТЫВАЕТ НАШ ТРИГГЕР: Если это наша кастомная поза, а аниматор еще не усыплен!
                            if (currentPoseData != null &&
                                currentPoseData.Type.Equals("CustomJSON", StringComparison.OrdinalIgnoreCase) &&
                                characterComp.anim.enabled == true) // Если аниматор включен — значит игра держит А-позу!
                            {
                                Plugin.Log.LogWarning($"[SDK_Mono_Bypass] Обнаружена А-поза для '{activePoseNameInUi}'. Исправляем скелет напрямую из MonoBehaviour...");
                                ApplyCustomBonesDirect(characterComp.transform, currentPoseData.JsonFileName);
                            }
                        }
                    }
                }

                // 3. АВТО-ПЕРЕКЛЮЧЕНИЕ ЦВЕТА И ТЕКСТА КНОПКИ SDK
                bool isHandEditingActive = currentCtrlName.Contains("custom") || _uiInstance.isCustomPoseMode == true || isGizmoActiveOnScene;
                bool isDefaultIdleActive = currentCtrlName.Contains("idle") || currentCtrlName.Contains("unarmed") || string.IsNullOrEmpty(currentCtrlName);
                bool isAnyPresetPoseActive = characterComp.anim.enabled == false || !isDefaultIdleActive;

                if (isHandEditingActive)
                {
                    _buttonText.text = "Save Custom Pose for Furniture";
                    _buttonText.color = Color.cyan;
                    _mainButton.interactable = true;
                }
                else if (isAnyPresetPoseActive)
                {
                    _buttonText.text = "Link Preset Pose for Furniture";
                    _buttonText.color = Color.green;
                    _mainButton.interactable = true;
                }
                else
                {
                    _buttonText.text = "No Furniture Pose";
                    _buttonText.color = Color.gray;
                    _mainButton.interactable = false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SDK_Controller] Ошибка автономного цикла: {ex.Message}");
            }
        }

        // Автономный изолированный метод раскатки Диорамы на скелет куклы
        private void ApplyCustomBonesDirect(Transform character, string jsonFileName)
        {
            string customAnimFullPath = Path.Combine(ConfigManager.CustomAnimsPath, jsonFileName);
            if (!File.Exists(customAnimFullPath)) return;

            try
            {
                string jsonContent = File.ReadAllText(customAnimFullPath);
                var rawBonesData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, BakedElementData>>(jsonContent);
                if (rawBonesData == null) return;

                Transform FindChildRecursive(Transform parent, string name)
                {
                    if (parent == null) return null;
                    if (parent.name == name) return parent;
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        Transform found = FindChildRecursive(parent.GetChild(i), name);
                        if (found != null) return found;
                    }
                    return null;
                }

                foreach (var kp in rawBonesData)
                {
                    Transform boneTrans = FindChildRecursive(character, kp.Key);
                    if (boneTrans == null || kp.Value == null) continue;

                    if ((kp.Value.type ?? "").Equals("Light", StringComparison.OrdinalIgnoreCase))
                    {
                        Light light = boneTrans.GetComponent<Light>();
                        if (light != null)
                        {
                            light.enabled = kp.Value.enabled;
                            light.intensity = kp.Value.intensity;
                            light.range = kp.Value.range;
                            if (kp.Value.color != null) light.color = new Color(kp.Value.color.r, kp.Value.color.g, kp.Value.color.b);
                        }
                    }
                    else
                    {
                        if (kp.Value.rot != null) boneTrans.localEulerAngles = new Vector3(kp.Value.rot.x, kp.Value.rot.y, kp.Value.rot.z);
                        if (DioramaConstants.PositionalObjectsRegistry.Contains(kp.Key) && kp.Value.pos != null)
                        {
                            boneTrans.localPosition = new Vector3(kp.Value.pos.x, kp.Value.pos.y, kp.Value.pos.z);
                        }
                    }
                }

                // Замораживаем аниматор куклы прямо на выходе из цикла
                Animator anim = character.GetComponent<Animator>();
                if (anim != null)
                {
                    anim.applyRootMotion = false;
                    anim.speed = 0f;
                    anim.enabled = false; // Усыпляем А-позу намертво!
                }
                Plugin.Log.LogWarning($"[SDK_Mono_Bypass] Кастомный скелет из файла {jsonFileName} успешно раскатан в обход Harmony!");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SDK_Mono_Bypass] Ошибка инъекции костей: {ex.Message}");
            }
        }
    }
}
