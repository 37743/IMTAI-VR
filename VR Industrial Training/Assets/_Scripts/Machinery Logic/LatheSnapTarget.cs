using UnityEngine;
using UnityEngine.Events;

public class LatheSnapTarget : MonoBehaviour
{
    [Header("Snap Target")]
    public Transform snapPoint;
    public float snapDistance = 0.08f;
    public bool matchRotation = true;
    public bool parentToSnapPoint = true;
    public bool snapOnlyOnce = true;

    [Header("Grab Requirement")]
    public bool requireGrabBeforeSnap = true;
    public Transform defaultCandidate;
    public Transform defaultCandidateAnchor;

    [Header("Candidate Filter")]
    public Transform[] allowedCandidates;
    public LayerMask allowedLayers = ~0;
    public string requiredTag = "";

    [Header("Locking")]
    public bool makeRigidbodyKinematic = true;
    public bool disableGravityOnSnap = true;
    public bool disableCollidersOnSnap;
    public Behaviour[] behavioursToDisableOnSnap;

    [Header("Debug")]
    public bool isCandidateGrabbed;
    public bool hasSnapped;
    public Transform currentCandidate;

    [Header("Events")]
    public UnityEvent unityOnSnapped;
    public UnityEvent<Transform> onSnapped;
    public UnityEvent unityOnUnsnapped;
    public UnityEvent<Transform> onUnsnapped;

    private Rigidbody snappedRigidbody;

    private Transform SnapPoint => snapPoint != null ? snapPoint : transform;

    private void Reset()
    {
        snapPoint = transform;
    }

    private void Update()
    {
        if (hasSnapped && snapOnlyOnce)
            return;

        Transform candidate = currentCandidate != null ? currentCandidate : defaultCandidate;

        if (candidate != null && CanSnapCandidate(candidate) && IsWithinSnapDistance(candidate))
        {
            Snap(candidate);
            return;
        }

        TrySnapGrabbedAllowedCandidate();
    }

    private void LateUpdate()
    {
        if (!hasSnapped || currentCandidate == null || !parentToSnapPoint)
            return;

        AlignCandidateToTarget(currentCandidate);
    }

    public void NotifyGrabStarted()
    {
        NotifyGrabStarted(defaultCandidate);
    }

    public void NotifyGrabEnded()
    {
        isCandidateGrabbed = false;
    }

    public void NotifyGrabStarted(GameObject candidate)
    {
        NotifyGrabStarted(candidate != null ? candidate.transform : null);
    }

    public void NotifyGrabEnded(GameObject candidate)
    {
        NotifyGrabEnded(candidate != null ? candidate.transform : null);
    }

    public void NotifyGrabStarted(Transform candidate)
    {
        if (candidate == null)
            candidate = defaultCandidate;

        candidate = GetCandidateRoot(candidate);

        if (candidate == null || !IsAllowedCandidate(candidate))
            return;

        currentCandidate = candidate;
        isCandidateGrabbed = true;
    }

    public void NotifyGrabEnded(Transform candidate)
    {
        candidate = GetCandidateRoot(candidate);

        if (candidate == null || currentCandidate == candidate)
            NotifyGrabEnded();
    }

    public void SnapDefaultCandidate()
    {
        if (defaultCandidate != null)
            Snap(defaultCandidate);
    }

    public void Unsnap()
    {
        if (currentCandidate == null)
            return;

        Transform unsnappedCandidate = currentCandidate;
        LatheSnapCandidate snapCandidate = GetSnapCandidateComponent(unsnappedCandidate);

        if (parentToSnapPoint)
            unsnappedCandidate.SetParent(null, true);

        if (snappedRigidbody != null)
            snappedRigidbody.isKinematic = false;

        snappedRigidbody = null;
        hasSnapped = false;
        isCandidateGrabbed = false;

        if (snapCandidate != null)
            snapCandidate.MarkRemovedFromMachine();

        unityOnUnsnapped?.Invoke();
        onUnsnapped?.Invoke(unsnappedCandidate);
    }

    private void OnTriggerEnter(Collider other)
    {
        TrySnapCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TrySnapCollider(other);
    }

    private void TrySnapCollider(Collider other)
    {
        if (hasSnapped && snapOnlyOnce)
            return;

        Transform candidate = GetCandidateFromCollider(other);

        if (candidate == null)
            return;

        if (!CanSnapCandidate(candidate))
            return;

        Snap(candidate);
    }

    private Transform GetCandidateFromCollider(Collider other)
    {
        if (other == null)
            return null;

        Transform candidateRoot = GetCandidateRoot(other.transform);

        if (candidateRoot != null && IsAllowedCandidate(candidateRoot))
            return candidateRoot;

        if (currentCandidate != null && other.transform.IsChildOf(currentCandidate))
            return currentCandidate;

        if (defaultCandidate != null && other.transform.IsChildOf(defaultCandidate))
            return defaultCandidate;

        Transform root = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : other.transform.root;

        return IsAllowedCandidate(root) ? root : null;
    }

