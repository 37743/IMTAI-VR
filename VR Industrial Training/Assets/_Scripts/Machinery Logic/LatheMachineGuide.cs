using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI; // Required for Button component
using TMPro;
using Piper.Samples;
using System.Linq; 

public class LatheMachineGuide : MonoBehaviour
{
    [Header("Server Configuration")]
    public string serverIP = "26.45.252.190";
    public string serverPort = "8000";
    public string endpoint = "/ask";

    [Header("References")]
    public PiperDriver piperDriver;
    public RuntimeHighlighter runtimeHighlighter;
    public TMP_Text stepText;

    [Header("UI Controls")]
    [Tooltip("Reference to the Next Step button")]
    public Button nextButton;
    [Tooltip("Reference to the Previous Step button")]
    public Button backButton;

    [Header("Current Query")]
    public string question = "Give me a step by step guide on how to operate the lathe machine";

    private string[] _currentSteps;
    private ResponseStructure _currentData;
    private int _currentStepIndex = 0;

    void Start()
    {
        InitializeButtons();

        AskQuestion(question);
    }

    private void InitializeButtons()
    {
        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(false);
            nextButton.onClick.AddListener(NextStep);
        }

        if (backButton != null)
        {
            backButton.gameObject.SetActive(false);
            backButton.onClick.AddListener(PreviousStep);
        }
    }

    public void AskQuestion(string newQuestion)
    {
        SetButtonsActive(false);
        
        question = newQuestion;
        StartCoroutine(SendRequest(question));
    }

    private IEnumerator SendRequest(string currentQuestion)
    {
        string baseUrl = $"http://{serverIP}:{serverPort}{endpoint}";
        string encodedQuestion = UnityWebRequest.EscapeURL(currentQuestion);
        string url = $"{baseUrl}?question={encodedQuestion}";

        UnityWebRequest request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Error connecting to {baseUrl}: {request.error}");
            yield break;
        }

        ProcessResponse(request.downloadHandler.text, currentQuestion);
    }

    private void ProcessResponse(string jsonResponse, string originalQuestion)
    {
        ResponseStructure data = JsonUtility.FromJson<ResponseStructure>(jsonResponse);

        if (data == null || string.IsNullOrEmpty(data.response)) return;

        bool isStepByStepIntent = originalQuestion.ToLower().Contains("step");

        if (isStepByStepIntent)
        {
            HandleStepByStepResponse(data);
        }
        else
        {
            HandleGeneralResponse(data);
        }
    }

    private void HandleStepByStepResponse(ResponseStructure data)
    {
        string[] steps = data.response.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
                                      .Select(s => s.Trim())
                                      .Where(s => s.StartsWith("Step"))
                                      .ToArray();

        if (steps.Length > 0)
        {
            _currentSteps = steps;
            _currentData = data;
            _currentStepIndex = 0;

            ShowCurrentStep();
            SetButtonsActive(true);
        }
        else
        {
            HandleGeneralResponse(data);
        }
    }

    private void HandleGeneralResponse(ResponseStructure data)
    {
        SetButtonsActive(false);

        string cleanedResponse = data.response.Trim();
        UpdateUIAndAudio(cleanedResponse);
        
        if (runtimeHighlighter != null) runtimeHighlighter.ClearHighlights();
    }

    public void NextStep()
    {
        Debug.Log($"<color=cyan>[LatheGuide]</color> Next button clicked! Current Index: {_currentStepIndex}, Total Steps: {(_currentSteps != null ? _currentSteps.Length : 0)}");

        if (_currentSteps == null || _currentStepIndex >= _currentSteps.Length - 1) 
        {
            Debug.LogWarning("<color=orange>[LatheGuide]</color> Reached the end of the steps or steps are null. Ignoring click.");
            return;
        }
        
        _currentStepIndex++;
        ShowCurrentStep();
    }

    public void PreviousStep()
    {
        if (_currentSteps == null || _currentStepIndex <= 0) return;

        _currentStepIndex--;
        ShowCurrentStep();
    }

    private void ShowCurrentStep()
    {
        string stepTextToDisplay = _currentSteps[_currentStepIndex];
        UpdateUIAndAudio(stepTextToDisplay);

        if (runtimeHighlighter != null) runtimeHighlighter.ClearHighlights(); 
        HighlightObjectsForStep(_currentData, (_currentStepIndex + 1).ToString());

        if (backButton != null) backButton.interactable = (_currentStepIndex > 0);
        if (nextButton != null) nextButton.interactable = (_currentStepIndex < _currentSteps.Length - 1);
    }

    private void SetButtonsActive(bool isActive)
    {
        if (nextButton != null) nextButton.gameObject.SetActive(isActive);
        if (backButton != null) backButton.gameObject.SetActive(isActive);
    }

    private void UpdateUIAndAudio(string textToDisplay)
    {
        if (stepText != null)
        {
            stepText.text = textToDisplay;
        }

        if (piperDriver != null)
        {
            piperDriver.Speak(textToDisplay);
        }
    }

    private void HighlightObjectsForStep(ResponseStructure data, string stepNumber)
    {
        if (runtimeHighlighter == null || data.component == null || data.component.Length == 0) return;

        var stepInfo = data.component.FirstOrDefault(c => c.step == stepNumber);

        if (stepInfo != null && stepInfo.index != null)
        {
            foreach (var objectName in stepInfo.index)
            {
                GameObject obj = GameObject.Find(objectName);
                if (obj != null)
                {
                    runtimeHighlighter.Highlight(obj);
                }
                else
                {
                    Debug.LogWarning($"Object '{objectName}' not found in scene.");
                }
            }
        }
    }

    [System.Serializable]
    public class ResponseStructure
    {
        public string message_id;
        public string question_type;
        public string response;
        public ComponentInfo[] component; 
    }

    [System.Serializable]
    public class ComponentInfo
    {
        public string step;
        public string[] index;
    }
}