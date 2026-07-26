using System;
using System.Collections.Generic;
using UnityEngine;

namespace Playa
{
    // The single manager for the crowd. Owns the pure-C# CrowdSimulator and
    // one transform per dancer. If DancerLibrary is populated, spawns rigged
    // Mixamo characters and drives their Animator via PhaseAnimatorSync so the
    // whole clip timeline is scrubbed by θ. Otherwise falls back to capsules
    // with procedural bob/sway.
    [DefaultExecutionOrder(-50)]
    public sealed class DanceFloor : MonoBehaviour
    {
        [Header("Population")]
        [Min(1)] public int count = 80;
        public float floorRadius = 16f;
        public int seed = 1337;

        [Header("Kuramoto (see §8)")]
        [Tooltip("ω_beat — beat angular frequency, rad/s. Default 4π ≈ 120 BPM.")]
        public float beatOmega = 4f * Mathf.PI;
        [Tooltip("σ — natural frequency spread. Sets K_c ≈ 1.596σ.")]
        public float freqSigma = 0.7f;
        [Tooltip("K_b — stage coupling. MUST be ≪ σ or crowd pre-locks.")]
        public float beatCoupling = 0.15f;
        [Tooltip("K_s — peer coupling. Target ~0.8·K_c to sit just below transition.")]
        public float peerCoupling = 0.9f;
        [Tooltip("r_s — peer neighbourhood radius (m).")]
        public float peerRadius = 4f;
        [Tooltip("K_p⁰ — player coupling at zero distance. Must dominate K_s locally.")]
        public float playerCoupling = 3.0f;
        [Tooltip("r_p — player coupling radius (m).")]
        public float playerRadius = 6f;

        [Header("Motion (fallback capsules only — ignored when library is populated)")]
        public float bobHeight = 0.22f;
        public float swayAmplitude = 0.09f;
        public float dancerScale = 0.6f;

        [Header("Look")]
        public Color coolBaseline = new Color(0.14f, 0.08f, 0.05f);
        public Color hotPlayerColor = new Color(1.0f, 0.52f, 0.16f);
        [Range(0f, 2f)] public float baseEmission = 0.12f;
        [Range(0f, 8f)] public float lockedEmission = 5.5f;

        [Header("Rigged characters (Mixamo)")]
        [Tooltip("If assigned and non-empty, spawn rigged Mixamo characters instead of capsules.")]
        public DancerLibrary library;
        [Tooltip("Multiplier on every clip's beatsPerLoop. 1 = library values; higher = slower dance playback.")]
        [Range(0.25f, 4f)] public float danceSpeedScale = 1f;

        public CrowdSimulator Simulator { get; private set; }
        public RobotDancer Robot { get; set; }
        public MusicBeat Music { get; set; }

        readonly KuramotoMath.ExternalInfluence[] extrasBuf =
            new KuramotoMath.ExternalInfluence[1];

        Transform[] dancers;
        Vector2[] swayAxis;
        MaterialPropertyBlock mpb;
        Renderer[][] renderers;             // one array per dancer (rigged chars have multiple)
        PhaseAnimatorSync[] phaseSyncs;     // null entries when using fallback capsules
        int[] clipBeatsPerLoop;             // raw library value per dancer (before speed scale)
        LinenSkirt[] skirts;                // per-dancer skirt sway helper
        int[] localSubsetBuffer;
        bool useRigged;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public Vector3 PlayerPosition { get; set; }
        public float PlayerPhase { get; set; }
        public bool PlayerActive { get; set; }

        public float RGlobal { get; private set; }
        public float RLocal { get; private set; }
        public int NLocal { get; private set; }

        public event Action Rebuilt;

        void Awake() { Rebuild(); }

