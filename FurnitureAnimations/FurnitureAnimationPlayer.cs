using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static ObjectRaycastPhysics;

namespace FurnitureAnimationsMod
{
    public enum EaseMode
    {
        Linear,
        Global,
        PerFrame
    }

    public class FurnitureAnimationPlayer : MonoBehaviour
    {
        public static FurnitureAnimationPlayer Instance { get; private set; }

        private CharacterCustomization _character;
        internal PoseAnimationData _animData;

        // Новая целочисленная кадровая сетка на базе стабильного FPS
        internal int _currentTransitionIndex = 1; // Человеческие индексы: 1 и 2
        internal int _gameFrameCounter = 0;
        internal int _totalTargetFrames = 0;
        internal bool _reversing = false;

        private Vector3 _baseWorldPos;
        private Quaternion _baseWorldRot;
        private Quaternion _modelRotationModifier;

        private struct AbsoluteWorldFrame
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        // Массив для хранения предрассчитанных координат всех кадров автора
        private AbsoluteWorldFrame[] _calculatedWorldFrames;

        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();

        private float _speedModifier = 1.0f;
        private EaseMode _currentEaseMode = EaseMode.Linear;

        public void Play(CharacterCustomization character, string animationName, Furniture furniture, PoseData poseConfig)
        {
            Instance = this;
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

            // Подключаем разделенные компоненты аудио и UI
            gameObject.AddComponent<AnimationAudioManager>().Initialize(animationName, _animData.loop);
            gameObject.AddComponent<AnimationUiControls>().Initialize(this);

            // --- ДОБАВЬТЕ ЭТУ СТРОКУ ДЛЯ ПОДКЛЮЧЕНИЯ РАДАРА ---
            gameObject.AddComponent<AnimationRadar>();

            if (furniture != null && poseConfig != null)
            {
                _baseWorldPos = furniture.transform.TransformPoint(new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z));
                _baseWorldRot = furniture.transform.rotation * Quaternion.Euler(new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z));
                _modelRotationModifier = _baseWorldRot * Quaternion.Inverse(Quaternion.Euler(ArrayToVector3(_animData.startRot)));
                _character.transform.position = _baseWorldPos;
                _character.transform.rotation = _baseWorldRot;
            }

            if (_character.anim != null)
            {
                _character.anim.applyRootMotion = false;
                _character.anim.speed = 0f;
                _character.anim.enabled = false;
            }

            _boneCache.Clear();
            CacheSkeletonRecursive(_character.transform);

            Dictionary<string, BoneDelta> firstFrameDatas = null;
            if (_animData.deltas.Count > 0)
            {
                firstFrameDatas = _animData.deltas[0].boneDatas;
            }

            AbsoluteSkeletalReset(firstFrameDatas);

            // Мягкая инициализация целочисленных состояний
            _currentTransitionIndex = 1;
            _gameFrameCounter = 0;
            _totalTargetFrames = 0; // Спровоцирует перерасчет плотности на первом тике
            _reversing = false;

            // =========================================================================
            // ПРЕДВАРИТЕЛЬНЫЙ РАСЧЕТ АБСОЛЮТНЫХ МИРОВЫХ КООРДИНАТ ДЛЯ ВСЕХ КАДРОВ - нужен ли он?
            // =========================================================================
            int totalFramesCount = _animData.deltas.Count;
            _calculatedWorldFrames = new AbsoluteWorldFrame[totalFramesCount];

            // 0-й (стартовый) кадр всегда равен базовым координатам мода на мебели
            _calculatedWorldFrames[0] = new AbsoluteWorldFrame
            {
                Position = _baseWorldPos,
                Rotation = _baseWorldRot
            };

            // Последовательно нанизываем смещения для всех последующих кадров автора
            Vector3 currentAccumulatedPos = _baseWorldPos;
            Quaternion currentAccumulatedRot = _baseWorldRot;

