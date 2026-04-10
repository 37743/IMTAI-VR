using UnityEngine;
using System;
using System.Threading.Tasks;

namespace Piper.Samples
{
    public class PiperDriver : MonoBehaviour
    {
        public PiperManager piper;

        private AudioSource _source;

        public bool IsSpeaking { get; private set; }
        public event Action OnSpeechEnded;

        private Coroutine _endMonitorRoutine;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
        }

        public async Task Speak(string text)
        {
            if (_source == null)
            {
                Debug.LogError("AudioSource is not assigned.");
                return;
            }

            if (piper == null)
            {
                Debug.LogError("PiperManager is not assigned.");
                return;
            }

            try
            {
                Debug.Log($"Speaking: {text}");

                InterruptCurrentSpeech();

                var audioClip = await piper.TextToSpeech(text);

                if (audioClip == null)
                {
                    Debug.LogWarning("No audio clip generated to play.");
                    return;
                }

                _source.clip = audioClip;
                _source.Play();

                IsSpeaking = true;

                _endMonitorRoutine = StartCoroutine(WaitForPlaybackEndAndNotify());
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in Speak method: {ex.Message}");
            }
        }

        private void InterruptCurrentSpeech()
        {
            if (_endMonitorRoutine != null)
            {
                StopCoroutine(_endMonitorRoutine);
                _endMonitorRoutine = null;
            }

            if (_source != null && _source.isPlaying)
                _source.Stop();

            IsSpeaking = false;
        }

        private System.Collections.IEnumerator WaitForPlaybackEndAndNotify()
        {
            while (_source != null && _source.isPlaying)
                yield return null;

            if (IsSpeaking)
            {
                IsSpeaking = false;
                OnSpeechEnded?.Invoke();
            }

            _endMonitorRoutine = null;
        }

        private void OnDestroy()
        {
            InterruptCurrentSpeech();

            if (_source?.clip)
                Destroy(_source.clip);
        }
    }
}