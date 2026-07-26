using UnityEngine;
using Playa.Urdf;

namespace Playa.Verification
{
    // Recursive Newton-Euler algorithm for kinematic-tree torque estimation.
    //
    // Given per-joint (q, q̇, q̈), computes the joint torque τ_i required to
    // produce that motion given the URDF-declared mass distribution, using
    // the classic two-pass Craig formulation:
    //
    //   Forward (root → leaves):
    //     ω_i = R_ptc · ω_p + q̇_i · z_i
    //     α_i = R_ptc · α_p + q̈_i · z_i + (R_ptc · ω_p) × (q̇_i · z_i)
    //     a_i = R_ptc · (a_p + α_p × r + ω_p × (ω_p × r))
    //
    //   Backward (leaves → root):
    //     a_c = a_i + α_i × c + ω_i × (ω_i × c)      // accel at CoM
    //     F   = m · a_c                                // inertial force
    //     N   = I · α_i + ω_i × (I · ω_i)              // inertial torque about CoM
    //     f_i = F + Σ_children ( R_ctp · f_k )         // net force at joint
    //     n_i = N + c × F +
    //           Σ_children ( R_ctp · n_k + r_k × (R_ctp · f_k) )
    //     τ_i = z_i · n_i                              // projection onto joint axis
    //
    // Gravity enters as the base link's linear acceleration: we set
    // LinAccelOrigin[base] = -g_world_in_base_frame so that every downstream
    // link's a_i naturally includes gravity, and the backward pass produces
    // gravity-inclusive torques.
    //
    // The solver processes joints in the order stored in RneaModel — this
    // must be a topological order (parent before child), which is what
    // UrdfInstantiator's DFS build produces.
    public sealed class RneaSolver
    {
        readonly RneaModel model;
        readonly float gravityMagnitude;

        public RneaSolver(RneaModel model, float gravityMagnitude = 9.81f)
        {
            this.model = model;
            this.gravityMagnitude = gravityMagnitude;
        }

