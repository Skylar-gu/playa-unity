using UnityEngine;
using Playa.Urdf;

namespace Playa.Verification
{
    // MonoBehaviour that runs every validator once per frame after the
    // choreographer has driven the joints. Owns the shared FeasibilityReport
    // that HUD, tint applier, and CrowdTelemetry all read from.
    [DefaultExecutionOrder(-20)]      // after RobotChoreographer, before HUD
    public sealed class FeasibilityAuditor : MonoBehaviour
    {
        public UrdfRobotInstance Robot { get; private set; }
        public FeasibilityReport Report { get; } = new FeasibilityReport();
        public bool IsBound => Robot != null;

        TorqueEstimator torqueEstimator;
        BalanceValidator balanceValidator;

        public void Bind(UrdfRobotInstance robot)
        {
            Robot = robot;
            torqueEstimator = new TorqueEstimator(robot);
            balanceValidator = new BalanceValidator(robot);
            Report.Joints.Clear();
            foreach (var j in robot.ActuatedJoints)
                Report.Joints.Add(new JointCheck { Joint = j });
        }

        void LateUpdate()
        {
            if (!IsBound) return;
            float now = Time.time;
            // Full-body RNEA pass — populates every joint's torque before the
            // per-joint check loop reads it.
            torqueEstimator.Recompute(now);
            for (int i = 0; i < Report.Joints.Count; i++)
            {
                var jc = JointLimitValidator.Check(Report.Joints[i].Joint);
                torqueEstimator.EstimateInto(ref jc, now);
                Report.Joints[i] = jc;
            }
            balanceValidator.CheckInto(Report);
            Report.Recompute();
        }
    }
}
