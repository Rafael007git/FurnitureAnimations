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

        /// <summary>
        /// Вызывается при старте анимации мебели. Сканирует папку на наличие треков с суффиксами и запускает случайный.
        /// </summary>
        public void Initialize(string animationName, bool loopAudio)
        {
            _loopAudio = loopAudio;

            // Прерываем предыдущую загрузку, если она шла, и останавливаем старый трек
            if (_currentLoadCoroutine != null) StopCoroutine(_currentLoadCoroutine);
            if (_audioSource != null && _audioSource.isPlaying) _audioSource.Stop();

            _currentPlaylist.Clear();
            _currentTrackIndex = -1;

            // --- ЕСЛИ СТОИТ РЕЖИМ ТИШИНЫ, ДАЖЕ НЕ СКАНИРУЕМ И НЕ ВКЛЮЧАЕМ ---
            if (_isGlobalMuted)
            {
                Plugin.Log.LogInfo("[AudioEngine] Старт анимации заблокирован глобальным Mute.");
                return;
            }

            string audioFolder = Path.Combine(BepInEx.Paths.PluginPath, "FurnitureAnimations", "Audio");
            if (!Directory.Exists(audioFolder)) Directory.CreateDirectory(audioFolder);

            if (Directory.Exists(audioFolder))
            {
                string[] files = Directory.GetFiles(audioFolder, animationName + "*.*");
                foreach (string file in files)
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (ext == ".wav" || ext == ".mp3" || ext == ".ogg")
                    {
                        _currentPlaylist.Add(file);
                    }
                }
            }

            if (_currentPlaylist.Count > 0)
            {
                // Выбираем случайный трек из найденных для старта
                _currentTrackIndex = UnityEngine.Random.Range(0, _currentPlaylist.Count);
                string selectedTrack = _currentPlaylist[_currentTrackIndex];

                _currentLoadCoroutine = StartCoroutine(LoadAndPlayAudio(selectedTrack, GetAudioType(selectedTrack)));
                Plugin.Log.LogInfo($"[AudioEngine] Найдено треков: {_currentPlaylist.Count}. Случайно выбран: {Path.GetFileName(selectedTrack)}");
            }
            else
            {
                Plugin.Log.LogWarning($"[AudioEngine] Не найдено аудиофайлов для анимации: {animationName}");
            }
        }

        /// <summary>
        /// Публичный метод для кнопки "Next Audio". Переключает на следующий трек в плейлисте по кругу.
        /// </summary>
        public void PlayNextTrack()
        {
            if (_currentPlaylist == null || _currentPlaylist.Count <= 1)
            {
                Plugin.Log.LogInfo("[AudioEngine] Переключение невозможно: в текущем плейлисте недостаточно треков.");
                return;
            }

            if (_currentLoadCoroutine != null) StopCoroutine(_currentLoadCoroutine);
            if (_audioSource != null) _audioSource.Stop();

            // Сдвигаем индекс по кругу
            _currentTrackIndex = (_currentTrackIndex + 1) % _currentPlaylist.Count;
            string nextTrackPath = _currentPlaylist[_currentTrackIndex];

            _currentLoadCoroutine = StartCoroutine(LoadAndPlayAudio(nextTrackPath, GetAudioType(nextTrackPath)));
            Plugin.Log.LogInfo($"[AudioEngine] Кнопка Next: переключено на {Path.GetFileName(nextTrackPath)} ({_currentTrackIndex + 1}/{_currentPlaylist.Count})");
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
            _isGlobalMuted = !_isGlobalMuted; // Переключаем глобальный флаг

            if (_audioSource != null)
            {
                if (_isGlobalMuted)
                {
                    _audioSource.Stop(); // Принудительно глушим текущий трек, если он играл
                    if (_currentLoadCoroutine != null) StopCoroutine(_currentLoadCoroutine);
                }
                else
                {
                    // Если Mute сняли, запускаем логику заново для текущего плейлиста
                    if (_currentTrackIndex >= 0 && _currentTrackIndex < _currentPlaylist.Count)
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
            _currentTrackIndex = -1; // Сбрасываем индекс плейлиста
            _currentPlaylist.Clear();
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
