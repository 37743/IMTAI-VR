using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class ASyncLoader : MonoBehaviour
{
    [SerializeField] private Canvas loadingScreen;
    [SerializeField] private Canvas previousScreen;
    [SerializeField] private Slider progressBar;

    [SerializeField] private float fillSpeed = 0.8f;
    [SerializeField] private float holdAtFull = 0.25f;

    public void LoadScene(string sceneToLoad)
    {
        previousScreen.gameObject.SetActive(false);
        loadingScreen.gameObject.SetActive(true);

        StartCoroutine(LoadSceneAsync(sceneToLoad));
    }

    private IEnumerator LoadSceneAsync(string sceneToLoad)
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneToLoad);
        asyncLoad.allowSceneActivation = false;

        float visualProgress = 0f;
        progressBar.value = 0f;

        while (asyncLoad.progress < 0.9f)
        {
            float target = Mathf.Clamp01(asyncLoad.progress / 0.9f);
            visualProgress = Mathf.MoveTowards(visualProgress, target, fillSpeed * Time.unscaledDeltaTime);
            progressBar.value = visualProgress;
            yield return null;
        }

        while (visualProgress < 1f - 0.001f)
        {
            visualProgress = Mathf.MoveTowards(visualProgress, 1f, fillSpeed * Time.unscaledDeltaTime);
            progressBar.value = visualProgress;
            yield return null;
        }

        progressBar.value = 1f;

        yield return new WaitForSecondsRealtime(holdAtFull);

        asyncLoad.allowSceneActivation = true;
    }
}
