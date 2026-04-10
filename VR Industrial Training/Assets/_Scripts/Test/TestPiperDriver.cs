using UnityEngine;
using System.Collections;

namespace Piper.Samples
{
    public class TestPiperDriver : MonoBehaviour
    {
        public PiperDriver piperDriver;
        public string testText = "Hello, this is a test message.";

        void Start()
        {
            if (piperDriver == null)
            {
                Debug.LogError("PiperDriver is not assigned!");
                return;
            }

            StartCoroutine(TestSpeak());
        }

        private IEnumerator TestSpeak()
        {
            yield return piperDriver.Speak(testText);

            Debug.Log("Speech has finished.");
        }
    }
}