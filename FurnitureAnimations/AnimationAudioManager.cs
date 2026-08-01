using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace FurnitureAnimationsMod
{
    public class AnimationAudioManager : MonoBehaviour
    {
        public static AnimationAudioManager Instance { get; private set; }

        private AudioSource _audioSource;
        private List<string> _audioPlaylist = new List<string>();
        private int _currentTrackIndex = 0;
        private bool _isMuted = false;
        private bool _loopPlaylist = false;

        public void Initialize(string animDataModName, bool loopPlaylist)
        {
            Instance = this;
            _loopPlaylist = loopPlaylist;

            string audioFolder = Path.Combine(BepInEx.Paths.PluginPath, "FurnitureAnimations", "Audio");
            if (!Directory.Exists(audioFolder)) Directory.CreateDirectory(audioFolder);

            _audioPlaylist.Clear();
            _currentTrackIndex = 0;

            string specificAudioDir = Path.Combine(audioFolder, animDataModName);
            string[] extensions = new string[] { "*.wav", "*.mp3", "*.ogg" };

            // Сканируем папку с именем позы (для поддержки нескольких треков)
            if (Directory.Exists(specificAudioDir))
            {
                foreach (string ext in extensions)
                {
                    _audioPlaylist.AddRange(Directory.GetFiles(specificAudioDir, ext));
                }
            }

            // Если папки нет или она пуста, ищем одиночный трек в корне
            if (_audioPlaylist.Count == 0)
            {
                foreach (string ext in extensions)
                {
                    string singleTrack = Path.Combine(audioFolder, animDataModName + ext.Substring(1));
                    if (File.Exists(singleTrack)) _audioPlaylist.Add(singleTrack);
                }
            }

            if (_audioPlaylist.Count > 0)
            {
                StartNextTrack();
            }
        }

        private void StartNextTrack()
        {
            if (_audioPlaylist.Count == 0) return;

            if (_currentTrackIndex >= _audioPlaylist.Count)
            {
                if (_loopPlaylist) _currentTrackIndex = 0;
                else return;
            }

            string trackPath = _audioPlaylist[_currentTrackIndex];
            AudioType type = AudioType.UNKNOWN;

            if (trackPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) type = AudioType.WAV;
            else if (trackPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) type = AudioType.MPEG;
            else if (trackPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) type = AudioType.OGGVORBIS;

            StartCoroutine(LoadAndPlayAudio(trackPath, type));
            _currentTrackIndex++;
        }

        private System.Collections.IEnumerator LoadAndPlayAudio(string filePath, AudioType type)
        {
            string fileUri = "file://" + filePath.Replace("\\", "/");

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(fileUri, type))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
                {
                    Plugin.Log.LogError($"[Audio] WebRequest error: {request.error}");
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip != null)
                {
                    if (_audioSource == null)
                    {
                        _audioSource = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
                    }
                    _audioSource.clip = clip;
                    _audioSource.loop = (_audioPlaylist.Count == 1) && _loopPlaylist; // Loop встроенный только если файл один

                    _audioSource.bypassEffects = true;
                    _audioSource.bypassListenerEffects = true;
                    _audioSource.spatialBlend = 0f;
                    _audioSource.volume = 1.0f;
                    _audioSource.mute = _isMuted;

                    _audioSource.Play();
                    Plugin.Log.LogInfo($"[Audio] Запущен трек: '{clip.name}'");

                    // Если треков несколько, следим за концом для переключения
                    if (_audioPlaylist.Count > 1)
                    {
                        StopCoroutine("TrackEndWatcher");
                        StartCoroutine(TrackEndWatcher(clip.length));
                    }
                }
            }
        }

        private System.Collections.IEnumerator TrackEndWatcher(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            StartNextTrack();
        }

        public void ToggleMute()
        {
            _isMuted = !_isMuted;
            if (_audioSource != null) _audioSource.mute = _isMuted;
            Plugin.Log.LogInfo($"[Audio] Режим тишины: {(_isMuted ? "ВКЛ" : "ВЫКЛ")}");
        }

        public bool IsMuted() => _isMuted;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_audioSource != null) _audioSource.Stop();
        }
    }
}
