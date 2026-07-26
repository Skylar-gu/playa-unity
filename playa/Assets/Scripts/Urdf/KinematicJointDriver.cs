namespace Playa.Urdf
{
    // Default driver: set the joint's scalar value directly. The UrdfJoint
    // handle takes care of translating that into Transform local pose.
    public sealed class KinematicJointDriver : IJointCommand
    {
        public void Apply(UrdfJoint joint, float targetValue, float nowSeconds)
        {
            joint.SetValue(targetValue, nowSeconds);
        }
    }
}
