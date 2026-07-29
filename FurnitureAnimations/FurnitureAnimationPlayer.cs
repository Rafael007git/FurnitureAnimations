using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking; // Обязательно для загрузки аудио

namespace FurnitureAnimationsMod
{
    public class FurnitureAnimationPlayer : MonoBehaviour
    {
        private CharacterCustomization _character;
        private PoseAnimationData _animData;
        private AudioSource _audioSource; // Наш звуковой движок

        private float _deltaTime = 0f;
        private int _currentDelta = 0;
        private int _currentFrame = 0;
        private bool _reversing = false;

        private Vector3 _baseWorldPos;
        private Quaternion _baseWorldRot;
        private Quaternion _modelRotationModifier;

        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();

        public void Play(CharacterCustomization character, string animationName, Furniture furniture, PoseData poseConfig)
        {
            _character = character;

            // 1. Загрузка JSON анимации (твой рабочий код)
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
            // УЛЬТИМАТИВНЫЙ ПОИСК АУДИО В ПАПКЕ ВАШЕГО МОДА 🎵
            // =========================================================================
            // Ищем музыку в папке ВАШЕГО мода, внутри подпапки "Audio"
            string myModFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string audioFolder = Path.Combine(BepInEx.Paths.PluginPath, "FurnitureAnimations", "Audio");

            // На всякий случай создаем папку Audio, если игрок её забыл сделать
            if (!Directory.Exists(audioFolder))
            {
                Directory.CreateDirectory(audioFolder);
            }

            Plugin.Log.LogWarning($"[AudioDebug] Поиск звука для анимации '{animationName}'");
            Plugin.Log.LogInfo($"[AudioDebug] Ожидаемый путь к папке: {audioFolder}");

            string matchedAudioPath = null;
            AudioType detectedType = AudioType.UNKNOWN;

            string[] extensions = new string[] { ".wav", ".mp3", ".ogg" };
            AudioType[] types = new AudioType[] { AudioType.WAV, AudioType.MPEG, AudioType.OGGVORBIS };

            for (int i = 0; i < extensions.Length; i++)
            {
                string checkPath = Path.Combine(audioFolder, animationName + extensions[i]);
                bool exists = File.Exists(checkPath);

                Plugin.Log.LogInfo($"[AudioDebug] Проверка: {animationName + extensions[i]} -> {exists}");

                if (exists)
                {
                    matchedAudioPath = checkPath;
                    detectedType = types[i];
                    break;
                }
            }

            if (!string.IsNullOrEmpty(matchedAudioPath))
            {
                Plugin.Log.LogWarning($"[AudioDebug] Файл найден! Запускаем стриминг: {Path.GetFileName(matchedAudioPath)}");
                StartCoroutine(LoadAndPlayAudio(matchedAudioPath, detectedType));
            }
            else
            {
                Plugin.Log.LogError($"[AudioDebug] Музыка не найдена. Положите файл '{animationName}.mp3' (или .wav/.ogg) в папку: {audioFolder}");
            }
            // =========================================================================


            // 2. Применение оффсетов мебели (твой рабочий код)
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

            // =========================================================================
            // ВНЕДРЕНИЕ: Абсолютный сброс скелета перед стартом анимации 🌟
            // =========================================================================
            Dictionary<string, BoneDelta> firstFrameDatas = null;
            if (_animData != null && _animData.deltas != null && _animData.deltas.Count > 0)
            {
                // Берем boneDatas из самого первого кадра в списке deltas
                firstFrameDatas = _animData.deltas[0].boneDatas;
            }

            // Вызываем очистку скелета
            AbsoluteSkeletalReset(firstFrameDatas);
            // =========================================================================

            _deltaTime = 0f;
            _currentDelta = 0;
            _currentFrame = 0;
            _reversing = false;
        }

        private void AbsoluteSkeletalReset(Dictionary<string, BoneDelta> firstFrameBoneDatas)
        {
            if (_character == null) return;

            // Проходим по абсолютно ВСЕМ анатомическим костям из реестра DioramaConstants
            foreach (string boneName in DioramaConstants.AnatomyBoneRegistry)
            {
                // Используем твой готовый _boneCache вместо тяжелого поиска!
                if (!_boneCache.TryGetValue(boneName, out Transform boneTrans))
                    continue;

                // Принудительно стираем ЛЮБОЙ старый поворот от предыдущей позы (убирает вертикальный наклон hips)
                boneTrans.localRotation = Quaternion.identity;

                // Если в первом кадре новой анимации есть данные для этой конкретной кости:
                if (firstFrameBoneDatas != null && firstFrameBoneDatas.TryGetValue(boneName, out BoneDelta delta))
                {
                    // Накатываем точные углы поворота из JSON поверх сброшенного нуля
                    if (delta.endRot != null && delta.endRot.Length >= 4)
                    {
                        boneTrans.localRotation = new Quaternion(delta.endRot[0], delta.endRot[1], delta.endRot[2], delta.endRot[3]);
                    }
                    else if (delta.endRot != null && delta.endRot.Length == 3) // На случай углов Эйлера
                    {
                        boneTrans.localRotation = Quaternion.Euler(delta.endRot[0], delta.endRot[1], delta.endRot[2]);
                    }

                    // Накатываем точную локальную позицию (высоту/смещение якоря) из JSON
                    if (delta.endPos != null && delta.endPos.Length >= 3)
                    {
                        boneTrans.localPosition = new Vector3(delta.endPos[0], delta.endPos[1], delta.endPos[2]);
                    }
                }
            }

            Plugin.Log.LogInfo($"[FurnitureAnimations] Скелет {_character.name} успешно очищен от старых поз. Якорь 'hip' выставлен в первый кадр.");
        }


        // КОРУТИНА СТРИМИНГА ЗВУКА С ДИСКА
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

                        // НАСТРОЙКИ ПРОБИТИЯ ИГРОВОГО ЭМБИЕНТА 📣
                        _audioSource.bypassEffects = true;       // Игнорировать глобальные эффекты зоны игры
                        _audioSource.bypassListenerEffects = true;
                        _audioSource.priority = 0;               // Максимальный приоритет в движке Unity (0 = первый)

                        // Делаем звук 2D, чтобы он играл на полную мощность прямо в уши игрока,
                        // пока мы тестируем и отлаживаем корутину (потом вернем 3D, если нужно)
                        _audioSource.spatialBlend = 0f;
                        _audioSource.volume = 1.0f;              // Полная громкость

                        _audioSource.Play();
                        Plugin.Log.LogWarning($"[AudioEngine] 🎉 Звуковой файл '{clip.name}' успешно пробился в рантайм и запущен!");
                    }
                }
            }
        }


        // Возвращает имя текущей проигрываемой JSON-анимации.
        public string GetPlayingAnimationName()
        {
            return _animData != null ? _animData.name : string.Empty;
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
            // ТУШИМ ЗВУК: Чтобы музыка не продолжала орать в воздухе
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