using UnityEngine;

public class LatheMachineManager : MonoBehaviour
{
    public enum SpindleSpeedLeverPosition
    {
        Low190High1255 = 0,
        Low300High2000 = 1,
        Low115High755 = 2,
        Low70High460 = 3
    }

    public enum GearSelectorAB
    {
        A = 0,
        B = 1
    }

    public enum GearSelector1234
    {
        One = 0,
        Two = 1,
        Three = 2,
        Four = 3
    }

    public enum GearSelectorCD
    {
        C = 0,
        D = 1
    }

    public enum GearSelectorRSTU
    {
        R = 0,
        S = 1,
        T = 2,
        U = 3
    }

    public enum TransmissionState
    {
        Neutral = 0,
        Spindle = 1,
        Feed = 2,
        Thread = 3
    }

    public enum DirectionState
    {
        Reverse = -1,
        Neutral = 0,
        Forward = 1
    }

    public static LatheMachineManager Instance;

    [Header("Setup")]
    public bool autoFindReferencesByName = true;
    public bool captureHomePositionsOnAwake = true;
    public bool resetPositionValuesWhenCapturingHome = true;
    public bool applyScreenshotDefaultsOnAwake = true;

    [Header("Control Panel State")]
    [Tooltip("Top speed range lever. Blue row is low range, red row is high range.")]
    public LatheGearbox.Range panelSpeedRange = LatheGearbox.Range.Low;

    [Tooltip("Large top spindle speed lever. Default matches the screenshot: blue 190 / red 1255 position.")]
    public SpindleSpeedLeverPosition panelSpindleSpeedLever = SpindleSpeedLeverPosition.Low190High1255;

    [Tooltip("Lower-left A/B selector. Default matches the screenshot: A.")]
    public GearSelectorAB panelGearAB = GearSelectorAB.A;

    [Tooltip("Upper-middle 1/2/3/4 selector. Default matches the screenshot: 1.")]
    public GearSelector1234 panelGear1234 = GearSelector1234.One;

    [Tooltip("Lower-left C/D selector. Default matches the screenshot: C.")]
    public GearSelectorCD panelGearCD = GearSelectorCD.C;

    [Tooltip("Lower-right R/S/T/U selector. Default matches the screenshot: S.")]
    public GearSelectorRSTU panelGearRSTU = GearSelectorRSTU.S;

    public TransmissionState panelTransmission = TransmissionState.Neutral;
    public DirectionState panelSpindleDirection = DirectionState.Forward;
    public DirectionState panelFeedDirection = DirectionState.Neutral;
    public bool panelMainSwitchOn;
    public bool panelSpindleStartLatched;
    public bool panelCoolantPumpOn;
    public bool panelSplitNutEngaged;
    public bool panelBrakeEngaged;
    public bool toolPostRotationLocked = true;

    [Header("Power / Safety")]
    public bool mainPower = false;
    public bool emergencyStop = false;
    public bool brakeEngaged = false;
    public bool protectiveDeviceClosed = true;

    [Header("Operator Requests")]
    public bool requestSpindleOn = false;
    public int spindleDirection = 1;
    public bool requestFeedOn = false;
    public int feedDirection = 1;
    public bool splitNutEngaged = false;
    public bool coolantOn = false;

    [Header("Gearbox Selectors")]
    public LatheGearbox.Range speedRange = LatheGearbox.Range.Low;

    [Header("Transmission")]
    public int transmissionMode = 0; // 0=neutral,1=spindle,2=feed,3=thread

    [Header("Machine Outputs")]
    public float currentRPM = 0f;
    public float targetRPM = 0f;
    public float currentFeedRate = 0f;
    public float targetFeedRate = 0f;

    [Header("Positions")]
    public float carriageX = 0f;
    public float crossSlideZ = 0f;
    public float compoundX = 0f;
    public float tailstockX = 0f;
    public float tailQuillExtension = 0f;

    [Header("Limits")]
    public Vector2 carriageLimits = new Vector2(-0.7f, 0.7f);
    public Vector2 crossSlideLimits = new Vector2(-0.1f, 0.1f);
    public Vector2 compoundLimits = new Vector2(-0.08f, 0.08f);
    public Vector2 tailstockLimits = new Vector2(-0.7f, 0.7f);
    public Vector2 tailQuillLimits = new Vector2(0f, 0.12f);

