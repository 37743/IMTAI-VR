using System.Text;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class LatheDebugStateDisplay : MonoBehaviour
{
    [Header("References")]
    public LatheMachineManager machineManager;
    public TMP_Text targetText;

    [Header("Refresh")]
    [Min(0.02f)]
    public float refreshInterval = 0.1f;

    [Header("Content")]
    public bool showSafety = true;
    public bool showControls = true;
    public bool showRequests = true;
    public bool showOutputs = true;
    public bool showPositions = true;
    public bool showLimits = false;
    public bool showManualWheelDrives = true;

    [Header("Timer")]
    public bool showTimeCounter = true;

    [Header("Formatting")]
    public string trueSymbol = "o";
    public string falseSymbol = "X";
    public bool useRichTextColors = true;
    public bool blankLineBetweenSections;

    private readonly StringBuilder builder = new StringBuilder(2048);
    private float nextRefreshTime;

    private void Awake()
    {
        if (targetText == null)
            targetText = GetComponent<TMP_Text>();

        if (machineManager == null)
            machineManager = LatheMachineManager.Instance != null
                ? LatheMachineManager.Instance
                : FindAnyObjectByType<LatheMachineManager>();
    }

    private void OnEnable()
    {
        nextRefreshTime = 0f;
        RefreshDisplay();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        RefreshDisplay();
        nextRefreshTime = Time.unscaledTime + refreshInterval;
    }

    [ContextMenu("Refresh Display")]
    public void RefreshDisplay()
    {
        if (targetText == null)
            return;

        if (machineManager == null)
        {
            targetText.text = "Lathe Debug\nMachine Manager: X";
            return;
        }

        builder.Clear();
        builder.AppendLine("Lathe Debug");

        if (showTimeCounter)
            AppendLine("Time", FormatElapsedTime(Time.timeSinceLevelLoad));

        if (showSafety)
            AppendSafety();

        if (showControls)
            AppendControls();

        if (showRequests)
            AppendRequests();

        if (showOutputs)
            AppendOutputs();

        if (showPositions)
            AppendPositions();

        if (showLimits)
            AppendLimits();

        if (showManualWheelDrives)
            AppendManualWheelDrives();

        targetText.text = builder.ToString();
    }

    private void AppendSafety()
    {
        AppendSection("Power / Safety");
        AppendBool("Main power", machineManager.mainPower);
        AppendBool("Emergency stop released", !machineManager.emergencyStop);
        AppendBool("Emergency stop pressed", machineManager.emergencyStop);
        AppendBool("Brake released", !machineManager.brakeEngaged);
        AppendBool("Brake engaged", machineManager.brakeEngaged);
        AppendBool("Protective shield closed", machineManager.protectiveGlassClosed);
    }

    private void AppendControls()
    {
        AppendSection("Panel Controls");
        AppendBool("Main switch", machineManager.panelMainSwitchOn);
        AppendLine("Speed range", FriendlyRange(machineManager.panelSpeedRange));
        AppendLine("Spindle speed lever", FriendlySpindleSpeed(machineManager.panelSpindleSpeedLever));
        AppendLine("A/B selector", machineManager.panelGearAB.ToString());
        AppendLine("1/2/3/4 selector", FriendlyGear1234(machineManager.panelGear1234));
        AppendLine("C/D selector", machineManager.panelGearCD.ToString());
        AppendLine("R/S/T/U selector", machineManager.panelGearRSTU.ToString());
        AppendLine("Transmission", FriendlyTransmission(machineManager.panelTransmission));
        AppendLine("Spindle direction", FriendlyDirection(machineManager.panelSpindleDirection));
        AppendLine("Feed direction", FriendlyDirection(machineManager.panelFeedDirection));
        AppendBool("Spindle lever active", machineManager.panelSpindleStartLatched);
        AppendBool("Coolant pump", machineManager.panelCoolantPumpOn);
        AppendBool("Split nut", machineManager.panelSplitNutEngaged);
        AppendBool("Panel brake", machineManager.panelBrakeEngaged);
        AppendBool("Toolpost rotation locked", machineManager.toolPostRotationLocked);
    }

    private void AppendRequests()
    {
        AppendSection("Runtime Requests");
        AppendBool("Spindle requested", machineManager.requestSpindleOn);
        AppendLine("Spindle direction request", FriendlySignedDirection(machineManager.spindleDirection));
        AppendBool("Feed requested", machineManager.requestFeedOn);
        AppendLine("Feed direction request", FriendlySignedDirection(machineManager.feedDirection));
        AppendBool("Split nut engaged", machineManager.splitNutEngaged);
        AppendBool("Coolant active", machineManager.coolantOn);
        AppendLine("Active speed range", FriendlyRange(machineManager.speedRange));
        AppendLine("Active A/B selector", machineManager.panelGearAB.ToString());
        AppendLine("Active 1/2/3/4 selector", FriendlyGear1234(machineManager.panelGear1234));
        AppendLine("Active C/D selector", machineManager.panelGearCD.ToString());
        AppendLine("Active R/S/T/U selector", machineManager.panelGearRSTU.ToString());
        AppendLine("Active transmission", FriendlyTransmission(machineManager.transmissionMode));
    }

    private void AppendOutputs()
    {
        AppendSection("Machine Output");
        AppendLine("Current spindle speed", $"{machineManager.currentRPM:F1} RPM");
        AppendLine("Target spindle speed", $"{machineManager.targetRPM:F1} RPM");
        AppendLine("Current feed rate", $"{machineManager.currentFeedRate:F4} m/s");
        AppendLine("Target feed rate", $"{machineManager.targetFeedRate:F4} m/s");
    }

    private void AppendPositions()
    {
        AppendSection("Positions");
        AppendLine("Carriage X", FormatMeters(machineManager.carriageX));
        AppendLine("Cross slide Z", FormatMeters(machineManager.crossSlideZ));
        AppendLine("Compound X", FormatMeters(machineManager.compoundX));
        AppendLine("Tailstock X", FormatMeters(machineManager.tailstockX));
        AppendLine("Tailstock quill", FormatMeters(machineManager.tailQuillExtension));
    }

    private void AppendLimits()
    {
        AppendSection("Travel Limits");
        AppendLine("Carriage X", FormatRange(machineManager.carriageLimits));
        AppendLine("Cross slide Z", FormatRange(machineManager.crossSlideLimits));
        AppendLine("Compound X", FormatRange(machineManager.compoundLimits));
        AppendLine("Tailstock X", FormatRange(machineManager.tailstockLimits));
        AppendLine("Tailstock quill", FormatRange(machineManager.tailQuillLimits));
    }

    private void AppendManualWheelDrives()
    {
        AppendSection("Manual Wheel Drives");
        AppendBool("Cross slide follows handwheel", machineManager.driveCrossSlideFromTransversalWheel);
        AppendLine("Cross slide wheel angle", FormatAngle(machineManager.toolTransversalWheelTransformer));
        AppendLine("Cross slide wheel travel", FormatMeters(machineManager.toolTransversalWheelTravel));
        AppendBool("Compound follows handwheel", machineManager.driveCompoundFromLongitudinalWheel);
        AppendLine("Compound wheel angle", FormatAngle(machineManager.toolLongitudinalWheelTransformer));
        AppendLine("Compound wheel angle limits", FormatAngleRange(machineManager.toolLongitudinalWheelAngleLimits));
        AppendLine("Compound wheel travel", FormatMeters(machineManager.toolLongitudinalWheelTravel));
        AppendBool("Tailstock follows handwheel", machineManager.driveTailstockFromHandwheel);
        AppendLine("Tailstock wheel angle", FormatAngle(machineManager.tailstockHandwheelTransformer));
        AppendLine("Tailstock wheel travel", FormatMeters(machineManager.tailstockHandwheelTravel));
        AppendBool("Carriage follows handwheel", machineManager.driveCarriageFromLongitudinalHandwheel);
        AppendLine("Carriage wheel angle", FormatAngle(machineManager.carriageLongitudinalHandwheelTransformer));
        AppendLine("Carriage wheel travel", FormatRange(machineManager.carriageLongitudinalHandwheelTravel));
        AppendBool("Turning bar follows carriage wheel", machineManager.rotateTurningBarWithCarriageHandwheel);
        AppendLine("Turning bar", machineManager.turningBar != null ? machineManager.turningBar.name : "Not assigned");
    }

    private void AppendSection(string title)
    {
        if (blankLineBetweenSections)
            builder.AppendLine();

        builder.AppendLine(title);
    }

    private void AppendBool(string label, bool value)
    {
        AppendLine(label, FormatBool(value));
    }

    private void AppendLine(string label, string value)
    {
        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(value);
    }

    private string FormatBool(bool value)
    {
        string symbol = value ? trueSymbol : falseSymbol;

        if (!useRichTextColors)
            return symbol;

        string color = value ? "#70D96B" : "#FF6B6B";
        return $"<color={color}>{symbol}</color>";
    }

    private static string FormatMeters(float value)
    {
        return $"{value:F3} m";
    }

    private static string FormatElapsedTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);

        int totalSeconds = Mathf.FloorToInt(seconds);
        int minutes = totalSeconds / 60;
        int remainingSeconds = totalSeconds % 60;

        return $"{minutes:00}:{remainingSeconds:00}";
    }

    private static string FormatRange(Vector2 range)
    {
        return $"{range.x:F3} to {range.y:F3} m";
    }

    private static string FormatAngleRange(Vector2 range)
    {
        return $"{range.x:F1} to {range.y:F1} deg";
    }

    private static string FormatAngle(ResistedOneGrabRotateTransformer transformer)
    {
        return transformer != null
            ? $"{transformer.CurrentRelativeAngle:F1} deg"
            : "Not assigned";
    }

    private static string FriendlyRange(LatheGearbox.Range range)
    {
        return range == LatheGearbox.Range.Low ? "Low" : "High";
    }

    private static string FriendlySpindleSpeed(LatheMachineManager.SpindleSpeedLeverPosition position)
    {
        switch (position)
        {
            case LatheMachineManager.SpindleSpeedLeverPosition.Low190High1255:
                return "Low 190 / High 1255";
            case LatheMachineManager.SpindleSpeedLeverPosition.Low300High2000:
                return "Low 300 / High 2000";
            case LatheMachineManager.SpindleSpeedLeverPosition.Low115High755:
                return "Low 115 / High 755";
            case LatheMachineManager.SpindleSpeedLeverPosition.Low70High460:
                return "Low 70 / High 460";
            default:
                return position.ToString();
        }
    }

    private static string FriendlyGear1234(LatheMachineManager.GearSelector1234 selector)
    {
        switch (selector)
        {
            case LatheMachineManager.GearSelector1234.One:
                return "1";
            case LatheMachineManager.GearSelector1234.Two:
                return "2";
            case LatheMachineManager.GearSelector1234.Three:
                return "3";
            case LatheMachineManager.GearSelector1234.Four:
                return "4";
            default:
                return selector.ToString();
        }
    }

    private static string FriendlyTransmission(LatheMachineManager.TransmissionState state)
    {
        switch (state)
        {
            case LatheMachineManager.TransmissionState.Neutral:
                return "Neutral";
            case LatheMachineManager.TransmissionState.Spindle:
                return "Spindle";
            case LatheMachineManager.TransmissionState.Feed:
                return "Feed";
            case LatheMachineManager.TransmissionState.Thread:
                return "Thread";
            default:
                return state.ToString();
        }
    }

    private static string FriendlyTransmission(int mode)
    {
        switch (mode)
        {
            case 0:
                return "Neutral";
            case 1:
                return "Spindle";
            case 2:
                return "Feed";
            case 3:
                return "Thread";
            default:
                return $"Unknown ({mode})";
        }
    }

    private static string FriendlyDirection(LatheMachineManager.DirectionState state)
    {
        switch (state)
        {
            case LatheMachineManager.DirectionState.Reverse:
                return "Reverse";
            case LatheMachineManager.DirectionState.Neutral:
                return "Neutral";
            case LatheMachineManager.DirectionState.Forward:
                return "Forward";
            default:
                return state.ToString();
        }
    }

    private static string FriendlySignedDirection(int direction)
    {
        if (direction > 0)
            return "Forward";

        if (direction < 0)
            return "Reverse";

        return "Neutral";
    }
}
