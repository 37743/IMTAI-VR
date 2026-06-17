using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

public class ProbeMachine : MonoBehaviour
{
    public string serverIP = "26.45.252.190";
    public int port = 8000;

    public GameObject targetMachine;
    public LatheMachineManager machineManager;

    [System.Serializable]
    public class ProbeRequest
    {
        public string machine;
        public List<ComponentProbe> components;
    }

    public class ComponentProbe
    {
        public string name;
        public string description;
        public Dictionary<string, object> default_state;
        public Dictionary<string, object> possible_states;

        public ComponentProbe(string name, Dictionary<string, object> defaultState)
        {
            this.name = name;
            default_state = defaultState;
        }
    }

    void Start()
    {
        if (targetMachine != null)
        {
            StartCoroutine(SendProbe());
        }
        else
        {
            Debug.LogError("ProbeMachine: targetMachine is not assigned.");
        }
    }

    IEnumerator SendProbe()
    {
        ResolveMachineManager();

        ProbeRequest request = new ProbeRequest
        {
            machine = targetMachine.name,
            components = BuildFunctionalComponentProbes()
        };

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(request);

        string url = $"http://{serverIP}:{port}/probe";

        UnityWebRequest requestWeb = new UnityWebRequest(url, "POST");

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        requestWeb.uploadHandler = new UploadHandlerRaw(bodyRaw);
        requestWeb.downloadHandler = new DownloadHandlerBuffer();
        requestWeb.SetRequestHeader("Content-Type", "application/json");

        yield return requestWeb.SendWebRequest();

        if (requestWeb.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Probe success: " + requestWeb.downloadHandler.text);
        }
        else
        {
            Debug.LogError("Probe failed: " + requestWeb.error);
        }
    }

    private void ResolveMachineManager()
    {
        if (machineManager != null)
            return;

        machineManager = targetMachine.GetComponentInChildren<LatheMachineManager>(true);

        if (machineManager == null)
            machineManager = targetMachine.GetComponentInParent<LatheMachineManager>();

        if (machineManager == null)
            machineManager = LatheMachineManager.Instance != null
                ? LatheMachineManager.Instance
                : FindAnyObjectByType<LatheMachineManager>();
    }

