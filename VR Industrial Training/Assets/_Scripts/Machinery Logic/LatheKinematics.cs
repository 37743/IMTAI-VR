using UnityEngine;

public class LatheKinematics
{
    public void Apply(LatheMachineManager m, float dt)
    {
        ApplySpindle(m, dt);
        ApplyCarriage(m);
        ApplyCrossSlide(m);
        ApplyTailstock(m);
        ApplyHandwheels(m, dt);
    }

    void ApplySpindle(LatheMachineManager m, float dt)
    {
        if (m.spindle == null) return;

        float degreesPerSecond = m.currentRPM * 6f;
        m.spindle.Rotate(Vector3.right, degreesPerSecond * dt, Space.Self);
    }

    void ApplyCarriage(LatheMachineManager m)
    {
        if (m.carriageBody != null)
        {
            Vector3 p = m.carriageBody.localPosition;
            p.x = m.carriageX;
            m.carriageBody.localPosition = p;
        }

        if (m.toolPost != null)
        {
            Vector3 p = m.toolPost.localPosition;
            p.x = m.compoundX;
            p.z = m.crossSlideZ;
            m.toolPost.localPosition = p;
        }
    }

    void ApplyCrossSlide(LatheMachineManager m)
    {
        if (m.carriageTop == null) return;

        Vector3 p = m.carriageTop.localPosition;
        p.z = m.crossSlideZ;
        m.carriageTop.localPosition = p;
    }

    void ApplyTailstock(LatheMachineManager m)
    {
        if (m.tailstockBlock != null)
        {
            Vector3 p = m.tailstockBlock.localPosition;
            p.x = m.tailstockX;
            m.tailstockBlock.localPosition = p;
        }

        if (m.drillTail != null)
        {
            Vector3 p = m.drillTail.localPosition;
            p.z = m.tailQuillExtension;
            m.drillTail.localPosition = p;
        }
    }

    void ApplyHandwheels(LatheMachineManager m, float dt)
    {
        if (m.carriageLongitudinalHandwheel != null)
        {
            float wheelDeg = m.currentFeedRate * 1200f * dt;
            m.carriageLongitudinalHandwheel.Rotate(Vector3.right, wheelDeg, Space.Self);
        }

        if (m.tailstockHandwheel != null)
        {
            float wheelDeg = m.tailQuillExtension * 300f;
            Vector3 e = m.tailstockHandwheel.localEulerAngles;
            e.x = wheelDeg;
            m.tailstockHandwheel.localEulerAngles = e;
        }
    }
}