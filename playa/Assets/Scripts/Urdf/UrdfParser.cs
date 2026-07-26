using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;

namespace Playa.Urdf
{
    // XML parser for a practical subset of URDF. Handles:
    //   <robot>, <link>, <joint>, <origin>, <axis>, <limit>, <dynamics>,
    //   <visual>, <geometry>{<mesh>|<box>|<cylinder>|<sphere>},
    //   <material><color/><texture/>,
    //   <inertial><origin/><mass/><inertia/>
    // Explicitly ignores <collision>, <gazebo>, <transmission>, xacro tags
    // (xacro must be expanded to plain URDF before parsing — CLI: xacro).
    public static class UrdfParser
    {
        public static UrdfRobot Parse(string urdfPath)
        {
            if (!File.Exists(urdfPath))
                throw new FileNotFoundException("URDF not found", urdfPath);
            var text = File.ReadAllText(urdfPath);
            var robot = ParseText(text);
            robot.SourcePath = urdfPath;
            robot.BaseDirectory = Path.GetDirectoryName(urdfPath);
            return robot;
        }

        public static UrdfRobot ParseText(string urdfText)
        {
            var doc = new XmlDocument();
            doc.LoadXml(urdfText);
            var root = doc.DocumentElement;
            if (root == null || root.Name != "robot")
                throw new FormatException($"Expected <robot> root, got '{root?.Name}'");
            var robot = new UrdfRobot { Name = root.GetAttribute("name") };

            // Materials first — links can reference them by name later.
            foreach (var m in root.ChildrenNamed("material"))
            {
                var mat = ParseMaterial(m);
                if (mat != null && !string.IsNullOrEmpty(mat.Name))
                    robot.Materials[mat.Name] = mat;
            }
            foreach (var l in root.ChildrenNamed("link"))
                robot.Links.Add(ParseLink(l, robot.Materials));
            foreach (var j in root.ChildrenNamed("joint"))
                robot.Joints.Add(ParseJoint(j));

            return robot;
        }

        // ---- element parsers ------------------------------------------------

        static UrdfLink ParseLink(XmlElement e, Dictionary<string, UrdfMaterial> matTable)
        {
            var link = new UrdfLink { Name = e.GetAttribute("name") };
            foreach (var child in e.ChildElements())
            {
                switch (child.Name)
                {
                    case "visual":   link.Visuals.Add(ParseVisual(child, matTable)); break;
                    case "inertial": link.Inertial = ParseInertial(child); break;
                    // <collision> intentionally skipped.
                }
            }
            return link;
        }

        static UrdfVisual ParseVisual(XmlElement e, Dictionary<string, UrdfMaterial> matTable)
        {
            var v = new UrdfVisual { Origin = UrdfOrigin.Identity };
            foreach (var child in e.ChildElements())
            {
                switch (child.Name)
                {
                    case "origin":   v.Origin = ParseOrigin(child); break;
                    case "geometry": v.Geometry = ParseGeometry(child); break;
                    case "material":
                        var mn = child.GetAttribute("name");
                        if (!string.IsNullOrEmpty(mn) && matTable.TryGetValue(mn, out var pre))
                            v.Material = pre;
                        else
                            v.Material = ParseMaterial(child);
                        break;
                }
            }
            return v;
        }

        static UrdfGeometry ParseGeometry(XmlElement e)
        {
            var g = new UrdfGeometry();
            foreach (var child in e.ChildElements())
            {
                switch (child.Name)
                {
                    case "mesh":
                        g.Kind = UrdfGeometryKind.Mesh;
                        g.MeshFilename = child.GetAttribute("filename");
                        var s = child.GetAttribute("scale");
                        if (!string.IsNullOrWhiteSpace(s)) g.MeshScale = ParseVec3(s);
                        break;
                    case "box":
                        g.Kind = UrdfGeometryKind.Box;
                        g.BoxSize = ParseVec3(child.GetAttribute("size"));
                        break;
                    case "cylinder":
                        g.Kind = UrdfGeometryKind.Cylinder;
                        g.CylinderRadius = ParseFloat(child.GetAttribute("radius"));
                        g.CylinderLength = ParseFloat(child.GetAttribute("length"));
                        break;
                    case "sphere":
                        g.Kind = UrdfGeometryKind.Sphere;
                        g.SphereRadius = ParseFloat(child.GetAttribute("radius"));
                        break;
                }
            }
            return g;
        }