        public void Rebuild()
        {
            if (dancers != null) foreach (var t in dancers) if (t) Destroy(t.gameObject);

            var kp = new KuramotoParams(
                beatOmega, beatCoupling,
                peerCoupling, peerRadius,
                playerCoupling, playerRadius);
            Simulator = new CrowdSimulator(count, floorRadius, seed, kp, freqSigma);

            useRigged = library != null && library.IsUsable;

            dancers = new Transform[count];
            swayAxis = new Vector2[count];
            renderers = new Renderer[count][];
            phaseSyncs = new PhaseAnimatorSync[count];
            clipBeatsPerLoop = new int[count];
            skirts = new LinenSkirt[count];
            mpb = new MaterialPropertyBlock();
            localSubsetBuffer = new int[count];

            var spawnRng = new System.Random(seed ^ 0x5a5a5a5a);
            var fallbackMat = useRigged ? null : MakeDancerMaterial();

            for (int i = 0; i < count; i++)
            {
                GameObject go;
                if (useRigged) SpawnRigged(i, spawnRng, out go);
                else SpawnCapsule(i, fallbackMat, out go);

                go.name = $"Dancer_{i:000}";
                go.transform.SetParent(transform, false);
                dancers[i] = go.transform;

                float a = (float)(spawnRng.NextDouble() * Math.PI * 2.0);
                swayAxis[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                go.transform.localPosition = new Vector3(
                    Simulator.PosXZ[2 * i], 0f, Simulator.PosXZ[2 * i + 1]);
                if (useRigged)
                {
                    // Random facing so the crowd doesn't all look one way.
                    go.transform.localRotation = Quaternion.Euler(
                        0f, (float)(spawnRng.NextDouble() * 360.0), 0f);
                }
            }

            Rebuilt?.Invoke();
        }

        void SpawnCapsule(int i, Material mat, out GameObject go)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
            go.transform.localScale = new Vector3(dancerScale, dancerScale, dancerScale);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            renderers[i] = new[] { r };
        }

        void SpawnRigged(int i, System.Random rng, out GameObject go)
        {
            var prefab = library.characterPrefabs[rng.Next(library.characterPrefabs.Length)];
            go = Instantiate(prefab);

            // Height variation.
            float s = Mathf.Lerp(library.minScale, library.maxScale, (float)rng.NextDouble());
            go.transform.localScale = new Vector3(s, s, s);

            var animator = go.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                var clipEntry = library.danceClips[rng.Next(library.danceClips.Length)];
                var sync = go.AddComponent<PhaseAnimatorSync>();
                sync.animator = animator;
                sync.clip = clipEntry.clip;
                // Auto-derive beatsPerLoop from clip length + music BPM so
                // Mixamo clips play at their natural recorded tempo when the
                // Kuramoto phase advances at the music beat rate. Ignore any
                // stale beatsPerLoop from older library generations.
                int autoBeats = Mathf.Max(1,
                    Mathf.RoundToInt(clipEntry.clip.length * SongLibrary.DemoBPM / 60f));
                sync.beatsPerLoop = Mathf.Max(1, Mathf.RoundToInt(autoBeats * danceSpeedScale));
                phaseSyncs[i] = sync;
                clipBeatsPerLoop[i] = autoBeats;
            }

            // Collect skinned mesh renderers (for tinting via MPB) and disable
            // per-dancer shadows — the crowd is dense, shadows are noise.
            var smrList = new List<Renderer>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smrList.Add(smr);
            }
            renderers[i] = smrList.ToArray();

