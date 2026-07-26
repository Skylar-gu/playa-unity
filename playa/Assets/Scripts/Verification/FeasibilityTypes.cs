using System.Collections.Generic;
using Playa.Urdf;

namespace Playa.Verification
{
    // How close to the limit the joint is running.
    //   OK        → margin > 20% of limit
    //   Warn      → margin between 5% and 20%
    //   Violation → beyond limit
    public enum FeasibilityStatus { OK, Warn, Violation }

    public struct JointCheck
    {
        public UrdfJoint Joint;
        public FeasibilityStatus Position;
        public FeasibilityStatus Velocity;
        public FeasibilityStatus Torque;
        public float PositionMarginNormalized;   // 1 = at midpoint, 0 = at limit
        public float VelocityMarginNormalized;
        public float TorqueMarginNormalized;
        public float EstimatedTorqueNm;

        public FeasibilityStatus Worst
        {
            get
            {
                var w = Position;
                if ((int)Velocity > (int)w) w = Velocity;
                if ((int)Torque > (int)w) w = Torque;
                return w;
            }
        }
    }

    public sealed class FeasibilityReport
    {
        public readonly List<JointCheck> Joints = new List<JointCheck>();
        public FeasibilityStatus BalanceStatus = FeasibilityStatus.OK;
        public float BalanceMarginNormalized = 1f;
        public string WorstJointName = "";
        public FeasibilityStatus Overall = FeasibilityStatus.OK;

        // Aggregate score in [0, 1] — 1 = perfectly nominal, 0 = many violations.
        public float FeasibilityScore = 1f;

        public void Recompute()
        {
            int nJoints = Joints.Count;
            int violations = 0, warns = 0;
            float marginSum = 0f;
            var worst = FeasibilityStatus.OK;
            WorstJointName = "";
            for (int i = 0; i < nJoints; i++)
            {
                var jc = Joints[i];
                var w = jc.Worst;
                if ((int)w > (int)worst) { worst = w; WorstJointName = jc.Joint.Name; }
                if (w == FeasibilityStatus.Violation) violations++;
                else if (w == FeasibilityStatus.Warn) warns++;
                // Take the tightest per-check margin as the joint's overall margin.
                float m = System.Math.Min(jc.PositionMarginNormalized,
                          System.Math.Min(jc.VelocityMarginNormalized, jc.TorqueMarginNormalized));
                marginSum += UnityEngine.Mathf.Clamp01(m);
            }
            if ((int)BalanceStatus > (int)worst) { worst = BalanceStatus; WorstJointName = "<balance>"; }

            Overall = worst;
            float avgMargin = nJoints > 0 ? marginSum / nJoints : 1f;
            // Penalize violations and warns; balance failure hurts a lot.
            float penalty = 0.6f * violations + 0.15f * warns;
            float balancePenalty = BalanceStatus == FeasibilityStatus.Violation ? 1.0f
                                 : BalanceStatus == FeasibilityStatus.Warn ? 0.25f : 0f;
            float raw = avgMargin - (nJoints > 0 ? penalty / nJoints : 0f) - balancePenalty;
            FeasibilityScore = UnityEngine.Mathf.Clamp01(raw);
        }
    }
}