    private List<ComponentProbe> BuildFunctionalComponentProbes()
    {
        List<ComponentProbe> components = new List<ComponentProbe>();

        if (machineManager == null)
        {
            Debug.LogWarning("ProbeMachine: LatheMachineManager was not found. Sending an empty functional component list.");
            return components;
        }

        components.Add(new ComponentProbe(
            "3JawLatheSpindle",
            State(
                "requestSpindleOn", machineManager.requestSpindleOn,
                "currentRPM", machineManager.currentRPM,
                "targetRPM", machineManager.targetRPM,
                "spindleDirection", FriendlySignedDirection(machineManager.spindleDirection))));

        components.Add(new ComponentProbe(
            "Brake2",
            State(
                "panelBrakeEngaged", machineManager.panelBrakeEngaged,
                "brakeEngaged", machineManager.brakeEngaged)));

        components.Add(new ComponentProbe(
            "CarriageBody",
            State("carriageX", machineManager.carriageX)));

        components.Add(new ComponentProbe(
            "CarriageLongitudinalHandwheel",
            State(
                "driveCarriageFromLongitudinalHandwheel", machineManager.driveCarriageFromLongitudinalHandwheel,
                "carriageX", machineManager.carriageX)));

        components.Add(new ComponentProbe(
            "CarriageTop",
            State("crossSlideZ", machineManager.crossSlideZ)));

        components.Add(new ComponentProbe(
            "ControlPanel",
            State(
                "panelMainSwitchOn", machineManager.panelMainSwitchOn,
                "panelTransmission", machineManager.panelTransmission.ToString(),
                "panelSpindleStartLatched", machineManager.panelSpindleStartLatched,
                "panelFeedDirection", machineManager.panelFeedDirection.ToString(),
                "panelSplitNutEngaged", machineManager.panelSplitNutEngaged,
                "panelBrakeEngaged", machineManager.panelBrakeEngaged)));

        components.Add(new ComponentProbe(
            "DrillTail",
            State("tailQuillExtension", machineManager.tailQuillExtension)));

        components.Add(new ComponentProbe(
            "EmergencyStop",
            State("emergencyStop", machineManager.emergencyStop)));

        components.Add(new ComponentProbe(
            "FeedBar2",
            State(
                "requestFeedOn", machineManager.requestFeedOn,
                "currentFeedRate", machineManager.currentFeedRate,
                "targetFeedRate", machineManager.targetFeedRate)));

        components.Add(new ComponentProbe(
            "FeedDirectionSelector",
            State(
                "panelFeedDirection", machineManager.panelFeedDirection.ToString(),
                "requestFeedOn", machineManager.requestFeedOn,
                "feedDirection", machineManager.feedDirection)));

        components.Add(new ComponentProbe(
            "GearApplication",
            State(
                "panelGearAB", machineManager.panelGearAB.ToString(),
                "panelGear1234", machineManager.panelGear1234.ToString(),
                "panelGearCD", machineManager.panelGearCD.ToString(),
                "panelGearRSTU", machineManager.panelGearRSTU.ToString())));

        components.Add(new ComponentProbe(
            "LatheActivationLever",
            State(
                "panelSpindleStartLatched", machineManager.panelSpindleStartLatched,
                "requestSpindleOn", machineManager.requestSpindleOn,
                "panelSpindleDirection", machineManager.panelSpindleDirection.ToString())));

        components.Add(new ComponentProbe(
            "MainSwitch",
            State(
                "panelMainSwitchOn", machineManager.panelMainSwitchOn,
                "mainPower", machineManager.mainPower)));

        components.Add(new ComponentProbe(
            "ProtectiveDevice",
            State("protectiveDeviceClosed", machineManager.protectiveGlassClosed)));

        components.Add(new ComponentProbe(
            "SpeedSwitch1",
            State("panelGearAB", machineManager.panelGearAB.ToString())));

        components.Add(new ComponentProbe(
            "SpeedSwitch2",
            State("panelGear1234", machineManager.panelGear1234.ToString())));

        components.Add(new ComponentProbe(
            "SpeedSwitch3",
            State("panelGearCD", machineManager.panelGearCD.ToString())));

        components.Add(new ComponentProbe(
            "SpeedSwitch4",
            State("panelGearRSTU", machineManager.panelGearRSTU.ToString())));

        components.Add(new ComponentProbe(
            "SpeedSwitchPanel",
            State(
                "panelGearAB", machineManager.panelGearAB.ToString(),
                "panelGear1234", machineManager.panelGear1234.ToString(),
                "panelGearCD", machineManager.panelGearCD.ToString(),
                "panelGearRSTU", machineManager.panelGearRSTU.ToString())));

        components.Add(new ComponentProbe(
            "SpindleSpeedLever1",
            State(
                "panelSpeedRange", machineManager.panelSpeedRange.ToString(),
                "speedRange", machineManager.speedRange.ToString())));

        components.Add(new ComponentProbe(
            "SpindleSpeedLever2",
            State("panelSpindleSpeedLever", machineManager.panelSpindleSpeedLever.ToString())));

        components.Add(new ComponentProbe(
            "SplitNutControlLever",
            State(
                "panelSplitNutEngaged", machineManager.panelSplitNutEngaged,
                "splitNutEngaged", machineManager.splitNutEngaged)));

        components.Add(new ComponentProbe(
            "TailstockBlock",
            State("tailstockX", machineManager.tailstockX)));

        components.Add(new ComponentProbe(
            "TailstockHandwheel",
            State(
                "driveTailstockFromHandwheel", machineManager.driveTailstockFromHandwheel,
                "tailstockX", machineManager.tailstockX,
                "tailQuillExtension", machineManager.tailQuillExtension)));

        components.Add(new ComponentProbe(
            "TailSupport",
            State("tailstockX", machineManager.tailstockX)));

        components.Add(new ComponentProbe(
            "ToolLockingLever",
            State("toolPostRotationLocked", machineManager.toolPostRotationLocked)));

        components.Add(new ComponentProbe(
            "ToolLongitudinalWheel",
            State(
                "driveCompoundFromLongitudinalWheel", machineManager.driveCompoundFromLongitudinalWheel,
                "compoundX", machineManager.compoundX)));

        components.Add(new ComponentProbe(
            "ToolPost",
            State(
                "toolPostRotationLocked", machineManager.toolPostRotationLocked,
                "compoundX", machineManager.compoundX,
                "crossSlideZ", machineManager.crossSlideZ)));

        components.Add(new ComponentProbe(
            "ToolTransversalWheel",
            State(
                "driveCrossSlideFromTransversalWheel", machineManager.driveCrossSlideFromTransversalWheel,
                "crossSlideZ", machineManager.crossSlideZ)));

        components.Add(new ComponentProbe(
            "TransmissionLever",
            State(
                "panelTransmission", machineManager.panelTransmission.ToString(),
                "transmissionMode", machineManager.transmissionMode)));

        components.Add(new ComponentProbe(
            "TurningBar",
            State("rotateTurningBarWithCarriageHandwheel", machineManager.rotateTurningBarWithCarriageHandwheel)));

        components.Add(new ComponentProbe(
            "VoltageIndicator",
            State(
                "mainPower", machineManager.mainPower,
                "panelMainSwitchOn", machineManager.panelMainSwitchOn)));

        AddDescriptions(components);
        AddPossibleStates(components);

        return components;
    }

