using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
#if URP_PRESENT
using UnityEngine.Rendering.Universal;
#endif
using Playa.Urdf;
using Playa.Verification;

namespace Playa
{
    // Builds the entire world procedurally at Awake so nothing has to live in
    // a hand-authored .unity binary. Add ONE empty GameObject with this
    // component to a fresh scene and press Play.
    //
    // Aesthetic direction (env-inspo/):
    //   • warm dust palette — amber fog, ember point lights, rose accents
    //   • a single large glowing GLOBE hovering above the dance floor as the
    //     unmistakable visual anchor everyone gathers around
    //   • crisscrossing canopy of amber string lights overhead
    //   • bohemian silhouettes — lantern totems, dome frames, sailcloth
    //     banners — instead of brutalist geometry
    //   • constant drifting dust + rising embers → ephemeral
    //   • dancers read as dark SILHOUETTES against the warm haze, then IGNITE
    //     with warm bloom as the player synchronizes them
    [DefaultExecutionOrder(-100)]
    public sealed class PlayaBoot : MonoBehaviour
    {
        [Header("World")]
        public float floorRadius = 18f;
        public int silhouetteCount = 11;
        public int seed = 4242;

        [Header("Robot (URDF drop-in)")]
        [Tooltip("Folder name under StreamingAssets/robots/. If empty or the URDF is missing, falls back to the built-in primitive body.")]
        public string robotUrdfName = "g1";
        [Tooltip("URDF filename within that folder — usually robot.urdf.")]
        public string robotUrdfFile = "robot.urdf";
        [Tooltip("Uniform scale applied to the loaded URDF root. G1 is ~1.3m; Mixamo humans are ~1.75m — 3× makes the robot a clear focal point.")]
        [Range(0.5f, 8f)] public float robotScale = 3f;
        [Tooltip("Robot's world-space walk speed during Approaching. ~0.9 m/s at 3× scale reads as a purposeful stride; drop toward 0.4 for a slow glide.")]
        [Range(0.05f, 3f)] public float robotWalkSpeed = 0.9f;
        [Tooltip("Robot is clamped to a disc of this radius so it never wanders off the dance floor. Set 0 to disable.")]
        [Range(0f, 40f)] public float robotStayInsideRadius = 15f;

        [Header("Crowd characters (optional)")]
        [Tooltip("Assign a DancerLibrary to spawn rigged Mixamo dancers instead of primitive capsules. Leave null for the primitive fallback.")]
        public DancerLibrary dancerLibrary;
        [Tooltip("How many dancers on the floor. 80 is the design target; drop to 20-30 while iterating so the editor stays responsive.")]
        [Range(4, 120)] public int crowdCount = 30;
        [Tooltip("Fine-tune multiplier on the auto-computed dance playback speed. 1 = natural (clip length × BPM); 2 = half speed; 0.5 = double speed.")]
        [Range(0.25f, 4f)] public float crowdDanceSpeedScale = 1f;

        [Header("Music")]
        [Tooltip("Optional audio file. Drag any track here (ideally near 124 BPM) and it will loop while the scene runs.")]
        public AudioClip musicTrack;
        [Range(0f, 1f)] public float musicVolume = 0.7f;

        [Header("Centerpiece globe")]
        public float globeHeight = 9.5f;
        public float globeRadius = 2.6f;
        [Range(1f, 20f)] public float globeEmission = 7.5f;

        [Header("Canopy")]
        public int canopyStrands = 6;
        public int bulbsPerStrand = 22;

        [Header("Palette")]
        public Color sandColor = new Color(0.42f, 0.30f, 0.20f);
        public Color skyZenith = new Color(0.02f, 0.015f, 0.02f);
        public Color skyHorizon = new Color(0.30f, 0.12f, 0.08f);
        public Color emberWarm = new Color(1.0f, 0.42f, 0.10f);
        public Color emberRose = new Color(0.95f, 0.32f, 0.55f);
        public Color emberDeep = new Color(1.0f, 0.20f, 0.06f);

        [Header("Fog — warm dust")]
        public Color fogColor = new Color(0.22f, 0.12f, 0.09f);
        public float fogDensity = 0.055f;

        [Header("Post-processing")]
        public float bloomIntensity = 1.8f;
        public float bloomThreshold = 0.55f;
        public float filmGrain = 0.35f;

        Transform world;
        DanceFloor floor;
        IgnitionController ignition;
        Light globeLight;
        MusicBeat music;
        RobotDancer robot;
        SongPicker songPicker;

        void Awake()
        {
            var rng = new System.Random(seed);
            world = new GameObject("~World").transform;
            world.SetParent(transform, false);

            BuildLightingAndSky();
            BuildGround(rng);
            var stageLights = BuildCenterpieceGlobe();
            BuildCanopy(rng);
            BuildSilhouettes(rng);
            BuildCampfire();
            BuildAmbientDust();
            var dustBurst = BuildDustBurst();
            BuildPostFX();

            music = BuildMusic();
            songPicker = BuildDJBooth();
            floor = BuildDanceFloor();
            robot = LoadUrdfRobotOrFallback();
            ignition = BuildIgnition(stageLights, dustBurst);
            var player = BuildPlayer();
            BuildHUDAndTelemetry(player);

            // Cross-wire late refs that FindAnyObjectByType would've missed.
            floor.Robot = robot;
            floor.Music = music;
            robot.floor = floor;
            robot.music = music;
            robot.player = player;
            ignition.robot = robot;
            songPicker.player = player.transform;
        }

