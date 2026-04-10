using UnityEngine;

public class LatheSafetySystem
{
    public bool CanRunSpindle(LatheMachineManager m)
    {
        if (!m.mainPower) return false;
        if (m.emergencyStop) return false;
        if (m.brakeEngaged) return false;
        if (!m.protectiveGlassClosed) return false;
        if (!m.protectiveDeviceClosed) return false;
        if (m.transmissionMode == 0) return false; // neutral
        return true;
    }

    public bool CanRunFeed(LatheMachineManager m)
    {
        if (!CanRunSpindle(m)) return false;
        if (Mathf.Abs(m.currentRPM) < 1f) return false;
        if (m.transmissionMode != 2 && m.transmissionMode != 3) return false;
        return true;
    }

    public bool CanThread(LatheMachineManager m)
    {
        if (!CanRunFeed(m)) return false;
        if (!m.splitNutEngaged) return false;
        if (m.transmissionMode != 3) return false;
        return true;
    }
}