    private void TrySnapGrabbedAllowedCandidate()
    {
        if (allowedCandidates == null)
            return;

        foreach (Transform allowedCandidate in allowedCandidates)
        {
            Transform candidate = GetCandidateRoot(allowedCandidate);

            if (candidate == null || !CanSnapCandidate(candidate) || !IsWithinSnapDistance(candidate))
                continue;

            Snap(candidate);
            return;
        }
    }

    private bool CanSnapCandidate(Transform candidate)
    {
        candidate = GetCandidateRoot(candidate);

        if (candidate == null || !IsAllowedCandidate(candidate))
            return false;

        if (!requireGrabBeforeSnap)
            return true;

        LatheSnapCandidate snapCandidate = GetSnapCandidateComponent(candidate);

        if (snapCandidate != null && snapCandidate.isGrabbed)
            return true;

        return isCandidateGrabbed && currentCandidate == candidate;
    }

    private bool IsWithinSnapDistance(Transform candidate)
    {
        return Vector3.Distance(GetCandidateAnchor(candidate).position, SnapPoint.position) <= snapDistance;
    }

    private Transform GetCandidateRoot(Transform candidate)
    {
        if (candidate == null)
            return null;

        LatheSnapCandidate snapCandidate = candidate.GetComponentInParent<LatheSnapCandidate>();
        return snapCandidate != null ? snapCandidate.Root : candidate;
    }

    private LatheSnapCandidate GetSnapCandidateComponent(Transform candidate)
    {
        if (candidate == null)
            return null;

        LatheSnapCandidate snapCandidate = candidate.GetComponent<LatheSnapCandidate>();

        if (snapCandidate != null)
            return snapCandidate;

        return candidate.GetComponentInChildren<LatheSnapCandidate>();
    }

    private bool IsAllowedCandidate(Transform candidate)
    {
        if (candidate == null)
            return false;

        if (!string.IsNullOrWhiteSpace(requiredTag) && !candidate.CompareTag(requiredTag))
            return false;

        if ((allowedLayers.value & (1 << candidate.gameObject.layer)) == 0)
            return false;

        if (allowedCandidates == null || allowedCandidates.Length == 0)
            return candidate == defaultCandidate || defaultCandidate == null;

        foreach (Transform allowedCandidate in allowedCandidates)
        {
            if (allowedCandidate == null)
                continue;

            if (candidate == allowedCandidate || candidate.IsChildOf(allowedCandidate) || allowedCandidate.IsChildOf(candidate))
                return true;
        }

        return false;
    }

    private void Snap(Transform candidate)
    {
        if (candidate == null || !IsAllowedCandidate(candidate))
            return;

        currentCandidate = candidate;
        hasSnapped = true;
        isCandidateGrabbed = false;
        LatheSnapCandidate snapCandidate = GetSnapCandidateComponent(candidate);

        Rigidbody rb = candidate.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            if (disableGravityOnSnap)
                rb.useGravity = false;

            if (makeRigidbodyKinematic)
                rb.isKinematic = true;

            snappedRigidbody = rb;
        }

        if (parentToSnapPoint)
            candidate.SetParent(SnapPoint, true);

        AlignCandidateToTarget(candidate);

        if (disableCollidersOnSnap)
        {
            Collider[] colliders = candidate.GetComponentsInChildren<Collider>();

            foreach (Collider targetCollider in colliders)
                targetCollider.enabled = false;
        }

        if (behavioursToDisableOnSnap != null)
        {
            foreach (Behaviour behaviour in behavioursToDisableOnSnap)
            {
                if (behaviour != null)
                    behaviour.enabled = false;
            }
        }

        if (snapCandidate != null)
            snapCandidate.MarkAttachedToMachine(this);

        unityOnSnapped?.Invoke();
        onSnapped?.Invoke(candidate);
    }

    private Transform GetCandidateAnchor(Transform candidate)
    {
        LatheSnapCandidate snapCandidate = GetSnapCandidateComponent(candidate);

        if (snapCandidate != null)
            return snapCandidate.Anchor;

        if (defaultCandidateAnchor != null && defaultCandidateAnchor.IsChildOf(candidate))
            return defaultCandidateAnchor;

        return candidate;
    }

    private void AlignCandidateToTarget(Transform candidate)
    {
        Transform anchor = GetCandidateAnchor(candidate);

        if (matchRotation)
        {
            Quaternion rotationDelta = SnapPoint.rotation * Quaternion.Inverse(anchor.rotation);
            candidate.rotation = rotationDelta * candidate.rotation;
        }

        Vector3 positionDelta = SnapPoint.position - anchor.position;
        candidate.position += positionDelta;
    }

    private void OnDrawGizmosSelected()
    {
        Transform target = snapPoint != null ? snapPoint : transform;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(target.position, snapDistance);
    }
}