    [Header("References")]
    public Transform spindle;
    public Transform carriageAssembly;
    public Transform tailstockAssembly;
    public Transform carriageBody;
    public Transform carriageTop;
    public Transform toolPost;
    public Transform tailstockBlock;
    public Transform drillTail;
    public Transform tailstockHandwheel;
    public Transform carriageLongitudinalHandwheel;
    public Transform turningBar;
    public Transform toolLongitudinalWheel;
    public Transform toolTransversalWheel;
    public LatheToolPostRotationLock toolPostRotationLock;

    [Header("Manual Wheel Controls")]
    public bool driveCrossSlideFromTransversalWheel = true;
    public ResistedOneGrabRotateTransformer toolTransversalWheelTransformer;
    public float toolTransversalWheelDegrees = 360f;
    public float toolTransversalWheelTravel = 0.02f;
    public bool invertToolTransversalWheelTravel;

    public bool driveCompoundFromLongitudinalWheel = true;
    public ResistedOneGrabRotateTransformer toolLongitudinalWheelTransformer;
    public Vector2 toolLongitudinalWheelAngleLimits = new Vector2(-360f, 360f);
    public float toolLongitudinalWheelDegrees = 360f;
    public float toolLongitudinalWheelTravel = 0.02f;
    public bool invertToolLongitudinalWheelTravel;

    public bool driveTailstockFromHandwheel = true;
    public ResistedOneGrabRotateTransformer tailstockHandwheelTransformer;
    public float tailstockHandwheelDegrees = 720f;
    public float tailstockHandwheelTravel = 0.35f;
    public bool invertTailstockHandwheelTravel;

    public bool driveCarriageFromLongitudinalHandwheel = true;
    public ResistedOneGrabRotateTransformer carriageLongitudinalHandwheelTransformer;
    [Tooltip("Min/max handwheel angles that correspond to the min/max carriage X travel.")]
    public Vector2 carriageLongitudinalHandwheelAngleLimits = new Vector2(-1036.36f, 720f);
    public Vector2 carriageLongitudinalHandwheelTravel = new Vector2(-0.475f, 0.33f);
    public bool invertCarriageLongitudinalHandwheelTravel;

    [Header("Visual Followers")]
    public bool rotateTurningBarWithCarriageHandwheel = true;
    public float turningBarRotationScale = 1f;
    public bool invertTurningBarRotation;

    [Header("Dynamics")]
    public float spindleAcceleration = 600f;
    public float spindleDeceleration = 180f;
    public float brakeDeceleration = 1800f;
    public float spindleInertia = 2.0f;
    public float feedAcceleration = 0.25f;
    public float feedDeceleration = 0.5f;
    public float carriageBacklash = 0.0015f;

    private float _lastManualCarriageDir = 0f;
    private float _backlashRemaining = 0f;
    private bool _manualWheelReferencesResolved;

    [SerializeField, HideInInspector] private Vector3 _carriageBodyHomeLocalPosition;
    [SerializeField, HideInInspector] private Vector3 _carriageAssemblyHomeLocalPosition;
    [SerializeField, HideInInspector] private Vector3 _carriageTopHomeLocalPosition;
    [SerializeField, HideInInspector] private Vector3 _toolPostHomeLocalPosition;
    [SerializeField, HideInInspector] private Vector3 _tailstockBlockHomeLocalPosition;
    [SerializeField, HideInInspector] private Vector3 _tailstockAssemblyHomeLocalPosition;
    [SerializeField, HideInInspector] private Vector3 _drillTailHomeLocalPosition;
    [SerializeField, HideInInspector] private Quaternion _turningBarHomeLocalRotation;

    private LatheGearbox gearbox;
    private LatheSafetySystem safety;
    private LatheKinematics kinematics;

