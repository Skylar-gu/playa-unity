using UnityEngine;

namespace Playa.Urdf
{
    // Runtime handle to a single actuated joint on an instantiated URDF robot.
    //
    // Value is the joint's scalar coordinate in URDF units — radians for
    // revolute/continuous, meters for prismatic. Setting Value updates the
    // Transform's localRotation (or localPosition) immediately.
    //
    // The rest transform (transform's local pose when Value == 0) is captured
    // at instantiation from the URDF <origin> tag, and the joint motion is
    // composed on top of it: T_current = T_rest * T_motion(Value).
    public sealed class UrdfJoint
    {
        public readonly string Name;
        public readonly UrdfJointType Type;
        public readonly UrdfLimit Limit;                    // may be null
        public readonly Transform JointTransform;           // the moving frame
        public readonly Vector3 AxisUnity;                  // in Unity frame, unit length
        public readonly Vector3 RestLocalPosition;
        public readonly Quaternion RestLocalRotation;
        public readonly UrdfLink ChildLinkSpec;             // for downstream inertia lookups
        public readonly string ParentLinkName;
        public readonly string ChildLinkName;

        float value;
        float previousValue;
        float lastSetTime;

        public float Value => value;
        public float VelocityEstimate { get; private set; }

        public UrdfJoint(
            UrdfJointSpec spec,
            Transform jointTransform,
            Vector3 axisUnity,
            Vector3 restLocalPosition,
            Quaternion restLocalRotation,
            UrdfLink childLink)
        {
            Name = spec.Name;
            Type = spec.Type;
            Limit = spec.Limit;
            ParentLinkName = spec.ParentLink;
            ChildLinkName = spec.ChildLink;
            ChildLinkSpec = childLink;
            JointTransform = jointTransform;
            AxisUnity = axisUnity.sqrMagnitude > 1e-8f ? axisUnity.normalized : Vector3.right;
            RestLocalPosition = restLocalPosition;
            RestLocalRotation = restLocalRotation;
            lastSetTime = Time.time;
        }

        public void SetValue(float q, float nowSeconds)
        {
            float dt = Mathf.Max(1e-4f, nowSeconds - lastSetTime);
            previousValue = value;
            value = q;
            VelocityEstimate = (value - previousValue) / dt;
            lastSetTime = nowSeconds;
            Apply();
        }

        void Apply()
        {
            switch (Type)
            {
                case UrdfJointType.Revolute:
                case UrdfJointType.Continuous:
                    JointTransform.localRotation =
                        RestLocalRotation * UrdfMath.RotationAboutAxis(AxisUnity, value);
                    JointTransform.localPosition = RestLocalPosition;
                    break;

                case UrdfJointType.Prismatic:
                    JointTransform.localRotation = RestLocalRotation;
                    JointTransform.localPosition =
                        RestLocalPosition + RestLocalRotation * (AxisUnity * value);
                    break;

                default:
                    // Fixed / Floating / Planar → not driven.
                    JointTransform.localRotation = RestLocalRotation;
                    JointTransform.localPosition = RestLocalPosition;
                    break;
            }
        }

        public bool IsActuated =>
            Type == UrdfJointType.Revolute ||
            Type == UrdfJointType.Continuous ||
            Type == UrdfJointType.Prismatic;
    }
}
