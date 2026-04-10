using UnityEngine;

public class LatheMachineManager : MonoBehaviour
{
    public static LatheMachineManager Instance;

    [Header("Power / Safety")]
    public bool mainPower = false;
    public bool emergencyStop = false;
    public bool brakeEngaged = false;
    public bool protectiveGlassClosed = true;
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

    public bool speedSwitch1;
    public bool speedSwitch2;
    public bool speedSwitch3;
    public bool speedSwitch4;

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
    public Transform carriageBody;
    public Transform carriageTop;
    public Transform toolPost;
    public Transform tailstockBlock;
    public Transform drillTail;
    public Transform tailstockHandwheel;
    public Transform carriageLongitudinalHandwheel;
    public Transform toolLongitudinalWheel;
    public Transform toolTransversalWheel;

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

    private LatheGearbox gearbox;
    private LatheSafetySystem safety;
    private LatheKinematics kinematics;

    void Awake()
    {
        Instance = this;
        gearbox = new LatheGearbox();
        safety = new LatheSafetySystem();
        kinematics = new LatheKinematics();
    }

    void Update()
    {
        EvaluateMachine();
        SimulateDynamics();
        kinematics.Apply(this, Time.deltaTime);
    }

    void EvaluateMachine()
    {
        bool spindleAllowed = safety.CanRunSpindle(this);
        bool feedAllowed = safety.CanRunFeed(this);

        targetRPM = spindleAllowed && requestSpindleOn
            ? gearbox.GetRPM(speedRange, speedSwitch1, speedSwitch2, speedSwitch3, speedSwitch4) * spindleDirection
            : 0f;

        if (feedAllowed && requestFeedOn)
        {
            float feedPerRev = gearbox.GetFeedPerRev(speedRange, speedSwitch1, speedSwitch2, speedSwitch3, speedSwitch4);
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

        float pitch = gearbox.GetThreadPitchMetric(speedRange, speedSwitch1, speedSwitch2, speedSwitch3, speedSwitch4);

        float revPerSecond = currentRPM / 60f;
        float carriageVelocity = revPerSecond * pitch;

        carriageX += carriageVelocity * Time.deltaTime;
        carriageX = Mathf.Clamp(carriageX, carriageLimits.x, carriageLimits.y);
    }

    public void SetMainPower(bool value) => mainPower = value;

    public void PressEmergencyStop()
    {
        emergencyStop = true;
        requestSpindleOn = false;
        requestFeedOn = false;
    }

    public void ResetEmergencyStop()
    {
        emergencyStop = false;
    }

    public void SetBrake(bool value) => brakeEngaged = value;

    public void SetSpindleRequest(bool on) => requestSpindleOn = on;
    public void SetSpindleDirection(int dir) => spindleDirection = Mathf.Clamp(dir, -1, 1);
    public void SetFeedRequest(bool on) => requestFeedOn = on;
    public void SetFeedDirection(int dir) => feedDirection = Mathf.Clamp(dir, -1, 1);
    public void SetSplitNut(bool value) => splitNutEngaged = value;

    public void SetSpeedRange(LatheGearbox.Range range) => speedRange = range;

    public void SetSwitch1(bool value) => speedSwitch1 = value;
    public void SetSwitch2(bool value) => speedSwitch2 = value;
    public void SetSwitch3(bool value) => speedSwitch3 = value;
    public void SetSwitch4(bool value) => speedSwitch4 = value;

    public void SetTransmissionMode(int mode) => transmissionMode = Mathf.Clamp(mode, 0, 3);

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