    private void AddDescriptions(List<ComponentProbe> components)
    {
        foreach (ComponentProbe component in components)
        {
            component.description = GetDescriptionForComponent(component.name);
        }
    }

    private string GetDescriptionForComponent(string componentName)
    {
        switch (componentName)
        {
            case "3JawLatheSpindle":
                return "Holds and rotates the workpiece; its runtime state tracks spindle request, RPM, and direction.";

            case "Brake2":
                return "Brake control used to stop or prevent spindle motion while engaged.";

            case "CarriageBody":
                return "Main carriage body that travels along the lathe bed on the X axis.";

            case "CarriageLongitudinalHandwheel":
                return "Manual handwheel that drives carriage travel along the bed.";

            case "CarriageTop":
                return "Cross-slide assembly that moves the tool laterally relative to the workpiece.";

            case "ControlPanel":
                return "Aggregated machine control panel state for power, transmission, spindle, feed, split nut, and brake controls.";

            case "DrillTail":
                return "Tailstock quill/drill element that extends toward the workpiece.";

            case "EmergencyStop":
                return "Safety stop that immediately disables spindle and feed requests.";

            case "FeedBar2":
                return "Feed drive element associated with automatic carriage feed rate and feed request state.";

            case "FeedDirectionSelector":
                return "Control that selects feed direction and whether feed motion is requested.";

            case "GearApplication":
                return "Gear selector group that determines feed-per-revolution and thread pitch combinations.";

            case "LatheActivationLever":
                return "Spindle activation lever that latches spindle start and direction request.";

            case "MainSwitch":
                return "Primary power switch for enabling machine power.";

            case "ProtectiveDevice":
                return "Protective device state used by the safety system before spindle/feed motion is allowed.";

            case "SpeedSwitch1":
                return "A/B gear selector for feed and threading gear calculations.";

            case "SpeedSwitch2":
                return "1/2/3/4 gear selector for feed, threading, and spindle speed table selection.";

            case "SpeedSwitch3":
                return "C/D gear selector for feed and threading gear calculations.";

            case "SpeedSwitch4":
                return "R/S/T/U gear selector for feed and threading gear calculations.";

            case "SpeedSwitchPanel":
                return "Grouped speed/feed gear selector panel containing A/B, 1/2/3/4, C/D, and R/S/T/U selections.";

            case "SpindleSpeedLever1":
                return "Selects low or high spindle speed range.";

            case "SpindleSpeedLever2":
                return "Selects the spindle speed lever position within the active speed range.";

            case "SplitNutControlLever":
                return "Engages the split nut for threading behavior.";

            case "TailstockBlock":
                return "Tailstock body position along the lathe bed.";

            case "TailstockHandwheel":
                return "Manual control for tailstock movement and quill extension.";

            case "TailSupport":
                return "Tailstock support assembly position along the lathe bed.";

            case "ToolLockingLever":
                return "Locks or unlocks toolpost rotation.";

            case "ToolLongitudinalWheel":
                return "Manual wheel that drives compound/tool longitudinal travel.";

            case "ToolPost":
                return "Tool holder assembly affected by compound, cross-slide, and rotation-lock states.";

            case "ToolTransversalWheel":
                return "Manual wheel that drives cross-slide transverse travel.";

            case "TransmissionLever":
                return "Selects machine transmission mode: neutral, spindle, feed, or thread.";

            case "TurningBar":
                return "Visual follower that can rotate with carriage handwheel motion.";

            case "VoltageIndicator":
                return "Indicates whether machine power is active.";

            default:
                return "Functional lathe component exposed to the probing API.";
        }
    }

    private void AddPossibleStates(List<ComponentProbe> components)
    {
        foreach (ComponentProbe component in components)
        {
            component.possible_states = BuildPossibleStates(component.default_state);
        }
    }

    private Dictionary<string, object> BuildPossibleStates(Dictionary<string, object> state)
    {
        Dictionary<string, object> possibleStates = new Dictionary<string, object>();

        foreach (string key in state.Keys)
        {
            possibleStates[key] = GetPossibleStatesForKey(key);
        }

        return possibleStates;
    }

