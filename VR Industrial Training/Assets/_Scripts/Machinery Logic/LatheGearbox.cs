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

    int GetGearIndex(bool s1,bool s2,bool s3,bool s4)
    {
        int index=0;

        if(s1) index|=1;
        if(s2) index|=2;
        if(s3) index|=4;
        if(s4) index|=8;

        return index;
    }

    public float GetRPM(Range range,bool s1,bool s2,bool s3,bool s4)
    {
        int index=GetGearIndex(s1,s2,s3,s4);

        if(range==Range.Low)
            return lowRange[index].rpm;

        return highRange[index].rpm;
    }

    public float GetFeedPerRev(Range range,bool s1,bool s2,bool s3,bool s4)
    {
        int index=GetGearIndex(s1,s2,s3,s4);

        if(range==Range.Low)
            return lowRange[index].feedPerRev;

        return highRange[index].feedPerRev;
    }

    public float GetThreadPitchMetric(Range range,bool s1,bool s2,bool s3,bool s4)
    {
        int index=GetGearIndex(s1,s2,s3,s4);

        if(range==Range.Low)
            return lowRange[index].threadPitchMetric;

        return highRange[index].threadPitchMetric;
    }

    public float GetThreadPitchInch(Range range,bool s1,bool s2,bool s3,bool s4)
    {
        int index=GetGearIndex(s1,s2,s3,s4);

        if(range==Range.Low)
            return lowRange[index].threadPitchInch;

        return highRange[index].threadPitchInch;
    }
}