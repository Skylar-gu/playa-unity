using UnityEngine;
using Playa.Urdf;

namespace Playa.Verification
{
    // Compares the current joint value and estimated velocity against the
    // URDF-declared <limit> for each joint. Fills the Position/Velocity fields
    // of a JointCheck. Torque is filled by TorqueEstimator.
    public static class JointLimitValidator
    {
        // Margin bands (as fraction of the limit range or limit magnitude).
        const float WarnThreshold = 0.20f;
        const float ViolationThreshold = 0.0f;

        public static JointCheck Check(UrdfJoint j)
        {
            var jc = new JointCheck
            {
                Joint = j,
                PositionMarginNormalized = 1f,
                VelocityMarginNormalized = 1f,
                TorqueMarginNormalized = 1f,
                Position = FeasibilityStatus.OK,
                Velocity = FeasibilityStatus.OK,
                Torque = FeasibilityStatus.OK,
            };

            var limit = j.Limit;
            if (limit == null)
            {
                // No limits declared — treat as unbounded. Continuous joints go here.
                jc.PositionMarginNormalized = 1f;
                jc.VelocityMarginNormalized = 1f;
                return jc;
            }

            // Position: distance from midpoint scaled by half-range.
            if (limit.HasPositionLimits)
            {
                float mid = 0.5f * (limit.Upper + limit.Lower);
                float halfRange = 0.5f * (limit.Upper - limit.Lower);
                if (halfRange > 1e-6f)
                {
                    float distFromMid = Mathf.Abs(j.Value - mid);
                    float margin = 1f - distFromMid / halfRange;
                    jc.PositionMarginNormalized = margin;
                    if (margin < ViolationThreshold) jc.Position = FeasibilityStatus.Violation;
                    else if (margin < WarnThreshold) jc.Position = FeasibilityStatus.Warn;
                }
            }

            // Velocity: |q̇| vs limit.
            if (limit.Velocity > 1e-6f)
            {
                float speed = Mathf.Abs(j.VelocityEstimate);
                float margin = 1f - speed / limit.Velocity;
                jc.VelocityMarginNormalized = margin;
                if (margin < ViolationThreshold) jc.Velocity = FeasibilityStatus.Violation;
                else if (margin < WarnThreshold) jc.Velocity = FeasibilityStatus.Warn;
            }

            return jc;
        }
    }
}