        void Update()
        {
            // Push live-tunable knobs to their runtime owners each frame so
            // moving sliders in the Inspector during Play mode actually does
            // something.
            if (floor != null) floor.danceSpeedScale = crowdDanceSpeedScale;
            if (music != null)
            {
                music.volume = musicVolume;
                music.defaultTrack = musicTrack;
            }
            if (robot != null)
            {
                robot.transform.localScale = Vector3.one * robotScale;
                robot.walkSpeed = robotWalkSpeed;

                // Keep the robot on the dance floor.
                if (robotStayInsideRadius > 0f)
                {
                    var p = robot.transform.position;
                    var flat = new Vector2(p.x, p.z);
                    if (flat.magnitude > robotStayInsideRadius)
                    {
                        flat = flat.normalized * robotStayInsideRadius;
                        robot.transform.position = new Vector3(flat.x, p.y, flat.y);
                    }
                }
            }
        }

        // ----- LIGHTING / SKY -----------------------------------------------

        void BuildLightingAndSky()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.10f, 0.06f, 0.05f);
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.08f, 0.05f);
            RenderSettings.ambientGroundColor = sandColor * 0.35f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;

            RenderSettings.skybox = MakeGradientSkybox();

            var moonGO = new GameObject("Moon");
            moonGO.transform.SetParent(world, false);
            moonGO.transform.rotation = Quaternion.Euler(52f, -140f, 0f);
            var moon = moonGO.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.35f, 0.28f, 0.42f);
            moon.intensity = 0.10f;   // very low — this is night, atmosphere carries the scene
            moon.shadows = LightShadows.None;
        }

        Material MakeGradientSkybox()
        {
            var proc = Shader.Find("Skybox/Procedural");
            Material sb;
            if (proc != null)
            {
                sb = new Material(proc) { name = "PlayaSky" };
                sb.SetColor("_SkyTint", skyHorizon);
                sb.SetColor("_GroundColor", sandColor * 0.5f);
                sb.SetFloat("_SunSize", 0f);
                sb.SetFloat("_AtmosphereThickness", 0.35f);
                sb.SetFloat("_Exposure", 0.5f);
            }
            else
            {
                sb = new Material(Shader.Find("Skybox/6 Sided") ?? Shader.Find("Skybox"))
                     { name = "PlayaSky" };
            }
            return sb;
        }

        // ----- GROUND -------------------------------------------------------

        void BuildGround(System.Random rng)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(world, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MakeUnlitLikeMaterial(sandColor * 0.55f, 0f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mf.sharedMesh = MakePlayaDisc(floorRadius * 3.5f, 64);

            // Invisible slab so the CharacterController has something to stand on.
            var groundCol = go.AddComponent<BoxCollider>();
            groundCol.center = new Vector3(0f, -0.1f, 0f);
            groundCol.size = new Vector3(floorRadius * 8f, 0.2f, floorRadius * 8f);
        }

        Mesh MakePlayaDisc(float radius, int segments)
        {
            var m = new Mesh { name = "PlayaDisc" };
            var verts = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a = i * (Mathf.PI * 2f) / segments;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                uvs[i + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a));
            }
            for (int i = 0; i < segments; i++)
            {
                tris[3 * i] = 0;
                tris[3 * i + 1] = i + 1;
                tris[3 * i + 2] = ((i + 1) % segments) + 1;
            }
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ----- CENTERPIECE GLOBE -------------------------------------------

        Light[] BuildCenterpieceGlobe()
        {
            var root = new GameObject("Globe").transform;
            root.SetParent(world, false);
            root.position = Vector3.zero;

            // The globe: a giant emissive paper-lantern sphere hovering above
            // the dance floor. This is THE anchor — visible from every angle,
            // bloomed to bathe the crowd in warm light.
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(orb.GetComponent<Collider>());
            orb.name = "Globe";
            orb.transform.SetParent(root, false);
            orb.transform.localPosition = new Vector3(0f, globeHeight, 0f);
            orb.transform.localScale = Vector3.one * (globeRadius * 2f);
            orb.GetComponent<Renderer>().sharedMaterial =
                MakeEmissiveMaterial(emberWarm, globeEmission);
            orb.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Tripod of thin cables suspending the globe from three tall poles.
            var poleMat = MakeUnlitLikeMaterial(new Color(0.05f, 0.04f, 0.05f), 0.02f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * (Mathf.PI * 2f / 3f);
                float px = Mathf.Cos(a) * floorRadius * 0.55f;
                float pz = Mathf.Sin(a) * floorRadius * 0.55f;
                float poleH = globeHeight + 2.5f;

                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(pole.GetComponent<Collider>());
                pole.name = $"Pole_{i}";
                pole.transform.SetParent(root, false);
                pole.transform.localScale = new Vector3(0.18f, poleH * 0.5f, 0.18f);
                pole.transform.localPosition = new Vector3(px, poleH * 0.5f, pz);
                pole.GetComponent<Renderer>().sharedMaterial = poleMat;

                // Guy cable from pole-top to globe (thin cube stretched).
                var guy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(guy.GetComponent<Collider>());
                guy.name = $"Cable_{i}";
                guy.transform.SetParent(root, false);
                var start = new Vector3(px, poleH, pz);
                var end = new Vector3(0f, globeHeight, 0f);
                var mid = (start + end) * 0.5f;
                var delta = end - start;
                guy.transform.localPosition = mid;
                guy.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
                guy.transform.localScale = new Vector3(0.04f, delta.magnitude * 0.5f, 0.04f);
                guy.GetComponent<Renderer>().sharedMaterial = poleMat;

                // A small lantern at each pole top — warm accents on the periphery.
                var lantern = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(lantern.GetComponent<Collider>());
                lantern.transform.SetParent(root, false);
                lantern.transform.localPosition = new Vector3(px, poleH + 0.3f, pz);
                lantern.transform.localScale = Vector3.one * 0.5f;
                lantern.GetComponent<Renderer>().sharedMaterial =
                    MakeEmissiveMaterial(emberWarm, 3.5f);
            }

            // The big-globe light itself — omni-directional, deep, warm.
            var lightGO = new GameObject("GlobeLight");
            lightGO.transform.SetParent(root, false);
            lightGO.transform.localPosition = new Vector3(0f, globeHeight, 0f);
            var l = lightGO.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 45f;
            l.intensity = 6f;
            l.color = emberWarm;
            l.shadows = LightShadows.None;
            globeLight = l;

            // A couple of accent uplights aimed AT the globe from below to give
            // it visual weight in the fog (silhouette-through-haze look).
            var accents = new List<Light>();
            for (int i = 0; i < 4; i++)
            {
                float a = i * (Mathf.PI * 0.5f) + 0.25f * Mathf.PI;
                float ax = Mathf.Cos(a) * 2.2f;
                float az = Mathf.Sin(a) * 2.2f;
                var lgo = new GameObject($"GlobeUplight_{i}");
                lgo.transform.SetParent(root, false);
                lgo.transform.localPosition = new Vector3(ax, 0.5f, az);
                lgo.transform.LookAt(root.TransformPoint(new Vector3(0f, globeHeight, 0f)));
                var s = lgo.AddComponent<Light>();
                s.type = LightType.Spot;
                s.range = globeHeight + 4f;
                s.spotAngle = 40f;
                s.intensity = 2.2f;
                s.color = i % 2 == 0 ? emberWarm : emberRose;
                s.shadows = LightShadows.None;
                accents.Add(s);
            }

            // Return the accents as the "stage lights" that IgnitionController
            // will drive to snap to the player's phase when the crowd ignites.
            accents.Add(globeLight);
            return accents.ToArray();
        }

        // ----- OVERHEAD CANOPY OF STRING LIGHTS ----------------------------

        void BuildCanopy(System.Random rng)
        {
            var canopy = new GameObject("Canopy").transform;
            canopy.SetParent(world, false);

            for (int s = 0; s < canopyStrands; s++)
            {
                // Each strand is a chord across the play area at a random
                // orientation, sagging catenary-style.
                float ang = (s / (float)canopyStrands) * Mathf.PI + (float)rng.NextDouble() * 0.3f;
                float cosA = Mathf.Cos(ang), sinA = Mathf.Sin(ang);
                // Anchor just outside the dance floor so poles don't collide
                // with silhouette camps (which start at floorRadius*1.35).
                float length = floorRadius * 1.15f;
                Vector3 a0 = new Vector3(-cosA * length, 0f, -sinA * length);
                Vector3 a1 = new Vector3( cosA * length, 0f,  sinA * length);
                float baseH = 7.0f + (float)rng.NextDouble() * 1.5f;
                float sag = 1.8f + (float)rng.NextDouble() * 0.7f;

                // Anchor poles at each end.
                var poleMat = MakeUnlitLikeMaterial(new Color(0.05f, 0.05f, 0.05f), 0f);
                for (int endIdx = 0; endIdx < 2; endIdx++)
                {
                    var pos = endIdx == 0 ? a0 : a1;
                    var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(pole.GetComponent<Collider>());
                    pole.transform.SetParent(canopy, false);
                    pole.transform.localScale = new Vector3(0.12f, baseH * 0.5f, 0.12f);
                    pole.transform.localPosition = new Vector3(pos.x, baseH * 0.5f, pos.z);
                    pole.GetComponent<Renderer>().sharedMaterial = poleMat;
                }

                var strandColor = PickCanopyColor(rng);
                for (int i = 0; i < bulbsPerStrand; i++)
                {
                    float u = i / (float)(bulbsPerStrand - 1);
                    Vector3 p = Vector3.Lerp(a0, a1, u);
                    float catenary = 4f * u * (1f - u); // 0 at ends, 1 at middle
                    p.y = baseH - sag * catenary;

                    Color c = i % 5 == 0 ? PickCanopyColor(rng) : strandColor;

                    var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(bulb.GetComponent<Collider>());
                    bulb.name = $"Bulb_{s}_{i}";
                    bulb.transform.SetParent(canopy, false);
                    bulb.transform.localPosition = p;
                    bulb.transform.localScale = Vector3.one * 0.16f;
                    bulb.GetComponent<Renderer>().sharedMaterial =
                        MakeEmissiveMaterial(c, 5f);
                    bulb.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

                    // 1-in-6 bulbs are real point lights — the rest are pure
                    // emissive dots kept alive by the bloom pass.
                    if (i % 6 == 3)
                    {
                        var lgo = new GameObject($"BulbLight_{s}_{i}");
                        lgo.transform.SetParent(bulb.transform, false);
                        var lt = lgo.AddComponent<Light>();
                        lt.type = LightType.Point;
                        lt.range = 7f;
                        lt.intensity = 0.7f;
                        lt.color = c;
                        lt.shadows = LightShadows.None;
                    }
                }
            }
        }

        Color PickCanopyColor(System.Random rng)
        {
            double roll = rng.NextDouble();
            if (roll < 0.65) return emberWarm;
            if (roll < 0.90) return Color.Lerp(emberWarm, emberRose, 0.5f + (float)rng.NextDouble() * 0.5f);
            return emberDeep;
        }

        // ----- SILHOUETTES --------------------------------------------------

        void BuildSilhouettes(System.Random rng)
        {
            var mat = MakeUnlitLikeMaterial(new Color(0.04f, 0.035f, 0.045f), 0.02f);
            for (int i = 0; i < silhouetteCount; i++)
            {
                float a = i * (Mathf.PI * 2f) / silhouetteCount +
                          (float)(rng.NextDouble() * 0.25);
                float dist = floorRadius * (1.35f + (float)rng.NextDouble() * 1.3f);
                float x = Mathf.Cos(a) * dist;
                float z = Mathf.Sin(a) * dist;

                var kind = rng.Next(4);
                GameObject go;
                switch (kind)
                {
                    case 0: go = MakeLanternTotem(mat, rng); break;
                    case 1: go = MakeDomeFrame(mat, rng); break;
                    case 2: go = MakeSailBanner(mat, rng); break;
                    default: go = MakeSpindlyArt(mat, rng); break;
                }
                go.name = $"Silhouette_{i:00}_{go.name}";
                go.transform.SetParent(world, false);
                go.transform.position = new Vector3(x, 0f, z);
                go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

                // Warm camp glow at the base — reads as "someone's camp".
                var lgo = new GameObject("CampGlow");
                lgo.transform.SetParent(go.transform, false);
                lgo.transform.localPosition = new Vector3(
                    (float)(rng.NextDouble() * 2f - 1f) * 1.2f, 0.5f,
                    (float)(rng.NextDouble() * 2f - 1f) * 1.2f);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 12f + (float)rng.NextDouble() * 6f;
                l.intensity = 1.4f;
                Color c = rng.NextDouble() < 0.7
                    ? emberWarm
                    : Color.Lerp(emberWarm, emberRose, (float)rng.NextDouble());
                l.color = c;
                l.shadows = LightShadows.None;

                // Tiny rising ember cloud from each camp base.
                BuildCampEmbers(go.transform, c);
            }
        }

        GameObject MakeLanternTotem(Material dark, System.Random rng)
        {
            var root = new GameObject("LanternTotem");
            float poleH = 4.5f + (float)rng.NextDouble() * 3.5f;
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(root.transform, false);
            pole.transform.localScale = new Vector3(0.16f, poleH * 0.5f, 0.16f);
            pole.transform.localPosition = new Vector3(0f, poleH * 0.5f, 0f);
            pole.GetComponent<Renderer>().sharedMaterial = dark;

            // Three lanterns strung down the pole.
            for (int k = 0; k < 3; k++)
            {
                var lantern = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(lantern.GetComponent<Collider>());
                lantern.transform.SetParent(root.transform, false);
                float ly = poleH - 0.4f - k * (poleH * 0.28f);
                lantern.transform.localPosition = new Vector3(0f, ly, 0f);
                lantern.transform.localScale = Vector3.one * (0.55f - k * 0.08f);
                Color lc = k == 0 ? emberWarm
                    : (k == 1 ? Color.Lerp(emberWarm, emberRose, 0.4f) : emberDeep);
                lantern.GetComponent<Renderer>().sharedMaterial =
                    MakeEmissiveMaterial(lc, 4.5f);
            }
            return root;
        }

        GameObject MakeDomeFrame(Material dark, System.Random rng)
        {
            var root = new GameObject("Dome");
            float r = 2.5f + (float)rng.NextDouble() * 1.5f;
            // Bottom ring + arches: cheap approximation of a geodesic dome
            int spokes = 8;
            for (int i = 0; i < spokes; i++)
            {
                float a0 = i * (Mathf.PI * 2f / spokes);
                float a1 = (i + 1) * (Mathf.PI * 2f / spokes);
                var p0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r);
                var p1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                var top = new Vector3(0f, r, 0f);

                AddBar(root.transform, p0, p1, 0.08f, dark);
                AddBar(root.transform, p0, top, 0.06f, dark);
                // A midway ring stub
                var mid0 = Vector3.Lerp(p0, top, 0.5f);
                var mid1 = Vector3.Lerp(p1, top, 0.5f);
                AddBar(root.transform, mid0, mid1, 0.05f, dark);
            }
            // A small warm lamp inside the dome so it reads as inhabited.
            var lampGO = new GameObject("DomeLamp");
            lampGO.transform.SetParent(root.transform, false);
            lampGO.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(lampGO.transform, false);
            lamp.transform.localScale = Vector3.one * 0.35f;
            lamp.GetComponent<Renderer>().sharedMaterial = MakeEmissiveMaterial(emberWarm, 5f);
            return root;
        }

        GameObject MakeSailBanner(Material dark, System.Random rng)
        {
            var root = new GameObject("SailBanner");
            float poleH = 5.5f + (float)rng.NextDouble() * 2f;
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(root.transform, false);
            pole.transform.localScale = new Vector3(0.14f, poleH * 0.5f, 0.14f);
            pole.transform.localPosition = new Vector3(0f, poleH * 0.5f, 0f);
            pole.GetComponent<Renderer>().sharedMaterial = dark;

            // Sailcloth — a leaning thin cube catching the ember light.
            var sail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(sail.GetComponent<Collider>());
            sail.transform.SetParent(root.transform, false);
            sail.transform.localScale = new Vector3(3.2f, poleH * 0.85f, 0.03f);
            sail.transform.localPosition = new Vector3(1.6f, poleH * 0.5f, 0f);
            sail.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            sail.GetComponent<Renderer>().sharedMaterial =
                MakeUnlitLikeMaterial(new Color(0.12f, 0.09f, 0.08f), 0.02f);

            // Bunting of tiny lantern beads along the pole top.
            for (int k = 0; k < 5; k++)
            {
                var bead = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(bead.GetComponent<Collider>());
                bead.transform.SetParent(root.transform, false);
                bead.transform.localScale = Vector3.one * 0.14f;
                bead.transform.localPosition = new Vector3(
                    Mathf.Lerp(0.1f, 3.0f, k / 4f),
                    poleH - 0.2f - Mathf.Sin(k * 0.6f) * 0.1f,
                    0f);
                bead.GetComponent<Renderer>().sharedMaterial =
                    MakeEmissiveMaterial(k % 2 == 0 ? emberWarm : emberRose, 4.2f);
            }
            return root;
        }

        GameObject MakeSpindlyArt(Material dark, System.Random rng)
        {
            var root = new GameObject("Spindly");
            float h = 10f + (float)rng.NextDouble() * 8f;
            int spines = 5 + rng.Next(3);
            for (int i = 0; i < spines; i++)
            {
                float a = i * (Mathf.PI * 2f / spines);
                float lean = 8f + (float)rng.NextDouble() * 5f;
                var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(pole.GetComponent<Collider>());
                pole.transform.SetParent(root.transform, false);
                pole.transform.localScale = new Vector3(0.12f, h * 0.5f, 0.12f);
                pole.transform.localPosition = new Vector3(
                    Mathf.Cos(a) * 0.3f, h * 0.5f, Mathf.Sin(a) * 0.3f);
                pole.transform.localRotation = Quaternion.Euler(
                    Mathf.Cos(a) * lean, 0f, -Mathf.Sin(a) * lean);
                pole.GetComponent<Renderer>().sharedMaterial = dark;
            }
            // A dot at the top.
            var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(root.transform, false);
            cap.transform.localScale = Vector3.one * 0.6f;
            cap.transform.localPosition = new Vector3(0f, h * 0.95f, 0f);
            cap.GetComponent<Renderer>().sharedMaterial = MakeEmissiveMaterial(emberWarm, 4f);
            return root;
        }

        static void AddBar(Transform parent, Vector3 a, Vector3 b, float thickness, Material mat)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(bar.GetComponent<Collider>());
            bar.transform.SetParent(parent, false);
            var mid = (a + b) * 0.5f;
            var delta = b - a;
            bar.transform.localPosition = mid;
            bar.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            bar.transform.localScale = new Vector3(thickness, delta.magnitude * 0.5f, thickness);
            bar.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ----- CAMPFIRE (central warm anchor at ground level) --------------

        void BuildCampfire()
        {
            var fire = new GameObject("Campfire").transform;
            fire.SetParent(world, false);
            fire.position = Vector3.zero;

            // Warm ground-level ember light — visible under the crowd.
            var lgo = new GameObject("FireLight");
            lgo.transform.SetParent(fire, false);
            lgo.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 14f;
            l.intensity = 2.0f;
            l.color = emberDeep;
            l.shadows = LightShadows.None;

            // Continuous rising embers.
            var psGO = new GameObject("Embers");
            psGO.transform.SetParent(fire, false);
            psGO.transform.localPosition = Vector3.zero;
            var ps = psGO.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startLifetime = 3.5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.55f, 0.15f), new Color(1f, 0.25f, 0.05f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            var emission = ps.emission;
            emission.rateOverTime = 60f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.6f;
            shape.radiusThickness = 1f;

            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            // All three axes must share the same MinMaxCurve mode.
            vol.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vol.y = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
            vol.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1.0f, 0.6f, 0.2f), 0f),
                    new GradientColorKey(new Color(0.5f, 0.1f, 0.05f), 1f)
                },
                new[] {
                    new GradientAlphaKey(0.0f, 0.0f),
                    new GradientAlphaKey(0.9f, 0.2f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            col.color = grad;

            var pr = psGO.GetComponent<ParticleSystemRenderer>();
            pr.material = MakeParticleMaterial();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
        }

        // ----- AMBIENT DRIFTING DUST ---------------------------------------

        void BuildAmbientDust()
        {
            var go = new GameObject("AmbientDust");
            go.transform.SetParent(world, false);
            go.transform.position = new Vector3(0f, 3f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 8f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.18f);
            main.startColor = new Color(1f, 0.75f, 0.5f, 0.14f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1000;

            var emission = ps.emission;
            emission.rateOverTime = 120f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(floorRadius * 2.2f, 6f, floorRadius * 2.2f);

            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            vol.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.15f);
            vol.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.8f, 0.55f), 0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.35f), 1f)
                },
                new[] {
                    new GradientAlphaKey(0.0f, 0.0f),
                    new GradientAlphaKey(0.18f, 0.3f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            col.color = grad;

            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.material = MakeParticleMaterial();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
        }

        void BuildCampEmbers(Transform parent, Color emberColor)
        {
            var go = new GameObject("CampEmbers");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 2.5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = emberColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.rateOverTime = 8f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.4f;

            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            // All three axes must share the same MinMaxCurve mode.
            vol.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vol.y = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            vol.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(emberColor, 0f),
                    new GradientColorKey(emberColor * 0.4f, 1f)
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.7f, 0.2f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = grad;

            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.material = MakeParticleMaterial();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
        }

        // ----- DUST BURST (ignition VFX) ------------------------------------

        ParticleSystem BuildDustBurst()
        {
            var go = new GameObject("DustBurst");
            go.transform.SetParent(world, false);
            go.transform.position = Vector3.zero;
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startLifetime = 3.8f;
            main.startSpeed = 9.0f;
            main.startSize = 0.4f;
            main.startColor = new Color(1.0f, 0.72f, 0.35f, 0.65f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = 2200;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1800) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 6f;
            shape.radiusThickness = 0.15f;

            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.radial = 5f;
            vol.speedModifier = 1f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1.0f, 0.72f, 0.30f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.28f, 0.15f), 1f)
                },
                new[] {
                    new GradientAlphaKey(0.0f, 0.00f),
                    new GradientAlphaKey(0.95f, 0.15f),
                    new GradientAlphaKey(0.0f, 1.0f)
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sizeOL = ps.sizeOverLifetime;
            sizeOL.enabled = true;
            sizeOL.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2.8f));

            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.material = MakeParticleMaterial();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;

            return ps;
        }

        // ----- POST-PROCESSING ---------------------------------------------

        void BuildPostFX()
        {
#if URP_PRESENT
            var go = new GameObject("PostFX");
            go.transform.SetParent(world, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 10f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "PlayaProfile";

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.overrideState = true;
            bloom.intensity.value = bloomIntensity;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = bloomThreshold;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.75f;
            bloom.tint.overrideState = true;
            bloom.tint.value = new Color(1.0f, 0.85f, 0.72f);

            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.overrideState = true;
            grain.intensity.value = filmGrain;

            var colorAdj = profile.Add<ColorAdjustments>(true);
            colorAdj.postExposure.overrideState = true;
            colorAdj.postExposure.value = 0.15f;
            colorAdj.contrast.overrideState = true;
            colorAdj.contrast.value = 12f;
            colorAdj.colorFilter.overrideState = true;
            colorAdj.colorFilter.value = new Color(1.0f, 0.94f, 0.86f);
            colorAdj.saturation.overrideState = true;
            colorAdj.saturation.value = 8f;

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.35f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.6f;
            vignette.color.overrideState = true;
            vignette.color.value = new Color(0.02f, 0.0f, 0.0f);

            v.profile = profile;
#endif
        }

        // ----- DANCE FLOOR / IGNITION / PLAYER / HUD ------------------------

        DanceFloor BuildDanceFloor()
        {
            var go = new GameObject("DanceFloor");
            go.transform.SetParent(transform, false);
            go.SetActive(false);
            var f = go.AddComponent<DanceFloor>();
            f.floorRadius = floorRadius - 1f;
            f.seed = seed ^ 0x0f0f0f0f;
            f.hotPlayerColor = emberWarm;
            f.library = dancerLibrary;
            f.count = crowdCount;
            f.danceSpeedScale = crowdDanceSpeedScale;
            go.SetActive(true);
            return f;
        }

        IgnitionController BuildIgnition(Light[] stageLights, ParticleSystem burst)
        {
            var go = new GameObject("Ignition");
            go.transform.SetParent(transform, false);
            var ic = go.AddComponent<IgnitionController>();
            ic.floor = floor;
            ic.stageLights = stageLights;
            ic.dustBurst = burst;
            return ic;
        }

        PlayerRig BuildPlayer()
        {
            var go = new GameObject("Player");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 0.1f, -floorRadius * 0.95f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, 0f);

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var camGO = new GameObject("Head");
            camGO.transform.SetParent(go.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            var cam = camGO.AddComponent<Camera>();
            cam.farClipPlane = 220f;
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 72f;
            cam.backgroundColor = skyZenith;
            cam.clearFlags = CameraClearFlags.Skybox;
            camGO.AddComponent<AudioListener>();
#if URP_PRESENT
            // URP auto-adds UniversalAdditionalCameraData when Camera is added.
            var extra = camGO.GetComponent<UniversalAdditionalCameraData>()
                        ?? camGO.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
#endif

            var rig = go.AddComponent<PlayerRig>();
            rig.head = camGO.transform;
            rig.floor = floor;
            return rig;
        }

        void BuildHUDAndTelemetry(PlayerRig player)
        {
            var hudGO = new GameObject("HUD");
            hudGO.transform.SetParent(transform, false);
            var hud = hudGO.AddComponent<HUD>();
            hud.floor = floor;
            hud.player = player;
            hud.ignition = ignition;

            var telGO = new GameObject("Telemetry");
            telGO.transform.SetParent(transform, false);
            telGO.AddComponent<CrowdTelemetry>();
        }

        // ----- MUSIC / DJ BOOTH / ROBOT -----------------------------------

        MusicBeat BuildMusic()
        {
            var go = new GameObject("Music");
            go.transform.SetParent(transform, false);
            go.SetActive(false);   // configure before Awake runs
            var m = go.AddComponent<MusicBeat>();
            m.defaultTrack = musicTrack;
            m.volume = musicVolume;
            go.SetActive(true);
            return m;
        }

        // A low glowing counter. Placed directly ahead of the player spawn
        // (player spawns at -floorRadius*0.95 facing +Z) so it's the first
        // thing they see. Walk within 5m and use ← / → (or scroll) to swap.
        SongPicker BuildDJBooth()
        {
            var root = new GameObject("DJBooth").transform;
            root.SetParent(world, false);
            root.position = new Vector3(0f, 0f, -floorRadius * 0.45f);
            root.rotation = Quaternion.Euler(0f, 180f, 0f);   // face the player

            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(deck.GetComponent<Collider>());
            deck.name = "BoothDeck";
            deck.transform.SetParent(root, false);
            deck.transform.localScale = new Vector3(3.2f, 1.1f, 1.4f);
            deck.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            deck.GetComponent<Renderer>().sharedMaterial =
                MakeUnlitLikeMaterial(new Color(0.08f, 0.07f, 0.08f), 0.05f);

            var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(glow.GetComponent<Collider>());
            glow.name = "BoothGlow";
            glow.transform.SetParent(root, false);
            glow.transform.localScale = new Vector3(3.0f, 0.7f, 0.05f);
            glow.transform.localPosition = new Vector3(0f, 0.55f, -0.72f);
            glow.GetComponent<Renderer>().sharedMaterial =
                MakeEmissiveMaterial(emberWarm, 4.5f);

            var lantern = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(lantern.GetComponent<Collider>());
            lantern.transform.SetParent(root, false);
            lantern.transform.localScale = Vector3.one * 0.4f;
            lantern.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            lantern.GetComponent<Renderer>().sharedMaterial =
                MakeEmissiveMaterial(emberWarm, 5f);

            var lgo = new GameObject("BoothLight");
            lgo.transform.SetParent(root, false);
            lgo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 10f;
            l.intensity = 2.5f;
            l.color = emberWarm;
            l.shadows = LightShadows.None;

            var pickerGO = new GameObject("SongPicker");
            pickerGO.transform.SetParent(root, false);
            pickerGO.transform.localPosition = Vector3.zero;
            pickerGO.SetActive(false);  // configure fields before Awake runs
            var picker = pickerGO.AddComponent<SongPicker>();
            picker.music = music;
            picker.boothGlowRenderer = glow.GetComponent<Renderer>();
            pickerGO.SetActive(true);
            return picker;
        }

        // Try to load a URDF drop-in from StreamingAssets/robots/<name>/. If
        // parsing or file access fails at any step, fall back to the built-in
        // primitive body so the demo always boots and never shows a null robot.
        RobotDancer LoadUrdfRobotOrFallback()
        {
            if (string.IsNullOrEmpty(robotUrdfName))
                return BuildRobot();

            var urdfPath = Path.Combine(
                Application.streamingAssetsPath, "robots", robotUrdfName, robotUrdfFile);
            if (!File.Exists(urdfPath))
            {
                Debug.LogWarning(
                    $"URDF '{urdfPath}' not found — falling back to primitive robot body. " +
                    "See Assets/StreamingAssets/robots/README.md for setup.");
                return BuildRobot();
            }

            UrdfRobot spec;
            UrdfRobotInstance instance;
            try
            {
                spec = UrdfParser.Parse(urdfPath);
                var root = new GameObject($"RobotDancer[{spec.Name}]").transform;
                root.SetParent(transform, false);
                root.position = new Vector3(floorRadius * 0.35f, 0f, -floorRadius * 0.35f);
                root.localScale = Vector3.one * robotScale;

                instance = UrdfInstantiator.Instantiate(spec, root);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"URDF load failed for '{urdfPath}': {ex.Message} — falling back.");
                return BuildRobot();
            }

            var rootGO = instance.Root.transform.parent.gameObject;

            // RobotDancer, choreographer, feasibility stack — wired here.
            rootGO.SetActive(false);
            var rd = rootGO.AddComponent<RobotDancer>();
            rd.visual = instance.UrdfSpaceRoot; // used only if choreographer is null

            var chore = rootGO.AddComponent<RobotChoreographer>();
            var morph = MorphologyDetector.Detect(instance);
            chore.Bind(instance, morph);
            rd.choreographer = chore;

            var auditor = rootGO.AddComponent<FeasibilityAuditor>();
            auditor.Bind(instance);

            var tint = rootGO.AddComponent<JointTintApplier>();
            tint.RebindFrom(auditor);

            rootGO.SetActive(true);
            Debug.Log(
                $"URDF loaded: {spec.Name} ({morph}) · {instance.ActuatedJoints.Count} actuated joints.");
            return rd;
        }

        // The robot NPC. Six primitives assembled by hand: body, head, visor,
        // two arms, two legs. Visual is a child of RobotDancer so its bob
        // doesn't fight state-machine locomotion. This is the fallback path
        // when no URDF is available.
        RobotDancer BuildRobot()
        {
            var root = new GameObject("RobotDancer").transform;
            root.SetParent(transform, false);
            root.position = new Vector3(floorRadius * 0.35f, 0.1f, -floorRadius * 0.35f);
            root.localScale = Vector3.one * robotScale;

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root, false);
            visual.localPosition = Vector3.zero;

            var chassisMat = MakeUnlitLikeMaterial(new Color(0.10f, 0.10f, 0.12f), 0.55f);
            var visorMat = MakeEmissiveMaterial(new Color(0.35f, 0.85f, 1.0f), 4.2f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(visual, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.42f, 0.55f, 0.42f);
            body.GetComponent<Renderer>().sharedMaterial = chassisMat;

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(head.GetComponent<Collider>());
            head.name = "Head";
            head.transform.SetParent(visual, false);
            head.transform.localPosition = new Vector3(0f, 1.75f, 0f);
            head.transform.localScale = new Vector3(0.55f, 0.5f, 0.55f);
            head.GetComponent<Renderer>().sharedMaterial = chassisMat;

            // NAMED "Visor" — RobotDancer finds it via visual.Find("Visor").
            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(visor.GetComponent<Collider>());
            visor.name = "Visor";
            visor.transform.SetParent(visual, false);
            visor.transform.localPosition = new Vector3(0f, 1.78f, 0.28f);
            visor.transform.localScale = new Vector3(0.42f, 0.12f, 0.05f);
            visor.GetComponent<Renderer>().sharedMaterial = visorMat;

            for (int side = -1; side <= 1; side += 2)
            {
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(arm.GetComponent<Collider>());
                arm.name = $"Arm_{(side < 0 ? "L" : "R")}";
                arm.transform.SetParent(visual, false);
                arm.transform.localScale = new Vector3(0.10f, 0.42f, 0.10f);
                arm.transform.localPosition = new Vector3(side * 0.35f, 1.05f, 0f);
                arm.transform.localRotation = Quaternion.Euler(0f, 0f, side * 12f);
                arm.GetComponent<Renderer>().sharedMaterial = chassisMat;
            }

            for (int side = -1; side <= 1; side += 2)
            {
                var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(leg.GetComponent<Collider>());
                leg.name = $"Leg_{(side < 0 ? "L" : "R")}";
                leg.transform.SetParent(visual, false);
                leg.transform.localScale = new Vector3(0.12f, 0.4f, 0.12f);
                leg.transform.localPosition = new Vector3(side * 0.15f, 0.4f, 0f);
                leg.GetComponent<Renderer>().sharedMaterial = chassisMat;
            }

            // SetActive(false) so we can wire fields before Awake fires.
            root.gameObject.SetActive(false);
            var rd = root.gameObject.AddComponent<RobotDancer>();
            rd.visual = visual;
            root.gameObject.SetActive(true);
            return rd;
        }

        // ----- MATERIALS ----------------------------------------------------

        static Material MakeUnlitLikeMaterial(Color c, float smoothness)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            var m = new Material(lit) { name = "Playa/Solid" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        static Material MakeEmissiveMaterial(Color c, float intensity)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            var m = new Material(lit) { name = "Playa/Emissive" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c * 0.5f);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c * 0.5f);
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * intensity);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return m;
        }

        static Material MakeParticleMaterial()
        {
            var s = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                    ?? Shader.Find("Particles/Standard Unlit")
                    ?? Shader.Find("Sprites/Default");
            var m = new Material(s) { name = "Playa/Dust" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            // For URP Particles/Unlit — set to additive so embers glow.
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 1f);    // Additive
            m.renderQueue = 3200;
            return m;
        }
    }
}
