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

        // Кэш для мгновенного доступа к трансформациям скелета (смерть микрофризам! ⚡)
        private Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();
        private GameObject _lastCachedCharacter = null;

        public void Setup(UIFreePose ui)
        {
            _uiInstance = ui;
            _mainButton = GetComponent<UnityEngine.UI.Button>();
            _buttonText = GetComponentInChildren<UnityEngine.UI.Text>();
            Plugin.Log.LogInfo("[SDK_Controller] Нативный Update-трекер запущен в дипломатическом режиме с оптимизированным кэшем скелета.");
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
                if (characterComp.interactingObject != null)
                {
                    string furnitureName = characterComp.interactingObject.name.Replace("(Clone)", "").Trim();

                    if (ConfigManager.LoadedConfigs.TryGetValue(furnitureName, out FurnitureConfig config))
                    {
                        if (Global.code != null && Global.code.uiPose != null && Global.code.uiPose.curpose != null)
                        {
                            string activePoseNameInUi = Global.code.uiPose.curpose.name;

                            // Ищем данные этой позы в нашем JSON
                            PoseData currentPoseData = config.InteractionPoses.Find(p => p != null && p.DisplayName == activePoseNameInUi);

                            // СРАБАТЫВАЕТ НАШ ТРИГГЕР: Если это наша кастомная поза, а аниматор еще не усыплен!
                            if (currentPoseData != null &&
                                currentPoseData.Type.Equals("CustomJSON", StringComparison.OrdinalIgnoreCase) &&
                                characterComp.anim.enabled == true)
                            {
                                Plugin.Log.LogWarning($"[SDK_Mono_Bypass] Обнаружена А-поза для '{activePoseNameInUi}'. Исправляем скелет напрямую из MonoBehaviour...");
                                ApplyCustomBonesDirect(characterComp.gameObject, currentPoseData.JsonFileName);
                            }
                        }
                    }
                }

                // ==========================================================
                // 3. ОБНОВЛЕННОЕ АВТО-ПЕРЕКЛЮЧЕНИЕ ЦВЕТА И ТЕКСТА КНОПКИ SDK
                // ==========================================================
                CharacterPoseState state = CharacterStateHelper.GetCurrentState(characterComp);

                switch (state)
                {
                    case CharacterPoseState.PoseAnimationsModActive:
                        SetButtonState("Link Animated Pose for Furniture", new Color(0.6f, 0.2f, 0.8f, 1f), true);
                        break;

                    case CharacterPoseState.GameAnimatorActive:
                        SetButtonState("Link Preset Pose for Furniture", Color.green, true);
                        break;

                    case CharacterPoseState.CustomPoseJSON:
                        bool isIdle = currentCtrlName.Contains("idle") || currentCtrlName.Contains("unarmed") || string.IsNullOrEmpty(currentCtrlName);

                        if (isIdle && characterComp.anim.enabled == true)
                        {
                            SetButtonState("No Furniture Pose", Color.gray, false);
                        }
                        else
                        {
                            SetButtonState("Save Custom Pose for Furniture", Color.cyan, true);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SDK_Controller] Ошибка автономного цикла: {ex.Message}");
            }
        }

        private void SetButtonState(string text, Color textColor, bool interactable)
        {
            _buttonText.text = text;
            _buttonText.color = textColor;
            _mainButton.interactable = interactable;
        }

        // Автономный изолированный метод раскатки Диорамы на скелет куклы с ПОЛНОЙ ОПТИМИЗАЦИЕЙ КЭША
        private void ApplyCustomBonesDirect(GameObject characterObj, string jsonFileName)
        {
            string customAnimFullPath = Path.Combine(ConfigManager.CustomAnimsPath, jsonFileName);
            if (!File.Exists(customAnimFullPath)) return;

            try
            {
                string jsonContent = File.ReadAllText(customAnimFullPath);
                var rawBonesData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, BakedElementData>>(jsonContent);
                if (rawBonesData == null) return;

                // --- СВЕРХБЫСТРОЕ КЭШИРОВАНИЕ СКЕЛЕТА ПРИ СМЕНЕ ПЕРСОНАЖА 🌟 ---
                if (_lastCachedCharacter != characterObj)
                {
                    _boneCache.Clear();
                    _lastCachedCharacter = characterObj;

                    foreach (Transform child in characterObj.GetComponentsInChildren<Transform>(true))
                    {
                        if (DioramaConstants.AnatomyBoneRegistry.Contains(child.name))
                        {
                            _boneCache[child.name] = child;
                        }
                    }
                    Plugin.Log.LogInfo($"[SDK_Cache] Успешно переиндексирован скелет для {characterObj.name}. В кэш мода занесено {_boneCache.Count} костей.");
                }

                // Накладываем позы на кости ИСКЛЮЧИТЕЛЬНО через O(1) Dictionary Lookups
                foreach (var kp in rawBonesData)
                {
                    if (!_boneCache.TryGetValue(kp.Key, out Transform boneTrans) || kp.Value == null)
                        continue;

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
                        // --- ИСПРАВЛЕНИЕ ХАРДКОДА СКРУЧИВАНИЯ HIPS И РОТАЦИИ СКЕЛЕТА ---
                        // Принудительно стираем старый наклон кости, возвращая в локальную нейтраль родителя
                        boneTrans.localRotation = Quaternion.identity;

                        if (kp.Value.rot != null)
                        {
                            boneTrans.localEulerAngles = new Vector3(kp.Value.rot.x, kp.Value.rot.y, kp.Value.rot.z);
                        }

                        // Смещение позиции накладываем строго на разрешенные Позиционные объекты (например, тазовый 'hip')
                        if (DioramaConstants.PositionalObjectsRegistry.Contains(kp.Key) && kp.Value.pos != null)
                        {
                            boneTrans.localPosition = new Vector3(kp.Value.pos.x, kp.Value.pos.y, kp.Value.pos.z);
                        }
                    }
                }

                Animator anim = characterObj.GetComponent<Animator>();
                if (anim != null)
                {
                    anim.applyRootMotion = false;
                    anim.speed = 0f;
                    anim.enabled = false; // Усыпляем А-позу намертво!
                }
                Plugin.Log.LogWarning($"[SDK_Mono_Bypass] Кастомный скелет из файла {jsonFileName} успешно раскатан БЕЗ микрофризов процессора!");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SDK_Mono_Bypass] Ошибка инъекции костей: {ex.Message}");
            }
        }
    }
}
