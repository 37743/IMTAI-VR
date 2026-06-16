using System.Collections;
using UnityEngine;

public enum MetaXRHand
{
    Left,
    Right
}

public class MetaXRControllerPoseProvider : MonoBehaviour
{
    [Header("OVR Camera Rig")]
    [Tooltip("Usually OVRCameraRig/TrackingSpace.")]
    public Transform trackingSpace;

    [Header("Grab Settings")]
    [Range(0f, 1f)]
    public float grabThreshold = 0.55f;

    private void Awake()
    {
        if (trackingSpace == null)
        {
            OVRCameraRig rig = FindAnyObjectByType<OVRCameraRig>();

            if (rig != null)
                trackingSpace = rig.trackingSpace;
        }
    }

    public bool IsGrabPressed(MetaXRHand hand)
    {
        OVRInput.Axis1D trigger = hand == MetaXRHand.Left
            ? OVRInput.Axis1D.PrimaryHandTrigger
            : OVRInput.Axis1D.SecondaryHandTrigger;

        return OVRInput.Get(trigger) >= grabThreshold;
    }

    public bool TryGetHandPose(
        MetaXRHand hand,
        out Vector3 worldPosition,
        out Quaternion worldRotation)
    {
        OVRInput.Controller controller = hand == MetaXRHand.Left
            ? OVRInput.Controller.LTouch
            : OVRInput.Controller.RTouch;

        Vector3 localPosition = OVRInput.GetLocalControllerPosition(controller);
        Quaternion localRotation = OVRInput.GetLocalControllerRotation(controller);

        if (trackingSpace != null)
        {
            worldPosition = trackingSpace.TransformPoint(localPosition);
            worldRotation = trackingSpace.rotation * localRotation;
        }
        else
        {
            worldPosition = localPosition;
            worldRotation = localRotation;
        }

        return true;
    }

    public void Pulse(
        MetaXRHand hand,
        float frequency = 0.4f,
        float amplitude = 0.45f,
        float duration = 0.06f)
    {
        OVRInput.Controller controller = hand == MetaXRHand.Left
            ? OVRInput.Controller.LTouch
            : OVRInput.Controller.RTouch;

        StartCoroutine(PulseRoutine(controller, frequency, amplitude, duration));
    }

    private IEnumerator PulseRoutine(
        OVRInput.Controller controller,
        float frequency,
        float amplitude,
        float duration)
    {
        OVRInput.SetControllerVibration(frequency, amplitude, controller);
        yield return new WaitForSeconds(duration);
        OVRInput.SetControllerVibration(0f, 0f, controller);
    }
}
