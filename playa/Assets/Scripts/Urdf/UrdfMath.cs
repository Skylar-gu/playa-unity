using UnityEngine;

namespace Playa.Urdf
{
    // URDF ↔ Unity frame conversion.
    //
    // URDF (ROS REP-103): X forward, Y left, Z up, right-handed, angles in radians,
    //   RPY is extrinsic XYZ (roll about world X, then pitch about world Y, then
    //   yaw about world Z — matrix form R = Rz(γ)·Ry(β)·Rx(α)).
    // Unity: X right, Y up, Z forward, LEFT-handed, angles in degrees.
    //
    // Strategy taken here (simple, composes cleanly at every joint):
    //   The whole robot lives inside a wrapper transform that we rotate -90°
    //   about world X, so that URDF's Z-up becomes Unity's Y-up. Inside the
    //   wrapper we treat URDF's (x, y, z) as Unity's (x, y, z) directly.
    //   That makes the wrapper's local frame an approximation of URDF frame.
    //
    // Handedness caveat: URDF is right-handed, Unity is left-handed. This
    // approximation gets orientation right for the vast majority of joints,
    // but joints on the mirrored axis may rotate in the reverse direction.
    // If a specific joint bends the wrong way on your robot, negate the axis
    // in the URDF or in UrdfJoint.axisUnity. Full RH↔LH conversion is doable
    // but adds complexity that isn't worth it for a dance demo.
    public static class UrdfMath
    {
        // Root wrapper rotation that reorients ROS Z-up → Unity Y-up.
        public static readonly Quaternion RootWrapperRotation =
            Quaternion.Euler(-90f, 0f, 0f);

        // Passthrough: within the wrapper, URDF (x,y,z) IS Unity (x,y,z).
        public static Vector3 UrdfToUnityPos(Vector3 v) => v;
        public static Vector3 UrdfToUnityDir(Vector3 v) => v;

        // Build a Unity quaternion from URDF RPY (radians, extrinsic XYZ).
        //   R = Rz(yaw) · Ry(pitch) · Rx(roll)
        // In Unity quaternion multiplication, right-most applies first, so
        // we compose Yaw * Pitch * Roll to match matrix order.
        public static Quaternion UrdfRpyToUnity(Vector3 rpyRadians)
        {
            float rollDeg  = rpyRadians.x * Mathf.Rad2Deg;
            float pitchDeg = rpyRadians.y * Mathf.Rad2Deg;
            float yawDeg   = rpyRadians.z * Mathf.Rad2Deg;
            return Quaternion.AngleAxis(yawDeg,  Vector3.forward) *  // URDF Z ≈ +Z inside wrapper
                   Quaternion.AngleAxis(pitchDeg, Vector3.up)      *  // URDF Y ≈ +Y
                   Quaternion.AngleAxis(rollDeg, Vector3.right);      // URDF X ≈ +X
        }

        // For applying a joint's motion. Angle is in radians (URDF convention).
        public static Quaternion RotationAboutAxis(Vector3 axisUnity, float angleRadians)
        {
            return Quaternion.AngleAxis(angleRadians * Mathf.Rad2Deg, axisUnity);
        }
    }
}