    public Vector3 CarriageAssemblyHomeLocalPosition => _carriageAssemblyHomeLocalPosition;
    public Vector3 CarriageBodyHomeLocalPosition => _carriageBodyHomeLocalPosition;
    public Vector3 CarriageTopHomeLocalPosition => _carriageTopHomeLocalPosition;
    public Vector3 ToolPostHomeLocalPosition => _toolPostHomeLocalPosition;
    public Vector3 TailstockAssemblyHomeLocalPosition => _tailstockAssemblyHomeLocalPosition;
    public Vector3 TailstockBlockHomeLocalPosition => _tailstockBlockHomeLocalPosition;
    public Vector3 DrillTailHomeLocalPosition => _drillTailHomeLocalPosition;

    void Reset()
    {
        AutoFindReferencesByName();
        CaptureHomePositions();
    }

    void Awake()
    {
        Instance = this;
        gearbox = new LatheGearbox();
        safety = new LatheSafetySystem();
        kinematics = new LatheKinematics();

        if (autoFindReferencesByName)
            AutoFindReferencesByName();

        if (captureHomePositionsOnAwake)
            CaptureHomePositions();

        if (applyScreenshotDefaultsOnAwake)
            ApplyScreenshotPanelDefaults();

        SetToolPostRotationLocked(toolPostRotationLocked);
    }

    void Update()
    {
        SyncRuntimeStateFromPanel();
        EvaluateMachine();
        SimulateDynamics();
        ApplyManualWheelControls();
        ApplyVisualFollowers();
        kinematics.Apply(this, Time.deltaTime);
    }

    void EvaluateMachine()
    {
        bool spindleAllowed = safety.CanRunSpindle(this);
        bool feedAllowed = safety.CanRunFeed(this);

        targetRPM = spindleAllowed && requestSpindleOn
            ? gearbox.GetRPM(speedRange, panelSpindleSpeedLever) * spindleDirection
            : 0f;

        if (feedAllowed && requestFeedOn)
        {
            float feedPerRev = gearbox.GetFeedPerRev(
                panelGearAB,
                panelGear1234,
                panelGearCD,
                panelGearRSTU);

            float revPerSecond = currentRPM / 60f;

            targetFeedRate = revPerSecond * feedPerRev * feedDirection;
        }
        else
        {
            targetFeedRate = 0f;
        }
    }

    void SimulateDynamics()
    {
        SimulateSpindle();
        SimulateFeed();
        SimulateThreading();
    }

    void SimulateSpindle()
    {
        float accelPerSecond;

        if (brakeEngaged || emergencyStop)
            accelPerSecond = brakeDeceleration / spindleInertia;
        else if (Mathf.Abs(targetRPM) > Mathf.Abs(currentRPM))
            accelPerSecond = spindleAcceleration / spindleInertia;
        else
            accelPerSecond = spindleDeceleration / spindleInertia;

        currentRPM = Mathf.MoveTowards(currentRPM, targetRPM, accelPerSecond * Time.deltaTime);

        if (Mathf.Abs(currentRPM) < 0.01f)
            currentRPM = 0f;
    }

    void SimulateFeed()
    {
        float accel = Mathf.Abs(targetFeedRate) > Mathf.Abs(currentFeedRate)
            ? feedAcceleration
            : feedDeceleration;

        currentFeedRate = Mathf.MoveTowards(currentFeedRate, targetFeedRate, accel * Time.deltaTime);

        if (!splitNutEngaged)
        {
            carriageX += currentFeedRate * Time.deltaTime;
            carriageX = Mathf.Clamp(carriageX, carriageLimits.x, carriageLimits.y);
        }
    }

    void SimulateThreading()
    {
        if (!splitNutEngaged) return;
        if (!safety.CanThread(this)) return;

        float pitch = gearbox.GetThreadPitchMetric(
            panelGearAB,
            panelGear1234,
            panelGearCD,
            panelGearRSTU);

        float revPerSecond = currentRPM / 60f;
        float carriageVelocity = revPerSecond * pitch;

        carriageX += carriageVelocity * Time.deltaTime;
        carriageX = Mathf.Clamp(carriageX, carriageLimits.x, carriageLimits.y);
    }

