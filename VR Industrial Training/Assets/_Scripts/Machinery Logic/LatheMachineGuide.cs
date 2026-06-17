using System.Collections;
using System.Collections.Generic;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using Piper.Samples;
using System.Linq;
using UnityEngine.Serialization;

public class LatheMachineGuide : MonoBehaviour
{
    [Header("Server Configuration")]
    public string serverIP = "26.45.252.190";
    public string serverPort = "8000";
    public string endpoint = "/ask";

    [Header("References")]
    public LatheMachineManager machineManager;
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
    [FormerlySerializedAs("question")]
    public string prompt = "provide a step by step guide to operating the lathe machine";
    public string machineName = "Bench-Lathe-Art-T999-230V3";

    [Header("Evaluation Configuration")]
    public string evalEndpoint = "/eval";
    public bool logEvaluationEvents = true;
    public string previousScorePlayerPrefsKey = "LatheMachineGuide.PreviousScore";

    [Header("Evaluation Runtime Metrics")]
    public string queryId;
    public int n_correct_steps;
    public int n_total_steps;
    public int n_safe_actions;
    public int n_total_actions;
    public int n_completed_tasks;
    public int n_assigned_tasks;
    public double t_start;
    public double t_end;
    public int n_errors_omission;
    public int n_errors_sequence;
    public int n_errors_unsafe;
    public int n_total_errors;
    public float score_t;
    public float score_t_minus_1;
    public float avg_fps;
    public float wer;

    [Header("Evaluation Derived Values")]
    public float proceduralAccuracy;
    public float safetyComplianceScore;
    public float taskCompletionRate;
    public float taskCompletionTime;
    public float learningProgression;
    public float omissionErrorRate;
    public float sequenceErrorRate;
    public float unsafeErrorRate;
    public float lastTutorLatencySeconds;
    public float averageTutorLatencySeconds;

    [Header("ASR Evaluation")]
    [Tooltip("Optional reference text used to calculate WER when speech is transcribed.")]
    public string werReferenceTranscript;
    public string lastAsrHypothesis;

    private string[] _currentSteps;
    private ComponentInfo[] _currentStepComponents;
    private ResponseStructure _currentData;
    private int _currentStepIndex = 0;
    private int _procedureStepCount;
    private bool _evaluationActive;
    private bool _evaluationSubmitted;
    private bool _evaluationSubmitting;
    private bool _hasStepByStepEvaluation;
    private float _askRequestStartRealtime;
    private float _fpsAccumulatedSeconds;
    private int _fpsFrameCount;
    private float _totalTutorLatencySeconds;
    private int _tutorLatencySamples;
    private string _pendingAsrHypothesis;
    private bool _lastEmergencyStopState;
    private bool _showingFinalStepScreen;

    private readonly List<LatheISDKStepInteractable> _activeInteractables =
        new List<LatheISDKStepInteractable>();

    private readonly HashSet<LatheISDKStepInteractable> _pendingInteractables =
        new HashSet<LatheISDKStepInteractable>();

    private readonly Dictionary<LatheISDKStepInteractable, string> _activeInteractableActionKeys =
        new Dictionary<LatheISDKStepInteractable, string>();

    private readonly HashSet<string> _assignedActionKeys =
        new HashSet<string>();

    private readonly HashSet<string> _completedActionKeys =
        new HashSet<string>();

    private readonly HashSet<string> _safeActionKeys =
        new HashSet<string>();

    private readonly HashSet<string> _completedStepKeys =
        new HashSet<string>();

    private readonly HashSet<string> _recordedOmissionErrorKeys =
        new HashSet<string>();

    private readonly HashSet<string> _recordedSequenceErrorKeys =
        new HashSet<string>();

    private void Start()
    {
        LoadPreviousEvaluationScore();
        ResolveMachineManager();
        _lastEmergencyStopState = machineManager != null && machineManager.emergencyStop;
        InitializeButtons();
        DisableAllLatheInteractablesInScene();
        AskQuestion(prompt);
    }

    private void Update()
    {
        SampleEvaluationFrameRate();
        DetectEmergencyStopEndCondition();
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
        _currentSteps = null;
        _currentStepComponents = null;
        _currentData = null;
        _currentStepIndex = 0;
        _procedureStepCount = 0;
        _showingFinalStepScreen = false;

        if (runtimeHighlighter != null)
            runtimeHighlighter.ClearHighlights();

        SetButtonsActive(false);

        prompt = newQuestion;
        StartCoroutine(SendRequest(prompt));
    }

