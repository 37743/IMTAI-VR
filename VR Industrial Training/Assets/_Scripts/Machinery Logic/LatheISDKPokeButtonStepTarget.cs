using UnityEngine;
using UnityEngine.Events;

public class LatheISDKPokeButtonStepTarget : LatheISDKStepInteractable
{
    [Header("Button Validation")]
    public float holdToCompleteSeconds = 0.15f;

    [Header("Debug")]
    public bool isPressed;
    public bool completed;
    public float holdProgress;

    [Header("Events")]
    public UnityEvent<float> onHoldProgress;

    private float holdTimer;

    private void Update()
    {
        if (!isStepActive || completed)
            return;

        if (!isPressed)
        {
            ResetHold();
            return;
        }

        holdTimer += Time.deltaTime;

        holdProgress = holdToCompleteSeconds <= 0f
            ? 1f
            : Mathf.Clamp01(holdTimer / holdToCompleteSeconds);

        onHoldProgress?.Invoke(holdProgress);

        if (holdTimer >= holdToCompleteSeconds)
        {
            completed = true;
            CompleteInteraction();
        }
    }

    public override void SetStepActive(bool active)
    {
        base.SetStepActive(active);

        isPressed = false;
        completed = false;
        ResetHold();
    }

    public void NotifyButtonPressed()
    {
        if (!isStepActive)
            return;

        isPressed = true;
    }

    public void NotifyButtonReleased()
    {
        isPressed = false;
        ResetHold();
    }

    private void ResetHold()
    {
        holdTimer = 0f;
        holdProgress = 0f;
        onHoldProgress?.Invoke(0f);
    }
}