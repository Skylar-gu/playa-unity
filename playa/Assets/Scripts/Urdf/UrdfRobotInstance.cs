using System.Collections.Generic;
using UnityEngine;

namespace Playa.Urdf
{
    // Runtime handle to an instantiated URDF robot. Holds the source spec,
    // the built GameObject hierarchy, and lookup tables from name → joint/link.
    public sealed class UrdfRobotInstance
    {
        public readonly UrdfRobot Spec;                     // source data
        public readonly GameObject Root;                    // wrapper GameObject (world-oriented)
        public readonly Transform UrdfSpaceRoot;            // -90°X child; URDF frame lives inside
        public readonly UrdfLink RootLink;
        public readonly List<UrdfJoint> Joints;
        public readonly List<UrdfJoint> ActuatedJoints;
        public readonly Dictionary<string, UrdfJoint> JointByName;
        public readonly Dictionary<string, Transform> LinkTransformByName;
        public readonly Dictionary<string, UrdfLink> LinkSpecByName;

        public UrdfRobotInstance(
            UrdfRobot spec,
            GameObject root,
            Transform urdfSpaceRoot,
            UrdfLink rootLink,
            List<UrdfJoint> joints,
            Dictionary<string, Transform> linkTransforms,
            Dictionary<string, UrdfLink> linkSpecs)
        {
            Spec = spec;
            Root = root;
            UrdfSpaceRoot = urdfSpaceRoot;
            RootLink = rootLink;
            Joints = joints;
            JointByName = new Dictionary<string, UrdfJoint>(joints.Count);
            ActuatedJoints = new List<UrdfJoint>(joints.Count);
            foreach (var j in joints)
            {
                JointByName[j.Name] = j;
                if (j.IsActuated) ActuatedJoints.Add(j);
            }
            LinkTransformByName = linkTransforms;
            LinkSpecByName = linkSpecs;
        }
    }
}
