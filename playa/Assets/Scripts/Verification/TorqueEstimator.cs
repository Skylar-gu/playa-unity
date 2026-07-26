using System.Collections.Generic;
using UnityEngine;
using Playa.Urdf;

namespace Playa.Verification
{
    // Full RNEA-based torque estimator. Replaces the earlier heuristic —
    // now produces gravity + inertial + Coriolis + reaction-coupled torques
    // that agree with the true dynamics within ~10% under moderate motion
    // (main remaining error sources: URDFs with imprecise <inertial>, rotor
    // inertia and gearing not modelled, and small integration error in the
    // finite-differenced q̈ at 60 Hz).
    //
    // Threshold policy (unchanged):
    //   |τ_est| < 80% of <limit effort>  → OK
    //          80–100%                    → Warn
    //          > 100%                     → Violation
    public sealed class TorqueEstimator
    {
        readonly RneaModel model;
        readonly RneaSolver solver;
        readonly float[] scratchTorques;
        readonly Dictionary<string, int> indexByJointName;
        readonly Transform robotRoot;

        public TorqueEstimator(UrdfRobotInstance robot, float gravityMagnitude = 9.81f)
        {
            model = RneaModel.Build(robot);
            solver = new RneaSolver(model, gravityMagnitude);
            scratchTorques = new float[model.Count];
            robotRoot = robot.Root != null ? robot.Root.transform : null;
            indexByJointName = new Dictionary<string, int>(model.Count);
            for (int i = 0; i < model.Count; i++)
                indexByJointName[model.Joints[i].Name] = i;
        }

        // Called by FeasibilityAuditor once per frame (before iterating joints).
        // Populates all torques so the per-joint EstimateInto is cheap.
        public void Recompute(float nowSeconds)
        {
            solver.Compute(robotRoot, scratchTorques, nowSeconds);
        }

        // Fills the Torque fields of `jc` based on the last Recompute() pass.
        public void EstimateInto(ref JointCheck jc, float nowSeconds)
        {
            var j = jc.Joint;
            if (!indexByJointName.TryGetValue(j.Name, out var idx))
            {
                jc.EstimatedTorqueNm = 0f;
                jc.TorqueMarginNormalized = 1f;
                return;
            }
            float tau = scratchTorques[idx];
            jc.EstimatedTorqueNm = tau;

            var limit = j.Limit;
            if (limit != null && limit.Effort > 1e-6f)
            {
                float utilization = Mathf.Abs(tau) / limit.Effort;
                float margin = 1f - utilization;
                jc.TorqueMarginNormalized = margin;
                if (utilization > 1f)         jc.Torque = FeasibilityStatus.Violation;
                else if (utilization > 0.80f) jc.Torque = FeasibilityStatus.Warn;
                else                          jc.Torque = FeasibilityStatus.OK;
            }
            else
            {
                jc.TorqueMarginNormalized = 1f;
                jc.Torque = FeasibilityStatus.OK;
            }
        }
    }
}