    void ApplyManualWheelControls()
    {
        ResolveManualWheelReferences();

        if (driveCrossSlideFromTransversalWheel && toolTransversalWheelTransformer != null)
        {
            float normalizedTravel = GetNormalizedWheelTravel(
                toolTransversalWheelTransformer,
                toolTransversalWheelDegrees,
                invertToolTransversalWheelTravel);

            crossSlideZ = Mathf.Clamp(
                normalizedTravel * toolTransversalWheelTravel,
                crossSlideLimits.x,
                crossSlideLimits.y);
        }

        if (driveCompoundFromLongitudinalWheel && toolLongitudinalWheelTransformer != null)
        {
            float normalizedTravel = GetNormalizedWheelTravel(
                toolLongitudinalWheelTransformer,
                toolLongitudinalWheelDegrees,
                invertToolLongitudinalWheelTravel);

            compoundX = Mathf.Clamp(
                normalizedTravel * toolLongitudinalWheelTravel,
                compoundLimits.x,
                compoundLimits.y);
        }

        if (driveTailstockFromHandwheel && tailstockHandwheelTransformer != null)
        {
            float normalizedTravel = GetNormalizedWheelTravel(
                tailstockHandwheelTransformer,
                tailstockHandwheelDegrees,
                invertTailstockHandwheelTravel);

            tailstockX = Mathf.Clamp(
                normalizedTravel * tailstockHandwheelTravel,
                tailstockLimits.x,
                tailstockLimits.y);
        }

        if (driveCarriageFromLongitudinalHandwheel && carriageLongitudinalHandwheelTransformer != null)
        {
            float angle = carriageLongitudinalHandwheelTransformer.CurrentRelativeAngle;

            if (invertCarriageLongitudinalHandwheelTravel)
                angle *= -1f;

            carriageX = Mathf.Clamp(
                MapWheelAngleToTravel(
                    angle,
                    carriageLongitudinalHandwheelAngleLimits,
                    carriageLongitudinalHandwheelTravel),
                carriageLongitudinalHandwheelTravel.x,
                carriageLongitudinalHandwheelTravel.y);
        }
    }

    void ApplyVisualFollowers()
    {
        if (!rotateTurningBarWithCarriageHandwheel || turningBar == null)
            return;

        ResolveManualWheelReferences();

        float angle = 0f;

        if (carriageLongitudinalHandwheelTransformer != null)
            angle = carriageLongitudinalHandwheelTransformer.CurrentRelativeAngle;
        else if (carriageLongitudinalHandwheel != null)
            angle = carriageLongitudinalHandwheel.localEulerAngles.x;

        if (invertTurningBarRotation)
            angle *= -1f;

        turningBar.localRotation =
            _turningBarHomeLocalRotation *
            Quaternion.AngleAxis(angle * turningBarRotationScale, Vector3.right);
    }

    private float GetNormalizedWheelTravel(
        ResistedOneGrabRotateTransformer transformer,
        float degrees,
        bool invert)
    {
        float maxDegrees = Mathf.Max(0.001f, Mathf.Abs(degrees));
        float normalizedTravel = Mathf.Clamp(
            transformer.CurrentRelativeAngle / maxDegrees,
            -1f,
            1f);

        return invert ? -normalizedTravel : normalizedTravel;
    }

    private float MapWheelAngleToTravel(
        float angle,
        Vector2 angleLimits,
        Vector2 travelLimits)
    {
        if (angle < 0f)
        {
            float minAngle = Mathf.Min(-0.001f, angleLimits.x);
            return Mathf.InverseLerp(0f, minAngle, angle) * travelLimits.x;
        }

        float maxAngle = Mathf.Max(0.001f, angleLimits.y);
        return Mathf.InverseLerp(0f, maxAngle, angle) * travelLimits.y;
    }

