using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FurnitureAnimationsMod
{
    public class FurnitureAnimationPlayer : MonoBehaviour
    {
        private CharacterCustomization _character;
        private PoseAnimationData _animData;

        private float _deltaTime = 0f;
        private int _currentDelta = 0;
        private int _currentFrame = 0;
        private bool _reversing = false;

        // Переменные для динамического расчета положения персонажа в пространстве
        private Vector3 _baseWorldPos;
        private Quaternion _baseWorldRot;
        private Quaternion _modelRotationModifier;

        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();

        public void Play(CharacterCustomization character, string animationName, Furniture furniture, PoseData poseConfig)
        {
            _character = character;

            string assetPath = Path.Combine(BepInEx.Paths.PluginPath, "PoseAnimations", $"{animationName}.json");
            if (!File.Exists(assetPath))
            {
                Plugin.Log.LogError($"[LocalPlayer] Анимация на диске не найдена: {assetPath}");
                Destroy(this);
                return;
            }

            try
            {
                string json = File.ReadAllText(assetPath);
                _animData = Newtonsoft.Json.JsonConvert.DeserializeObject<PoseAnimationData>(json);
                _animData.name = animationName;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LocalPlayer] Краш парсинга JSON: {ex.Message}");
                Destroy(this);
                return;
            }

            if (_animData == null || _animData.deltas == null || _animData.deltas.Count == 0)
            {
                Plugin.Log.LogError($"[LocalPlayer] В файле '{animationName}' нет доступных дельт!");
                Destroy(this);
                return;
            }

            // 1. ВЫЧИСЛЯЕМ БАЗОВУЮ ТОЧКУ ОТСЧЕТА НА МЕБЕЛИ
            if (furniture != null && poseConfig != null)
            {
                _baseWorldPos = furniture.transform.TransformPoint(
                    new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z)
                );

                _baseWorldRot = furniture.transform.rotation * Quaternion.Euler(
                    new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z)
                );

                // Модификатор вращения автора для правильного наложения смещений (Страница 4, строка 185)
                _modelRotationModifier = _baseWorldRot * Quaternion.Inverse(Quaternion.Euler(ArrayToVector3(_animData.startRot)));

                // Выставляем персонажа в стартовую позицию интерактива
                _character.transform.position = _baseWorldPos;
                _character.transform.rotation = _baseWorldRot;
            }

            // 2. Замораживаем оригинальный Animator Юнити
            if (_character.anim != null)
            {
                _character.anim.applyRootMotion = false;
                _character.anim.speed = 0f;
                _character.anim.enabled = false;
            }

            _boneCache.Clear();
            CacheSkeletonRecursive(_character.transform);

            _deltaTime = 0f;
            _currentDelta = 0;
            _currentFrame = 0;
            _reversing = false;
        }

        private void LateUpdate()
        {
            if (_character == null || _animData == null) return;

            _deltaTime += Time.deltaTime;
            if (_deltaTime < _animData.rate) return;

            _deltaTime = 0f;
            int nextFrame;
            int nextDelta;

            // Логика циклов и реверсов автора
            if (_reversing)
            {
                if (_currentFrame <= 0)
                {
                    _currentFrame = 0;
                    if (_currentDelta <= 0)
                    {
                        _currentDelta = 0;
                        if (!_animData.loop) { Destroy(this); return; }
                        _reversing = false;
                        nextFrame = 1;
                        nextDelta = _currentDelta;
                    }
                    else
                    {
                        nextDelta = _currentDelta - 1;
                        nextFrame = _animData.deltas[nextDelta].frames - 1;
                    }
                }
                else
                {
                    nextFrame = _currentFrame - 1;
                    nextDelta = _currentDelta;
                }
            }
            else
            {
                if (_currentFrame >= _animData.deltas[_currentDelta].frames - 1)
                {
                    if (_currentDelta >= _animData.deltas.Count - 1)
                    {
                        if (_animData.reverse)
                        {
                            _reversing = true;
                            nextFrame = Mathf.Max(0, _animData.deltas[_currentDelta].frames - 2);
                            nextDelta = _currentDelta;
                        }
                        else
                        {
                            if (!_animData.loop) { Destroy(this); return; }
                            nextDelta = 0;
                            nextFrame = 0;
                        }
                    }
                    else
                    {
                        nextDelta = _currentDelta + 1;
                        nextFrame = 0;
                    }
                }
                else
                {
                    nextFrame = _currentFrame + 1;
                    nextDelta = _currentDelta;
                }
            }

            try
            {
                PoseAnimationDelta currentDeltaData = _animData.deltas[_currentDelta];
                float lerpFraction = (float)(_currentFrame + 1) / (float)currentDeltaData.frames;

                // =========================================================================
                // АВТОРСКИЙ ДИНАМИЧЕСКИЙ РАСЧЕТ ДВИЖЕНИЯ ТЕЛА (Страница 4 декомпилятора) 🏃‍♀️
                // =========================================================================
                Vector3 startFramePos = _baseWorldPos;
                Quaternion startFrameRot = _baseWorldRot;

                // Накапливаем смещения из предыдущих дельт, если мы ушли дальше первого кадра
                if (_currentDelta > 0)
                {
                    startFrameRot *= Quaternion.Euler(ArrayToVector3(_animData.deltas[_currentDelta - 1].endRotDelta));
                    startFramePos += _modelRotationModifier * ArrayToVector3(_animData.deltas[_currentDelta - 1].endPosDelta);
                }

                // Вычисляем финальную точку, куда персонаж должен прийти к концу текущей дельты
                Quaternion endFrameRot = _baseWorldRot * Quaternion.Euler(ArrayToVector3(currentDeltaData.endRotDelta));
                Vector3 endFramePos = _baseWorldPos + _modelRotationModifier * ArrayToVector3(currentDeltaData.endPosDelta);

                // ПЛАВНО ПЕРЕМЕЩАЕМ КОРПУС ПЕРСОНАЖА В ПРОСТРАНСТВЕ (Елозим, прыгаем, смещаемся)
                _character.transform.position = Vector3.Lerp(startFramePos, endFramePos, lerpFraction);
                _character.transform.rotation = Quaternion.Lerp(startFrameRot, endFrameRot, lerpFraction);

                // =========================================================================
                // ПЛАВНЫЙ LERP ДЛЯ ВСЕХ ОСТАЛЬНЫХ КОСТЕЙ СКЕЛЕТА
                // =========================================================================
                foreach (var keyValuePair in currentDeltaData.boneDatas)
                {
                    string boneName = keyValuePair.Key;
                    BoneDelta boneDelta = keyValuePair.Value;

                    if (_boneCache.TryGetValue(boneName, out Transform boneTransform) && boneTransform != null)
                    {
                        boneTransform.localPosition = Vector3.Lerp(
                            ArrayToVector3(boneDelta.startPos),
                            ArrayToVector3(boneDelta.endPos),
                            lerpFraction
                        );

                        boneTransform.localRotation = Quaternion.Lerp(
                            Quaternion.Euler(ArrayToVector3(boneDelta.startRot)),
                            Quaternion.Euler(ArrayToVector3(boneDelta.endRot)),
                            lerpFraction
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LocalPlayer] Ошибка шага интерполяции: {ex.Message}");
            }

            _currentFrame = nextFrame;
            _currentDelta = nextDelta;
        }

        private void CacheSkeletonRecursive(Transform parent)
        {
            if (parent == null) return;
            string cleanName = FixBoneName(parent.name);
            if (!_boneCache.ContainsKey(cleanName)) _boneCache[cleanName] = parent;

            for (int i = 0; i < parent.childCount; i++) CacheSkeletonRecursive(parent.GetChild(i));
        }

        private string FixBoneName(string name)
        {
            if (name.EndsWith("eyesRoot")) return "eyesRoot";
            if (name.EndsWith("head parent")) return "head parent";
            if (name.EndsWith("head target")) return "head target";
            return name;
        }

        private Vector3 ArrayToVector3(float[] arr)
        {
            if (arr == null || arr.Length < 3) return Vector3.zero;
            return new Vector3(arr[0], arr[1], arr[2]);
        }

        private void OnDestroy()
        {
            if (_character != null && _character.anim != null)
            {
                _character.anim.enabled = true; _character.anim.speed = 1f;
            }
            Plugin.Log.LogWarning("[LocalPlayer] Встроенный движок выключен, управление возвращено Юнити.");
        }
    }
}