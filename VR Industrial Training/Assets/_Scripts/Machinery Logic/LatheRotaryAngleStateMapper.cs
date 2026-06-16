using System;
using UnityEngine;
using UnityEngine.Events;

public class LatheRotaryAngleStateMapper : MonoBehaviour
{
    [Serializable]
    public class AngleState
    {
        public string label;
        public float minAngle;
        public float maxAngle;
        public UnityEvent onEnter;

        public bool Contains(float angle)
        {
            return angle >= Mathf.Min(minAngle, maxAngle) &&
                   angle <= Mathf.Max(minAngle, maxAngle);
        }
    }

    [Header("Angle Source")]
    public ResistedOneGrabRotateTransformer transformer;
    public bool invertAngle;

    [Header("States")]
    public AngleState[] states;
    public bool invokeInitialState = true;

    [Header("Debug")]
    public float currentAngle;
    public int currentStateIndex = -1;
    public string currentStateLabel;

    private bool hasEvaluated;

    private void Awake()
    {
        if (transformer == null)
            transformer = GetComponent<ResistedOneGrabRotateTransformer>();
    }

    private void Start()
    {
        EvaluateState(invokeInitialState);
    }

    private void Update()
    {
        EvaluateState(true);
    }

    [ContextMenu("Evaluate Now")]
    public void EvaluateNow()
    {
        EvaluateState(true);
    }

    private void EvaluateState(bool invokeEvents)
    {
        if (transformer == null || states == null || states.Length == 0)
            return;

        currentAngle = transformer.CurrentRelativeAngle;

        if (invertAngle)
            currentAngle *= -1f;

        int nextStateIndex = FindStateIndex(currentAngle);

        if (nextStateIndex == currentStateIndex && hasEvaluated)
            return;

        currentStateIndex = nextStateIndex;
        currentStateLabel = GetStateLabel(currentStateIndex);
        hasEvaluated = true;

        if (invokeEvents && currentStateIndex >= 0)
            states[currentStateIndex].onEnter?.Invoke();
    }

    private int FindStateIndex(float angle)
    {
        for (int i = 0; i < states.Length; i++)
        {
            AngleState state = states[i];

            if (state != null && state.Contains(angle))
                return i;
        }

        return -1;
    }

    private string GetStateLabel(int stateIndex)
    {
        if (stateIndex < 0 || stateIndex >= states.Length || states[stateIndex] == null)
            return string.Empty;

        return states[stateIndex].label;
    }
}
