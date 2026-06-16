using System;
using Oculus.Interaction;
using UnityEngine;

public class ResistedOneGrabRotateTransformer : MonoBehaviour, ITransformer
{
    public enum Axis
    {
        Right = 0,
        Up = 1,
        Forward = 2
    }

    [SerializeField, Optional]
    private Transform _pivotTransform;

    [SerializeField]
    private Axis _rotationAxis = Axis.Up;

    [SerializeField]
    [Tooltip("Flip this if the lever or dial moves in the opposite direction from the constraint range you want.")]
    private bool _invertRotationAxis;

    [Serializable]
    public class OneGrabRotateConstraints
    {
        public FloatConstraint MinAngle;
        public FloatConstraint MaxAngle;
    }

    [SerializeField]
    private OneGrabRotateConstraints _constraints = new OneGrabRotateConstraints
    {
        MinAngle = new FloatConstraint(),
        MaxAngle = new FloatConstraint()
    };

    [Header("Resistance")]
    [SerializeField, Min(0.01f)]
    [Tooltip("Lower values feel heavier. 2 is heavy, 6 is medium, 15 is light, 40 is almost direct.")]
    private float _rotationFollowSpeed = 6f;

    public Transform Pivot => _pivotTransform != null ? _pivotTransform : transform;
    public Axis RotationAxis => _rotationAxis;
    public float CurrentRelativeAngle => _filteredRelativeAngle;
    public float RequestedRelativeAngle => _requestedRelativeAngle;

    public OneGrabRotateConstraints Constraints
    {
        get => _constraints;
        set => _constraints = value;
    }

    private IGrabbable _grabbable;
    private Vector3 _grabPositionInPivotSpace;
    private Pose _transformPoseInPivotSpace;
    private Pose _worldPivotPose;
    private Vector3 _previousVectorInPivotSpace;
    private Quaternion _localRotation;

    private float _requestedRelativeAngle;
    private float _filteredRelativeAngle;
    private float _startFilteredAngle;

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
    }

    public void BeginTransform()
    {
        Pose grabPoint = _grabbable.GrabPoints[0];
        Transform targetTransform = _grabbable.Transform;

        if (_pivotTransform == null)
            _localRotation = targetTransform.localRotation;

        Vector3 localAxis = GetLocalAxis();
        _worldPivotPose = ComputeWorldPivotPose();
        Vector3 rotationAxis = _worldPivotPose.rotation * localAxis;
        Quaternion inversePivotRotation = Quaternion.Inverse(_worldPivotPose.rotation);

        Vector3 grabDelta = grabPoint.position - _worldPivotPose.position;

        if (grabDelta.magnitude < 0.001f)
            grabDelta = _worldPivotPose.rotation * GetFallbackLocalAxis();

        _grabPositionInPivotSpace = inversePivotRotation * grabDelta;

        Vector3 worldPositionDelta =
            inversePivotRotation * (targetTransform.position - _worldPivotPose.position);

        Quaternion worldRotationDelta = inversePivotRotation * targetTransform.rotation;
        _transformPoseInPivotSpace = new Pose(worldPositionDelta, worldRotationDelta);

        Vector3 initialOffset = _worldPivotPose.rotation * _grabPositionInPivotSpace;
        Vector3 initialVector = Vector3.ProjectOnPlane(initialOffset, rotationAxis);
        _previousVectorInPivotSpace = inversePivotRotation * initialVector;

        _requestedRelativeAngle = _filteredRelativeAngle;
        _startFilteredAngle = _filteredRelativeAngle;

        float parentScale = targetTransform.parent != null ? targetTransform.parent.lossyScale.x : 1f;
        _transformPoseInPivotSpace.position /= parentScale;
    }

    public void UpdateTransform()
    {
        Pose grabPoint = _grabbable.GrabPoints[0];
        Transform targetTransform = _grabbable.Transform;

        Vector3 localAxis = GetLocalAxis();
        _worldPivotPose = ComputeWorldPivotPose();
        Vector3 rotationAxis = _worldPivotPose.rotation * localAxis;

        Vector3 targetOffset = grabPoint.position - _worldPivotPose.position;
        Vector3 targetVector = Vector3.ProjectOnPlane(targetOffset, rotationAxis);
        Vector3 previousVectorInWorldSpace = _worldPivotPose.rotation * _previousVectorInPivotSpace;

        _previousVectorInPivotSpace = Quaternion.Inverse(_worldPivotPose.rotation) * targetVector;

        float signedAngle = Vector3.SignedAngle(previousVectorInWorldSpace, targetVector, rotationAxis);
        _requestedRelativeAngle = ConstrainAngle(_requestedRelativeAngle + signedAngle);

        float follow = 1f - Mathf.Exp(-_rotationFollowSpeed * Time.deltaTime);
        _filteredRelativeAngle = Mathf.Lerp(_filteredRelativeAngle, _requestedRelativeAngle, follow);
        _filteredRelativeAngle = ConstrainAngle(_filteredRelativeAngle);

        Quaternion deltaRotation =
            Quaternion.AngleAxis(_filteredRelativeAngle - _startFilteredAngle, rotationAxis);

        float parentScale = targetTransform.parent != null ? targetTransform.parent.lossyScale.x : 1f;
        Pose transformDeltaInWorldSpace = new Pose(
            _worldPivotPose.rotation * (parentScale * _transformPoseInPivotSpace.position),
            _worldPivotPose.rotation * _transformPoseInPivotSpace.rotation);

        Pose transformDeltaRotated = new Pose(
            deltaRotation * transformDeltaInWorldSpace.position,
            deltaRotation * transformDeltaInWorldSpace.rotation);

        targetTransform.position = _worldPivotPose.position + transformDeltaRotated.position;
        targetTransform.rotation = transformDeltaRotated.rotation;
    }

    public void EndTransform()
    {
    }

    private Pose ComputeWorldPivotPose()
    {
        if (_pivotTransform != null)
            return new Pose(_pivotTransform.position, _pivotTransform.rotation);

        Transform targetTransform = _grabbable.Transform;

        Vector3 worldPosition = targetTransform.position;
        Quaternion worldRotation = targetTransform.parent != null
            ? targetTransform.parent.rotation * _localRotation
            : _localRotation;

        return new Pose(worldPosition, worldRotation);
    }

    private float ConstrainAngle(float angle)
    {
        if (_constraints.MinAngle.Constrain)
            angle = Mathf.Max(angle, _constraints.MinAngle.Value);

        if (_constraints.MaxAngle.Constrain)
            angle = Mathf.Min(angle, _constraints.MaxAngle.Value);

        return angle;
    }

    private Vector3 GetLocalAxis()
    {
        Vector3 axis = Vector3.zero;
        axis[(int)_rotationAxis] = _invertRotationAxis ? -1f : 1f;
        return axis;
    }

    private Vector3 GetFallbackLocalAxis()
    {
        Vector3 axis = Vector3.zero;
        axis[((int)_rotationAxis + 1) % 3] = _invertRotationAxis ? -0.001f : 0.001f;
        return axis;
    }
}
