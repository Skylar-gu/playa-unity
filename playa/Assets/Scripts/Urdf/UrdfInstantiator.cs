using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Playa.Urdf
{
    // Builds a Unity GameObject hierarchy from a parsed UrdfRobot.
    //
    // Hierarchy shape:
    //   Root (world-positioned, owned by caller)
    //   └── UrdfSpaceRoot (localRotation reorients ROS-Z-up → Unity-Y-up)
    //       └── <root link GO>
    //           ├── visual GOs (each with MeshFilter/Renderer)
    //           └── <child link GO>  (transform IS the incoming joint frame)
    //               └── ...
    //
    // A link's Transform is the incoming joint's frame — that's what we rotate
    // when the joint's value changes. Visual <origin>s hang off links as leaf
    // children so their local pose is independent of joint motion.
    public static class UrdfInstantiator
    {
        public class Options
        {
            public Material fallbackMaterial;   // used when a link's <material> has no color match
            public bool logMissingMeshes = true;
        }

        public static UrdfRobotInstance Instantiate(
            UrdfRobot spec,
            Transform parent,
            Options options = null)
        {
            options ??= new Options();

            var root = new GameObject($"Urdf:{spec.Name}");
            root.transform.SetParent(parent, false);

            var urdfSpaceRoot = new GameObject("UrdfSpaceRoot").transform;
            urdfSpaceRoot.SetParent(root.transform, false);
            urdfSpaceRoot.localRotation = UrdfMath.RootWrapperRotation;

            var linkSpecs = IndexBy(spec.Links, l => l.Name);
            var jointsByChild = IndexBy(spec.Joints, j => j.ChildLink);

            var rootLink = FindRootLink(spec);
            if (rootLink == null)
                throw new System.InvalidOperationException(
                    $"URDF '{spec.Name}' has no root link (every link is a joint child).");

            var linkTransforms = new Dictionary<string, Transform>();
            var joints = new List<UrdfJoint>();

            // Recursively build the tree.
            BuildLink(rootLink, urdfSpaceRoot, incomingJoint: null,
                      spec, linkSpecs, linkTransforms, joints, options);

            return new UrdfRobotInstance(
                spec, root, urdfSpaceRoot, rootLink,
                joints, linkTransforms, linkSpecs);
        }

        // ---- recursion ------------------------------------------------------

        static void BuildLink(
            UrdfLink link,
            Transform parentTransform,
            UrdfJointSpec incomingJoint,
            UrdfRobot robot,
            Dictionary<string, UrdfLink> linkSpecs,
            Dictionary<string, Transform> linkTransforms,
            List<UrdfJoint> joints,
            Options options)
        {
            var go = new GameObject($"link:{link.Name}");
            go.transform.SetParent(parentTransform, false);

            // Rest pose of the link's transform = incoming joint's origin.
            Vector3 restPos = Vector3.zero;
            Quaternion restRot = Quaternion.identity;
            if (incomingJoint != null)
            {
                restPos = UrdfMath.UrdfToUnityPos(incomingJoint.Origin.Xyz);
                restRot = UrdfMath.UrdfRpyToUnity(incomingJoint.Origin.Rpy);
                go.transform.localPosition = restPos;
                go.transform.localRotation = restRot;
            }

            linkTransforms[link.Name] = go.transform;

            // Register the joint handle so the driver can animate it.
            if (incomingJoint != null)
            {
                var axisUnity = UrdfMath.UrdfToUnityDir(incomingJoint.Axis);
                joints.Add(new UrdfJoint(
                    spec: incomingJoint,
                    jointTransform: go.transform,
                    axisUnity: axisUnity,
                    restLocalPosition: restPos,
                    restLocalRotation: restRot,
                    childLink: link));
            }

            // Attach visuals.
            foreach (var vis in link.Visuals)
                AttachVisual(vis, go.transform, robot, options);

            // Recurse into children (joints where this link is the parent).
            for (int i = 0; i < robot.Joints.Count; i++)
            {
                var j = robot.Joints[i];
                if (j.ParentLink != link.Name) continue;
                if (!linkSpecs.TryGetValue(j.ChildLink, out var childLink))
                {
                    Debug.LogWarning(
                        $"URDF joint '{j.Name}' references unknown child link '{j.ChildLink}'.");
                    continue;
                }
                BuildLink(childLink, go.transform, j, robot, linkSpecs,
                          linkTransforms, joints, options);
            }
        }

        // ---- visuals --------------------------------------------------------

        static void AttachVisual(
            UrdfVisual vis,
            Transform parent,
            UrdfRobot robot,
            Options options)
        {
            if (vis.Geometry == null) return;

            var visGo = new GameObject("visual");
            visGo.transform.SetParent(parent, false);
            visGo.transform.localPosition = UrdfMath.UrdfToUnityPos(vis.Origin.Xyz);
            visGo.transform.localRotation = UrdfMath.UrdfRpyToUnity(vis.Origin.Rpy);

            var mesh = BuildMesh(vis.Geometry, robot, options);
            if (mesh == null) return;

            var mf = visGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = visGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ResolveMaterial(vis.Material, options);

            // Apply mesh scale (URDF <mesh scale=/>).
            if (vis.Geometry.Kind == UrdfGeometryKind.Mesh &&
                vis.Geometry.MeshScale != Vector3.one)
            {
                visGo.transform.localScale = Vector3.Scale(
                    visGo.transform.localScale, vis.Geometry.MeshScale);
            }
        }

        static Mesh BuildMesh(UrdfGeometry g, UrdfRobot robot, Options options)
        {
            switch (g.Kind)
            {
                case UrdfGeometryKind.Mesh:
                    return LoadMeshFile(g.MeshFilename, robot, options);

                case UrdfGeometryKind.Box:
                {
                    var m = MakePrimitive(PrimitiveType.Cube);
                    // A Unity cube is 1×1×1; scale the local transform outside.
                    m.name = "box";
                    return m;
                }
                case UrdfGeometryKind.Cylinder:
                {
                    var m = MakePrimitive(PrimitiveType.Cylinder);
                    m.name = "cylinder";
                    return m;
                }
                case UrdfGeometryKind.Sphere:
                {
                    var m = MakePrimitive(PrimitiveType.Sphere);
                    m.name = "sphere";
                    return m;
                }
            }
            return null;
        }

        static Mesh LoadMeshFile(string filename, UrdfRobot robot, Options options)
        {
            var path = UrdfMeshResolver.Resolve(filename, robot.BaseDirectory);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (options.logMissingMeshes)
                    Debug.LogWarning($"URDF mesh not found: '{filename}' → '{path}'");
                return null;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".stl":
                    return StlLoader.Load(path);
                case ".obj":
                case ".dae":
                    // Not supported at runtime in this build. Users can
                    // convert to STL — Blender does it in one export.
                    if (options.logMissingMeshes)
                        Debug.LogWarning(
                            $"URDF mesh '{filename}' is {ext} — only STL supported at runtime. Convert to STL.");
                    return null;
                default:
                    if (options.logMissingMeshes)
                        Debug.LogWarning($"URDF mesh has unknown extension: {path}");
                    return null;
            }
        }

        static Mesh MakePrimitive(PrimitiveType t)
        {
            // Extract the mesh from a temporary primitive; destroy the GO.
            var tmp = GameObject.CreatePrimitive(t);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            // Destroy immediately without touching physics collider setup.
            Object.Destroy(tmp);
            return mesh;
        }

        static Material ResolveMaterial(UrdfMaterial m, Options options)
        {
            if (options.fallbackMaterial == null)
                options.fallbackMaterial = MakeDefaultUrpMaterial();
            if (m == null || m.Color.a <= 0f)
                return options.fallbackMaterial;

            // Clone fallback so we can tint per-visual without leaking.
            var mat = new Material(options.fallbackMaterial);
            mat.name = string.IsNullOrEmpty(m.Name) ? "urdf_mat" : $"urdf_{m.Name}";
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", m.Color);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", m.Color);
            return mat;
        }

        static Material MakeDefaultUrpMaterial()
        {
            var s = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");
            var mat = new Material(s) { name = "urdf_default" };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.65f, 0.65f, 0.68f));
            return mat;
        }

        // ---- helpers --------------------------------------------------------

        static UrdfLink FindRootLink(UrdfRobot robot)
        {
            var childNames = new HashSet<string>();
            foreach (var j in robot.Joints)
                childNames.Add(j.ChildLink);
            foreach (var l in robot.Links)
                if (!childNames.Contains(l.Name)) return l;
            return null;
        }

        static Dictionary<string, T> IndexBy<T>(
            IReadOnlyList<T> items, System.Func<T, string> keySelector)
        {
            var d = new Dictionary<string, T>(items.Count);
            foreach (var it in items)
            {
                var k = keySelector(it);
                if (!string.IsNullOrEmpty(k)) d[k] = it;
            }
            return d;
        }
    }
}
