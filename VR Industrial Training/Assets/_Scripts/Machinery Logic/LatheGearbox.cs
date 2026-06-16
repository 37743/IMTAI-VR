using UnityEngine;

public class LatheGearbox
{
    public enum Range
    {
        Low,
        High
    }

    public struct GearState
    {
        public float rpm;
        public float feedPerRev;
        public float threadPitchMetric;
        public float threadPitchInch;
    }

    private GearState[] lowRange;
    private GearState[] highRange;

    public LatheGearbox()
    {
        lowRange = new GearState[16];
        highRange = new GearState[16];

        InitializeTables();
    }

    void InitializeTables()
    {
        // Low range RPM values based on manual
        float[] lowRPM = {190,300,115,70};
        float[] highRPM = {1255,2000,755,460};

        for(int i=0;i<16;i++)
        {
            int step = i % 4;

            lowRange[i] = new GearState
            {
                rpm = lowRPM[step],
                feedPerRev = 0.03f + step*0.02f,
                threadPitchMetric = 0.5f + step*0.5f,
                threadPitchInch = 20f - step*4
            };

            highRange[i] = new GearState
            {
                rpm = highRPM[step],
                feedPerRev = 0.04f + step*0.03f,
                threadPitchMetric = 0.75f + step*0.5f,
                threadPitchInch = 16f - step*3
            };
        }
    }

    public float GetRPM(
        Range range,
        LatheMachineManager.SpindleSpeedLeverPosition spindleSpeedLever)
    {
        int index = Mathf.Clamp((int)spindleSpeedLever, 0, 3);

        if (range == Range.Low)
            return lowRange[index].rpm;

        return highRange[index].rpm;
    }

    public float GetFeedPerRev(
        LatheMachineManager.GearSelectorAB gearAB,
        LatheMachineManager.GearSelector1234 gear1234,
        LatheMachineManager.GearSelectorCD gearCD,
        LatheMachineManager.GearSelectorRSTU gearRSTU)
    {
        int index = GetPanelGearIndex(gearAB, gear1234, gearCD, gearRSTU);
        return 0.02f + index * 0.005f;
    }

    public float GetThreadPitchMetric(
        LatheMachineManager.GearSelectorAB gearAB,
        LatheMachineManager.GearSelector1234 gear1234,
        LatheMachineManager.GearSelectorCD gearCD,
        LatheMachineManager.GearSelectorRSTU gearRSTU)
    {
        int index = GetPanelGearIndex(gearAB, gear1234, gearCD, gearRSTU);
        return 0.25f + index * 0.125f;
    }

    public float GetThreadPitchInch(
        LatheMachineManager.GearSelectorAB gearAB,
        LatheMachineManager.GearSelector1234 gear1234,
        LatheMachineManager.GearSelectorCD gearCD,
        LatheMachineManager.GearSelectorRSTU gearRSTU)
    {
        int index = GetPanelGearIndex(gearAB, gear1234, gearCD, gearRSTU);
        return Mathf.Max(4f, 32f - index);
    }

    private int GetPanelGearIndex(
        LatheMachineManager.GearSelectorAB gearAB,
        LatheMachineManager.GearSelector1234 gear1234,
        LatheMachineManager.GearSelectorCD gearCD,
        LatheMachineManager.GearSelectorRSTU gearRSTU)
    {
        int index = 0;
        index += (int)gear1234;
        index += (int)gearRSTU * 4;
        index += (int)gearCD * 16;
        index += (int)gearAB * 32;
        return index;
    }
}
