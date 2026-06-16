using UnityEngine;
using UnityEngine.Events;

public class LatheISDKRotaryStepTarget : LatheISDKStepInteractable
{
    [Header("Rotation Target")]
    public Transform rotatingTransform;

    [Tooltip("Local rotation axis used for measuring the angle. Try X, Y, or Z.")]
    public Vector3 localAxis = Vector3.right;

    [SerializeField]
    private Quaternion zeroLocalRotation = Quaternion.identity;

    [Header("Target Validation")]
    public float targetAngle = 45f;
    public float targetToleranceDegrees = 5f;
    public float targetHoldSeconds = 0.75f;

    [Header("Selection Requirement")]
    [Tooltip("If true, wire Meta's PointableUnityEventWrapper Select/Unselect events to NotifyInteractionSelected/NotifyInteractionUnselected.")]
    public bool requireSelectionForCompletion = true;

    [Header("Debug")]
    public bool isInteractionSelected;
    public bool completed;
    public float currentAngle;
    public float angleError;
    public float targetHoldProgress;

    [Header("Events")]
    public UnityEvent<float> onAngleChanged;
    public UnityEvent<float> onTargetHoldProgress;

    private float targetHoldTimer;

    private Vector3 Axis
    {
        get
        {
            if (localAxis.sqrMagnitude < 0.0001f)
                return Vector3.right;

            return localAxis.normalized;
        }
    }

    protected override void Reset()
    {
        base.Reset();

        rotatingTransform = transform;
        zeroLocalRotation = transform.localRotation;
    }

    protected override void Awake()
    {
        if (rotatingTransform == null)
            rotatingTransform = transform;

        currentAngle = GetAngleFromTransform();
        angleError = Mathf.Abs(Mathf.DeltaAngle(currentAngle, targetAngle));

        base.Awake();
    }

    private void Update()
    {
        if (!isStepActive || completed)
            return;

        currentAngle = GetAngleFromTransform();
        angleError = Mathf.Abs(Mathf.DeltaAngle(currentAngle, targetAngle));

        onAngleChanged?.Invoke(currentAngle);

        if (requireSelectionForCompletion && !isInteractionSelected)
        {
            ResetTargetHold();
            return;
        }

        bool isAtTarget = angleError <= targetToleranceDegrees;

        if (!isAtTarget)
        {
            ResetTargetHold();
            return;
        }

        targetHoldTimer += Time.deltaTime;

        targetHoldProgress = targetHoldSeconds <= 0f
            ? 1f
            : Mathf.Clamp01(targetHoldTimer / targetHoldSeconds);

        onTargetHoldProgress?.Invoke(targetHoldProgress);

        if (targetHoldTimer >= targetHoldSeconds)
        {
            completed = true;
            CompleteInteraction();
        }
    }

    public override void SetStepActive(bool active)
    {
        base.SetStepActive(active);

        completed = false;
        isInteractionSelected = false;
        ResetTargetHold();

        currentAngle = GetAngleFromTransform();
        angleError = Mathf.Abs(Mathf.DeltaAngle(currentAngle, targetAngle));
    }

    public void NotifyInteractionSelected()
    {
        if (!isStepActive)
            return;

        isInteractionSelected = true;
    }

    public void NotifyInteractionUnselected()
    {
        isInteractionSelected = false;
        ResetTargetHold();
    }

    private void ResetTargetHold()
    {
        targetHoldTimer = 0f;
        targetHoldProgress = 0f;
        onTargetHoldProgress?.Invoke(0f);
    }

    private float GetAngleFromTransform()
    {
        if (rotatingTransform == null)
            return 0f;

        Quaternion delta = Quaternion.Inverse(zeroLocalRotation) * rotatingTransform.localRotation;
        delta.ToAngleAxis(out float angle, out Vector3 axis);

        if (angle > 180f)
            angle -= 360f;

        if (Vector3.Dot(axis, Axis) < 0f)
            angle = -angle;

        return angle;
    }

    [ContextMenu("Capture Current Rotation As Zero")]
    public void CaptureCurrentRotationAsZero()
    {
        if (rotatingTransform == null)
            rotatingTransform = transform;

        zeroLocalRotation = rotatingTransform.localRotation;
        currentAngle = 0f;

        Debug.Log($"{name}: captured current rotation as ZERO.");
    }

    [ContextMenu("Record Current Angle As Target")]
    public void RecordCurrentAngleAsTarget()
    {
        targetAngle = GetAngleFromTransform();

        Debug.Log($"{name}: recorded target angle = {targetAngle:F2} degrees.");
    }
}