        static UrdfMaterial ParseMaterial(XmlElement e)
        {
            var m = new UrdfMaterial { Name = e.GetAttribute("name") };
            foreach (var child in e.ChildElements())
            {
                switch (child.Name)
                {
                    case "color":
                        var rgba = ParseVec4(child.GetAttribute("rgba"));
                        m.Color = new Color(rgba.x, rgba.y, rgba.z, rgba.w);
                        break;
                    case "texture":
                        m.TextureFilename = child.GetAttribute("filename");
                        break;
                }
            }
            return m;
        }

        static UrdfInertial ParseInertial(XmlElement e)
        {
            var i = new UrdfInertial { Origin = UrdfOrigin.Identity };
            foreach (var child in e.ChildElements())
            {
                switch (child.Name)
                {
                    case "origin": i.Origin = ParseOrigin(child); break;
                    case "mass":   i.Mass = ParseFloat(child.GetAttribute("value")); break;
                    case "inertia":
                        i.Ixx = ParseFloat(child.GetAttribute("ixx"));
                        i.Iyy = ParseFloat(child.GetAttribute("iyy"));
                        i.Izz = ParseFloat(child.GetAttribute("izz"));
                        i.Ixy = ParseFloat(child.GetAttribute("ixy"));
                        i.Ixz = ParseFloat(child.GetAttribute("ixz"));
                        i.Iyz = ParseFloat(child.GetAttribute("iyz"));
                        break;
                }
            }
            return i;
        }

        static UrdfJointSpec ParseJoint(XmlElement e)
        {
            var j = new UrdfJointSpec
            {
                Name = e.GetAttribute("name"),
                Type = ParseJointType(e.GetAttribute("type")),
                Origin = UrdfOrigin.Identity,
            };
            foreach (var child in e.ChildElements())
            {
                switch (child.Name)
                {
                    case "parent": j.ParentLink = child.GetAttribute("link"); break;
                    case "child":  j.ChildLink  = child.GetAttribute("link"); break;
                    case "origin": j.Origin = ParseOrigin(child); break;
                    case "axis":
                        var xyz = child.GetAttribute("xyz");
                        if (!string.IsNullOrWhiteSpace(xyz))
                        {
                            var raw = ParseVec3(xyz);
                            j.Axis = raw.sqrMagnitude > 1e-8f ? raw.normalized : new Vector3(1, 0, 0);
                        }
                        break;
                    case "limit":
                        j.Limit = new UrdfLimit
                        {
                            Lower    = ParseFloat(child.GetAttribute("lower")),
                            Upper    = ParseFloat(child.GetAttribute("upper")),
                            Effort   = ParseFloat(child.GetAttribute("effort")),
                            Velocity = ParseFloat(child.GetAttribute("velocity")),
                        };
                        break;
                    case "dynamics":
                        j.Damping  = ParseFloat(child.GetAttribute("damping"));
                        j.Friction = ParseFloat(child.GetAttribute("friction"));
                        break;
                }
            }
            return j;
        }

        static UrdfOrigin ParseOrigin(XmlElement e)
        {
            var o = UrdfOrigin.Identity;
            var xyz = e.GetAttribute("xyz");
            var rpy = e.GetAttribute("rpy");
            if (!string.IsNullOrWhiteSpace(xyz)) o.Xyz = ParseVec3(xyz);
            if (!string.IsNullOrWhiteSpace(rpy)) o.Rpy = ParseVec3(rpy);
            return o;
        }

        static UrdfJointType ParseJointType(string s)
        {
            switch (s)
            {
                case "revolute":   return UrdfJointType.Revolute;
                case "continuous": return UrdfJointType.Continuous;
                case "prismatic":  return UrdfJointType.Prismatic;
                case "floating":   return UrdfJointType.Floating;
                case "planar":     return UrdfJointType.Planar;
                default:           return UrdfJointType.Fixed;
            }
        }

        // ---- primitives -----------------------------------------------------

        static float ParseFloat(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0f;
            return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static Vector3 ParseVec3(string s)
        {
            var parts = s.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return Vector3.zero;
            return new Vector3(ParseFloat(parts[0]), ParseFloat(parts[1]), ParseFloat(parts[2]));
        }

        static Vector4 ParseVec4(string s)
        {
            var parts = s.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return Vector4.zero;
            return new Vector4(
                ParseFloat(parts[0]), ParseFloat(parts[1]),
                ParseFloat(parts[2]), ParseFloat(parts[3]));
        }
    }

    internal static class UrdfXmlExtensions
    {
        public static IEnumerable<XmlElement> ChildElements(this XmlElement e)
        {
            foreach (XmlNode n in e.ChildNodes)
                if (n is XmlElement el) yield return el;
        }

        public static IEnumerable<XmlElement> ChildrenNamed(this XmlElement e, string name)
        {
            foreach (XmlNode n in e.ChildNodes)
                if (n is XmlElement el && el.Name == name) yield return el;
        }
    }
}
