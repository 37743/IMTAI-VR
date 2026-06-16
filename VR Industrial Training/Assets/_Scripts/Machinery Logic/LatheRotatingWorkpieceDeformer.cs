using System.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class LatheRotatingWorkpieceDeformer : MonoBehaviour
{
    public enum DirectionMode
    {
        ContactNormal = 0,
        TowardLocalAxis = 1
    }

    public enum LocalAxis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    [Header("Lathe")]
    public LatheMachineManager machineManager;

    [Header("Attachment Gate")]
    public bool requireWorkpieceAttachedToMachine = true;
    public LatheSnapCandidate workpieceSnapCandidate;
    public bool requireCuttingToolAttachedToMachine = true;

    [Tooltip("Below this speed the tool will touch the rod, but not cut/deform it.")]
    public float minCuttingRPM = 20f;

    [Tooltip("At this RPM the base deformation strength is applied at 1x.")]
    public float rpmForBaseStrength = 600f;

    [Tooltip("Caps how much spindle RPM can multiply the cut strength.")]
    public float maxRPMStrengthMultiplier = 2f;

    [Header("Cutting")]
    [Tooltip("Depth added per accepted contact sample at rpmForBaseStrength and material multiplier 1.")]
    public float baseDeformationStrength = 0.002f;

    [Tooltip("Radius around the contact point affected by each cut.")]
    public float maxDistance = 0.025f;

    [Tooltip("Fallback material multiplier when the tool has no LatheCuttingToolMaterial component.")]
    public float defaultToolMaterialMultiplier = 1f;

    [Tooltip("Only colliders on these layers can deform the rod.")]
    public LayerMask cuttingToolLayers = ~0;

    [Tooltip("Minimum collision impulse for the first contact. Continuous contact uses the interval below.")]
    public float minImpactImpulse = 0.05f;

    [Tooltip("Seconds between deformation samples while the tool remains in contact.")]
    public float cuttingInterval = 0.04f;

    [Tooltip("Prevents a vertex from being pushed too far from its original position. Set 0 for no limit.")]
    public float maxTotalVertexDeformation = 0.03f;

    [Header("Direction")]
    public DirectionMode deformationDirection = DirectionMode.ContactNormal;

    [Tooltip("Use this if the deformation bulges outward instead of cutting inward.")]
    public bool invertContactNormal;

    [Tooltip("Used only by TowardLocalAxis. The spindle in this project rotates around local X.")]
    public LocalAxis workpieceAxis = LocalAxis.X;

    [Header("Mesh")]
    public bool updateCollider = true;
    public bool recalculateNormals = true;
    public bool recalculateBounds = true;
    public bool reset;

    private MeshFilter _meshFilter;
    private MeshCollider _meshCollider;
    private Mesh _mesh;

    private Vector3[] _originalVertices;
    private Vector3[] _deformedVertices;

    private readonly object _resultLock = new object();
    private Vector3[] _resultVertices;
    private bool _hasResult;
    private bool _workerRunning;
    private bool _destroyed;
    private float _nextCutTime;

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshCollider = GetComponent<MeshCollider>();

        _mesh = Instantiate(_meshFilter.sharedMesh);
        _mesh.name = _meshFilter.sharedMesh.name + " Runtime Deformed";
        _mesh.MarkDynamic();
        _meshFilter.sharedMesh = _mesh;

        _originalVertices = _mesh.vertices;
        _deformedVertices = (Vector3[])_originalVertices.Clone();

        _meshCollider.sharedMesh = _mesh;

        if (machineManager == null)
            machineManager = LatheMachineManager.Instance != null
                ? LatheMachineManager.Instance
                : FindAnyObjectByType<LatheMachineManager>();

        if (workpieceSnapCandidate == null)
            workpieceSnapCandidate = GetComponentInParent<LatheSnapCandidate>();
    }

    private void Update()
    {
        if (reset)
        {
            ResetDeformation();
            reset = false;
        }

        Vector3[] verticesToApply = null;

        lock (_resultLock)
        {
            if (_hasResult)
            {
                verticesToApply = _resultVertices;
                _resultVertices = null;
                _hasResult = false;
                _workerRunning = false;
            }
        }

        if (verticesToApply == null)
            return;

        _deformedVertices = verticesToApply;
        ApplyToMesh(_deformedVertices);
    }

    private void OnDestroy()
    {
        _destroyed = true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.impulse.magnitude < minImpactImpulse)
            return;

        TryDeformFromCollision(collision, true);
    }

    private void OnCollisionStay(Collision collision)
    {
        TryDeformFromCollision(collision, false);
    }

    public void ResetDeformation()
    {
        if (_originalVertices == null)
            return;

        _deformedVertices = (Vector3[])_originalVertices.Clone();
        ApplyToMesh(_deformedVertices);
    }

    public void DeformAtWorldPoint(
        Vector3 impactPointWS,
        Vector3 deformationDirectionWS,
        float toolMaterialMultiplier = 1f)
    {
        if (!CanDeformWorkpiece())
            return;

        float rpm = machineManager != null ? Mathf.Abs(machineManager.currentRPM) : rpmForBaseStrength;

        if (rpm < minCuttingRPM)
            return;

        float rpmStrength = Mathf.Clamp(
            rpm / Mathf.Max(0.001f, rpmForBaseStrength),
            0f,
            Mathf.Max(0f, maxRPMStrengthMultiplier));

        float effectiveStrength =
            baseDeformationStrength *
            rpmStrength *
            Mathf.Max(0f, toolMaterialMultiplier);

        if (effectiveStrength <= 0f)
            return;

        StartDeformationWorker(impactPointWS, deformationDirectionWS, effectiveStrength, maxDistance);
    }

    private void TryDeformFromCollision(Collision collision, bool force)
    {
        if (collision.contactCount == 0)
            return;

        if ((cuttingToolLayers.value & (1 << collision.collider.gameObject.layer)) == 0)
            return;

        if (!CanCutWithTool(collision.collider))
            return;

        if (!force && Time.time < _nextCutTime)
            return;

        _nextCutTime = Time.time + Mathf.Max(0.001f, cuttingInterval);

        ContactPoint contact = collision.GetContact(0);
        float materialMultiplier = GetToolMaterialMultiplier(collision.collider);
        Vector3 directionWS = GetDeformationDirection(contact);

        DeformAtWorldPoint(contact.point, directionWS, materialMultiplier);
    }

    private bool CanDeformWorkpiece()
    {
        if (!requireWorkpieceAttachedToMachine)
            return true;

        return workpieceSnapCandidate != null && workpieceSnapCandidate.isAttachedToMachine;
    }

    private bool CanCutWithTool(Collider toolCollider)
    {
        if (!requireCuttingToolAttachedToMachine)
            return true;

        LatheSnapCandidate toolSnapCandidate = toolCollider.GetComponentInParent<LatheSnapCandidate>();
        return toolSnapCandidate != null && toolSnapCandidate.isAttachedToMachine;
    }

    private float GetToolMaterialMultiplier(Collider toolCollider)
    {
        LatheCuttingToolMaterial material = toolCollider.GetComponentInParent<LatheCuttingToolMaterial>();
        return material != null
            ? material.deformationMultiplier
            : defaultToolMaterialMultiplier;
    }

    private Vector3 GetDeformationDirection(ContactPoint contact)
    {
        if (deformationDirection == DirectionMode.TowardLocalAxis)
            return GetDirectionTowardLocalAxis(contact.point);

        Vector3 normal = contact.normal.normalized;
        return invertContactNormal ? normal : -normal;
    }

    private Vector3 GetDirectionTowardLocalAxis(Vector3 impactPointWS)
    {
        Vector3 pointLS = transform.InverseTransformPoint(impactPointWS);
        Vector3 pointOnAxisLS = pointLS;
        pointOnAxisLS[(int)workpieceAxis] = 0f;

        Vector3 directionLS = -pointOnAxisLS;

        if (directionLS.sqrMagnitude < 0.000001f)
            return GetDeformationDirectionFallback();

        return transform.TransformDirection(directionLS.normalized).normalized;
    }

    private Vector3 GetDeformationDirectionFallback()
    {
        Vector3 direction = Vector3.zero;
        direction[((int)workpieceAxis + 1) % 3] = -1f;
        return transform.TransformDirection(direction).normalized;
    }

    private void StartDeformationWorker(
        Vector3 impactPointWS,
        Vector3 deformationDirectionWS,
        float effectiveStrength,
        float radius)
    {
        lock (_resultLock)
        {
            if (_workerRunning || _destroyed)
                return;

            _workerRunning = true;
        }

        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
        Vector3 direction = deformationDirectionWS.normalized;
        Vector3[] baseVertices = (Vector3[])_deformedVertices.Clone();
        Vector3[] originalVertices = _originalVertices;
        float radiusSafe = Mathf.Max(0.0001f, radius);
        float maxVertexOffset = Mathf.Max(0f, maxTotalVertexDeformation);
        bool clampVertexOffset = maxVertexOffset > 0f;

        Task.Run(() =>
        {
            try
            {
                float radiusSqr = radiusSafe * radiusSafe;
                float maxVertexOffsetSqr = maxVertexOffset * maxVertexOffset;

                for (int i = 0; i < baseVertices.Length; i++)
                {
                    Vector3 vertexWS = localToWorld.MultiplyPoint3x4(baseVertices[i]);
                    Vector3 toVertex = vertexWS - impactPointWS;
                    float distanceSqr = toVertex.sqrMagnitude;

                    if (distanceSqr > radiusSqr)
                        continue;

                    float distance = Mathf.Sqrt(distanceSqr);
                    float t = Mathf.Clamp01(distance / radiusSafe);
                    float weight = Mathf.SmoothStep(1f, 0f, t);
                    Vector3 deformedWS = vertexWS + direction * (effectiveStrength * weight);
                    Vector3 candidateLS = worldToLocal.MultiplyPoint3x4(deformedWS);

                    if (clampVertexOffset)
                    {
                        Vector3 offset = candidateLS - originalVertices[i];

                        if (offset.sqrMagnitude > maxVertexOffsetSqr)
                            candidateLS = originalVertices[i] + offset.normalized * maxVertexOffset;
                    }

                    baseVertices[i] = candidateLS;
                }

                lock (_resultLock)
                {
                    if (_destroyed)
                        return;

                    _resultVertices = baseVertices;
                    _hasResult = true;
                }
            }
            catch
            {
                lock (_resultLock)
                {
                    _workerRunning = false;
                }
            }
        });
    }

    private void ApplyToMesh(Vector3[] vertices)
    {
        _mesh.vertices = vertices;

        if (recalculateNormals)
            _mesh.RecalculateNormals();

        if (recalculateBounds)
            _mesh.RecalculateBounds();

        if (!updateCollider)
            return;

        _meshCollider.sharedMesh = null;
        _meshCollider.sharedMesh = _mesh;
    }
}
