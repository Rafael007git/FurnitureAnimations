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
            // ПРЕДВАРИТЕЛЬНЫЙ РАСЧЕТ АБСОЛЮТНЫХ МИРОВЫХ КООРДИНАТ ДЛЯ ВСЕХ КАДРОВ
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
            if (_character == null || _animData == null) return;

            // Общее количество переходов в анимации (3 ключевых кадра = 2 перехода)
            int totalTransitions = _animData.deltas.Count - 1;
            if (totalTransitions < 1) return;

            // 1. ИНИЦИАЛИЗАЦИЯ И РАСЧЕТ ПЛОТНОСТИ КАДРОВ НА ГРАНИЦЕ ПЕРЕХОДА
            if (_totalTargetFrames == 0)
            {
                int currentFrameArrayIndex = _currentTransitionIndex - 1;
                PoseAnimationDelta currentDeltaData = _animData.deltas[currentFrameArrayIndex];

                float baseRate = _animData.rate > 0 ? _animData.rate : 0.0333f;

                // Рассчитываем целевую длительность текущего перехода в секундах с учетом скорости
                float durationInSeconds = (currentDeltaData.frames * baseRate) / _speedModifier;

                // Переводим секунды в стабильное количество целых кадров Unity
                _totalTargetFrames = Mathf.RoundToInt(durationInSeconds / Time.deltaTime);
                if (_totalTargetFrames < 1) _totalTargetFrames = 1;

                _gameFrameCounter = 0;
            }

            // 2. ВЫЧИСЛЕНИЕ ЧИСТОЙ ФРАКЦИИ С УЧЕТОМ НАПРАВЛЕНИЯ РЕВЕРСА
            float localFraction = Mathf.Clamp01((float)_gameFrameCounter / _totalTargetFrames);
            if (_reversing)
            {
                localFraction = 1f - localFraction; // Пятимся назад
            }

            float lerpFraction = localFraction; // По умолчанию для Linear

            // 3. ПРИМЕНЕНИЕ ВЫБРАННОГО РЕЖИМА СГЛАЖИВАНИЯ (EASEMODE)
            switch (_currentEaseMode)
            {
                case EaseMode.PerFrame:
                    lerpFraction = Mathf.SmoothStep(0f, 1f, localFraction);
                    break;

                case EaseMode.Global:
                    float totalAnimationFrames = 0f;
                    for (int i = 0; i < totalTransitions; i++)
                    {
                        totalAnimationFrames += _animData.deltas[i].frames; // Итог: 70 кадров автора
                    }

                    float passedAuthorFrames = 0f;
                    for (int i = 0; i < _currentTransitionIndex - 1; i++)
                    {
                        passedAuthorFrames += _animData.deltas[i].frames;
                    }

                    int arrayIdx = _currentTransitionIndex - 1;
                    float currentGlobalAuthorFrame = passedAuthorFrames + (localFraction * _animData.deltas[arrayIdx].frames);
                    float globalFraction = Mathf.Clamp01(currentGlobalAuthorFrame / totalAnimationFrames);

                    float smoothedGlobal = Mathf.SmoothStep(0f, 1f, globalFraction);

                    float currentDeltaStartGlobal = passedAuthorFrames / totalAnimationFrames;
                    float currentDeltaEndGlobal = (passedAuthorFrames + _animData.deltas[arrayIdx].frames) / totalAnimationFrames;
                    float globalDeltaDuration = currentDeltaEndGlobal - currentDeltaStartGlobal;

                    lerpFraction = globalDeltaDuration > 0
                        ? Mathf.Clamp01((smoothedGlobal - currentDeltaStartGlobal) / globalDeltaDuration)
                        : 1f;
                    break;

                case EaseMode.Linear:
                default:
                    break;
            }

            // =========================================================================
            // 4. МАТЕМАТИЧЕСКИЙ РЕНДЕР И ИНЪЕКЦИЯ КОСТЕЙ В СКЕЛЕТ (ОПТИМИЗИРОВАННЫЙ)
            // =========================================================================
            try
            {
                int fromFrameIndex = _currentTransitionIndex - 1; // Откуда идем
                int toFrameIndex = _currentTransitionIndex;     // Куда идем

                // Защитная проверка, чтобы не выйти за пределы предрассчитанного массива
                if (_calculatedWorldFrames != null && toFrameIndex < _calculatedWorldFrames.Length)
                {
                    Vector3 startFramePos = _calculatedWorldFrames[fromFrameIndex].Position;
                    Quaternion startFrameRot = _calculatedWorldFrames[fromFrameIndex].Rotation;

                    Vector3 endFramePos = _calculatedWorldFrames[toFrameIndex].Position;
                    Quaternion endFrameRot = _calculatedWorldFrames[toFrameIndex].Rotation;

                    // Плавный Lerp тела персонажа между готовыми мировыми точками за O(1)
                    _character.transform.position = Vector3.Lerp(startFramePos, endFramePos, localFraction);
                    _character.transform.rotation = Quaternion.Lerp(startFrameRot, endFrameRot, localFraction);
                }

                // Интерполяция внутренних костей скелета куклы (остается без изменений)
                PoseAnimationDelta toDelta = _animData.deltas[toFrameIndex];
                foreach (var kp in toDelta.boneDatas)
                {
                    if (_boneCache.TryGetValue(kp.Key, out Transform boneTransform) && boneTransform != null)
                    {
                        boneTransform.localPosition = Vector3.Lerp(ArrayToVector3(kp.Value.startPos), ArrayToVector3(kp.Value.endPos), localFraction);
                        boneTransform.localRotation = Quaternion.Lerp(Quaternion.Euler(ArrayToVector3(kp.Value.startRot)), Quaternion.Euler(ArrayToVector3(kp.Value.endRot)), localFraction);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SubframePlayer] Критическая ошибка интерполяции: {ex.Message}");
            }

            // 5. ИНКРЕМЕНТ И ЦИКЛ ПЕРЕКЛЮЧЕНИЯ ЧЕЛОВЕЧЕСКИХ ПЕРЕХОДОВ (1 И 2)
            _gameFrameCounter++;

            if (_gameFrameCounter > _totalTargetFrames)
            {
                _totalTargetFrames = 0; // Сигнал сброса для следующей дельты

                if (!_reversing)
                {
                    if (_currentTransitionIndex < totalTransitions)
                    {
                        _currentTransitionIndex++;
                    }
                    else
                    {
                        if (_animData.reverse) { _reversing = true; }
                        else if (_animData.loop) { _currentTransitionIndex = 1; }
                        else { Destroy(this); return; }
                    }
                }
                else
                {
                    if (_currentTransitionIndex > 1)
                    {
                        _currentTransitionIndex--;
                    }
                    else
                    {
                        if (_animData.loop) { _reversing = false; }
                        else { Destroy(this); return; }
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