            // Procedural linen skirt.
            if (library.spawnSkirt)
            {
                var hip = FindBone(go.transform, library.skirtParentBone);
                if (hip != null)
                {
                    var palette = library.palettes.Length > 0
                        ? library.palettes[rng.Next(library.palettes.Length)]
                        : new DancerLibrary.OutfitPalette { linen = Color.white };
                    skirts[i] = LinenSkirt.Attach(hip, library, palette.linen);
                }
            }
        }

        static Transform FindBone(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var hit = FindBone(c, name);
                if (hit != null) return hit;
            }
            return null;
        }

        static Material MakeDancerMaterial()
        {
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            var shader = urp != null ? urp : Shader.Find("Standard");
            var m = new Material(shader) { name = "Dancer" };
            if (m.HasProperty("_EmissionColor")) m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.0f);
            return m;
        }

        void Update()
        {
            if (Simulator == null) return;
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);

            if (Music != null) Simulator.SetBeatOmega(Music.BpmOmega);

            KuramotoMath.ExternalInfluence[] extras = null;
            if (Robot != null && Robot.CrowdInfluence.Active)
            {
                extrasBuf[0] = Robot.CrowdInfluence;
                extras = extrasBuf;
            }

            Simulator.Step(
                dt,
                PlayerPosition.x, PlayerPosition.z,
                PlayerPhase, PlayerActive,
                extras: extras);

            var subset = localSubsetBuffer;
            int n = Simulator.OrderLocal(
                PlayerPosition.x, PlayerPosition.z, playerRadius,
                subset, out float rLocal, out _);
            NLocal = n;
            RLocal = rLocal;
            RGlobal = Simulator.OrderGlobal();

            ApplyVisuals();
        }

        void ApplyVisuals()
        {
            var theta = Simulator.Theta;
            var pos = Simulator.PosXZ;
            float rp = playerRadius;
            float px = PlayerPosition.x, pz = PlayerPosition.z;
            float pp = PlayerPhase;
            for (int i = 0; i < count; i++)
            {
                float t = theta[i];

                if (useRigged)
                {
                    // Real animation drives vertical/lateral motion — hold
                    // the base spawn position, just pass phase to the sync.
                    dancers[i].localPosition = new Vector3(
                        pos[2 * i], 0f, pos[2 * i + 1]);
                    if (phaseSyncs[i] != null)
                    {
                        phaseSyncs[i].Phase = t;
                        // Push current speed scale each frame so the slider
                        // is live-tunable without re-Play.
                        phaseSyncs[i].beatsPerLoop = Mathf.Max(1,
                            Mathf.RoundToInt(clipBeatsPerLoop[i] * danceSpeedScale));
                    }
                }
                else
                {
                    float bob = 0.5f * bobHeight * (1f - Mathf.Cos(t));
                    float sway = swayAmplitude * Mathf.Sin(t * 0.5f);
                    var axis = swayAxis[i];
                    dancers[i].localPosition = new Vector3(
                        pos[2 * i] + axis.x * sway,
                        bob,
                        pos[2 * i + 1] + axis.y * sway);
                }

                float pulse = 0.5f + 0.5f * Mathf.Cos(t);
                float distanceToPlayer = float.PositiveInfinity;
                if (PlayerActive)
                {
                    float dx = pos[2 * i] - px;
                    float dz = pos[2 * i + 1] - pz;
                    distanceToPlayer = Mathf.Sqrt(dx * dx + dz * dz);
                }
                float proximity = PlayerActive
                    ? Mathf.Clamp01(1f - distanceToPlayer / rp)
                    : 0f;
                float phaseAffinity = PlayerActive
                    ? 0.5f + 0.5f * Mathf.Cos(t - pp)
                    : 0f;
                float lockFactor = proximity * phaseAffinity;

                var baseC = Color.Lerp(coolBaseline, hotPlayerColor, lockFactor);
                float emit = Mathf.Lerp(baseEmission, lockedEmission, lockFactor) * pulse;
                var emitC = baseC * emit;

                var rs = renderers[i];
                for (int k = 0; k < rs.Length; k++)
                {
                    var r = rs[k];
                    r.GetPropertyBlock(mpb);
                    var sm = r.sharedMaterial;
                    if (sm != null)
                    {
                        if (sm.HasProperty(BaseColorId)) mpb.SetColor(BaseColorId, baseC);
                        if (sm.HasProperty(ColorId)) mpb.SetColor(ColorId, baseC);
                        if (sm.HasProperty(EmissionColorId)) mpb.SetColor(EmissionColorId, emitC);
                    }
                    r.SetPropertyBlock(mpb);
                }
            }
        }
    }
}
