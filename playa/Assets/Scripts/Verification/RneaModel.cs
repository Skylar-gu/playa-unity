using System.Collections.Generic;
using UnityEngine;
using Playa.Urdf;

namespace Playa.Verification
{
    // Precomputed per-robot data for RNEA. Built once at load; the solver
    // reads from this every frame without allocating.
    //
    // Frames and conventions (Craig-style RNEA):
    //   - Each joint (index i, 0-based) has a body-fixed frame at the joint
    //     origin. This frame IS the child link's frame — the child link is
    //     rigidly attached to it (its inertials expressed relative to it).
    //   - The base link has no incoming joint. In this model we treat the
    //     robot's root link as index -1 (implicit) with ω=α=0 and gravity
    //     applied to its acceleration.
    //   - For each joint i:
    //       ParentIndex[i] = index of the joint whose child link is joint i's
    //                        parent link. -1 if joint i is attached to base.
    //       JointOriginTranslationParentFrame[i] = position of joint i's
    //                        origin in the PARENT frame (== child link
    //                        Transform.localPosition, at rest).
    //       RestRotationParentToChild[i] = rotation that takes a vector
    //                        expressed in parent frame to child frame
    //                        (== inverse of child link's local rotation at
    //                        rest, before joint motion). The current
    //                        joint value adds a rotation about JointAxisChild.
    //       JointAxisChild[i] = joint axis in child frame (unit).
    //       LinkMass[i], LinkComChild[i], InertiaAboutCom[i] = child link
    //                        mass/CoM/inertia. Inertia is symmetric 3x3
    //                        stored as 6 floats (Ixx Iyy Izz Ixy Ixz Iyz).
    //                        If the URDF omitted <inertial> or set mass=0,
    //                        the link is treated as massless (F=N=0).
    //
    // Only revolute + continuous joints are supported. Prismatic joints
    // exist in the URDF but are treated as fixed for the torque pass —
    // Playa's target robots (G1, Spot) have no actuated prismatic joints,
    // and adding prismatic to RNEA is 20 more lines when needed.
    public sealed class RneaModel
    {
        public int Count;
        public UrdfJoint[] Joints;
        public int[] ParentIndex;

        public Vector3[] JointOriginTranslationParentFrame;
        public Quaternion[] RestRotationParentToChild;
        public Vector3[] JointAxisChild;

        public float[] LinkMass;
        public Vector3[] LinkComChild;
        public Sym3x3[] InertiaAboutCom;

        // Frame-to-frame state (per joint) written by the solver each frame.
        public Vector3[] Omega;         // angular vel in child frame
        public Vector3[] Alpha;         // angular accel in child frame
        public Vector3[] LinAccelOrigin; // linear accel at child origin, child frame

        // Backward-pass scratch: net force + net torque at child origin,
        // in child frame. Read as τ_i = dot(JointAxisChild[i], NetTorque[i]).
        public Vector3[] NetForce;
        public Vector3[] NetTorque;

        // Finite-difference storage for joint acceleration.
        public float[] PrevJointVelocity;
        public float[] PrevAccelTime;

        // ---- construction --------------------------------------------------