    private IEnumerator SendRequest(string currentQuestion)
    {
        string baseUrl = $"http://{serverIP}:{serverPort}{endpoint}";
        string encodedQuestion = UnityWebRequest.EscapeURL(currentQuestion);
        string encodedMachine = UnityWebRequest.EscapeURL(machineName);
        string url = $"{baseUrl}?question={encodedQuestion}&machine={encodedMachine}";

        _askRequestStartRealtime = Time.realtimeSinceStartup;

        UnityWebRequest request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Error connecting to {baseUrl}: {request.error}");
            yield break;
        }

        ProcessResponse(request.downloadHandler.text);
    }

    private void ProcessResponse(string jsonResponse)
    {
        ResponseStructure data;

        try
        {
            data = Newtonsoft.Json.JsonConvert.DeserializeObject<ResponseStructure>(jsonResponse);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"Failed to parse RAG response: {exception.Message}");
            return;
        }

        if (data == null || string.IsNullOrEmpty(data.response))
        {
            Debug.LogWarning("RAG response was empty or invalid.");
            return;
        }

        string questionType = NormalizeQuestionType(data.question_type);

        switch (questionType)
        {
            case "stepbystep":
                HandleStepByStepResponse(data);
                break;

            case "summary":
            case "qna":
            case "misc":
                HandleGeneralResponse(data);
                break;

            default:
                Debug.LogWarning($"Unknown RAG question_type '{data.question_type}'. Showing response as general text.");
                HandleGeneralResponse(data);
                break;
        }
    }

    private void HandleStepByStepResponse(ResponseStructure data)
    {
        ComponentInfo[] structuredSteps = data.component == null
            ? new ComponentInfo[0]
            : data.component
                .Where(IsValidStepComponent)
                .ToArray();

        if (structuredSteps.Length > 0)
        {
            string[] procedureSteps = structuredSteps
                .Select((stepInfo, index) => GetStepText(stepInfo, data.response, index + 1))
                .ToArray();
            _currentSteps = AppendFinalStepScreen(procedureSteps);
            _currentStepComponents = structuredSteps;
            _currentData = data;
            _currentStepIndex = 0;

            BeginEvaluationRun(data, structuredSteps, procedureSteps);
            ShowCurrentStep();
            SetButtonsActive(true);
            return;
        }

        string[] steps = ParseStepLines(data.response);

        if (steps.Length > 0)
        {
            _currentSteps = AppendFinalStepScreen(steps);
            _currentStepComponents = null;
            _currentData = data;
            _currentStepIndex = 0;

            BeginEvaluationRun(data, null, steps);
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
        if (_evaluationActive && !_evaluationSubmitted)
            FinishEvaluationRun(false, "Interrupted by non-step response.");

        SetButtonsActive(false);
        ClearActiveStepInteractables();
        _currentSteps = null;
        _currentStepComponents = null;
        _currentData = data;
        _currentStepIndex = 0;
        _procedureStepCount = 0;
        _showingFinalStepScreen = false;

        string cleanedResponse = CleanResponseText(data.response);
        UpdateUIAndAudio(cleanedResponse);

        if (runtimeHighlighter != null)
            runtimeHighlighter.ClearHighlights();
    }

    public void NextStep()
    {
        if (_showingFinalStepScreen ||
            _currentSteps == null ||
            _currentStepIndex >= _currentSteps.Length - 1)
            return;

        if (requireInteractionBeforeNextStep && _pendingInteractables.Count > 0)
        {
            string stepKey = GetCurrentStepKey();
            RecordOmissionErrorOnce(
                $"next_omission:{stepKey}",
                "Next was pressed before completing all required step actions.");
            RecordSequenceErrorOnce(
                $"next_sequence:{stepKey}",
                "Next was pressed before the current step was complete.");
            Debug.LogWarning("Complete the highlighted Meta ISDK interaction before continuing.");
            return;
        }

        _currentStepIndex++;
        ShowCurrentStep();
    }

    public void PreviousStep()
    {
        if (_showingFinalStepScreen ||
            _currentSteps == null ||
            _currentStepIndex <= 0)
            return;

        _currentStepIndex--;
        ShowCurrentStep();
    }

    private void ShowCurrentStep()
    {
        if (_currentSteps == null || _currentSteps.Length == 0)
            return;

        if (IsFinalStepScreenIndex())
        {
            if (_evaluationActive)
            {
                FinishEvaluationRun(
                    true,
                    "All procedure steps were completed.",
                    true);
            }

            ShowFinalStepScreen(true, null);
            return;
        }

        _showingFinalStepScreen = false;

        string stepTextToDisplay = _currentSteps[_currentStepIndex];
        LogEvaluationEvent($"Showing step {_currentStepIndex + 1}/{_procedureStepCount}: {stepTextToDisplay}");
        UpdateUIAndAudio(stepTextToDisplay);

        ClearActiveStepInteractables();

        if (runtimeHighlighter != null)
            runtimeHighlighter.ClearHighlights();

        ComponentInfo currentStepInfo = _currentStepComponents != null &&
            _currentStepIndex < _currentStepComponents.Length
                ? _currentStepComponents[_currentStepIndex]
                : null;

        if (currentStepInfo != null)
            HighlightObjectsForStep(currentStepInfo);
        else
            HighlightObjectsForStep(_currentData, (_currentStepIndex + 1).ToString());

        if (currentStepInfo != null && !StepRequiresInteraction(currentStepInfo))
            MarkCurrentStepCorrect("No index/state interaction requirement for this step.");
        else if (_pendingInteractables.Count == 0)
            MarkCurrentStepCorrect("No pending interactable actions for this step.");

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

        if (_showingFinalStepScreen)
        {
            nextButton.interactable = false;
            return;
        }

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

        HighlightObjectsForStep(stepInfo);
    }

    private void HighlightObjectsForStep(ComponentInfo stepInfo)
    {
        if (stepInfo == null || stepInfo.index == null)
            return;

        bool stepRequiresInteraction = StepRequiresInteraction(stepInfo);

        foreach (string objectName in stepInfo.index)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                continue;

            GameObject obj = GameObject.Find(objectName);

            if (obj == null)
            {
                Debug.LogWarning($"Object '{objectName}' not found in scene.");
                LogEvaluationEvent($"Expected component was not found in scene: {objectName}");
                continue;
            }

            if (runtimeHighlighter != null)
                runtimeHighlighter.Highlight(obj);

            if (!enableOnlyHighlightedInteractables)
                continue;

            if (!stepRequiresInteraction)
            {
                LogEvaluationEvent($"Component highlighted without required state interaction: {objectName}");
                continue;
            }

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

            if (interactables.Length == 0)
            {
                LogEvaluationEvent($"Component highlighted without a tracked interactable: {objectName}");
                continue;
            }

            string actionKey = BuildActionKey(stepInfo, objectName);

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
                _activeInteractableActionKeys[interactable] = actionKey;

                LogEvaluationEvent($"Assigned action for current step: {actionKey} via {interactable.name}");
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
        RecordSafeAction(interactable);

        Debug.Log($"Completed Meta ISDK interaction: {interactable.name}");

        if (_pendingInteractables.Count == 0)
        {
            MarkCurrentStepCorrect("All pending interactable actions completed.");
            UpdateNextButtonState();
        }
    }

    private void ClearActiveStepInteractables()
    {
        foreach (LatheISDKStepInteractable interactable in _activeInteractables)
        {
            if (interactable == null)
                continue;

            interactable.Completed -= HandleStepInteractableCompleted;
            interactable.SetStepActive(false);
            _activeInteractableActionKeys.Remove(interactable);
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

    private void ResolveMachineManager()
    {
        if (machineManager != null)
            return;

        machineManager = LatheMachineManager.Instance != null
            ? LatheMachineManager.Instance
            : FindAnyObjectByType<LatheMachineManager>();
    }

    private void DetectEmergencyStopEndCondition()
    {
        ResolveMachineManager();

        bool emergencyStopPressed =
            machineManager != null &&
            machineManager.emergencyStop;

        if (_evaluationActive &&
            !_evaluationSubmitted &&
            !_lastEmergencyStopState &&
            emergencyStopPressed)
        {
            FinishEvaluationRun(true, "Emergency stop was pressed.", false);
        }

        _lastEmergencyStopState = emergencyStopPressed;
    }

    private void LoadPreviousEvaluationScore()
    {
        score_t_minus_1 = PlayerPrefs.GetFloat(previousScorePlayerPrefsKey, score_t_minus_1);
    }

    private void BeginEvaluationRun(
        ResponseStructure data,
        ComponentInfo[] structuredSteps,
        string[] stepTexts)
    {
        queryId = string.IsNullOrWhiteSpace(data.message_id)
            ? "missing_message_id"
            : data.message_id;
        _hasStepByStepEvaluation = true;
        _evaluationActive = true;
        _evaluationSubmitted = false;
        _evaluationSubmitting = false;
        _showingFinalStepScreen = false;
        RecordTutorLatency();
        t_start = GetUnixTimestampSeconds();
        t_end = 0f;

        _fpsAccumulatedSeconds = 0f;
        _fpsFrameCount = 0;
        avg_fps = 0f;

        _assignedActionKeys.Clear();
        _completedActionKeys.Clear();
        _safeActionKeys.Clear();
        _completedStepKeys.Clear();
        _recordedOmissionErrorKeys.Clear();
        _recordedSequenceErrorKeys.Clear();

        n_errors_omission = 0;
        n_errors_sequence = 0;
        n_errors_unsafe = 0;

        n_total_steps = stepTexts == null ? 0 : stepTexts.Length;
        _procedureStepCount = n_total_steps;
        RegisterAssignedActions(structuredSteps);
        LoadPreviousEvaluationScore();
        ApplyPendingAsrHypothesisToEvaluation();
        UpdateDerivedEvaluationMetrics();

        LogEvaluationEvent(
            $"Evaluation started. message_id={data.message_id}, steps={n_total_steps}, assigned_actions={_assignedActionKeys.Count}");
    }

    private void RegisterAssignedActions(ComponentInfo[] structuredSteps)
    {
        if (structuredSteps == null)
            return;

        for (int stepIndex = 0; stepIndex < structuredSteps.Length; stepIndex++)
        {
            ComponentInfo stepInfo = structuredSteps[stepIndex];

            if (!StepRequiresInteraction(stepInfo))
                continue;

            foreach (string objectName in stepInfo.index)
            {
                if (string.IsNullOrWhiteSpace(objectName))
                    continue;

                _assignedActionKeys.Add(
                    BuildActionKey(stepInfo, objectName, stepIndex + 1));
            }
        }
    }

    private void SampleEvaluationFrameRate()
    {
        if (!_evaluationActive)
            return;

        _fpsFrameCount++;
        _fpsAccumulatedSeconds += Time.unscaledDeltaTime;

        if (_fpsAccumulatedSeconds > 0f)
            avg_fps = _fpsFrameCount / _fpsAccumulatedSeconds;
    }

    private void RecordTutorLatency()
    {
        if (_askRequestStartRealtime <= 0f)
            return;

        lastTutorLatencySeconds = Time.realtimeSinceStartup - _askRequestStartRealtime;
        _totalTutorLatencySeconds += lastTutorLatencySeconds;
        _tutorLatencySamples++;
        averageTutorLatencySeconds = _totalTutorLatencySeconds / _tutorLatencySamples;

        LogEvaluationEvent(
            $"Tutor latency recorded: last={lastTutorLatencySeconds:F3}s, avg={averageTutorLatencySeconds:F3}s");
    }

    private void RecordSafeAction(LatheISDKStepInteractable interactable)
    {
        if (!_evaluationActive || interactable == null)
            return;

        string actionKey;

        if (!_activeInteractableActionKeys.TryGetValue(interactable, out actionKey))
            actionKey = BuildInteractableFallbackActionKey(interactable);

        if (_completedActionKeys.Add(actionKey))
        {
            _safeActionKeys.Add(actionKey);
            UpdateDerivedEvaluationMetrics();
            LogEvaluationEvent($"Safe action completed: {actionKey}");
        }
    }

    private void MarkCurrentStepCorrect(string reason)
    {
        if (!_evaluationActive || _currentSteps == null || _currentSteps.Length == 0)
            return;

        string stepKey = GetCurrentStepKey();

        if (_completedStepKeys.Add(stepKey))
        {
            UpdateDerivedEvaluationMetrics();
            LogEvaluationEvent($"Step marked correct: {stepKey}. {reason}");
        }

        if (_procedureStepCount > 0 &&
            _currentStepIndex >= _procedureStepCount - 1 &&
            n_correct_steps >= n_total_steps)
        {
            FinishEvaluationRun(
                true,
                "All procedure steps were completed.",
                true);
        }
    }

    public void RecordOmissionError(string detail)
    {
        RecordOmissionErrorOnce(
            $"manual_omission:{Time.frameCount}:{detail}",
            detail);
    }

    private void RecordOmissionErrorOnce(string errorKey, string detail)
    {
        if (!_recordedOmissionErrorKeys.Add(errorKey))
            return;

        n_errors_omission++;
        UpdateDerivedEvaluationMetrics();
        LogEvaluationEvent($"Omission error recorded: {detail}");
    }

    public void RecordSequenceError(string detail)
    {
        RecordSequenceErrorOnce(
            $"manual_sequence:{Time.frameCount}:{detail}",
            detail);
    }

    private void RecordSequenceErrorOnce(string errorKey, string detail)
    {
        if (!_recordedSequenceErrorKeys.Add(errorKey))
            return;

        n_errors_sequence++;
        UpdateDerivedEvaluationMetrics();
        LogEvaluationEvent($"Sequence error recorded: {detail}");
    }

    public void RecordUnsafeAction(string detail)
    {
        n_errors_unsafe++;
        UpdateDerivedEvaluationMetrics();
        LogEvaluationEvent($"Unsafe action recorded: {detail}");
    }

    public void RecordAsrHypothesis(string hypothesis)
    {
        _pendingAsrHypothesis = hypothesis;
        lastAsrHypothesis = hypothesis;
    }

    private void ApplyPendingAsrHypothesisToEvaluation()
    {
        if (string.IsNullOrWhiteSpace(_pendingAsrHypothesis))
            return;

        lastAsrHypothesis = _pendingAsrHypothesis;

        if (!string.IsNullOrWhiteSpace(werReferenceTranscript))
        {
            wer = CalculateWer(werReferenceTranscript, lastAsrHypothesis);
            LogEvaluationEvent(
                $"ASR hypothesis recorded. WER={wer:F3}, reference='{werReferenceTranscript}', hypothesis='{lastAsrHypothesis}'");
        }
        else
        {
            LogEvaluationEvent(
                $"ASR hypothesis recorded without WER reference: '{lastAsrHypothesis}'");
        }
    }

    private void FinishEvaluationRun(
        bool submit,
        string reason,
        bool completedSuccessfully = false)
    {
        if (_evaluationActive || t_end <= 0f)
        {
            t_end = GetUnixTimestampSeconds();
            _evaluationActive = false;
            UpdateDerivedEvaluationMetrics();
            LogEvaluationEvent(
                $"Evaluation finished. {reason} duration={taskCompletionTime:F2}s, score={score_t:F3}, progression={learningProgression:F3}");
        }

        if (submit)
        {
            SubmitEvaluation();

            if (!completedSuccessfully)
                ShowFinalStepScreen(false, reason);
        }
    }

    private void ShowFinalStepScreen(bool completedSuccessfully, string reason)
    {
        if (_showingFinalStepScreen)
            return;

        _showingFinalStepScreen = true;

        ClearActiveStepInteractables();

        if (runtimeHighlighter != null)
            runtimeHighlighter.ClearHighlights();

        UpdateDerivedEvaluationMetrics();

        string title = completedSuccessfully
            ? "Training Complete!"
            : "Training Ended";

        string finalText = completedSuccessfully
            ? $"{title}\n\nCompletion Time: {FormatDuration(taskCompletionTime)}\nCorrect Steps: {n_correct_steps}/{n_total_steps}"
            : $"{title}\n\nReason: {reason}\nCompletion Time: {FormatDuration(taskCompletionTime)}\nCorrect Steps: {n_correct_steps}/{n_total_steps}";

        UpdateUIAndAudio(finalText);
        SetButtonsActive(false);
        LogEvaluationEvent($"Final step screen shown: {title}");
    }

    private void SubmitEvaluation()
    {
        if (_evaluationSubmitted || _evaluationSubmitting)
        {
            LogEvaluationEvent("Evaluation was already submitted or is currently submitting.");
            return;
        }

        if (!_hasStepByStepEvaluation)
        {
            Debug.LogWarning("Evaluation submit skipped: no step-by-step evaluation has been started.");
            return;
        }

        if (string.IsNullOrWhiteSpace(queryId))
            queryId = "missing_message_id";

        if (t_start <= 0f)
            t_start = GetUnixTimestampSeconds();

        if (_evaluationActive || t_end <= 0f)
        {
            t_end = GetUnixTimestampSeconds();
            _evaluationActive = false;
        }

        UpdateDerivedEvaluationMetrics();
        _evaluationSubmitting = true;
        StartCoroutine(SendEvaluation());
    }

    private IEnumerator SendEvaluation()
    {
        string url = $"http://{serverIP}:{serverPort}{evalEndpoint}";
        EvalRequest payload = BuildEvaluationRequest();
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);

        LogEvaluationEvent($"Submitting evaluation payload: {json}");

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            _evaluationSubmitting = false;
            _evaluationSubmitted = true;
            PlayerPrefs.SetFloat(previousScorePlayerPrefsKey, score_t);
            PlayerPrefs.Save();
            Debug.Log($"Evaluation submitted successfully: {request.downloadHandler.text}");
        }
        else
        {
            _evaluationSubmitting = false;
            Debug.LogError($"Evaluation submit failed: {request.error}");
        }
    }

    private EvalRequest BuildEvaluationRequest()
    {
        UpdateDerivedEvaluationMetrics();

        return new EvalRequest
        {
            query_id = queryId,
            n_correct_steps = n_correct_steps,
            n_total_steps = n_total_steps,
            n_safe_actions = n_safe_actions,
            n_total_actions = n_total_actions,
            n_completed_tasks = n_completed_tasks,
            n_assigned_tasks = n_assigned_tasks,
            t_start = t_start,
            t_end = t_end,
            n_errors_omission = n_errors_omission,
            n_errors_sequence = n_errors_sequence,
            n_errors_unsafe = n_errors_unsafe,
            n_total_errors = n_total_errors,
            score_t = score_t,
            score_t_minus_1 = score_t_minus_1,
            avg_fps = avg_fps,
            wer = wer,
            tlx_score = 0f,
            sus_score = 0f,
            ipq_score = 0f,
            ssq_score = 0f
        };
    }

    private void UpdateDerivedEvaluationMetrics()
    {
        n_correct_steps = _completedStepKeys.Count;
        n_safe_actions = _safeActionKeys.Count;
        n_total_actions = Mathf.Max(
            _assignedActionKeys.Count,
            n_safe_actions + n_errors_unsafe);

        n_assigned_tasks = _assignedActionKeys.Count > 0
            ? _assignedActionKeys.Count
            : n_total_steps;

        n_completed_tasks = _assignedActionKeys.Count > 0
            ? _completedActionKeys.Count
            : n_correct_steps;

        n_total_errors =
            n_errors_omission +
            n_errors_sequence +
            n_errors_unsafe;

        proceduralAccuracy = SafeRatio(n_correct_steps, n_total_steps);
        safetyComplianceScore = SafeRatio(n_safe_actions, n_total_actions);
        taskCompletionRate = SafeRatio(n_completed_tasks, n_assigned_tasks);
        taskCompletionTime = t_start > 0f
            ? (float)((t_end > 0f ? t_end : GetUnixTimestampSeconds()) - t_start)
            : 0f;

        omissionErrorRate = SafeRatio(n_errors_omission, n_total_errors);
        sequenceErrorRate = SafeRatio(n_errors_sequence, n_total_errors);
        unsafeErrorRate = SafeRatio(n_errors_unsafe, n_total_errors);

        score_t = AverageAvailable(
            proceduralAccuracy,
            n_total_steps > 0,
            safetyComplianceScore,
            n_total_actions > 0,
            taskCompletionRate,
            n_assigned_tasks > 0);

        learningProgression = score_t - score_t_minus_1;
    }

    private float SafeRatio(int numerator, int denominator)
    {
        return denominator <= 0 ? 0f : (float)numerator / denominator;
    }

    private float AverageAvailable(
        float firstValue,
        bool includeFirst,
        float secondValue,
        bool includeSecond,
        float thirdValue,
        bool includeThird)
    {
        float total = 0f;
        int count = 0;

        if (includeFirst)
        {
            total += firstValue;
            count++;
        }

        if (includeSecond)
        {
            total += secondValue;
            count++;
        }

        if (includeThird)
        {
            total += thirdValue;
            count++;
        }

        return count == 0 ? 0f : total / count;
    }

    private float CalculateWer(string reference, string hypothesis)
    {
        string[] referenceWords = TokenizeWords(reference);
        string[] hypothesisWords = TokenizeWords(hypothesis);

        if (referenceWords.Length == 0)
            return 0f;

        int[,] distance = new int[referenceWords.Length + 1, hypothesisWords.Length + 1];

        for (int i = 0; i <= referenceWords.Length; i++)
            distance[i, 0] = i;

        for (int j = 0; j <= hypothesisWords.Length; j++)
            distance[0, j] = j;

        for (int i = 1; i <= referenceWords.Length; i++)
        {
            for (int j = 1; j <= hypothesisWords.Length; j++)
            {
                int substitutionCost = referenceWords[i - 1] == hypothesisWords[j - 1]
                    ? 0
                    : 1;

                distance[i, j] = Mathf.Min(
                    Mathf.Min(
                        distance[i - 1, j] + 1,
                        distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + substitutionCost);
            }
        }

        return (float)distance[referenceWords.Length, hypothesisWords.Length] /
            referenceWords.Length;
    }

    private string[] TokenizeWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new string[0];

        char[] separators =
        {
            ' ', '\t', '\n', '\r',
            '.', ',', ';', ':', '!', '?',
            '"', '\'', '(', ')', '[', ']',
            '{', '}', '/', '\\', '-', '_'
        };

        return text
            .ToLowerInvariant()
            .Split(separators, StringSplitOptions.RemoveEmptyEntries);
    }

    private string GetCurrentStepKey()
    {
        ComponentInfo stepInfo = _currentStepComponents != null &&
            _currentStepIndex >= 0 &&
            _currentStepIndex < _currentStepComponents.Length
                ? _currentStepComponents[_currentStepIndex]
                : null;

        return BuildStepKey(stepInfo, _currentStepIndex + 1);
    }

    private string BuildStepKey(ComponentInfo stepInfo, int fallbackStepNumber)
    {
        string stepNumber = stepInfo != null && !string.IsNullOrWhiteSpace(stepInfo.step)
            ? stepInfo.step
            : fallbackStepNumber.ToString();

        return $"step:{stepNumber}";
    }

    private string BuildActionKey(ComponentInfo stepInfo, string objectName)
    {
        return BuildActionKey(stepInfo, objectName, _currentStepIndex + 1);
    }

    private string BuildActionKey(
        ComponentInfo stepInfo,
        string objectName,
        int fallbackStepNumber)
    {
        string stepKey = BuildStepKey(stepInfo, fallbackStepNumber);
        return $"{stepKey}:component:{objectName}";
    }

    private string BuildInteractableFallbackActionKey(LatheISDKStepInteractable interactable)
    {
        string interactableKey = !string.IsNullOrWhiteSpace(interactable.targetId)
            ? interactable.targetId
            : GetTransformPath(interactable.transform);

        return $"{GetCurrentStepKey()}:interaction:{interactableKey}";
    }

    private string GetTransformPath(Transform targetTransform)
    {
        if (targetTransform == null)
            return "unknown";

        List<string> pathParts = new List<string>();
        Transform current = targetTransform;

        while (current != null)
        {
            pathParts.Add(current.name);
            current = current.parent;
        }

        pathParts.Reverse();
        return string.Join("/", pathParts);
    }

    private double GetUnixTimestampSeconds()
    {
        return (DateTime.UtcNow - new DateTime(
            1970,
            1,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc)).TotalSeconds;
    }

    private string FormatDuration(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int remainingSeconds = totalSeconds % 60;

        if (hours > 0)
            return $"{hours:00}:{minutes:00}:{remainingSeconds:00}";

        return $"{minutes:00}:{remainingSeconds:00}";
    }

    private void LogEvaluationEvent(string message)
    {
        if (!logEvaluationEvents)
            return;

        string id = string.IsNullOrWhiteSpace(queryId)
            ? "no-session"
            : queryId;

        Debug.Log($"[LatheEval:{id}] {message}");
    }

    private bool IsValidStepComponent(ComponentInfo stepInfo)
    {
        if (stepInfo == null)
            return false;

        bool hasStepText = !string.IsNullOrWhiteSpace(stepInfo.step_text);
        bool hasComponents = stepInfo.index != null && stepInfo.index.Length > 0;

        return hasStepText || hasComponents;
    }

    private string[] AppendFinalStepScreen(string[] procedureSteps)
    {
        if (procedureSteps == null || procedureSteps.Length == 0)
            return new[] { "Training Results" };

        string[] stepsWithFinalScreen = new string[procedureSteps.Length + 1];
        Array.Copy(procedureSteps, stepsWithFinalScreen, procedureSteps.Length);
        stepsWithFinalScreen[stepsWithFinalScreen.Length - 1] = "Training Results";
        return stepsWithFinalScreen;
    }

    private bool IsFinalStepScreenIndex()
    {
        return _procedureStepCount > 0 &&
            _currentStepIndex >= _procedureStepCount;
    }

    private bool StepRequiresInteraction(ComponentInfo stepInfo)
    {
        return HasStepIndices(stepInfo) && HasStepState(stepInfo);
    }

    private bool HasStepIndices(ComponentInfo stepInfo)
    {
        if (stepInfo == null || stepInfo.index == null)
            return false;

        foreach (string objectName in stepInfo.index)
        {
            if (!string.IsNullOrWhiteSpace(objectName))
                return true;
        }

        return false;
    }

    private bool HasStepState(ComponentInfo stepInfo)
    {
        if (stepInfo == null || stepInfo.state == null || stepInfo.state.Count == 0)
            return false;

        foreach (string key in stepInfo.state.Keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
                return true;
        }

        return false;
    }

    private string NormalizeQuestionType(string questionType)
    {
        if (string.IsNullOrWhiteSpace(questionType))
            return string.Empty;

        return questionType
            .Trim()
            .ToLowerInvariant()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);
    }

    private string GetStepText(ComponentInfo stepInfo, string response, int fallbackStepNumber)
    {
        if (stepInfo != null && !string.IsNullOrWhiteSpace(stepInfo.step_text))
            return CleanResponseText(stepInfo.step_text);

        string stepNumber = stepInfo != null && !string.IsNullOrWhiteSpace(stepInfo.step)
            ? stepInfo.step
            : fallbackStepNumber.ToString();

        string responseStep = FindStepLine(response, stepNumber);

        if (!string.IsNullOrWhiteSpace(responseStep))
            return CleanResponseText(responseStep);

        return $"Step {stepNumber}";
    }

    private string[] ParseStepLines(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new string[0];

        return response
            .Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanResponseText)
            .Where(IsStepLine)
            .ToArray();
    }

    private string FindStepLine(string response, string stepNumber)
    {
        if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(stepNumber))
            return null;

        string expectedPrefix = $"Step {stepNumber}";

        return response
            .Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanResponseText)
            .FirstOrDefault(line => line.StartsWith(expectedPrefix, System.StringComparison.OrdinalIgnoreCase));
    }

    private bool IsStepLine(string line)
    {
        return !string.IsNullOrWhiteSpace(line) &&
            line.TrimStart().StartsWith("Step", System.StringComparison.OrdinalIgnoreCase);
    }

    private string CleanResponseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text.Replace("**", string.Empty).Trim();
    }

    [System.Serializable]
    public class EvalRequest
    {
        public string query_id;
        public int n_correct_steps;
        public int n_total_steps;
        public int n_safe_actions;
        public int n_total_actions;
        public int n_completed_tasks;
        public int n_assigned_tasks;
        public double t_start;
        public double t_end;
        public int n_errors_omission;
        public int n_errors_sequence;
        public int n_errors_unsafe;
        public int n_total_errors;
        public float score_t;
        public float score_t_minus_1;
        public float avg_fps;
        public float wer;
        public float tlx_score;
        public float sus_score;
        public float ipq_score;
        public float ssq_score;
    }

    [System.Serializable]
    public class ResponseStructure
    {
        public string message_id;
        public string question_type;
        public string question;
        public string response;
        public ComponentInfo[] component;
    }

    [System.Serializable]
    public class ComponentInfo
    {
        public string step;
        public string step_text;
        public string[] index;
        public Dictionary<string, object> state;

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