    void ResolveManualWheelReferences()
    {
        if (_manualWheelReferencesResolved)
            return;

        if (autoFindReferencesByName &&
            (toolTransversalWheel == null ||
             toolLongitudinalWheel == null ||
             turningBar == null ||
             tailstockHandwheel == null ||
             carriageLongitudinalHandwheel == null))
        {
            AutoFindReferencesByName();
        }

        if (toolTransversalWheelTransformer == null && toolTransversalWheel != null)
            toolTransversalWheelTransformer = toolTransversalWheel.GetComponent<ResistedOneGrabRotateTransformer>();

        if (toolLongitudinalWheelTransformer == null && toolLongitudinalWheel != null)
            toolLongitudinalWheelTransformer = toolLongitudinalWheel.GetComponent<ResistedOneGrabRotateTransformer>();

        ApplyWheelAngleLimits(toolLongitudinalWheelTransformer, toolLongitudinalWheelAngleLimits);

        if (tailstockHandwheelTransformer == null && tailstockHandwheel != null)
            tailstockHandwheelTransformer = tailstockHandwheel.GetComponent<ResistedOneGrabRotateTransformer>();

        if (carriageLongitudinalHandwheelTransformer == null && carriageLongitudinalHandwheel != null)
            carriageLongitudinalHandwheelTransformer = carriageLongitudinalHandwheel.GetComponent<ResistedOneGrabRotateTransformer>();

        _manualWheelReferencesResolved = true;
    }

    [ContextMenu("Apply Screenshot Panel Defaults")]
    public void ApplyScreenshotPanelDefaults()
    {
        panelSpeedRange = LatheGearbox.Range.Low;
        panelSpindleSpeedLever = SpindleSpeedLeverPosition.Low190High1255;
        panelGearAB = GearSelectorAB.A;
        panelGear1234 = GearSelector1234.One;
        panelGearCD = GearSelectorCD.C;
        panelGearRSTU = GearSelectorRSTU.S;
        panelTransmission = TransmissionState.Neutral;
        panelSpindleDirection = DirectionState.Forward;
        panelFeedDirection = DirectionState.Neutral;
        panelMainSwitchOn = false;
        panelSpindleStartLatched = false;
        panelCoolantPumpOn = false;
        panelSplitNutEngaged = false;
        panelBrakeEngaged = false;
        emergencyStop = false;

        SyncRuntimeStateFromPanel();
    }

    private void SyncRuntimeStateFromPanel()
    {
        mainPower = panelMainSwitchOn;
        requestSpindleOn = panelSpindleStartLatched;
        spindleDirection = (int)panelSpindleDirection;
        requestFeedOn = panelFeedDirection != DirectionState.Neutral;
        feedDirection = (int)panelFeedDirection;
        splitNutEngaged = panelSplitNutEngaged;
        coolantOn = panelCoolantPumpOn;
        brakeEngaged = panelBrakeEngaged;
        speedRange = panelSpeedRange;
        transmissionMode = (int)panelTransmission;

    }

    [ContextMenu("Auto Find References By Name")]
    public void AutoFindReferencesByName()
    {
        if (spindle == null)
            spindle = FindChildByName("Spindle");

        if (carriageAssembly == null)
            carriageAssembly = FindChildByName("Carriage");

        if (tailstockAssembly == null)
            tailstockAssembly = FindChildByName("Tailstock");

        if (carriageBody == null)
            carriageBody = FindChildByName("CarriageBody");

        if (carriageTop == null)
            carriageTop = FindChildByName("CarriageTop");

        if (toolPost == null)
            toolPost = FindChildByName("ToolPost");

        if (tailstockBlock == null)
            tailstockBlock = FindChildByName("TailstockBlock");

        if (drillTail == null)
            drillTail = FindChildByName("DrillTail");

        if (tailstockHandwheel == null)
            tailstockHandwheel = FindChildByName("TailstockHandwheel");

        if (carriageLongitudinalHandwheel == null)
            carriageLongitudinalHandwheel = FindChildByName("CarriageLongitudinalHandwheel");

        if (turningBar == null)
            turningBar = FindChildByName("TurningBar");

        if (turningBar == null)
            turningBar = FindChildByName("turningbar");

        if (toolLongitudinalWheel == null)
            toolLongitudinalWheel = FindChildByName("ToolLongitudinalWheel");

        if (toolTransversalWheel == null)
            toolTransversalWheel = FindChildByName("ToolTransversalWheel");

        if (toolPostRotationLock == null && toolPost != null)
            toolPostRotationLock = toolPost.GetComponentInChildren<LatheToolPostRotationLock>(true);

        if (toolPostRotationLock == null)
            toolPostRotationLock = FindAnyObjectByType<LatheToolPostRotationLock>();

        if (toolTransversalWheelTransformer == null && toolTransversalWheel != null)
            toolTransversalWheelTransformer = toolTransversalWheel.GetComponent<ResistedOneGrabRotateTransformer>();

        if (toolLongitudinalWheelTransformer == null && toolLongitudinalWheel != null)
            toolLongitudinalWheelTransformer = toolLongitudinalWheel.GetComponent<ResistedOneGrabRotateTransformer>();

        ApplyWheelAngleLimits(toolLongitudinalWheelTransformer, toolLongitudinalWheelAngleLimits);

        if (tailstockHandwheelTransformer == null && tailstockHandwheel != null)
            tailstockHandwheelTransformer = tailstockHandwheel.GetComponent<ResistedOneGrabRotateTransformer>();

        if (carriageLongitudinalHandwheelTransformer == null && carriageLongitudinalHandwheel != null)
            carriageLongitudinalHandwheelTransformer = carriageLongitudinalHandwheel.GetComponent<ResistedOneGrabRotateTransformer>();

        _manualWheelReferencesResolved = false;
    }

