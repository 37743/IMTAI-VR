using System.Collections;
using UnityEngine;

public class LatheEmergencyStopButton : MonoBehaviour
{
    [Header("Machine")]
    public LatheMachineManager machineManager;

    [Header("Visual")]
    [Tooltip("The moving button cap mesh. This should be a visual child, not the root with the PokeInteractable.")]
    public Transform buttonCap;

    [Tooltip("If true, the button stays pressed until ResetButton is called.")]
    public bool latchUntilReset = true;

    [Header("Motion")]
    public Vector3 pressedLocalOffset = new Vector3(0f, 0f, -0.025f);
    public float pressTime = 0.06f;
    public float releaseTime = 0.12f;

    [Header("Bounce")]
    public AnimationCurve pressCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public AnimationCurve releaseCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.7f, 1.12f),
        new Keyframe(1f, 1f)
    );

    [Header("Debug")]
    public bool isPressed;

    private Vector3 restLocalPosition;
    private Coroutine moveRoutine;

    private void Awake()
    {
        if (machineManager == null)
            machineManager = LatheMachineManager.Instance != null
                ? LatheMachineManager.Instance
                : FindAnyObjectByType<LatheMachineManager>();

        if (buttonCap != null)
            restLocalPosition = buttonCap.localPosition;
        else
            Debug.LogWarning($"{name}: Button Cap is not assigned. The emergency stop will work, but no visual button motion will play.");
    }

    public void PressButton()
    {
        if (isPressed && latchUntilReset)
            return;

        isPressed = true;

        if (machineManager != null)
            machineManager.PressEmergencyStop();
        else
            Debug.LogWarning($"{name}: No LatheMachineManager assigned for emergency stop.");

        MoveTo(restLocalPosition + pressedLocalOffset, pressTime, pressCurve);
    }

    public void ReleaseButton()
    {
        if (latchUntilReset)
            return;

        isPressed = false;
        MoveTo(restLocalPosition, releaseTime, releaseCurve);
    }

    public void ResetButton()
    {
        isPressed = false;

        if (machineManager != null)
            machineManager.ResetEmergencyStop();

        MoveTo(restLocalPosition, releaseTime, releaseCurve);
    }

    private void MoveTo(Vector3 targetLocalPosition, float duration, AnimationCurve curve)
    {
        if (buttonCap == null)
            return;

        if (moveRoutine != null)
            StopCoroutine(moveRoutine);

        moveRoutine = StartCoroutine(MoveRoutine(targetLocalPosition, duration, curve));
    }

    [ContextMenu("Restore Button Cap Position")]
    public void RestoreButtonCapPosition()
    {
        if (buttonCap == null)
            return;

        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }

        isPressed = false;
        buttonCap.localPosition = restLocalPosition;
    }

    private IEnumerator MoveRoutine(Vector3 targetLocalPosition, float duration, AnimationCurve curve)
    {
        Vector3 startLocalPosition = buttonCap.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration));
            float eased = curve != null ? curve.Evaluate(t) : t;

            buttonCap.localPosition = Vector3.LerpUnclamped(
                startLocalPosition,
                targetLocalPosition,
                eased);

            yield return null;
        }

        buttonCap.localPosition = targetLocalPosition;
        moveRoutine = null;
    }
}
