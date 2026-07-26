namespace Playa.Urdf
{
    // Abstraction over how joint targets are realized on the visual body.
    //
    // KinematicJointDriver → sets Transform local pose directly. Deterministic
    //   choreography, no physics interference. Default for Playa's dance mode.
    //
    // ArticulationJointDriver (future) → sets PD-drive targets on Unity
    //   ArticulationBody chains. The physics solver then integrates gravity,
    //   contact, joint constraints. Needed if we later want to test sim-to-real
    //   feasibility under actual dynamics.
    public interface IJointCommand
    {
        void Apply(UrdfJoint joint, float targetValue, float nowSeconds);
    }
}
