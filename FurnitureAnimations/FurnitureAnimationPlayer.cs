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

        private Furniture _targetFurniture;                 // Ссылка на целевой объект мебели
        private Vector3 _localBasePos;                      // Стартовая локальная позиция из конфига
        private Quaternion _localBaseRot;                   // Стартовый локальный поворот из конфига
        private Quaternion _recordToFurnitureRotModifier;   // Переводчик осей из пространства записи в мебель


        // Новая целочисленная кадровая сетка на базе стабильного FPS
        internal int _currentTransitionIndex = 1; // Человеческие индексы: 1 и 2
        internal int _gameFrameCounter = 0;
        internal int _totalTargetFrames = 0;
        internal bool _reversing = false;

        private Vector3 _baseWorldPos;
        private Quaternion _baseWorldRot;
        private Quaternion _modelRotationModifier;

        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();

        private float _speedModifier = 1.0f;
        private EaseMode _currentEaseMode = EaseMode.Linear;

        public void Play(CharacterCustomization character, string animationName, Furniture furniture, PoseData poseConfig)
        {
            // --- ХИРУРГИЧЕСКИЙ ВКОЛ: Радар создается в первую наносекунду запуска ---
            if (gameObject.GetComponent<AnimationRadar>() == null)
            {
                gameObject.AddComponent<AnimationRadar>();
            }

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

            if (furniture != null && poseConfig != null)
            {
                // 1. Сохраняем ссылку на мебель, чтобы LateUpdate знал её наклон
                _targetFurniture = furniture;

                // 2. Запоминаем стартовые локальные координаты посадки из конфига
                _localBasePos = new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z);
                _localBaseRot = Quaternion.Euler(new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z));

                // 3. Рассчитываем переводчик осей из пространства записи в локальное пространство мебели
                Quaternion recordStartRot = Quaternion.Euler(ArrayToVector3(_animData.startRot));
                _recordToFurnitureRotModifier = _localBaseRot * Quaternion.Inverse(recordStartRot);

                // 4. Физически позиционируем персонажа в мире с учетом любого наклона стула
                _character.transform.position = _targetFurniture.transform.TransformPoint(_localBasePos);
                _character.transform.rotation = _targetFurniture.transform.rotation * _localBaseRot;
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
            if (_character == null || _animData == null || _animData.deltas == null || _targetFurniture == null) return;

            // 1. Общее количество дельта-переходов в JSON
            int totalTransitions = _animData.deltas.Count;
            if (totalTransitions < 1) return;

            if (_currentTransitionIndex >= totalTransitions) _currentTransitionIndex = totalTransitions - 1;
            if (_currentTransitionIndex < 0) _currentTransitionIndex = 0;

            PoseAnimationDelta currentTransitionData = _animData.deltas[_currentTransitionIndex];

            float durationInSeconds = currentTransitionData.frames * (_animData.rate > 0 ? _animData.rate : 0.0333f);
            if (_totalTargetFrames <= 0)
            {
                float currentFps = (Time.deltaTime > 0f) ? (1f / Time.deltaTime) : 60f;
                if (currentFps < 10f) currentFps = 10f;

                _totalTargetFrames = Mathf.RoundToInt((durationInSeconds * currentFps) / _speedModifier);
                if (_totalTargetFrames <= 0) _totalTargetFrames = 1;
            }

            _gameFrameCounter++;
            float localFraction = Mathf.Clamp01((float)_gameFrameCounter / _totalTargetFrames);
            float actualFraction = _reversing ? (1f - localFraction) : localFraction;

            // Обработка режимов сглаживания (модификация фракции)
            float lerpFraction = actualFraction;
            switch (_currentEaseMode)
            {
                case EaseMode.PerFrame:
                    lerpFraction = Mathf.SmoothStep(0f, 1f, actualFraction);
                    break;
                case EaseMode.Global:
                    float totalAnimationFrames = 0f;
                    for (int i = 0; i < totalTransitions; i++) totalAnimationFrames += _animData.deltas[i].frames;
                    float passedAuthorFrames = 0f;
                    for (int i = 0; i < _currentTransitionIndex; i++) passedAuthorFrames += _animData.deltas[i].frames;

                    float currentGlobalAuthorFrame = passedAuthorFrames + (actualFraction * _animData.deltas[_currentTransitionIndex].frames);
                    float globalFraction = Mathf.Clamp01(currentGlobalAuthorFrame / totalAnimationFrames);
                    float smoothedGlobal = Mathf.SmoothStep(0f, 1f, globalFraction);

                    float currentDeltaStartGlobal = passedAuthorFrames / totalAnimationFrames;
                    float currentDeltaEndGlobal = (passedAuthorFrames + _animData.deltas[_currentTransitionIndex].frames) / totalAnimationFrames;
                    float globalDeltaDuration = currentDeltaEndGlobal - currentDeltaStartGlobal;

                    lerpFraction = globalDeltaDuration > 0 ? Mathf.Clamp01((smoothedGlobal - currentDeltaStartGlobal) / globalDeltaDuration) : 1f;
                    break;
                case EaseMode.Linear:
                default:
                    break;
            }

            // =========================================================================
            // МАТЕМАТИЧЕСКИЙ РЕНДЕР В ЛОКАЛЬНОМ ПРОСТРАНСТВЕ МЕБЕЛИ 🪑🌀
            // =========================================================================
            try
            {
                // Начинаем накопление строго с локальной базовой точки посадки на мебель
                Vector3 accumulatedLocalPos = _localBasePos;

                // Базовый поворот автора в пространстве записи (Blender/World при создании)
                Quaternion authorAccumulatedRot = Quaternion.Euler(ArrayToVector3(_animData.startRot));

                // НАКОПЛЕНИЕ ТРАЕКТОРИИ В ПРОСТРАНСТВЕ ЗАПИСИ И ЕЕ ПЕРЕВОД В ЛОКАЛЬНЫЕ ОСИ МЕБЕЛИ
                for (int i = 0; i < _currentTransitionIndex; i++)
                {
                    var prevDelta = _animData.deltas[i];

                    // Берём чистый вектор шага автора и разворачиваем его по текущему направлению записи
                    Vector3 localPosDelta = ArrayToVector3(prevDelta.endPosDelta);
                    Vector3 recordWorldPosDelta = authorAccumulatedRot * localPosDelta;

                    // Переводим вектор записи в локальную систему координат мебели через модификатор базиса!
                    Vector3 furnitureLocalPosDelta = _recordToFurnitureRotModifier * recordWorldPosDelta;
                    accumulatedLocalPos += furnitureLocalPosDelta;

                    // Накапливаем поворот в пространстве записи
                    authorAccumulatedRot *= Quaternion.Euler(ArrayToVector3(prevDelta.endRotDelta));
                }

                // Вычисляем старт и финиш для ТЕКУЩЕГО активного перехода (в локальном пространстве мебели)
                Vector3 startLocalPos = accumulatedLocalPos;
                Quaternion startLocalRot = _recordToFurnitureRotModifier * authorAccumulatedRot;

                Vector3 currentLocalPosDelta = ArrayToVector3(currentTransitionData.endPosDelta);
                Vector3 recordCurrentWorldPosDelta = authorAccumulatedRot * currentLocalPosDelta;
                Vector3 furnitureCurrentLocalPosDelta = _recordToFurnitureRotModifier * recordCurrentWorldPosDelta;

                Vector3 endLocalPos = startLocalPos + furnitureCurrentLocalPosDelta;
                Quaternion endLocalRot = _recordToFurnitureRotModifier * (authorAccumulatedRot * Quaternion.Euler(ArrayToVector3(currentTransitionData.endRotDelta)));

                // Интерполируем промежуточную ЛОКАЛЬНУЮ позицию куклы относительно стула
                Vector3 targetLocalPos = Vector3.Lerp(startLocalPos, endLocalPos, lerpFraction);
                Quaternion targetLocalRot = Quaternion.Lerp(startLocalRot, endLocalRot, lerpFraction);

                // ФИНАЛЬНЫЙ СВЕРХВКОЛ: Переводим локальные координаты мебели в глобальный мир Unity
                // Благодаря этому, если bugerry положил стул на бок, персонаж ляжет на бок вслед за ним автоматически!
                _character.transform.position = _targetFurniture.transform.TransformPoint(targetLocalPos);
                _character.transform.rotation = _targetFurniture.transform.rotation * targetLocalRot;

                // 3. ИНТЕРПОЛЯЦИЯ ВНУТРЕННИХ КОСТЕЙ СКЕЛЕТА (Остается локальной внутри тела)
                if (currentTransitionData.boneDatas != null)
                {
                    foreach (var kp in currentTransitionData.boneDatas)
                    {
                        if (_boneCache.TryGetValue(kp.Key, out Transform boneTransform) && boneTransform != null)
                        {
                            Vector3 boneStartPos = ArrayToVector3(kp.Value.startPos);
                            Vector3 boneEndPos = ArrayToVector3(kp.Value.endPos);
                            Quaternion boneStartRot = Quaternion.Euler(ArrayToVector3(kp.Value.startRot));
                            Quaternion boneEndRot = Quaternion.Euler(ArrayToVector3(kp.Value.endRot));

                            // Плавный Lerp/SmoothStep для абсолютно всех костей, включая таз
                            boneTransform.localPosition = Vector3.Lerp(boneStartPos, boneEndPos, lerpFraction);
                            boneTransform.localRotation = Quaternion.Lerp(boneStartRot, boneEndRot, lerpFraction);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SubframePlayer] Критическая ошибка наклона пивота: {ex.Message}");
            }

            // 4. АВТОМАТ СМЕНЫ ДЕЛЬТ И ЗАЦИКЛИВАНИЯ (С фиксом уплывания)
            if (_gameFrameCounter >= _totalTargetFrames)
            {
                _gameFrameCounter = 0;
                _totalTargetFrames = 0;

                if (!_reversing)
                {
                    if (_currentTransitionIndex < totalTransitions - 1)
                    {
                        _currentTransitionIndex++;
                    }
                    else
                    {
                        if (_animData.reverse) { _reversing = true; }
                        else if (_animData.loop)
                        {
                            _currentTransitionIndex = 0;
                            Plugin.Log.LogInfo($"[Engine] Бесшовный перезапуск цикла. Координаты удерживаются в базисе мебели.");
                        }
                        else { Destroy(this); return; }
                    }
                }
                else
                {
                    if (_currentTransitionIndex > 0)
                    {
                        _currentTransitionIndex--;
                    }
                    else
                    {
                        if (_animData.loop) { _reversing = false; _currentTransitionIndex = 0; }
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

            // --- УНИЧТОЖАЕМ РАДАР ВМЕСТЕ С ПЛЕЕРОМ ---
            var oldRadar = gameObject.GetComponent<AnimationRadar>();
            if (oldRadar != null)
            {
                Destroy(oldRadar);
            }

            if (_character != null && _character.anim != null)
            {
                _character.anim.enabled = true;
                _character.anim.speed = 1f;
            }
            Plugin.Log.LogWarning("[LocalPlayer] Встроенный движок выключен, управление возвращено Юнити.");
        }

    }
}
