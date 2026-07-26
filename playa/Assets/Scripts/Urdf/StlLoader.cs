using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Playa.Urdf
{
    // Runtime STL → UnityEngine.Mesh. Supports binary and ASCII.
    //
    // Vertices are emitted flat (3 per triangle) — smooth shading via
    // dedup is skipped because STL has no shared-vertex concept and
    // hashing Vector3s with tolerance is expensive for the mesh sizes
    // we see on robot links. Faceted look reads as "mechanical" which
    // is on-brief for a robot dancer.
    //
    // Meshes come out in the source file's native coordinates. Axis
    // conversion (URDF → Unity) is the caller's job — applied at the
    // parent GameObject transform, not baked into geometry.
    public static class StlLoader
    {
        public static Mesh Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("STL not found", path);
            var bytes = File.ReadAllBytes(path);
            var mesh = ParseBytes(bytes);
            mesh.name = Path.GetFileNameWithoutExtension(path);
            return mesh;
        }

        public static Mesh ParseBytes(byte[] bytes)
        {
            // Binary STL layout: 80B header, uint32 tri count, then
            //   tris * (12*float + uint16 attribute) = 50 bytes each.
            // ASCII STL starts with "solid " but so do many binary STLs —
            // so we use the exact-size check as the reliable detector.
            if (bytes.Length >= 84)
            {
                uint claimedTriCount = BitConverter.ToUInt32(bytes, 80);
                long expectedSize = 84L + 50L * claimedTriCount;
                if (expectedSize == bytes.Length)
                    return ParseBinary(bytes, (int)claimedTriCount);
            }
            return ParseAscii(Encoding.ASCII.GetString(bytes));
        }

        static Mesh ParseBinary(byte[] b, int triCount)
        {
            var verts = new Vector3[triCount * 3];
            var normals = new Vector3[triCount * 3];
            var tris = new int[triCount * 3];

            int off = 84;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 n = ReadVec3(b, off); off += 12;
                Vector3 v0 = ReadVec3(b, off); off += 12;
                Vector3 v1 = ReadVec3(b, off); off += 12;
                Vector3 v2 = ReadVec3(b, off); off += 12;
                off += 2; // attribute byte count

                int i0 = t * 3, i1 = i0 + 1, i2 = i0 + 2;
                verts[i0] = v0; verts[i1] = v1; verts[i2] = v2;
                normals[i0] = n; normals[i1] = n; normals[i2] = n;
                tris[i0] = i0; tris[i1] = i1; tris[i2] = i2;
            }

            return BuildMesh(verts, normals, tris);
        }

        static Mesh ParseAscii(string text)
        {
            var verts = new List<Vector3>(1024);
            var normals = new List<Vector3>(1024);
            var tris = new List<int>(1024);

            Vector3 currentNormal = Vector3.up;
            int vertsInFacet = 0;
            Vector3 v0 = default, v1 = default, v2 = default;

            using (var sr = new StringReader(text))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;

                    // Tokenize aggressively — STL exporters vary on whitespace.
                    var toks = trimmed.Split(default(char[]),
                        StringSplitOptions.RemoveEmptyEntries);
                    if (toks.Length == 0) continue;

                    if (toks[0] == "facet" && toks.Length >= 5 && toks[1] == "normal")
                    {
                        currentNormal = new Vector3(
                            Parse(toks[2]), Parse(toks[3]), Parse(toks[4]));
                        vertsInFacet = 0;
                    }
                    else if (toks[0] == "vertex" && toks.Length >= 4)
                    {
                        var p = new Vector3(
                            Parse(toks[1]), Parse(toks[2]), Parse(toks[3]));
                        switch (vertsInFacet)
                        {
                            case 0: v0 = p; break;
                            case 1: v1 = p; break;
                            case 2: v2 = p; break;
                        }
                        vertsInFacet++;
                    }
                    else if (toks[0] == "endfacet" && vertsInFacet == 3)
                    {
                        int i0 = verts.Count;
                        verts.Add(v0); verts.Add(v1); verts.Add(v2);
                        normals.Add(currentNormal);
                        normals.Add(currentNormal);
                        normals.Add(currentNormal);
                        tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                        vertsInFacet = 0;
                    }
                }
            }

            return BuildMesh(verts.ToArray(), normals.ToArray(), tris.ToArray());
        }

        static Mesh BuildMesh(Vector3[] verts, Vector3[] normals, int[] tris)
        {
            var mesh = new Mesh();
            if (verts.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.triangles = tris;
            // Prefer provided normals if they look non-zero; else recompute.
            bool anyNormal = false;
            for (int i = 0; i < normals.Length && !anyNormal; i++)
                if (normals[i].sqrMagnitude > 1e-8f) anyNormal = true;
            if (anyNormal) mesh.normals = normals;
            else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Vector3 ReadVec3(byte[] b, int off)
        {
            return new Vector3(
                BitConverter.ToSingle(b, off),
                BitConverter.ToSingle(b, off + 4),
                BitConverter.ToSingle(b, off + 8));
        }

        static float Parse(string s) =>
            float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