    [ContextMenu("Capture Current Positions As Home")]
    public void CaptureHomePositions()
    {
        if (carriageAssembly != null)
            _carriageAssemblyHomeLocalPosition = carriageAssembly.localPosition;

        if (carriageBody != null)
            _carriageBodyHomeLocalPosition = carriageBody.localPosition;

        if (carriageTop != null)
            _carriageTopHomeLocalPosition = carriageTop.localPosition;

        if (toolPost != null)
            _toolPostHomeLocalPosition = toolPost.localPosition;

        if (tailstockBlock != null)
            _tailstockBlockHomeLocalPosition = tailstockBlock.localPosition;

        if (tailstockAssembly != null)
            _tailstockAssemblyHomeLocalPosition = tailstockAssembly.localPosition;

        if (drillTail != null)
            _drillTailHomeLocalPosition = drillTail.localPosition;

        if (turningBar != null)
            _turningBarHomeLocalRotation = turningBar.localRotation;

        if (!resetPositionValuesWhenCapturingHome)
            return;

        carriageX = 0f;
        crossSlideZ = 0f;
        compoundX = 0f;
        tailstockX = 0f;
        tailQuillExtension = 0f;
    }

    private Transform FindChildByName(string childName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child.name == childName)
                return child;
        }

        return null;
    }

    private void ApplyWheelAngleLimits(ResistedOneGrabRotateTransformer transformer, Vector2 angleLimits)
    {
        if (transformer == null || transformer.Constraints == null)
            return;

        transformer.Constraints.MinAngle.Constrain = true;
        transformer.Constraints.MinAngle.Value = Mathf.Min(angleLimits.x, angleLimits.y);
        transformer.Constraints.MaxAngle.Constrain = true;
        transformer.Constraints.MaxAngle.Value = Mathf.Max(angleLimits.x, angleLimits.y);
    }

    public void SetMainPower(bool value) => SetPanelMainSwitch(value);
    public void SetPanelMainSwitch(bool value) => panelMainSwitchOn = value;

    public void PressEmergencyStop()
    {
        emergencyStop = true;
        panelSpindleStartLatched = false;
        panelFeedDirection = DirectionState.Neutral;
        SyncRuntimeStateFromPanel();
    }

    public void ResetEmergencyStop()
    {
        emergencyStop = false;
    }

    public void SetBrake(bool value) => panelBrakeEngaged = value;
    public void SetPanelBrake(bool value) => panelBrakeEngaged = value;
    public void SetProtectiveGlassClosed(bool value) => protectiveDeviceClosed = value;
    public void SetProtectiveDeviceClosed(bool value) => SetProtectiveGlassClosed(value);
    public void SetToolPostRotationLocked(bool value)
    {
        toolPostRotationLocked = value;

        if (toolPostRotationLock == null)
        {
            if (toolPost != null)
                toolPostRotationLock = toolPost.GetComponentInChildren<LatheToolPostRotationLock>(true);

            if (toolPostRotationLock == null)
                toolPostRotationLock = FindAnyObjectByType<LatheToolPostRotationLock>();
        }

        if (toolPostRotationLock != null)
            toolPostRotationLock.SetLocked(value);
    }

    public void LockToolPostRotation() => SetToolPostRotationLocked(true);
    public void UnlockToolPostRotation() => SetToolPostRotationLocked(false);

    public void SetSpindleRequest(bool on) => panelSpindleStartLatched = on;
    public void SetSpindleDirection(int dir) => panelSpindleDirection = ToDirectionState(dir);
    public void SetSpindleDirectionFromReverseLever(int dir)
    {
        panelSpindleDirection = ToDirectionState(dir);
        panelSpindleStartLatched = panelSpindleDirection != DirectionState.Neutral;
    }

    public void SetFeedRequest(bool on) => panelFeedDirection = on ? DirectionState.Forward : DirectionState.Neutral;
    public void SetFeedDirection(int dir) => panelFeedDirection = ToDirectionState(dir);
    public void SetSplitNut(bool value) => panelSplitNutEngaged = value;
    public void SetCoolantPump(bool value) => panelCoolantPumpOn = value;

    public void SetSpeedRange(LatheGearbox.Range range) => panelSpeedRange = range;
    public void SetSpeedRangeIndex(int index) => panelSpeedRange = index <= 0 ? LatheGearbox.Range.Low : LatheGearbox.Range.High;
    public void SetSpindleSpeedLeverIndex(int index) => panelSpindleSpeedLever = (SpindleSpeedLeverPosition)Mathf.Clamp(index, 0, 3);

    public void SetGearABIndex(int index) => panelGearAB = index <= 0 ? GearSelectorAB.A : GearSelectorAB.B;
    public void SetGear1234Index(int index) => panelGear1234 = (GearSelector1234)Mathf.Clamp(index, 0, 3);
    public void SetGearCDIndex(int index) => panelGearCD = index <= 0 ? GearSelectorCD.C : GearSelectorCD.D;
    public void SetGearRSTUIndex(int index) => panelGearRSTU = (GearSelectorRSTU)Mathf.Clamp(index, 0, 3);

    public void SetTransmissionMode(int mode) => panelTransmission = (TransmissionState)Mathf.Clamp(mode, 0, 3);
    public void SetTransmissionState(TransmissionState state) => panelTransmission = state;

    public void PressStartButton()
    {
        panelSpindleStartLatched = true;
    }

    public void PressStopButton()
    {
        panelSpindleStartLatched = false;
        panelFeedDirection = DirectionState.Neutral;
    }

    private DirectionState ToDirectionState(int dir)
    {
        if (dir > 0)
            return DirectionState.Forward;

        if (dir < 0)
            return DirectionState.Reverse;

        return DirectionState.Neutral;
    }

    public void ManualMoveCarriage(float input, float speed)
    {
        float dir = Mathf.Sign(input);

        if (dir != 0f && dir != _lastManualCarriageDir)
        {
            _backlashRemaining = carriageBacklash;
            _lastManualCarriageDir = dir;
        }

        float delta = input * speed * Time.deltaTime;

        if (_backlashRemaining > 0f)
        {
            float consume = Mathf.Min(Mathf.Abs(delta), _backlashRemaining);
            _backlashRemaining -= consume;
            delta -= Mathf.Sign(delta) * consume;
        }

        carriageX += delta;
        carriageX = Mathf.Clamp(carriageX, carriageLimits.x, carriageLimits.y);
    }

    public void ManualMoveCrossSlide(float input, float speed)
    {
        crossSlideZ += input * speed * Time.deltaTime;
        crossSlideZ = Mathf.Clamp(crossSlideZ, crossSlideLimits.x, crossSlideLimits.y);
    }

    public void ManualMoveCompound(float input, float speed)
    {
        compoundX += input * speed * Time.deltaTime;
        compoundX = Mathf.Clamp(compoundX, compoundLimits.x, compoundLimits.y);
    }

    public void MoveTailstockBody(float input, float speed)
    {
        tailstockX += input * speed * Time.deltaTime;
        tailstockX = Mathf.Clamp(tailstockX, tailstockLimits.x, tailstockLimits.y);
    }

    public void MoveTailQuill(float input, float speed, bool locked)
    {
        if (locked) return;

        tailQuillExtension += input * speed * Time.deltaTime;
        tailQuillExtension = Mathf.Clamp(tailQuillExtension, tailQuillLimits.x, tailQuillLimits.y);
    }
}