        // Compute τ into torqueOut[i] for each joint. Returns model.Count.
        public int Compute(Transform robotRoot, float[] torqueOut, float nowSeconds)
        {
            int n = model.Count;
            if (torqueOut == null || torqueOut.Length < n) return 0;

            // Base-link "gravity acceleration" expressed in the robot root's
            // local frame. In Craig's convention we treat gravity as an upward
            // pseudo-acceleration of the base so that the recursion produces
            // the same torques it would under real gravity.
            Vector3 gravityWorld = new Vector3(0f, -gravityMagnitude, 0f);
            Vector3 baseAccelLocal = robotRoot != null
                ? robotRoot.InverseTransformDirection(-gravityWorld)  // note the sign
                : new Vector3(0f, gravityMagnitude, 0f);

            // ---- FORWARD PASS -------------------------------------------------
            for (int i = 0; i < n; i++)
            {
                var joint = model.Joints[i];
                float q  = joint.Value;
                float qd = joint.VelocityEstimate;

                // Finite-difference acceleration from velocity samples.
                float dt = Mathf.Max(1e-3f, nowSeconds - model.PrevAccelTime[i]);
                float qdd = (qd - model.PrevJointVelocity[i]) / dt;
                model.PrevJointVelocity[i] = qd;
                model.PrevAccelTime[i] = nowSeconds;

                // Rotation from parent frame to child frame:
                //   full = rest_R_ptc * motion_R_ptc(-q)
                // We stored RestRotationParentToChild = inv(child.localRotation_rest).
                // The joint motion adds a rotation of q about JointAxisChild in the
                // child frame. To convert this to a P→C transform, invert its sign.
                Quaternion motionCtp = Quaternion.AngleAxis(q * Mathf.Rad2Deg, model.JointAxisChild[i]);
                Quaternion motionPtc = Quaternion.Inverse(motionCtp);
                Quaternion R_ptc = motionPtc * model.RestRotationParentToChild[i];

                Vector3 z = model.JointAxisChild[i];
                Vector3 r = model.JointOriginTranslationParentFrame[i];

                Vector3 omegaP, alphaP, aP;
                int p = model.ParentIndex[i];
                if (p < 0)
                {
                    omegaP = Vector3.zero;
                    alphaP = Vector3.zero;
                    aP = baseAccelLocal;
                }
                else
                {
                    omegaP = model.Omega[p];
                    alphaP = model.Alpha[p];
                    aP = model.LinAccelOrigin[p];
                }

                // Parent-frame contribution to child origin's linear accel.
                Vector3 aParentContribParent =
                    aP + Vector3.Cross(alphaP, r) + Vector3.Cross(omegaP, Vector3.Cross(omegaP, r));

                Vector3 R_omegaP = R_ptc * omegaP;
                Vector3 R_alphaP = R_ptc * alphaP;
                Vector3 R_aParent = R_ptc * aParentContribParent;

                Vector3 omegaI = R_omegaP + qd * z;
                Vector3 alphaI = R_alphaP + qdd * z + Vector3.Cross(R_omegaP, qd * z);
                Vector3 aI = R_aParent;                // linear accel at child origin, child frame

                model.Omega[i] = omegaI;
                model.Alpha[i] = alphaI;
                model.LinAccelOrigin[i] = aI;
            }

            // Zero the backward accumulator slots — they'll be summed into
            // during the pass (children write into parent slot in this scheme
            // is inverted below, so we clear per-joint self slots first).
            for (int i = 0; i < n; i++)
            {
                model.NetForce[i] = Vector3.zero;
                model.NetTorque[i] = Vector3.zero;
            }

            // ---- BACKWARD PASS ------------------------------------------------
            //
            // Process leaves → root by iterating in reverse of the topological
            // build order. At each joint i we already have downstream children's
            // wrenches accumulated into NetForce[i] / NetTorque[i] (children
            // contribute upstream during their own step, so we do the write to
            // parent AFTER computing our own local F/N).
            for (int i = n - 1; i >= 0; i--)
            {
                Vector3 omega = model.Omega[i];
                Vector3 alpha = model.Alpha[i];
                Vector3 aO = model.LinAccelOrigin[i];
                Vector3 c = model.LinkComChild[i];
                float m = model.LinkMass[i];

                Vector3 F = Vector3.zero, N = Vector3.zero;
                if (m > 0f)
                {
                    Vector3 aC = aO + Vector3.Cross(alpha, c)
                                    + Vector3.Cross(omega, Vector3.Cross(omega, c));
                    F = m * aC;
                    N = model.InertiaAboutCom[i].Mul(alpha)
                        + Vector3.Cross(omega, model.InertiaAboutCom[i].Mul(omega))
                        + Vector3.Cross(c, F);
                }

                // Add child accumulations already summed into our slot.
                F += model.NetForce[i];
                N += model.NetTorque[i];

                model.NetForce[i] = F;
                model.NetTorque[i] = N;

                // Propagate to parent: transform F, N back to parent frame,
                // and add r × F for the moment shift.
                int p = model.ParentIndex[i];
                if (p >= 0)
                {
                    Quaternion motionCtp = Quaternion.AngleAxis(
                        model.Joints[i].Value * Mathf.Rad2Deg, model.JointAxisChild[i]);
                    Quaternion R_ctp_rest = model.Joints[i].RestLocalRotation;
                    Quaternion R_ctp = R_ctp_rest * motionCtp;   // child → parent
                    Vector3 r = model.JointOriginTranslationParentFrame[i];

                    Vector3 F_parent = R_ctp * F;
                    Vector3 N_parent = R_ctp * N + Vector3.Cross(r, F_parent);
                    model.NetForce[p] += F_parent;
                    model.NetTorque[p] += N_parent;
                }
                // p < 0 → force/torque terminate at the base; they'd be reacted
                // by the base support / fixed mount. Ignored for τ_joint purposes.
            }

            // Project each joint's net torque onto its axis to get scalar τ.
            for (int i = 0; i < n; i++)
                torqueOut[i] = Vector3.Dot(model.JointAxisChild[i], model.NetTorque[i]);

            return n;
        }
    }
}
