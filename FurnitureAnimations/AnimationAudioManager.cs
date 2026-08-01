using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace FurnitureAnimationsMod
{
    public class AnimationAudioManager : MonoBehaviour
    {
        public static AnimationAudioManager Instance { get; private set; }

        private AudioSource _audioSource;
        private bool _loopAudio = false;
        private bool _isMuted = false;

        public void Initialize(string animationName, bool loopAudio)
        {
            Instance = this;
            _loopAudio = loopAudio;

            string audioFolder = Path.Combine(BepInEx.Paths.PluginPath, "FurnitureAnimations", "Audio");
            if (!Directory.Exists(audioFolder))
                Directory.CreateDirectory(audioFolder);

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
                        _audioSource.loop = _loopAudio;

                        _audioSource.bypassEffects = true;
                        _audioSource.bypassListenerEffects = true;
                        _audioSource.priority = 0;
                        _audioSource.spatialBlend = 0f;
                        _audioSource.volume = 1.0f;
                        _audioSource.mute = _isMuted;

                        _audioSource.Play();
                        Plugin.Log.LogWarning($"[AudioEngine] Звуковой файл '{clip.name}' запущен!");
                    }
                }
            }
        }

        public void ToggleMute()
        {
            _isMuted = !_isMuted;
            if (_audioSource != null) _audioSource.mute = _isMuted;
            Plugin.Log.LogInfo($"[AudioEngine] Режим тишины: {(_isMuted ? "ВКЛ" : "ВЫКЛ")}");
        }

        public bool IsMuted() => _isMuted;

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