        public static RneaModel Build(UrdfRobotInstance robot)
        {
            var joints = robot.ActuatedJoints;
            int n = joints.Count;
            var m = new RneaModel
            {
                Count = n,
                Joints = new UrdfJoint[n],
                ParentIndex = new int[n],
                JointOriginTranslationParentFrame = new Vector3[n],
                RestRotationParentToChild = new Quaternion[n],
                JointAxisChild = new Vector3[n],
                LinkMass = new float[n],
                LinkComChild = new Vector3[n],
                InertiaAboutCom = new Sym3x3[n],
                Omega = new Vector3[n],
                Alpha = new Vector3[n],
                LinAccelOrigin = new Vector3[n],
                NetForce = new Vector3[n],
                NetTorque = new Vector3[n],
                PrevJointVelocity = new float[n],
                PrevAccelTime = new float[n],
            };

            // Name → index for parent lookup.
            var indexByChildLink = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
                indexByChildLink[joints[i].ChildLinkName] = i;

            for (int i = 0; i < n; i++)
            {
                var j = joints[i];
                m.Joints[i] = j;

                // Parent joint: whichever actuated joint has ChildLink == j.ParentLink.
                // If not found, j is attached to the base link (or to a fixed
                // sub-chain we're ignoring for RNEA) → parent index -1.
                m.ParentIndex[i] = indexByChildLink.TryGetValue(j.ParentLinkName, out var p) ? p : -1;

                m.JointOriginTranslationParentFrame[i] = j.RestLocalPosition;
                // Rotation from parent frame to child frame: inverse of child's
                // rest local rotation (Unity localRotation takes child→parent).
                m.RestRotationParentToChild[i] = Quaternion.Inverse(j.RestLocalRotation);
                m.JointAxisChild[i] = j.AxisUnity.normalized;

                // Link inertials.
                var link = j.ChildLinkSpec;
                if (link != null && link.Inertial != null && link.Inertial.Mass > 0f)
                {
                    var inert = link.Inertial;
                    m.LinkMass[i] = inert.Mass;
                    m.LinkComChild[i] = UrdfMath.UrdfToUnityPos(inert.Origin.Xyz);
                    // URDF inertia is expressed in the inertial frame (about CoM,
                    // aligned with link frame if inertial origin rpy is zero).
                    // Handle rpy!=0 with a rotation of the tensor — see below.
                    var rawI = new Sym3x3
                    {
                        Xx = inert.Ixx, Yy = inert.Iyy, Zz = inert.Izz,
                        Xy = inert.Ixy, Xz = inert.Ixz, Yz = inert.Iyz,
                    };
                    Vector3 rpy = inert.Origin.Rpy;
                    if (rpy.sqrMagnitude > 1e-12f)
                    {
                        Quaternion q = UrdfMath.UrdfRpyToUnity(rpy);
                        rawI = Sym3x3.Rotate(rawI, q);
                    }
                    m.InertiaAboutCom[i] = rawI;
                }
                else
                {
                    m.LinkMass[i] = 0f;
                    m.LinkComChild[i] = Vector3.zero;
                    m.InertiaAboutCom[i] = Sym3x3.Zero;
                }
            }
            return m;
        }
    }

    // Symmetric 3x3 (physically an inertia tensor). Six floats + the ops we need.
    public struct Sym3x3
    {
        public float Xx, Yy, Zz;
        public float Xy, Xz, Yz;

        public static Sym3x3 Zero => new Sym3x3();

        // I · v (symmetric matrix times vector)
        public Vector3 Mul(Vector3 v)
        {
            return new Vector3(
                Xx * v.x + Xy * v.y + Xz * v.z,
                Xy * v.x + Yy * v.y + Yz * v.z,
                Xz * v.x + Yz * v.y + Zz * v.z);
        }

        // R · I · R^T — rotate tensor by orientation q. Preserves symmetry.
        // Implemented as 3 basis multiplications; not the fastest but readable
        // and only called at load time (once per link).
        public static Sym3x3 Rotate(Sym3x3 I, Quaternion q)
        {
            Vector3 rx = q * new Vector3(1, 0, 0);
            Vector3 ry = q * new Vector3(0, 1, 0);
            Vector3 rz = q * new Vector3(0, 0, 1);
            // Columns of R I R^T are R · (I · R^T_col_j)
            // R^T column j = R row j = (basis vectors rotated by q^-1)... simpler:
            // Compute Q = I · R^T then result = R · Q. Column-major.
            var qInv = Quaternion.Inverse(q);
            Vector3 c0 = qInv * new Vector3(1, 0, 0);   // column 0 of R^T (in original basis)
            Vector3 c1 = qInv * new Vector3(0, 1, 0);
            Vector3 c2 = qInv * new Vector3(0, 0, 1);
            Vector3 Ic0 = I.Mul(c0);
            Vector3 Ic1 = I.Mul(c1);
            Vector3 Ic2 = I.Mul(c2);
            // Rotate each column back by q to get R · I · R^T columns.
            Vector3 col0 = q * Ic0;
            Vector3 col1 = q * Ic1;
            Vector3 col2 = q * Ic2;
            // Symmetric result — take upper triangle.
            return new Sym3x3
            {
                Xx = col0.x,
                Yy = col1.y,
                Zz = col2.z,
                Xy = 0.5f * (col0.y + col1.x),
                Xz = 0.5f * (col0.z + col2.x),
                Yz = 0.5f * (col1.z + col2.y),
            };
        }
    }
}
