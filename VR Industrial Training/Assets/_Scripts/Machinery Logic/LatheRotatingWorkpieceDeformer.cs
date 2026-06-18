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

    [Tooltip("Allows cutting if a snap candidate is not marked attached but is also not currently grabbed. This prevents stale snap state from blocking visible tool/workpiece contact.")]
    public bool allowCuttingWhenSnapStateIsStale = true;

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
    public float maxDistance = 0.015f;

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
    public bool invertContactNormal = true;

    [Tooltip("Used only by TowardLocalAxis. The spindle in this project rotates around local X.")]
    public LocalAxis workpieceAxis = LocalAxis.X;

    [Header("Mesh")]
    public bool updateCollider = true;
    public bool recalculateNormals = true;
    public bool recalculateBounds = true;
    public bool reset;

    [Header("Performance")]
    [Tooltip("Uses a conservative local-space distance check before the exact world-space distance check. This avoids expensive world transforms for most vertices on dense rods.")]
    public bool useLocalDistancePrefilter = true;

    [Tooltip("Extra local-space padding for the conservative prefilter. Increase if deformation seems to miss edge contacts.")]
    public float localPrefilterPadding = 0.005f;

    [Tooltip("Minimum seconds between full normal recalculations. Set 0 to recalculate every mesh update.")]
    public float normalsRefreshInterval = 0.12f;

    [Tooltip("Minimum seconds between MeshCollider recooks. Set 0 to update every mesh update.")]
    public float colliderRefreshInterval = 0.2f;

    [Header("Cutting Sparks")]
    public bool emitCuttingSparks = true;

    [Tooltip("Number of particles emitted per accepted cut sample.")]
    public int sparksPerCutSample = 6;

    [Tooltip("Minimum seconds between spark bursts.")]
    public float sparkMinInterval = 0.025f;

    public int sparkMaxParticles = 128;

    [Tooltip("How long each spark stays alive. Higher values make sparks travel farther.")]
    public float sparkLifetime = 0.6f;

    [Tooltip("Initial spark velocity. Higher values make sparks travel farther.")]
    public float sparkSpeed = 0.45f;

    [Tooltip("Spark billboard size.")]
    public float sparkSize = 0.005f;

    [Tooltip("Multiplier for Unity Physics.gravity. Use 0 for no falling, 1 for normal gravity.")]
    public float sparkGravityMultiplier = 1.2f;

    public Color sparkColor = new Color(1f, 0.82f, 0.05f, 1f);

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
    private float _nextNormalRefreshTime;
    private float _nextColliderRefreshTime;
    private bool _forceFullMeshRefresh;
    private ParticleSystem _sparkParticles;
    private ParticleSystem.EmitParams _sparkEmitParams;
    private Material _sparkMaterial;
    private float _nextSparkTime;

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

        if (_sparkMaterial != null)
            Destroy(_sparkMaterial);

        if (_sparkParticles != null)
            Destroy(_sparkParticles.gameObject);
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
        _forceFullMeshRefresh = true;
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

        if (StartDeformationWorker(impactPointWS, deformationDirectionWS, effectiveStrength, maxDistance))
            EmitCuttingSparks(impactPointWS, deformationDirectionWS, rpmStrength * toolMaterialMultiplier);
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

        return IsSnapCandidateReadyForCutting(workpieceSnapCandidate);
    }

    private bool CanCutWithTool(Collider toolCollider)
    {
        if (!requireCuttingToolAttachedToMachine)
            return true;

        LatheSnapCandidate toolSnapCandidate = toolCollider.GetComponentInParent<LatheSnapCandidate>();
        return IsSnapCandidateReadyForCutting(toolSnapCandidate);
    }

    private bool IsSnapCandidateReadyForCutting(LatheSnapCandidate snapCandidate)
    {
        if (snapCandidate == null)
            return allowCuttingWhenSnapStateIsStale;

        if (snapCandidate.isAttachedToMachine)
            return true;

        return allowCuttingWhenSnapStateIsStale && !snapCandidate.isGrabbed;
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

    private bool StartDeformationWorker(
        Vector3 impactPointWS,
        Vector3 deformationDirectionWS,
        float effectiveStrength,
        float radius)
    {
        lock (_resultLock)
        {
            if (_workerRunning || _destroyed)
                return false;

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
        Vector3 impactPointLS = worldToLocal.MultiplyPoint3x4(impactPointWS);
        float localRadius = GetConservativeLocalRadius(radiusSafe) + Mathf.Max(0f, localPrefilterPadding);
        float localRadiusSqr = localRadius * localRadius;
        bool usePrefilter = useLocalDistancePrefilter && localRadius > 0f;

        Task.Run(() =>
        {
            try
            {
                float radiusSqr = radiusSafe * radiusSafe;
                float maxVertexOffsetSqr = maxVertexOffset * maxVertexOffset;

                for (int i = 0; i < baseVertices.Length; i++)
                {
                    if (usePrefilter &&
                        (baseVertices[i] - impactPointLS).sqrMagnitude > localRadiusSqr)
                    {
                        continue;
                    }

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

        return true;
    }

    private void EmitCuttingSparks(Vector3 positionWS, Vector3 deformationDirectionWS, float strengthMultiplier)
    {
        if (!emitCuttingSparks || sparksPerCutSample <= 0)
            return;

        if (Time.time < _nextSparkTime)
            return;

        EnsureSparkParticles();

        if (_sparkParticles == null)
            return;

        ConfigureSparkParticles();
        _nextSparkTime = Time.time + Mathf.Max(0f, sparkMinInterval);

        Vector3 sparkDirection = -deformationDirectionWS.normalized;

        if (sparkDirection.sqrMagnitude < 0.000001f)
            sparkDirection = transform.up;

        int burstCount = Mathf.Clamp(sparksPerCutSample, 1, 32);
        float speedScale = Mathf.Clamp(strengthMultiplier, 0.5f, 2f);
        float baseSpeed = Mathf.Max(0f, sparkSpeed) * speedScale;
        float baseSize = Mathf.Max(0.0001f, sparkSize);
        float baseLifetime = Mathf.Max(0.01f, sparkLifetime);

        for (int i = 0; i < burstCount; i++)
        {
            Vector3 scatter = Random.insideUnitSphere * 0.65f;
            Vector3 velocity = sparkDirection + scatter;

            if (velocity.sqrMagnitude < 0.000001f)
                velocity = sparkDirection;

            _sparkEmitParams.position = positionWS + Random.insideUnitSphere * 0.002f;
            _sparkEmitParams.velocity = velocity.normalized * Random.Range(baseSpeed * 0.4f, baseSpeed * 1.2f);
            _sparkEmitParams.startLifetime = baseLifetime * Random.Range(0.65f, 1.25f);
            _sparkEmitParams.startSize = baseSize * Random.Range(0.65f, 1.25f);
            _sparkEmitParams.startColor = Color.Lerp(
                sparkColor,
                new Color(1f, 0.35f, 0.05f, sparkColor.a),
                Random.Range(0f, 0.35f));

            _sparkParticles.Emit(_sparkEmitParams, 1);
        }
    }

    private void EnsureSparkParticles()
    {
        if (_sparkParticles != null)
            return;

        GameObject sparkObject = new GameObject("Cutting Sparkles");
        sparkObject.hideFlags = HideFlags.DontSave;
        sparkObject.transform.SetParent(transform, false);

        _sparkParticles = sparkObject.AddComponent<ParticleSystem>();
        ConfigureSparkParticles();

        ParticleSystem.EmissionModule emission = _sparkParticles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = _sparkParticles.shape;
        shape.enabled = false;

        ParticleSystemRenderer renderer = _sparkParticles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

        if (particleShader == null)
            particleShader = Shader.Find("Particles/Standard Unlit");

        if (particleShader == null)
            particleShader = Shader.Find("Sprites/Default");

        if (particleShader != null)
        {
            _sparkMaterial = new Material(particleShader);
            renderer.sharedMaterial = _sparkMaterial;
        }

        _sparkParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void ConfigureSparkParticles()
    {
        ParticleSystem.MainModule main = _sparkParticles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(16, sparkMaxParticles);
        main.startLifetime = Mathf.Max(0.01f, sparkLifetime);
        main.startSpeed = 0f;
        main.startSize = Mathf.Max(0.0001f, sparkSize);
        main.startColor = sparkColor;
        main.gravityModifier = sparkGravityMultiplier;
    }

    private void ApplyToMesh(Vector3[] vertices)
    {
        _mesh.vertices = vertices;

        bool shouldRecalculateNormals =
            recalculateNormals &&
            (_forceFullMeshRefresh ||
             normalsRefreshInterval <= 0f ||
             Time.time >= _nextNormalRefreshTime);

        if (shouldRecalculateNormals)
        {
            _mesh.RecalculateNormals();
            _nextNormalRefreshTime = Time.time + Mathf.Max(0f, normalsRefreshInterval);
        }

        if (recalculateBounds)
            _mesh.bounds = CalculateBounds(vertices);

        if (!updateCollider)
        {
            _forceFullMeshRefresh = false;
            return;
        }

        bool shouldUpdateCollider =
            _forceFullMeshRefresh ||
            colliderRefreshInterval <= 0f ||
            Time.time >= _nextColliderRefreshTime;

        if (!shouldUpdateCollider)
        {
            _forceFullMeshRefresh = false;
            return;
        }

        _meshCollider.sharedMesh = null;
        _meshCollider.sharedMesh = _mesh;
        _nextColliderRefreshTime = Time.time + Mathf.Max(0f, colliderRefreshInterval);
        _forceFullMeshRefresh = false;
    }

    private float GetConservativeLocalRadius(float worldRadius)
    {
        Vector3 scale = transform.lossyScale;
        float minScale = Mathf.Min(
            Mathf.Abs(scale.x),
            Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

        if (minScale <= 0.0001f)
            return worldRadius;

        return worldRadius / minScale;
    }

    private Bounds CalculateBounds(Vector3[] vertices)
    {
        if (vertices == null || vertices.Length == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        Bounds bounds = new Bounds(vertices[0], Vector3.zero);

        for (int i = 1; i < vertices.Length; i++)
            bounds.Encapsulate(vertices[i]);

        return bounds;
    }
}