    private object GetPossibleStatesForKey(string key)
    {
        switch (key)
        {
            case "requestSpindleOn":
            case "panelBrakeEngaged":
            case "brakeEngaged":
            case "driveCarriageFromLongitudinalHandwheel":
            case "panelMainSwitchOn":
            case "panelSpindleStartLatched":
            case "panelSplitNutEngaged":
            case "emergencyStop":
            case "requestFeedOn":
            case "mainPower":
            case "protectiveDeviceClosed":
            case "splitNutEngaged":
            case "driveTailstockFromHandwheel":
            case "toolPostRotationLocked":
            case "driveCompoundFromLongitudinalWheel":
            case "driveCrossSlideFromTransversalWheel":
            case "rotateTurningBarWithCarriageHandwheel":
                return BoolStates();

            case "spindleDirection":
            case "panelSpindleDirection":
            case "panelFeedDirection":
                return DirectionStringStates();

            case "feedDirection":
                return DirectionIntStates();

            case "currentRPM":
                return NumericRange(-2000f, 2000f, "RPM", "Continuous runtime spindle speed.");

            case "targetRPM":
                return SpindleTargetRpmStates();

            case "currentFeedRate":
                return RuntimeFloat("m/s", "Continuous runtime carriage feed rate derived from RPM, feed gearing, and feed direction.");

            case "targetFeedRate":
                return RuntimeFloat("m/s", "Target feed rate derived from RPM, feed gearing, transmission mode, and feed direction.");

            case "carriageX":
                return NumericRange(machineManager.carriageLimits.x, machineManager.carriageLimits.y, "m", "Carriage travel.");

            case "crossSlideZ":
                return NumericRange(machineManager.crossSlideLimits.x, machineManager.crossSlideLimits.y, "m", "Cross-slide travel.");

            case "compoundX":
                return NumericRange(machineManager.compoundLimits.x, machineManager.compoundLimits.y, "m", "Compound/toolpost travel.");

            case "tailstockX":
                return NumericRange(machineManager.tailstockLimits.x, machineManager.tailstockLimits.y, "m", "Tailstock body travel.");

            case "tailQuillExtension":
                return NumericRange(machineManager.tailQuillLimits.x, machineManager.tailQuillLimits.y, "m", "Tailstock quill extension.");

            case "panelTransmission":
                return EnumNames(typeof(LatheMachineManager.TransmissionState));

            case "transmissionMode":
                return new List<int> { 0, 1, 2, 3 };

            case "panelGearAB":
                return EnumNames(typeof(LatheMachineManager.GearSelectorAB));

            case "panelGear1234":
                return EnumNames(typeof(LatheMachineManager.GearSelector1234));

            case "panelGearCD":
                return EnumNames(typeof(LatheMachineManager.GearSelectorCD));

            case "panelGearRSTU":
                return EnumNames(typeof(LatheMachineManager.GearSelectorRSTU));

            case "panelSpeedRange":
            case "speedRange":
                return EnumNames(typeof(LatheGearbox.Range));

            case "panelSpindleSpeedLever":
                return EnumNames(typeof(LatheMachineManager.SpindleSpeedLeverPosition));

            default:
                return RuntimeValue();
        }
    }

    private List<bool> BoolStates()
    {
        return new List<bool> { false, true };
    }

    private List<string> DirectionStringStates()
    {
        return new List<string> { "Reverse", "Neutral", "Forward" };
    }

    private List<int> DirectionIntStates()
    {
        return new List<int> { -1, 0, 1 };
    }

    private List<string> EnumNames(System.Type enumType)
    {
        return new List<string>(System.Enum.GetNames(enumType));
    }

    private List<float> SpindleTargetRpmStates()
    {
        return new List<float>
        {
            -2000f,
            -1255f,
            -755f,
            -460f,
            -300f,
            -190f,
            -115f,
            -70f,
            0f,
            70f,
            115f,
            190f,
            300f,
            460f,
            755f,
            1255f,
            2000f
        };
    }

    private Dictionary<string, object> NumericRange(
        float min,
        float max,
        string unit,
        string description)
    {
        return State(
            "type", "float",
            "min", min,
            "max", max,
            "unit", unit,
            "description", description);
    }

    private Dictionary<string, object> RuntimeFloat(
        string unit,
        string description)
    {
        return State(
            "type", "float",
            "unit", unit,
            "description", description);
    }

    private Dictionary<string, object> RuntimeValue()
    {
        return State(
            "type", "runtime",
            "description", "Runtime value controlled by the lathe simulation.");
    }

    private Dictionary<string, object> State(params object[] keyValues)
    {
        Dictionary<string, object> state = new Dictionary<string, object>();

        for (int i = 0; i < keyValues.Length - 1; i += 2)
        {
            string key = keyValues[i] as string;

            if (string.IsNullOrWhiteSpace(key))
                continue;

            state[key] = keyValues[i + 1];
        }

        return state;
    }

    private string FriendlySignedDirection(int direction)
    {
        if (direction > 0)
            return "Forward";

        if (direction < 0)
            return "Reverse";

        return "Neutral";
    }
}
