using UnityEngine;

namespace Playa
{
    // Procedural truncated-cone skirt attached at the hip bone. Sways in
    // response to hip translational velocity (feels like fabric drag) and
    // returns to rest via critically-damped spring. No PhysX Cloth — the
    // crowd is dense enough that Unity Cloth performance is prohibitive.
    //
    // If we later want real cloth simulation this can be swapped for a
    // SkinnedMeshRenderer + Cloth component setup without touching the
    // spawn code — DanceFloor only holds the component reference.
    public sealed class LinenSkirt : MonoBehaviour
    {
        [Range(0.5f, 20f)] public float dragResponse = 8f;
        [Range(0.5f, 30f)] public float restoreStiffness = 14f;
        [Range(0.1f, 5f)] public float restoreDamping = 2.2f;
        [Range(0.02f, 0.6f)] public float maxTiltRadians = 0.35f;

        Transform hip;
        Vector3 lastHipWorldPos;
        Vector2 tilt;             // current sway angle: (pitchX, rollZ)
        Vector2 tiltVel;

        public static LinenSkirt Attach(Transform hipBone, DancerLibrary lib, Color color)
        {
            var go = new GameObject("LinenSkirt");
            go.transform.SetParent(hipBone, false);
            go.transform.localPosition = new Vector3(0f, lib.skirtHipOffset, 0f);
            go.transform.localRotation = Quaternion.identity;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = BuildMesh(lib);

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BuildMaterial(color);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;

            var s = go.AddComponent<LinenSkirt>();
            s.hip = hipBone;
            s.lastHipWorldPos = hipBone.position;
            return s;
        }

        static Mesh BuildMesh(DancerLibrary lib)
        {
            int segs = Mathf.Max(6, lib.skirtSegments);
            int rings = Mathf.Max(2, lib.skirtRings);
            int vertsPerRing = segs + 1;                 // duplicate seam vert for clean UVs
            var verts = new Vector3[vertsPerRing * rings];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segs * (rings - 1) * 6];

            for (int r = 0; r < rings; r++)
            {
                float rt = r / (float)(rings - 1);       // 0 top .. 1 bottom
                float radius = Mathf.Lerp(lib.skirtTopRadius, lib.skirtBottomRadius, rt);
                float y = -rt * lib.skirtLength;
                for (int s = 0; s <= segs; s++)
                {
                    float a = (s / (float)segs) * Mathf.PI * 2f;
                    int vi = r * vertsPerRing + s;
                    verts[vi] = new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
                    uvs[vi] = new Vector2(s / (float)segs, rt);
                }
            }

            int ti = 0;
            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < segs; s++)
                {
                    int a = r * vertsPerRing + s;
                    int b = a + 1;
                    int c = a + vertsPerRing;
                    int d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            }

            var m = new Mesh { name = "LinenSkirt" };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Material BuildMaterial(Color color)
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            var shader = urp != null ? urp : Shader.Find("Standard");
            var m = new Material(shader) { name = "Linen" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            // Two-sided so we see the inside as the skirt flares.
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            return m;
        }

        void LateUpdate()
        {
            if (hip == null) return;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);

            // Hip translational velocity (world space) drives a lag/drag.
            Vector3 hipWorld = hip.position;
            Vector3 vel = (hipWorld - lastHipWorldPos) / dt;
            lastHipWorldPos = hipWorld;

            // Project velocity onto hip's local XZ so the drag tilts opposite
            // to the walking direction (fabric trails behind the body).
            Vector3 local = hip.InverseTransformDirection(vel);
            Vector2 target = new Vector2(
                Mathf.Clamp(-local.z / dragResponse,  -maxTiltRadians, maxTiltRadians),
                Mathf.Clamp( local.x / dragResponse,  -maxTiltRadians, maxTiltRadians));

            // Critically-ish-damped spring toward target.
            Vector2 accel = (target - tilt) * restoreStiffness - tiltVel * restoreDamping;
            tiltVel += accel * dt;
            tilt += tiltVel * dt;

            transform.localRotation = Quaternion.Euler(
                tilt.x * Mathf.Rad2Deg, 0f, tilt.y * Mathf.Rad2Deg);
        }
    }
}
