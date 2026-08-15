using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace FurnitureAnimationsMod
{
    public class AnimationAudioManager : MonoBehaviour
    {
        public static AnimationAudioManager Instance { get; private set; }

        // --- КРИТИЧЕСКОЕ ИЗМЕНЕНИЕ: Статическая переменная сохраняет состояние глобально ---
        private static bool _isGlobalMuted = false;

        private AudioSource _audioSource;
        private bool _loopAudio = false;
        private string _lastInitializedAnimation = "";

        // Поля для поддержки списков воспроизведения и переключения
        private List<string> _currentPlaylist = new List<string>();
        private int _currentTrackIndex = -1;
        private Coroutine _currentLoadCoroutine;

        private void Awake()
        {
            // Синглтон инициализируем строго один раз при создании компонента
            if (Instance == null)
            {
                Instance = this;
            }

            _audioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        }


        public void Initialize(string animationName, bool loopAudio)
        {
            _loopAudio = loopAudio;
            _lastInitializedAnimation = animationName; // Запоминаем имя для возможного UnMute позже

            if (_currentLoadCoroutine != null) StopCoroutine(_currentLoadCoroutine);
            if (_audioSource != null && _audioSource.isPlaying) _audioSource.Stop();

            _currentPlaylist.Clear();
            _currentTrackIndex = -1;

            // --- ЕСЛИ СТОИТ РЕЖИМ ТИШИНЫ, НЕ ЗАПУСКАЕМ ТРЕК ---
            if (_isGlobalMuted)
            {
                Plugin.Log.LogInfo("[AudioEngine] Старт анимации без звука (активен глобальный Mute).");
                return;
            }

            ScanAndPlay(animationName);
        }

        // Внутренний метод сканирования папки и запуска (вынесен для переиспользования при UnMute)
        public void ScanAndPlay(string animationName)
        {
            string audioFolder = Path.Combine(BepInEx.Paths.PluginPath, "FurnitureAnimations", "Audio");
            if (!Directory.Exists(audioFolder)) Directory.CreateDirectory(audioFolder);

            string[] files = Directory.GetFiles(audioFolder, animationName + "*.*");
            foreach (string file in files)
            {
                string ext = Path.GetExtension(file).ToLower();
                if (ext == ".wav" || ext == ".mp3" || ext == ".ogg")
                {
                    _currentPlaylist.Add(file);
                }
            }

            if (_currentPlaylist.Count > 0)
            {
                _currentTrackIndex = UnityEngine.Random.Range(0, _currentPlaylist.Count);
                string selectedTrack = _currentPlaylist[_currentTrackIndex];

                _currentLoadCoroutine = StartCoroutine(LoadAndPlayAudio(selectedTrack, GetAudioType(selectedTrack)));
                Plugin.Log.LogInfo($"[AudioEngine] Запущено воспроизведение: {Path.GetFileName(selectedTrack)}");
            }
        }

        public void PlayNextTrack()
        {
            // ПРАВКА СЛУЧАЯ 2: Если стоял Mute, принудительно выключаем его
            if (_isGlobalMuted)
            {
                _isGlobalMuted = false;
                Plugin.Log.LogInfo("[AudioEngine] Нажата кнопка Next Track: глобальный Mute автоматически снят.");

                // Переинициализируем плейлист для текущей анимации, если он был пуст из-за Mute
                if (_currentPlaylist.Count == 0 && !string.IsNullOrEmpty(_lastInitializedAnimation))
                {
                    ScanAndPlay(_lastInitializedAnimation);
                    return;
                }
            }

            if (_currentPlaylist == null || _currentPlaylist.Count <= 1)
            {
                Plugin.Log.LogInfo("[AudioEngine] Переключение невозможно: в текущем плейлисте недостаточно треков.");
                return;
            }

            if (_currentLoadCoroutine != null) StopCoroutine(_currentLoadCoroutine);
            if (_audioSource != null) _audioSource.Stop();

            _currentTrackIndex = (_currentTrackIndex + 1) % _currentPlaylist.Count;
            string nextTrackPath = _currentPlaylist[_currentTrackIndex];

            _currentLoadCoroutine = StartCoroutine(LoadAndPlayAudio(nextTrackPath, GetAudioType(nextTrackPath)));
            Plugin.Log.LogInfo($"[AudioEngine] Переключено на {Path.GetFileName(nextTrackPath)} ({_currentTrackIndex + 1}/{_currentPlaylist.Count})");

            // --- ЭТАП 1: ТРИГГЕР МУЗЫКАЛЬНОГО АВТОМАТА ПРИ СМЕНЕ ТРЕКА (Пункт 24-25 ТЗ) --- 🎼🚀
            UIPose uiPoseNext = GameObject.FindObjectOfType<UIPose>();
            FurnitureAnimationPlayer activePlayer = GameObject.FindObjectOfType<FurnitureAnimationPlayer>();

            if (uiPoseNext != null && uiPoseNext.curFurniture != null && activePlayer != null && activePlayer.isActiveAndEnabled)
            {
                string cleanFurnName = uiPoseNext.curFurniture.name.Replace("(Clone)", "").Trim();

                // Опрашиваем наш центральный ОЗУ-кэш LoadedConfigs
                if (ConfigManager.LoadedConfigs.TryGetValue(cleanFurnName, out FurnitureConfig config) && config != null && config.RuntimePlaybackMemory != null)
                {
                    string activeAnimName = activePlayer.GetPlayingAnimationName();

                    // ФИКС РАССИНХРОНА: Берем чистое имя файла СИНХРОННО прямо из вычисленного пути,
                    // не дожидаясь, пока асинхронная корутина обновит AudioSource!
                    string newTrackKey = Path.GetFileName(nextTrackPath);
                    string sessionKey = $"{activeAnimName}_{newTrackKey}";

                    if (config.RuntimePlaybackMemory.TryGetValue(sessionKey, out PlaybackSettingsData savedSettings) && savedSettings != null)
                    {
                        // А) Легальная связка найдена в ОЗУ: применяем её параметры!
                        activePlayer.ChangeSpeed(savedSettings.Speed - activePlayer.GetSpeed());
                        // activePlayer.SetEaseMode(savedSettings.EaseMode); 

                        Plugin.Log.LogInfo($"[Автомат_ОЗУ] Трек изменился! Из памяти применена пара [{sessionKey}]: Скорость={savedSettings.Speed * 100}%");
                    }
                    else
                    {
                        // Б) ЖЕСТКИЙ БЛОК ФАНТОМОВ ПО ТЗ (Путь А): 
                        // Если этой пары нет в изначальном слепке мебели, включаем временный дефолт,
                        // но КАТЕГОРИЧЕСКИ НЕ ЗАСОРЯЕМ ОЗУ-словарь фантомной строкой!
                        activePlayer.ChangeSpeed(1.5f - activePlayer.GetSpeed());

                        Plugin.Log.LogWarning($"[Автомат_ОЗУ_ФАНТОМ] Для пары [{sessionKey}] применен временный дефолт. Запись в ОЗУ заблокирована.");
                    }

                    // Мгновенно заставляем нашу UI-панель обновить цифры на кнопках скорости, чтобы интерфейс не врал
                    AnimationUiControls uiControls = GameObject.FindObjectOfType<AnimationUiControls>();
                    if (uiControls != null)
                    {
                        uiControls.UpdateInterfaceStates();
                    }
                }
            }
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
                    if (clip != null && _audioSource != null)
                    {
                        _audioSource.clip = clip;
                        _audioSource.loop = _loopAudio;
                        _audioSource.bypassEffects = true;
                        _audioSource.bypassListenerEffects = true;
                        _audioSource.priority = 0;
                        _audioSource.spatialBlend = 0f;
                        _audioSource.volume = 1.0f;
                        _audioSource.mute = _isGlobalMuted;
                        _audioSource.Play();
                        Plugin.Log.LogWarning($"[AudioEngine] Звуковой файл '{clip.name}' запущен!");
                    }
                }
            }
            _currentLoadCoroutine = null;
        }

        private AudioType GetAudioType(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            if (ext == ".wav") return AudioType.WAV;
            if (ext == ".ogg") return AudioType.OGGVORBIS;
            if (ext == ".mp3") return AudioType.MPEG;
            return AudioType.UNKNOWN;
        }

        public void ToggleMute()
        {
            _isGlobalMuted = !_isGlobalMuted;

            if (_audioSource != null)
            {
                if (_isGlobalMuted)
                {
                    _audioSource.Stop();
                    if (_currentLoadCoroutine != null) StopCoroutine(_currentLoadCoroutine);
                }
                else
                {
                    // ПРАВКА СЛУЧАЯ 1: Если Mute сняли, а плейлист пуст — сканируем и запускаем
                    if (_currentPlaylist.Count == 0 && !string.IsNullOrEmpty(_lastInitializedAnimation))
                    {
                        ScanAndPlay(_lastInitializedAnimation);
                    }
                    // Если плейлист уже был готов, просто перезапускаем текущий трек
                    else if (_currentTrackIndex >= 0 && _currentTrackIndex < _currentPlaylist.Count)
                    {
                        string currentTrack = _currentPlaylist[_currentTrackIndex];
                        _currentLoadCoroutine = StartCoroutine(LoadAndPlayAudio(currentTrack, GetAudioType(currentTrack)));
                    }
                }
            }
            Plugin.Log.LogInfo($"[AudioEngine] Глобальный режим тишины: {(_isGlobalMuted ? "ВКЛ" : "ВЫКЛ")}");
        }


        public bool IsMuted() => _isGlobalMuted;

        public void StopAudio()
        {
            if (_currentLoadCoroutine != null) StopCoroutine(_currentLoadCoroutine);
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
        }

        // =========================================================================
        // ЭТАП 1: ГЕТТЕР АКТУАЛЬНОГО ИМЕНИ ФАЙЛА ДЛЯ ЛЕНИВОГО СЛОВАРЯ ОЗУ 🧠🎵
        // =========================================================================
        public string GetCurrentTrackName()
        {
            // Если включен режим тишины или плейлист пуст — это жесткий кейс "noAudio" по ТЗ!
            if (_isGlobalMuted || _currentPlaylist == null || _currentPlaylist.Count == 0 || _currentTrackIndex < 0 || _currentTrackIndex >= _currentPlaylist.Count)
            {
                return "noAudio";
            }

            // Возвращаем чистое имя файла с расширением (например, "danceBachata-01.ogg")
            return Path.GetFileName(_currentPlaylist[_currentTrackIndex]);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
                Plugin.Log.LogInfo("[AudioEngine] Музыка интерактива успешно остановлена.");
            }
        }
    }
}
