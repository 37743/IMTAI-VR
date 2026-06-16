using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
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
    public Button nextButton;
    public Button backButton;

    [Header("Meta ISDK Step Interaction")]
    public bool enableOnlyHighlightedInteractables = true;

    [Tooltip("If true, Next stays disabled until the highlighted Meta ISDK interaction is completed.")]
    public bool requireInteractionBeforeNextStep = true;

    [Header("Current Query")]
    public string question = "Give me a step by step guide on how to operate the lathe machine";

    private string[] _currentSteps;
    private ResponseStructure _currentData;
    private int _currentStepIndex = 0;

    private readonly List<LatheISDKStepInteractable> _activeInteractables =
        new List<LatheISDKStepInteractable>();

    private readonly HashSet<LatheISDKStepInteractable> _pendingInteractables =
        new HashSet<LatheISDKStepInteractable>();

    private void Start()
    {
        InitializeButtons();
        DisableAllLatheInteractablesInScene();
        AskQuestion(question);
    }

    private void OnDestroy()
    {
        ClearActiveStepInteractables();

        if (nextButton != null)
            nextButton.onClick.RemoveListener(NextStep);

        if (backButton != null)
            backButton.onClick.RemoveListener(PreviousStep);
    }

    private void InitializeButtons()
    {
        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(false);
            nextButton.onClick.RemoveListener(NextStep);
            nextButton.onClick.AddListener(NextStep);
        }

        if (backButton != null)
        {
            backButton.gameObject.SetActive(false);
            backButton.onClick.RemoveListener(PreviousStep);
            backButton.onClick.AddListener(PreviousStep);
        }
    }

    public void AskQuestion(string newQuestion)
    {
        ClearActiveStepInteractables();

        if (runtimeHighlighter != null)
            runtimeHighlighter.ClearHighlights();

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

        if (data == null || string.IsNullOrEmpty(data.response))
        {
            Debug.LogWarning("RAG response was empty or invalid.");
            return;
        }

        bool isStepByStepIntent = originalQuestion.ToLower().Contains("step");

        if (isStepByStepIntent)
            HandleStepByStepResponse(data);
        else
            HandleGeneralResponse(data);
    }

    private void HandleStepByStepResponse(ResponseStructure data)
    {
        string[] steps = data.response
            .Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
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
        ClearActiveStepInteractables();

        string cleanedResponse = data.response.Trim();
        UpdateUIAndAudio(cleanedResponse);

        if (runtimeHighlighter != null)
            runtimeHighlighter.ClearHighlights();
    }

    public void NextStep()
    {
        if (_currentSteps == null || _currentStepIndex >= _currentSteps.Length - 1)
            return;

        if (requireInteractionBeforeNextStep && _pendingInteractables.Count > 0)
        {
            Debug.LogWarning("Complete the highlighted Meta ISDK interaction before continuing.");
            return;
        }

        _currentStepIndex++;
        ShowCurrentStep();
    }

    public void PreviousStep()
    {
        if (_currentSteps == null || _currentStepIndex <= 0)
            return;

        _currentStepIndex--;
        ShowCurrentStep();
    }

    private void ShowCurrentStep()
    {
        if (_currentSteps == null || _currentSteps.Length == 0)
            return;

        string stepTextToDisplay = _currentSteps[_currentStepIndex];
        UpdateUIAndAudio(stepTextToDisplay);

        ClearActiveStepInteractables();

        if (runtimeHighlighter != null)
            runtimeHighlighter.ClearHighlights();

        HighlightObjectsForStep(_currentData, (_currentStepIndex + 1).ToString());

        if (backButton != null)
            backButton.interactable = _currentStepIndex > 0;

        UpdateNextButtonState();
    }

    private void SetButtonsActive(bool isActive)
    {
        if (nextButton != null)
            nextButton.gameObject.SetActive(isActive);

        if (backButton != null)
            backButton.gameObject.SetActive(isActive);
    }

    private void UpdateNextButtonState()
    {
        if (nextButton == null || _currentSteps == null)
            return;

        bool hasNextStep = _currentStepIndex < _currentSteps.Length - 1;
        bool waitingForInteraction =
            requireInteractionBeforeNextStep &&
            _pendingInteractables.Count > 0;

        nextButton.interactable = hasNextStep && !waitingForInteraction;
    }

    private void UpdateUIAndAudio(string textToDisplay)
    {
        if (stepText != null)
            stepText.text = textToDisplay;

        if (piperDriver != null)
            _ = piperDriver.Speak(textToDisplay);
    }

    private void HighlightObjectsForStep(ResponseStructure data, string stepNumber)
    {
        if (data == null || data.component == null || data.component.Length == 0)
            return;

        ComponentInfo stepInfo = data.component.FirstOrDefault(c => c.step == stepNumber);

        if (stepInfo == null || stepInfo.index == null)
            return;

        foreach (string objectName in stepInfo.index)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                continue;

            GameObject obj = GameObject.Find(objectName);

            if (obj == null)
            {
                Debug.LogWarning($"Object '{objectName}' not found in scene.");
                continue;
            }

            if (runtimeHighlighter != null)
                runtimeHighlighter.Highlight(obj);

            if (!enableOnlyHighlightedInteractables)
                continue;

            LatheISDKStepInteractable[] interactables =
                obj.GetComponentsInChildren<LatheISDKStepInteractable>(true);

            if (interactables == null || interactables.Length == 0)
            {
                LatheISDKStepInteractable parentInteractable =
                    obj.GetComponentInParent<LatheISDKStepInteractable>();

                if (parentInteractable != null)
                    interactables = new[] { parentInteractable };
            }

            if (interactables == null)
                continue;

            foreach (LatheISDKStepInteractable interactable in interactables)
            {
                if (interactable == null)
                    continue;

                if (_activeInteractables.Contains(interactable))
                    continue;

                ApplyOptionalStepSettings(interactable, stepInfo);

                interactable.SetStepActive(true);
                interactable.Completed -= HandleStepInteractableCompleted;
                interactable.Completed += HandleStepInteractableCompleted;

                _activeInteractables.Add(interactable);
                _pendingInteractables.Add(interactable);

                Debug.Log($"Enabled Meta ISDK interaction for: {interactable.name}");
            }
        }
    }

    private void ApplyOptionalStepSettings(
        LatheISDKStepInteractable interactable,
        ComponentInfo stepInfo)
    {
        LatheISDKRotaryStepTarget rotary = interactable as LatheISDKRotaryStepTarget;

        if (rotary == null || stepInfo == null)
            return;

        bool overrideAngle =
            stepInfo.overrideTargetAngle ||
            stepInfo.override_target_angle;

        if (overrideAngle)
        {
            rotary.targetAngle = stepInfo.overrideTargetAngle
                ? stepInfo.targetAngle
                : stepInfo.target_angle;
        }

        float tolerance = stepInfo.targetToleranceDegrees > 0f
            ? stepInfo.targetToleranceDegrees
            : stepInfo.target_tolerance_degrees;

        if (tolerance > 0f)
            rotary.targetToleranceDegrees = tolerance;

        float holdSeconds = stepInfo.targetHoldSeconds > 0f
            ? stepInfo.targetHoldSeconds
            : stepInfo.target_hold_seconds;

        if (holdSeconds > 0f)
            rotary.targetHoldSeconds = holdSeconds;
    }

    private void HandleStepInteractableCompleted(LatheISDKStepInteractable interactable)
    {
        if (interactable == null)
            return;

        _pendingInteractables.Remove(interactable);

        Debug.Log($"Completed Meta ISDK interaction: {interactable.name}");

        if (_pendingInteractables.Count == 0)
            UpdateNextButtonState();
    }

    private void ClearActiveStepInteractables()
    {
        foreach (LatheISDKStepInteractable interactable in _activeInteractables)
        {
            if (interactable == null)
                continue;

            interactable.Completed -= HandleStepInteractableCompleted;
            interactable.SetStepActive(false);
        }

        _activeInteractables.Clear();
        _pendingInteractables.Clear();
    }

    private void DisableAllLatheInteractablesInScene()
    {
        LatheISDKStepInteractable[] interactables =
            FindObjectsByType<LatheISDKStepInteractable>(
                FindObjectsInactive.Include);

        foreach (LatheISDKStepInteractable interactable in interactables)
        {
            if (interactable != null)
                interactable.SetStepActive(false);
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

        public bool overrideTargetAngle;
        public float targetAngle;
        public float targetToleranceDegrees;
        public float targetHoldSeconds;

        public bool override_target_angle;
        public float target_angle;
        public float target_tolerance_degrees;
        public float target_hold_seconds;
    }
}
