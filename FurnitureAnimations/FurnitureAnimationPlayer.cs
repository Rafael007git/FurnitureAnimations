using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FurnitureAnimationsMod
{
    public enum EaseMode
    {
        Linear,
        Global,
        PerFrame,
        Bezier
    }

    public class FurnitureAnimationPlayer : MonoBehaviour
    {
        public static FurnitureAnimationPlayer Instance { get; private set; }

        private CharacterCustomization _character;
        internal PoseAnimationData _animData;
        internal Vector3 targetLocalPos;
        // internal Quaternion targetLocalRot;

        private Furniture _targetFurniture;                 // Ссылка на целевой объект мебели
        private bool _isPreviewMode = false;                // Флаг предпросмотра в мировых координатах Free Pose
        private Vector3 _localBasePos;                      // Стартовая локальная позиция из конфига
        private Quaternion _localBaseRot;                   // Стартовый локальный поворот из конфига
        private Quaternion _recordToFurnitureRotModifier;   // Переводчик осей из пространства записи в мебель


        // Новая целочисленная кадровая сетка на базе стабильного FPS
        internal int _currentTransitionIndex = 1; // Человеческие индексы: 1 и 2
        internal int _gameFrameCounter = 0;
        internal int _totalTargetFrames = 0;
        internal bool _reversing = false;

        // private Vector3 _baseWorldPos;
        // private Quaternion _baseWorldRot;
        // private Quaternion _modelRotationModifier;

        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();

        private float _speedModifier = 1.0f;
        private EaseMode _currentEaseMode = EaseMode.Linear;

        private void Awake()
        {
            // Появление OnGUI радара
            if (gameObject.GetComponent<AnimationRadar>() == null)
            {
                gameObject.AddComponent<AnimationRadar>();
            }
        }

        public void Play(CharacterCustomization character, string animationName, Furniture furniture, PoseData poseConfig)
        {
            Instance = this;
            _character = character;
            _isPreviewMode = false;

            // === TEMPORARY RUNTIME SPEED DEBUG SPY ===
            string trace = Environment.StackTrace;
            Plugin.Log.LogWarning($"[SPEED_BUG_DEBUG] Play() forcefully invoked! Current StackTrace:\n{trace}");

            // --- СВЕРХТОЧНЫЙ ПЕРЕХВАТ И ОЖИВЛЕНИЕ РАДАРА ---
            var existingRadar = _character.gameObject.GetComponent<AnimationRadar>();
            if (existingRadar == null)
            {
                // Если радара вообще нет на персонаже — создаем свежий
                _character.gameObject.AddComponent<AnimationRadar>();
            }
            else
            {
                // Если радар остался от прошлой анимации, мы пробиваем приватное поле '_player'
                // и принудительно записываем туда ссылку на наш текущий НОВЫЙ живой плеер!
                var radarType = existingRadar.GetType();
                var field = radarType.GetField("_player", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(existingRadar, this);
                }

                Plugin.Log.LogWarning("[Engine] Существующий радар успешно перехвачен и привязан к новой анимации!");
            }
            // -------------------------------------------------------------------------

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

            // =========================================================================
            // --- MONOLITHIC AUDIO-CHARACTER BINDING ---
            // Wipe out any old audio managers on this character to prevent overlap chaos
            var oldAudio = gameObject.GetComponent<AnimationAudioManager>();
            if (oldAudio != null)
            {
                oldAudio.StopAudio();
                DestroyImmediate(oldAudio);
            }

            // Spawn a fresh clean manager directly tied to this character lifecycle
            var audioManager = gameObject.AddComponent<AnimationAudioManager>();
            audioManager.Initialize(animationName, _animData.loop, furniture);
            // =========================================================================

            // Интерфейс теперь создается глобально через UIPose.Open, плеер его больше не спавнит! 🛡
            // gameObject.AddComponent<AnimationUiControls>().Initialize(this); 

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

                // === КРИТИЧЕСКИЙ ФИКС БАГА ПАМЯТИ МЕБЕЛИ: МГНОВЕННЫЙ СБРОС И КУПИРОВАНИЕ А-ПОЗЫ ===
                // Перед тем как выключить аниматор, мы жестко синхронизируем трансформ персонажа с живым локатором позы игры!
                Transform activeLoc = _character.curInteractionLoc != null ? _character.curInteractionLoc : _targetFurniture.transform.Find("loc");
                if (activeLoc != null)
                {
                    // Сбрасываем иерархию Юнити, временно отвязывая персонажа от старых родителей
                    // _character.transform.SetParent(null);

                    // Жестко привязываем мировые координаты куклы к новой мебели
                    _character.transform.position = activeLoc.position;
                    _character.transform.rotation = activeLoc.rotation;

                    Plugin.Log.LogInfo($"[Engine_Warp] Character safely locked onto explicit interaction locator: {activeLoc.name}");
                }
                else
                {
                    // Фаллбэк на случай отсутствия локатора
                    _character.transform.position = _targetFurniture.transform.TransformPoint(_localBasePos);
                    _character.transform.rotation = _targetFurniture.transform.rotation * _localBaseRot;
                }
            }

            // Включаем нативный аниматор на долю секунды, чтобы Unity принудительно обновил и очистил внутренний кэш матриц костей
            if (_character.anim != null)
            {
                _character.anim.enabled = true;
                _character.anim.speed = 1f;
                _character.anim.Update(0f); // Принудительный внутренний тик очистки кэша Unity! 🔄⚡

                _character.anim.applyRootMotion = false;
                _character.anim.speed = 0f;
                _character.anim.enabled = false;
            }

            _boneCache.Clear();
            CacheSkeletonRecursive(_character.transform);

            // --- НАШ ХИРУРГИЧЕСКИЙ ФИКС СТАРТОВОЙ ПОЗЫ ---
            // Передаем в метод сброса скелета не дельты первого кадра, а полноценный стартовый реестр костей из boneStartDict!
            AbsoluteSkeletalReset(_animData.boneStartDict);
            // ---------------------------------------------

            // Мягкая инициализация целочисленных состояний под нулевую базу
            _currentTransitionIndex = 0; // ИСПРАВЛЕНО НА 0
            _gameFrameCounter = 0;
            _totalTargetFrames = 0;
            _reversing = false;

            // =========================================================================
            // ЭТАП 1: ЛЕНИВОЕ СЧИТЫВАНИЕ ИЗ ОЗУ ПРИ СТАРТЕ АНИМАЦИИ 🧠⚡ 
            // =========================================================================
            if (_targetFurniture != null)
            {
                string cleanFurnName = _targetFurniture.name.Replace("(Clone)", "").Trim();

                // Извлекаем актуальную конфигурацию мебели из ОЗУ-менеджера
                if (ConfigManager.LoadedConfigs.TryGetValue(cleanFurnName, out FurnitureConfig config))
                {
                    // Защита: инициализируем пустой словарь в ОЗУ, если его не было
                    if (config.RuntimePlaybackMemory == null)
                    {
                        config.RuntimePlaybackMemory = new Dictionary<string, PlaybackSettingsData>(StringComparer.OrdinalIgnoreCase);
                    }

                    // === ИСПРАВЛЕНО: Локальный поиск аудиоменеджера без синглтона ===
                    var localAudio = gameObject.GetComponent<AnimationAudioManager>();
                    string currentTrackPath = (localAudio != null) ? localAudio.GetCurrentTrackName() : "";
                    string trackKey = string.IsNullOrEmpty(currentTrackPath) ? "noAudio" : Path.GetFileName(currentTrackPath);

                    // === КРИТИЧЕСКИЙ АРХИТЕКТУРНЫЙ РАЗДЕЛИТЕЛЬ ПАРАМЕТРОВ ОЗУ === 🎯⚙️
                    string trackSessionKey = $"{animationName}_{trackKey}";
                    string easeSessionKey = $"{animationName}_globalEase";

                    // 1. Извлекаем скорость воспроизведения (намертво привязана к конкретному аудио)
                    if (config.RuntimePlaybackMemory.TryGetValue(trackSessionKey, out PlaybackSettingsData speedSettings) && speedSettings != null)
                    {
                        _speedModifier = speedSettings.Speed;
                    }
                    else
                    {
                        // Применяем временный дефолт 150% только если скорость еще не была кастомизирована в этой сессии
                        if (_speedModifier == 1.0f)
                        {
                            _speedModifier = 1.5f;
                        }
                    }

                    // 2. Извлекаем структурное сглаживание (запечатано глобально за геометрией JSON-файла)
                    if (config.RuntimePlaybackMemory.TryGetValue(easeSessionKey, out PlaybackSettingsData easeSettings) && easeSettings != null)
                    {
                        _currentEaseMode = easeSettings.EaseMode;
                    }
                    else if (HasAnyBezier(_animData))
                    {
                        _currentEaseMode = EaseMode.Bezier;
                    }
                    else
                    {
                        // Применяем временный дефолт Linear только если EaseMode еще не был изменен в этой сессии
                        if (_speedModifier == 1.0f || _speedModifier == 1.5f)
                        {
                            _currentEaseMode = EaseMode.Linear;
                        }
                    }

                    Plugin.Log.LogInfo($"[RAM_Start] Decoupled profile load for '{animationName}'. Speed: {_speedModifier * 100}% (via track), EaseMode: {_currentEaseMode} (via global ease key)");

                    // Синхронизируем uGUI буферы хамелеона под новые примененные параметры
                    var uiControls = UnityEngine.Object.FindObjectOfType<AnimationUiControls>();
                    if (uiControls != null)
                    {
                        uiControls.UpdateInterfaceStates();
                    }
                }
            }
        }

        public void PlayPreview(CharacterCustomization character, PoseAnimationData rawData, float speed = 1.0f)
        {
            if (character == null || rawData == null || rawData.deltas == null || rawData.deltas.Count == 0)
            {
                Plugin.Log.LogError("[PreviewPlayer] Некорректные данные для предпросмотра анимации.");
                Destroy(this);
                return;
            }

            Instance = this;
            _character = character;
            _animData = rawData;
            CharacterStateHelper.SetLastAnimatedClip(character, rawData.name);
            _targetFurniture = null;
            _isPreviewMode = true;
            _speedModifier = Mathf.Clamp(speed, 0.1f, 3.0f);
            _currentEaseMode = HasAnyBezier(_animData) ? EaseMode.Bezier : EaseMode.Linear;

            var existingRadar = _character.gameObject.GetComponent<AnimationRadar>();
            if (existingRadar == null)
            {
                _character.gameObject.AddComponent<AnimationRadar>();
            }
            else
            {
                var field = existingRadar.GetType().GetField("_player", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(existingRadar, this);
            }

            _localBasePos = _character.transform.position;
            _localBaseRot = _character.transform.rotation;
            _recordToFurnitureRotModifier = Quaternion.identity;

            if (_character.anim != null)
            {
                _character.anim.enabled = true;
                _character.anim.speed = 1f;
                _character.anim.Update(0f);

                _character.anim.applyRootMotion = false;
                _character.anim.speed = 0f;
                _character.anim.enabled = false;
            }

            _boneCache.Clear();
            CacheSkeletonRecursive(_character.transform);
            AbsoluteSkeletalReset(_animData.boneStartDict);

            _currentTransitionIndex = 0;
            _gameFrameCounter = 0;
            _totalTargetFrames = 0;
            _reversing = false;

            Plugin.Log.LogInfo($"[PreviewPlayer] Запущен предпросмотр '{rawData.name ?? "Unsaved"}' (скорость {_speedModifier * 100:F0}%).");
        }

        public void ChangeSpeed(float delta)
        {
            _speedModifier = Mathf.Clamp(_speedModifier + delta, 0.1f, 3.0f);
            Plugin.Log.LogInfo($"[SpeedManager] Текущая скорость: {_speedModifier * 100:F0}%");

            // --- ЭТАП 1: МГНОВЕННАЯ ПЕРЕЗАПИСЬ ПАРАМЕТРОВ СКОРОСТИ В ОЗУ (Пункт 18 ТЗ) --- 🧠🎵
            if (_targetFurniture != null && _animData != null)
            {
                string cleanFurnName = _targetFurniture.name.Replace("(Clone)", "").Trim();

                if (ConfigManager.LoadedConfigs.TryGetValue(cleanFurnName, out FurnitureConfig config))
                {
                    if (config.RuntimePlaybackMemory == null)
                    {
                        config.RuntimePlaybackMemory = new Dictionary<string, PlaybackSettingsData>(StringComparer.OrdinalIgnoreCase);
                    }

                    // === ИСПРАВЛЕНО: Локальный поиск аудиоменеджера на кукле без синглтона ===
                    var localAudio = gameObject.GetComponent<AnimationAudioManager>();
                    string currentTrackPath = (localAudio != null) ? localAudio.GetCurrentTrackName() : "";
                    string trackKey = string.IsNullOrEmpty(currentTrackPath) ? "noAudio" : Path.GetFileName(currentTrackPath);

                    string sessionKey = $"{_animData.name}_{trackKey}";

                    if (!config.RuntimePlaybackMemory.TryGetValue(sessionKey, out PlaybackSettingsData currentSettings) || currentSettings == null)
                    {
                        currentSettings = new PlaybackSettingsData { EaseMode = _currentEaseMode };
                        config.RuntimePlaybackMemory[sessionKey] = currentSettings;
                    }

                    currentSettings.Speed = _speedModifier;
                    Plugin.Log.LogInfo($"[ОЗУ_КЛИК] Скорость {_speedModifier * 100:F0}% успешно зафиксирована в ОЗУ для пары [{sessionKey}]");
                }
            }
        }

        public float GetSpeed() => _speedModifier;

        public void ToggleEaseMode()
        {
            // 1. Переключаем режим по кругу
            _currentEaseMode = (EaseMode)(((int)_currentEaseMode + 1) % 4);
            Plugin.Log.LogWarning($"[EaseManager] Режим сглаживания изменен на: {_currentEaseMode}");

            // 2. ДОБАВЛЕНО: Мгновенно синхронизируем изменения с нашей ОЗУ-картой памяти! 🧠⚡
            if (_targetFurniture != null)
            {
                // === ИСПРАВЛЕНО: Локальный поиск имени трека без синглтона ===
                var localAudio = gameObject.GetComponent<AnimationAudioManager>();
                string trackKey = (localAudio != null) ? localAudio.GetCurrentTrackName() : "noAudio";

                ConfigManager.UpdateRuntimePlaybackMemory(
                    _targetFurniture.name,
                    GetPlayingAnimationName(),
                    trackKey,
                    _speedModifier,
                    _currentEaseMode
                );
            }
        }

        public EaseMode GetEaseMode() => _currentEaseMode;

        public string GetPlayingAnimationName() => _animData != null ? _animData.name : string.Empty;
                
        private void LateUpdate()
        {
            if (_character == null || _animData == null || _animData.deltas == null) return;
            if (!_isPreviewMode && _targetFurniture == null) return;

            // --- THE COMPACT CHARACTER-CENTRIC SANITY GUARD ---
            // We only self-destruct if the native game or Free Pose Mode forced the Animator back ON.
            // This means another system explicitly requested to take over the character's bones!
            if (_character.anim != null && _character.anim.enabled)
            {
                Plugin.Log.LogWarning($"[Engine_Sanity] Native animator was forced ON for '{_character.name}'. Self-destructing custom loop player.");
                // This safely triggers OnDestroy(), enabling native speed and cleaning up audio
                Destroy(this);
                return;
            }

            // 1. СТРОГАЯ СИСТЕМНАЯ НУЛЕВАЯ БАЗА
            int totalTransitions = _animData.deltas.Count;
            if (totalTransitions < 1) return;

            // Защита от выхода за границы индексов (строго от 0 до totalTransitions - 1)
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

            // Сглаживание фракции (EaseMode)
            float lerpFraction = actualFraction;
            bool allowBezier = (_currentEaseMode == EaseMode.Bezier);

            if (allowBezier && currentTransitionData.bezier != null && currentTransitionData.bezier.Length >= 4)
            {
                float p1x = currentTransitionData.bezier[0];
                float p1y = currentTransitionData.bezier[1];
                float p2x = currentTransitionData.bezier[2];
                float p2y = currentTransitionData.bezier[3];
                lerpFraction = EvaluateCubicBezier(actualFraction, p1x, p1y, p2x, p2y);
            }
            else
            {
                EaseMode activeTransitionEase = (_currentEaseMode == EaseMode.Bezier)
                    ? (currentTransitionData.easeMode ?? EaseMode.PerFrame)
                    : (currentTransitionData.easeMode ?? _currentEaseMode);

                switch (activeTransitionEase)
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
            }

            // =========================================================================
            // ЧИСТЫЙ ДИФФЕРЕНЦИАЛЬНЫЙ РЕНДЕР ТЕЛА (ПО КАНOНАМ AEDENTHORN) 🪑🌀
            // =========================================================================
            try
            {
                // Базовый кватернион-переводчик осей мебели (наш константный модификатор)
                Quaternion quaternionOffset = _isPreviewMode ? _localBaseRot : _recordToFurnitureRotModifier;

                // 1. ВЫЧИСЛЯЕМ СТАРТОВУЮ ТОЧКУ ТЕКУЩЕГО ПЕРЕХОДА
                Vector3 startLocalPos = _localBasePos;
                Quaternion startLocalRot = _localBaseRot;

                // Если это не самый первый переход, старт текущего шага — это финиш предыдущего!
                if (_currentTransitionIndex > 0)
                {
                    var prevDelta = _animData.deltas[_currentTransitionIndex - 1];

                    // Проекция смещения предыдущей дельты от базового нуля кровати
                    startLocalPos += quaternionOffset * ArrayToVector3(prevDelta.endPosDelta);
                    startLocalRot *= Quaternion.Euler(ArrayToVector3(prevDelta.endRotDelta));
                }

                // 2. ВЫЧИСЛЯЕМ КОНЕЧНУЮ ТОЧКУ ТЕКУЩЕГО ПЕРЕХОДА
                // Финиш всегда рассчитывается как базовый нуль плюс текущая дельта!
                Vector3 endLocalPos = _localBasePos + quaternionOffset * ArrayToVector3(currentTransitionData.endPosDelta);
                Quaternion endLocalRot = _localBaseRot * Quaternion.Euler(ArrayToVector3(currentTransitionData.endRotDelta));

                // 3. ИНТЕРПОЛЯЦИЯ И ФИНАЛЬНАЯ ПРОЕКЦИЯ В МИР UNITY
                Vector3 targetLocalPos = Vector3.LerpUnclamped(startLocalPos, endLocalPos, lerpFraction);
                Quaternion targetLocalRot = Quaternion.SlerpUnclamped(startLocalRot, endLocalRot, lerpFraction);

                if (_isPreviewMode)
                {
                    _character.transform.position = targetLocalPos;
                    _character.transform.rotation = targetLocalRot;
                }
                else
                {
                    _character.transform.position = _targetFurniture.transform.TransformPoint(targetLocalPos);
                    _character.transform.rotation = _targetFurniture.transform.rotation * targetLocalRot;
                }

                // Сохраняем в кэш для радара
                this.targetLocalPos = targetLocalPos;

                // =========================================================================
                // 4. ИНТЕРПОЛЯЦИЯ КОСТЕЙ (Остается без изменений)
                // =========================================================================
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

                            boneTransform.localPosition = Vector3.LerpUnclamped(boneStartPos, boneEndPos, lerpFraction);
                            boneTransform.localRotation = Quaternion.SlerpUnclamped(boneStartRot, boneEndRot, lerpFraction);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SubframePlayer] Ошибка каноничного рендера: {ex.Message}");
            }

            // =========================================================================
            // 4. АВТОМАТ СМЕНЫ ДЕЛЬТ (НА БАЗЕ ИНДЕКСА 0) 🎛🔄
            // =========================================================================
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
                        if (_animData.reverse)
                        {
                            _reversing = true;
                        }
                        else if (_animData.loop)
                        {
                            _currentTransitionIndex = 0; // На чистый ноль!

                            if (_isPreviewMode)
                            {
                                _character.transform.position = _localBasePos;
                                _character.transform.rotation = _localBaseRot;
                            }
                            else
                            {
                                _character.transform.position = _targetFurniture.transform.TransformPoint(_localBasePos);
                                _character.transform.rotation = _targetFurniture.transform.rotation * _localBaseRot;
                            }
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
                        if (_animData.loop)
                        {
                            _reversing = false;
                            _currentTransitionIndex = 0;
                        }
                        else { Destroy(this); return; }
                    }
                }
            }
        }

        private void AbsoluteSkeletalReset(Dictionary<string, BoneDelta> boneStartDict)
        {
            if (!_isPreviewMode && Plugin.ForceAbsoluteSkeletalReset != null && !Plugin.ForceAbsoluteSkeletalReset.Value)
                return;

            if (_character == null) return;
            ApplyBoneDictToCache(_boneCache, boneStartDict);
        }

        public static void ApplyStaticPose(CharacterCustomization character, Dictionary<string, BoneDelta> boneDict)
        {
            if (character == null) return;

            if (character.anim != null)
            {
                character.anim.applyRootMotion = false;
                character.anim.speed = 0f;
                character.anim.enabled = false;
            }

            var tempCache = new Dictionary<string, Transform>();
            CollectSkeleton(character.transform, tempCache);
            ApplyBoneDictToCache(tempCache, boneDict);
        }

        private static void ApplyBoneDictToCache(Dictionary<string, Transform> boneCache, Dictionary<string, BoneDelta> boneDict)
        {
            foreach (string boneName in DioramaConstants.AnatomyBoneRegistry)
            {
                if (!boneCache.TryGetValue(boneName, out Transform boneTrans) || boneTrans == null)
                    continue;

                if (boneDict != null && boneDict.TryGetValue(boneName, out BoneDelta delta))
                {
                    if (delta.endRot != null && delta.endRot.Length >= 4)
                        boneTrans.localRotation = new Quaternion(delta.endRot[0], delta.endRot[1], delta.endRot[2], delta.endRot[3]);
                    else if (delta.endRot != null && delta.endRot.Length == 3)
                        boneTrans.localRotation = Quaternion.Euler(delta.endRot[0], delta.endRot[1], delta.endRot[2]);
                    else
                        boneTrans.localRotation = Quaternion.identity;

                    if (delta.endPos != null && delta.endPos.Length >= 3)
                        boneTrans.localPosition = new Vector3(delta.endPos[0], delta.endPos[1], delta.endPos[2]);
                }
                else
                {
                    boneTrans.localRotation = Quaternion.identity;
                }
            }
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

        private static void CollectSkeleton(Transform parent, Dictionary<string, Transform> cache)
        {
            if (parent == null) return;
            string cleanName = parent.name;
            if (cleanName.EndsWith("eyesRoot")) cleanName = "eyesRoot";
            else if (cleanName.EndsWith("head parent")) cleanName = "head parent";
            else if (cleanName.EndsWith("head target")) cleanName = "head target";

            if (!cache.ContainsKey(cleanName)) cache[cleanName] = parent;

            for (int i = 0; i < parent.childCount; i++)
                CollectSkeleton(parent.GetChild(i), cache);
        }

        private Vector3 ArrayToVector3(float[] arr)
        {
            if (arr == null || arr.Length < 3) return Vector3.zero;
            return new Vector3(arr[0], arr[1], arr[2]);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // Clean up the specific audio manager bound to this character instance
            var boundAudio = gameObject.GetComponent<AnimationAudioManager>();
            if (boundAudio != null)
            {
                boundAudio.StopAudio();
                DestroyImmediate(boundAudio);
            }

            if (_character != null && _character.anim != null)
            {
                _character.anim.enabled = true;
                _character.anim.speed = 1f;
            }
            Plugin.Log.LogWarning("[LocalPlayer] Animation engine deactivated, control returned to native Unity.");
        }

        private static bool HasAnyBezier(PoseAnimationData data)
        {
            if (data == null) return false;
            if (data.easeMode == EaseMode.Bezier) return true;
            if (data.deltas != null)
            {
                for (int i = 0; i < data.deltas.Count; i++)
                {
                    if (data.deltas[i] != null && data.deltas[i].bezier != null && data.deltas[i].bezier.Length >= 4)
                        return true;
                }
            }
            return false;
        }

        private static float EvaluateCubicBezier(float t, float x1, float y1, float x2, float y2)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;

            x1 = Mathf.Clamp01(x1);
            x2 = Mathf.Clamp01(x2);

            float cx = 3f * x1;
            float bx = 3f * (x2 - x1) - cx;
            float ax = 1f - cx - bx;

            float cy = 3f * y1;
            float by = 3f * (y2 - y1) - cy;
            float ay = 1f - cy - by;

            // 1. Метод Ньютона-Рафсона для быстрого нахождения параметра u по X(u) = t
            float u = t;
            for (int i = 0; i < 6; i++)
            {
                float xEst = ((ax * u + bx) * u + cx) * u - t;
                if (Mathf.Abs(xEst) < 1e-5f) break;
                float dx = (3f * ax * u + 2f * bx) * u + cx;
                if (Mathf.Abs(dx) < 1e-5f) break;
                u -= xEst / dx;
            }

            // 2. Фолбэк бинарным поиском, если касательная была слишком крутой
            if (u < 0f || u > 1f)
            {
                float low = 0f, high = 1f;
                u = t;
                for (int i = 0; i < 12; i++)
                {
                    float xEst = ((ax * u + bx) * u + cx) * u;
                    if (Mathf.Abs(xEst - t) < 1e-5f) break;
                    if (xEst < t) low = u;
                    else high = u;
                    u = 0.5f * (low + high);
                }
            }

            u = Mathf.Clamp01(u);
            return ((ay * u + by) * u + cy) * u;
        }

        public void RestartFromBeginning()
        {
            if (_animData == null) return;

            _currentTransitionIndex = 0;
            _gameFrameCounter = 0;
            _totalTargetFrames = 0;
            _reversing = false;

            if (_animData.boneStartDict != null)
            {
                AbsoluteSkeletalReset(_animData.boneStartDict);
            }

            if (_character != null)
            {
                if (_isPreviewMode)
                {
                    _character.transform.position = _localBasePos;
                    _character.transform.rotation = _localBaseRot;
                }
                else if (_targetFurniture != null)
                {
                    _character.transform.position = _targetFurniture.transform.TransformPoint(_localBasePos);
                    _character.transform.rotation = _targetFurniture.transform.rotation * _localBaseRot;
                }
            }
        }

    }
}
