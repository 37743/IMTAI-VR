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
        if (m.carriageAssembly != null)
        {
            Vector3 p = m.CarriageAssemblyHomeLocalPosition;
            p.x += m.carriageX;
            m.carriageAssembly.localPosition = p;
        }
        else if (m.carriageBody != null)
        {
            Vector3 p = m.CarriageBodyHomeLocalPosition;
            p.x += m.carriageX;
            m.carriageBody.localPosition = p;
        }

        if (m.toolPost != null)
        {
            bool toolPostFollowsCrossSlideFromParent =
                m.carriageTop != null && m.toolPost.IsChildOf(m.carriageTop);

            Vector3 p = m.ToolPostHomeLocalPosition;
            p.x += m.compoundX;

            if (!toolPostFollowsCrossSlideFromParent)
                p.z += m.crossSlideZ;

            m.toolPost.localPosition = p;
        }
    }

    void ApplyCrossSlide(LatheMachineManager m)
    {
        if (m.carriageTop == null) return;

        Vector3 p = m.CarriageTopHomeLocalPosition;
        p.z += m.crossSlideZ;
        m.carriageTop.localPosition = p;
    }

    void ApplyTailstock(LatheMachineManager m)
    {
        if (m.tailstockAssembly != null)
        {
            Vector3 p = m.TailstockAssemblyHomeLocalPosition;
            p.x += m.tailstockX;
            m.tailstockAssembly.localPosition = p;
        }
        else if (m.tailstockBlock != null)
        {
            Vector3 p = m.TailstockBlockHomeLocalPosition;
            p.x += m.tailstockX;
            m.tailstockBlock.localPosition = p;
        }

        if (m.drillTail != null)
        {
            Vector3 p = m.DrillTailHomeLocalPosition;
            p.z += m.tailQuillExtension;
            m.drillTail.localPosition = p;
        }
    }

    void ApplyHandwheels(LatheMachineManager m, float dt)
    {
        if (!m.driveCarriageFromLongitudinalHandwheel && m.carriageLongitudinalHandwheel != null)
        {
            float wheelDeg = m.currentFeedRate * 1200f * dt;
            m.carriageLongitudinalHandwheel.Rotate(Vector3.right, wheelDeg, Space.Self);
        }

        if (!m.driveTailstockFromHandwheel && m.tailstockHandwheel != null)
        {
            float wheelDeg = m.tailQuillExtension * 300f;
            Vector3 e = m.tailstockHandwheel.localEulerAngles;
            e.x = wheelDeg;
            m.tailstockHandwheel.localEulerAngles = e;
        }
    }
}
