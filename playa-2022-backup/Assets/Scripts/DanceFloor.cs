using System;
using UnityEngine;

namespace Playa
{
    // The single manager for the crowd. Owns the pure-C# CrowdSimulator and
    // one transform per dancer. No per-agent MonoBehaviour, no per-agent
    // Animator — bob/sway/tint are driven procedurally from θ.
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

        [Header("Motion")]
        public float bobHeight = 0.22f;
        public float swayAmplitude = 0.09f;
        public float dancerScale = 0.6f;

        [Header("Look")]
        // Warm dim baseline so unlocked dancers read as SILHOUETTES against the
        // amber fog (see env-inspo image #3 — dancers as dark shapes on fire).
        public Color coolBaseline = new Color(0.14f, 0.08f, 0.05f);
        public Color hotPlayerColor = new Color(1.0f, 0.52f, 0.16f);
        [Range(0f, 2f)] public float baseEmission = 0.12f;
        [Range(0f, 8f)] public float lockedEmission = 5.5f;

        public CrowdSimulator Simulator { get; private set; }
        public RobotDancer Robot { get; set; }
        public MusicBeat Music { get; set; }

        readonly KuramotoMath.ExternalInfluence[] extrasBuf =
            new KuramotoMath.ExternalInfluence[1];

        Transform[] dancers;
        Vector2[] swayAxis;   // per-dancer unit XZ direction
        MaterialPropertyBlock mpb;
        Renderer[] renderers;
        int[] localSubsetBuffer;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        // Player state supplied by PlayerRig each frame.
        public Vector3 PlayerPosition { get; set; }
        public float PlayerPhase { get; set; }
        public bool PlayerActive { get; set; }

        // Read by HUD / IgnitionController.
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

            dancers = new Transform[count];
            swayAxis = new Vector2[count];
            renderers = new Renderer[count];
            mpb = new MaterialPropertyBlock();
            localSubsetBuffer = new int[count];

            var sharedMat = MakeDancerMaterial();
            var axisRng = new System.Random(seed ^ 0x5a5a5a5a);

            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"Dancer_{i:000}";
                if (go.TryGetComponent<Collider>(out var col)) Destroy(col);
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(dancerScale, dancerScale, dancerScale);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = sharedMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                dancers[i] = go.transform;
                renderers[i] = r;
                float a = (float)(axisRng.NextDouble() * Math.PI * 2.0);
                swayAxis[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                go.transform.localPosition = new Vector3(
                    Simulator.PosXZ[2 * i], 0f, Simulator.PosXZ[2 * i + 1]);
            }

            Rebuilt?.Invoke();
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
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f); // §12 stability clamp

            // Music becomes the authoritative beat if present.
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
                float bob = 0.5f * bobHeight * (1f - Mathf.Cos(t));
                float sway = swayAmplitude * Mathf.Sin(t * 0.5f);
                var axis = swayAxis[i];
                dancers[i].localPosition = new Vector3(
                    pos[2 * i] + axis.x * sway,
                    bob,
                    pos[2 * i + 1] + axis.y * sway);

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

                renderers[i].GetPropertyBlock(mpb);
                if (renderers[i].sharedMaterial.HasProperty(BaseColorId))
                    mpb.SetColor(BaseColorId, baseC);
                if (renderers[i].sharedMaterial.HasProperty(ColorId))
                    mpb.SetColor(ColorId, baseC);
                if (renderers[i].sharedMaterial.HasProperty(EmissionColorId))
                    mpb.SetColor(EmissionColorId, emitC);
                renderers[i].SetPropertyBlock(mpb);
            }
        }
    }
}