            for (int i = 1; i < totalFramesCount; i++)
            {
                var currentDelta = _animData.deltas[i];
                currentAccumulatedRot *= Quaternion.Euler(ArrayToVector3(currentDelta.endRotDelta));
                currentAccumulatedPos += _modelRotationModifier * ArrayToVector3(currentDelta.endPosDelta);

                _calculatedWorldFrames[i] = new AbsoluteWorldFrame
                {
                    Position = currentAccumulatedPos,
                    Rotation = currentAccumulatedRot
                };
            }
            // =========================================================================
            _currentTransitionIndex = 0;
            _gameFrameCounter = 0;
            _totalTargetFrames = 0;
            _reversing = false;
        }

        public void ChangeSpeed(float delta)
        {
            _speedModifier = Mathf.Clamp(_speedModifier + delta, 0.1f, 3.0f);
            Plugin.Log.LogInfo($"[SpeedManager] Текущая скорость: {_speedModifier * 100:F0}%");
        }

        public float GetSpeed() => _speedModifier;

        public void ToggleEaseMode()
        {
            _currentEaseMode = (EaseMode)(((int)_currentEaseMode + 1) % 3);
            Plugin.Log.LogWarning($"[EaseManager] Режим сглаживания изменен на: {_currentEaseMode}");
        }

        public EaseMode GetEaseMode() => _currentEaseMode;

        public string GetPlayingAnimationName() => _animData != null ? _animData.name : string.Empty;

        private void LateUpdate()
        {
            if (_character == null || _animData == null || _animData.deltas == null) return;

            // 1. Общее количество переходов (фаз), заложенных автором в JSON
            int totalTransitions = _animData.deltas.Count;
            if (totalTransitions < 1) return;

            // Защита от выхода за границы массива (индексация строго от 0 до totalTransitions - 1)
            if (_currentTransitionIndex >= totalTransitions) _currentTransitionIndex = totalTransitions - 1;
            if (_currentTransitionIndex < 0) _currentTransitionIndex = 0;

            // Берем данные текущего активного перехода
            PoseAnimationDelta currentTransitionData = _animData.deltas[_currentTransitionIndex];

            float durationInSeconds = currentTransitionData.frames * (_animData.rate > 0 ? _animData.rate : 0.0333f);
            if (_totalTargetFrames <= 0)
            {
                float currentFps = (Time.deltaTime > 0f) ? (1f / Time.deltaTime) : 60f;
                if (currentFps < 10f) currentFps = 10f;

                // Рассчитываем целевые игровые кадры с учётом модификатора скорости
                _totalTargetFrames = Mathf.RoundToInt((durationInSeconds * currentFps) / _speedModifier);
                if (_totalTargetFrames <= 0) _totalTargetFrames = 1;
            }

            // 2. Инкремент шага игровых кадров
            _gameFrameCounter++;
            float localFraction = Mathf.Clamp01((float)_gameFrameCounter / _totalTargetFrames);

            // Если включен реверс, фракция идет в обратную сторону (от 1 к 0)
            float actualFraction = _reversing ? (1f - localFraction) : localFraction;

            // 3. МАТЕМАТИЧЕСКИЙ РЕНДЕР И ИНЪЕКЦИЯ КОСТЕЙ
            try
            {
                // Для самого тела (рута) персонажа интерполируем базовое смещение плашки
                Vector3 startFramePos = _baseWorldPos;
                Quaternion startFrameRot = _baseWorldRot;

                // Если это не первый переход, накапливаем трансформации предыдущих фаз
                if (_currentTransitionIndex > 0)
                {
                    for (int i = 0; i < _currentTransitionIndex; i++)
                    {
                        var prevDelta = _animData.deltas[i];
                        startFrameRot *= Quaternion.Euler(ArrayToVector3(prevDelta.endRotDelta));
                        startFramePos += _modelRotationModifier * ArrayToVector3(prevDelta.endPosDelta);
                    }
                }

                Quaternion endFrameRot = startFrameRot * Quaternion.Euler(ArrayToVector3(currentTransitionData.endRotDelta));
                Vector3 endFramePos = startFramePos + _modelRotationModifier * ArrayToVector3(currentTransitionData.endPosDelta);

                _character.transform.position = Vector3.Lerp(startFramePos, endFramePos, actualFraction);
                _character.transform.rotation = Quaternion.Lerp(startFrameRot, endFrameRot, actualFraction);

                // ИНТЕРПОЛЯЦИЯ КОСТЕЙ: строго внутри ОДНОЙ дельты от start к end!
                if (currentTransitionData.boneDatas != null)
                {
                    foreach (var kp in currentTransitionData.boneDatas)
                    {
                        if (_boneCache.TryGetValue(kp.Key, out Transform boneTransform) && boneTransform != null)
                        {
                            // Извлекаем start и end прямо из текущей дельты
                            Vector3 boneStartPos = ArrayToVector3(kp.Value.startPos);
                            Vector3 boneEndPos = ArrayToVector3(kp.Value.endPos);
                            Quaternion boneStartRot = Quaternion.Euler(ArrayToVector3(kp.Value.startRot));
                            Quaternion boneRotEnd = Quaternion.Euler(ArrayToVector3(kp.Value.endRot));

                            boneTransform.localPosition = Vector3.Lerp(boneStartPos, boneEndPos, actualFraction);
                            boneTransform.localRotation = Quaternion.Lerp(boneStartRot, boneRotEnd, actualFraction);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SubframePlayer] Ошибка рендера костей: {ex.Message}");
            }

            // 4. АВТОМАТ СМЕНЫ ПЕРЕХОДОВ И РЕВЕРСА
            if (_gameFrameCounter >= _totalTargetFrames)
            {
                _gameFrameCounter = 0;
                _totalTargetFrames = 0; // Сбрасываем для пересчета на следующем шаге

                if (!_reversing)
                {
                    // Идем вперед по цепочке дельт
                    if (_currentTransitionIndex < totalTransitions - 1)
                    {
                        _currentTransitionIndex++;
                    }
                    else
                    {
                        // Достигли конца самой последней дельты в JSON
                        if (_animData.reverse)
                        {
                            _reversing = true;
                            // Остаемся на этом же индексе, чтобы начать проигрывать эту же дельту в обратную сторону!
                        }
                        else if (_animData.loop)
                        {
                            _currentTransitionIndex = 0;
                        }
                        else
                        {
                            Destroy(this);
                            return;
                        }
                    }
                }
                else
                {
                    // Идем назад (реверс)
                    if (_currentTransitionIndex > 0)
                    {
                        _currentTransitionIndex--;
                    }
                    else
                    {
                        // Вернулись в самое начало первой дельты (индекс 0)
                        if (_animData.loop)
                        {
                            _reversing = false;
                            // Флаг снят, со следующего кадра снова плавно идем вперед от start к end
                        }
                        else
                        {
                            Destroy(this);
                            return;
                        }
                    }
                }
            }
        }

        private void AbsoluteSkeletalReset(Dictionary<string, BoneDelta> firstFrameBoneDatas)
        {
            if (_character == null) return;

            foreach (string boneName in DioramaConstants.AnatomyBoneRegistry)
            {
                if (!_boneCache.TryGetValue(boneName, out Transform boneTrans))
                    continue;

                boneTrans.localRotation = Quaternion.identity;

                if (firstFrameBoneDatas != null && firstFrameBoneDatas.TryGetValue(boneName, out BoneDelta delta))
                {
                    if (delta.endRot != null && delta.endRot.Length >= 4)
                    {
                        boneTrans.localRotation = new Quaternion(delta.endRot[0], delta.endRot[1], delta.endRot[2], delta.endRot[3]);
                    }
                    else if (delta.endRot != null && delta.endRot.Length == 3)
                    {
                        boneTrans.localRotation = Quaternion.Euler(delta.endRot[0], delta.endRot[1], delta.endRot[2]);
                    }

                    if (delta.endPos != null && delta.endPos.Length >= 3)
                    {
                        boneTrans.localPosition = new Vector3(delta.endPos[0], delta.endPos[1], delta.endPos[2]);
                    }
                }
            }
            Plugin.Log.LogInfo($"[FurnitureAnimations] Скелет {_character.name} очищен. Якорь 'hip' выставлен.");
        }

        private void CacheSkeletonRecursive(Transform parent)
        {
            if (parent == null) return;
            string cleanName = FixBoneName(parent.name);
            if (!_boneCache.ContainsKey(cleanName)) _boneCache[cleanName] = parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                CacheSkeletonRecursive(parent.GetChild(i));
            }
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
            if (Instance == this) Instance = null;

            if (_character != null && _character.anim != null)
            {
                _character.anim.enabled = true;
                _character.anim.speed = 1f;
            }
            Plugin.Log.LogWarning("[LocalPlayer] Встроенный движок выключен, управление возвращено Юнити.");
        }
    }
}
