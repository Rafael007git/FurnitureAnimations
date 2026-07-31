using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

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
        // Синглтон для управления из UI
        public static FurnitureAnimationPlayer Instance { get; private set; }

        private CharacterCustomization _character;
        private PoseAnimationData _animData;
        private AudioSource _audioSource;

        private float _deltaTime = 0f;
        private int _currentDelta = 0;
        private int _currentFrame = 0;
        private bool _reversing = false;

        private Vector3 _baseWorldPos;
        private Quaternion _baseWorldRot;
        private Quaternion _modelRotationModifier;

        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();

        // Модификаторы рантайма
        private float _speedModifier = 1.0f;
        private EaseMode _currentEaseMode = EaseMode.Linear;

        public void Play(CharacterCustomization character, string animationName, Furniture furniture, PoseData poseConfig)
        {
            Instance = this;
            _character = character;

            // 1. Загрузка JSON анимации
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

            // УЛЬТИМАТИВНЫЙ ПОИСК АУДИО
            string audioFolder = Path.Combine(BepInEx.Paths.PluginPath, "FurnitureAnimations", "Audio");
            if (!Directory.Exists(audioFolder)) Directory.CreateDirectory(audioFolder);

            string matchedAudioPath = null;
            AudioType detectedType = AudioType.UNKNOWN;
            string[] extensions = new string[] { ".wav", ".mp3", ".ogg" };
            AudioType[] types = new AudioType[] { AudioType.WAV, AudioType.MPEG, AudioType.OGGVORBIS };

            for (int i = 0; i < extensions.Length; i++)
            {
                string checkPath = Path.Combine(audioFolder, animationName + extensions[i]);
                if (File.Exists(checkPath))
                {
                    matchedAudioPath = checkPath;
                    detectedType = types[i];
                    break;
                }
            }

            if (!string.IsNullOrEmpty(matchedAudioPath))
            {
                StartCoroutine(LoadAndPlayAudio(matchedAudioPath, detectedType));
            }

            // 2. Применение оффсетов мебели
            if (furniture != null && poseConfig != null)
            {
                _baseWorldPos = furniture.transform.TransformPoint(new Vector3(poseConfig.LocPosition.x, poseConfig.LocPosition.y, poseConfig.LocPosition.z));
                _baseWorldRot = furniture.transform.rotation * Quaternion.Euler(new Vector3(poseConfig.LocRotation.x, poseConfig.LocRotation.y, poseConfig.LocRotation.z));
                _modelRotationModifier = _baseWorldRot * Quaternion.Inverse(Quaternion.Euler(ArrayToVector3(_animData.startRot)));
                _character.transform.position = _baseWorldPos;
                _character.transform.rotation = _baseWorldRot;
            }

            // 3. Замораживаем Animator Юнити
            if (_character.anim != null)
            {
                _character.anim.applyRootMotion = false;
                _character.anim.speed = 0f;
                _character.anim.enabled = false;
            }

            _boneCache.Clear();
            CacheSkeletonRecursive(_character.transform);

            Dictionary<string, BoneDelta> firstFrameDatas = null;
            if (_animData != null && _animData.deltas != null && _animData.deltas.Count > 0)
            {
                firstFrameDatas = _animData.deltas[0].boneDatas;
            }

            AbsoluteSkeletalReset(firstFrameDatas);

            _deltaTime = 0f;
            _currentDelta = 0;
            _currentFrame = 0;
            _reversing = false;
        }
        // Публичные методы управления скоростью для UI
        public void ChangeSpeed(float delta)
        {
            _speedModifier = Mathf.Clamp(_speedModifier + delta, 0.1f, 3.0f);
            Plugin.Log.LogInfo($"[SpeedManager] Текущая скорость: {_speedModifier * 100:F0}%");
        }

        public float GetSpeed() => _speedModifier;

        // Публичные методы переключения сглаживания для UI
        public void ToggleEaseMode()
        {
            _currentEaseMode = (EaseMode)(((int)_currentEaseMode + 1) % 3);
            Plugin.Log.LogWarning($"[EaseManager] Режим сглаживания изменен на: {_currentEaseMode}");
        }

        public EaseMode GetEaseMode() => _currentEaseMode;

        // Функция математического расчета сглаживания (SmoothStep)
        private float ApplyEasing(float fraction)
        {
            switch (_currentEaseMode)
            {
                case EaseMode.Global:
                    return fraction * fraction * (3f - 2f * fraction);

                case EaseMode.PerFrame:
                    return Mathf.SmoothStep(0f, 1f, fraction);

                case EaseMode.Linear:
                default:
                    return fraction;
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

        private System.Collections.IEnumerator LoadAndPlayAudio(string filePath, AudioType type)
        {
            string fileUri = "file://" + filePath.Replace("\\", "/");

            using (UnityWebRequest multimediaRequest = UnityWebRequestMultimedia.GetAudioClip(fileUri, type))
            {
                yield return multimediaRequest.SendWebRequest();

                if (multimediaRequest.result == UnityWebRequest.Result.ConnectionError || multimediaRequest.result == UnityWebRequest.Result.ProtocolError)
                {
                    Plugin.Log.LogError($"[AudioEngine] Ошибка UnityWebRequest: {multimediaRequest.error}");
                }
                else
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(multimediaRequest);
                    if (clip != null)
                    {
                        _audioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                        _audioSource.clip = clip;
                        _audioSource.loop = _animData.loop;

                        _audioSource.bypassEffects = true;
                        _audioSource.bypassListenerEffects = true;
                        _audioSource.priority = 0;
                        _audioSource.spatialBlend = 0f;
                        _audioSource.volume = 1.0f;

                        _audioSource.Play();
                        Plugin.Log.LogWarning($"[AudioEngine] Звуковой файл '{clip.name}' запущен!");
                    }
                }
            }
        }

        public string GetPlayingAnimationName()
        {
            return _animData != null ? _animData.name : string.Empty;
        }
        private void LateUpdate()
        {
            if (_character == null || _animData == null) return;

            // Накопление времени с учетом модификатора скорости воспроизведения
            _deltaTime += Time.deltaTime * _speedModifier;
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

                // Расчет коэффициента интерполяции с применением сглаживания
                float rawFraction = (float)(_currentFrame + 1) / (float)currentDeltaData.frames;
                float lerpFraction = ApplyEasing(rawFraction);

                // Динамический расчет движения корпуса персонажа
                Vector3 startFramePos = _baseWorldPos;
                Quaternion startFrameRot = _baseWorldRot;

                if (_currentDelta > 0)
                {
                    startFrameRot *= Quaternion.Euler(ArrayToVector3(_animData.deltas[_currentDelta - 1].endRotDelta));
                    startFramePos += _modelRotationModifier * ArrayToVector3(_animData.deltas[_currentDelta - 1].endPosDelta);
                }

                Quaternion endFrameRot = _baseWorldRot * Quaternion.Euler(ArrayToVector3(currentDeltaData.endRotDelta));
                Vector3 endFramePos = _baseWorldPos + _modelRotationModifier * ArrayToVector3(currentDeltaData.endPosDelta);

                _character.transform.position = Vector3.Lerp(startFramePos, endFramePos, lerpFraction);
                _character.transform.rotation = Quaternion.Lerp(startFrameRot, endFrameRot, lerpFraction);

                // Плавный Lerp для всех остальных костей скелета
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

            for (int i = 0; i < parent.childCount; i++)
                CacheSkeletonRecursive(parent.GetChild(i));
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

            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
                Plugin.Log.LogInfo("[AudioEngine] Музыка интерактива успешно остановлена.");
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
