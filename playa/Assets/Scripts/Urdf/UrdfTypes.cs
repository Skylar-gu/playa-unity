using System.Collections.Generic;
using UnityEngine;

namespace Playa.Urdf
{
    // Data model for a parsed URDF. Kept intentionally close to the XML —
    // downstream code (instantiator, choreographer, validators) reads these
    // structs directly. Positions and rotations are in ROS/URDF convention
    // (X-forward, Y-left, Z-up, right-handed). Axis conversion to Unity is
    // done by UrdfMath at instantiation time.

    public enum UrdfJointType
    {
        Revolute,    // ranged rotational
        Continuous,  // unbounded rotational
        Prismatic,   // ranged linear
        Fixed,       // no motion
        Floating,    // 6-DoF (unsupported at driver level; treated as Fixed)
        Planar,      // 2-DoF planar (unsupported; treated as Fixed)
    }

    public enum UrdfGeometryKind { Mesh, Box, Cylinder, Sphere }

    public sealed class UrdfRobot
    {
        public string Name;
        public string SourcePath;
        public string BaseDirectory;
        public readonly List<UrdfLink> Links = new List<UrdfLink>();
        public readonly List<UrdfJointSpec> Joints = new List<UrdfJointSpec>();
        public readonly Dictionary<string, UrdfMaterial> Materials =
            new Dictionary<string, UrdfMaterial>(System.StringComparer.Ordinal);
    }

    public sealed class UrdfLink
    {
        public string Name;
        public UrdfInertial Inertial;                       // may be null
        public readonly List<UrdfVisual> Visuals = new List<UrdfVisual>();
    }

    public sealed class UrdfInertial
    {
        public UrdfOrigin Origin;                           // CoM in link frame
        public float Mass;
        public float Ixx, Iyy, Izz, Ixy, Ixz, Iyz;
    }

    public sealed class UrdfVisual
    {
        public UrdfOrigin Origin;
        public UrdfGeometry Geometry;
        public UrdfMaterial Material;                       // may be null
    }

    public sealed class UrdfGeometry
    {
        public UrdfGeometryKind Kind;
        public string MeshFilename;                         // Kind == Mesh
        public Vector3 MeshScale = Vector3.one;
        public Vector3 BoxSize;                             // Kind == Box
        public float CylinderRadius;                        // Kind == Cylinder
        public float CylinderLength;
        public float SphereRadius;                          // Kind == Sphere
    }

    public sealed class UrdfMaterial
    {
        public string Name;
        public Color Color = new Color(0.65f, 0.65f, 0.68f, 1f);
        public string TextureFilename;
    }

    public struct UrdfOrigin
    {
        public Vector3 Xyz;                                 // meters, URDF frame
        public Vector3 Rpy;                                 // radians, extrinsic XYZ
        public static UrdfOrigin Identity =>
            new UrdfOrigin { Xyz = Vector3.zero, Rpy = Vector3.zero };
    }

    public sealed class UrdfJointSpec
    {
        public string Name;
        public UrdfJointType Type;
        public string ParentLink;
        public string ChildLink;
        public UrdfOrigin Origin;                           // in parent link frame
        public Vector3 Axis = new Vector3(1, 0, 0);         // default per URDF spec
        public UrdfLimit Limit;                             // may be null (continuous joints often omit)
        public float Damping;
        public float Friction;
    }

    public sealed class UrdfLimit
    {
        public float Lower;         // rad (revolute/continuous) or m (prismatic)
        public float Upper;
        public float Effort;        // N·m or N
        public float Velocity;      // rad/s or m/s
        public bool HasPositionLimits => Upper > Lower;
    }
}
