using System.IO;

namespace Playa.Urdf
{
    // URDFs reference meshes in a few different flavors:
    //   package://<pkg>/meshes/foo.stl
    //   file:///abs/path.stl
    //   meshes/foo.stl                   (relative to URDF dir)
    //   /abs/path.stl
    // Some pipelines rewrite package:// to file:// before shipping; this
    // resolver handles all four uniformly and returns a filesystem path.
    public static class UrdfMeshResolver
    {
        public static string Resolve(string urdfFilename, string urdfBaseDir)
        {
            if (string.IsNullOrEmpty(urdfFilename)) return null;
            string s = urdfFilename.Trim();

            if (s.StartsWith("file://"))
                s = s.Substring("file://".Length);

            if (s.StartsWith("package://"))
            {
                // Strip "package://<pkg>/" — treat everything after the package
                // name as relative to the URDF's directory. Works if the URDF
                // ships alongside a "meshes/" folder, which is the norm.
                var rest = s.Substring("package://".Length);
                int slash = rest.IndexOf('/');
                s = slash >= 0 ? rest.Substring(slash + 1) : rest;
            }

            if (Path.IsPathRooted(s)) return s;
            return Path.Combine(urdfBaseDir ?? "", s);
        }
    }
}
