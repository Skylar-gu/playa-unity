using System;
using UnityEngine;

namespace Playa
{
    public enum RobotState
    {
        Observing,   // idle in crowd, dancing to beat
        Approaching, // walking toward the player
        Asking,      // stopped near player, gesturing
        Partnering,  // mutual coupling with player; orbit motion
        Recovering,  // partner released, walk back to crowd, cool down
    }

    // The robot NPC. It always targets the player (design brief: keep the
    // demo interaction unambiguous). Existence is orchestrated by a small
    // state machine so every transition is testable in isolation.
    //
    // The robot is EXTERNAL to CrowdSimulator's SoA — it has its own phase and
    // position. During Partnering it broadcasts an ExternalInfluence into the
    // sim so nearby dancers feel the pair-lock too (that's the visual "circle
    // ignites around us" moment).
    [DefaultExecutionOrder(-40)]
    public sealed class RobotDancer : MonoBehaviour
    {
        [Header("References")]
        public DanceFloor floor;
        public MusicBeat music;
        public PlayerRig player;
        public Transform visual;   // driven for bob/sway

        [Header("Timings (seconds)")]
        [Tooltip("Min/max seconds spent Observing before choosing to approach.")]
        public Vector2 observeSeconds = new Vector2(6f, 12f);
        [Tooltip("Time in Asking before giving up if player doesn't engage.")]
        public float askTimeoutSeconds = 8f;
        [Tooltip("Continuous seconds in Partnering that trigger ignition.")]
        public float partnerDwellForIgnition = 8f;
        [Tooltip("Seconds in Recovering before the robot can approach again.")]
        public float recoverSeconds = 6f;

        [Header("Motion")]
        public float walkSpeed = 1.8f;
        public float approachStopDistance = 1.6f;
        public float orbitRadius = 1.4f;
        public float orbitSecondsPerRevolution = 6f;
        public float bobHeight = 0.14f;
        public float bobFrequency = 1f;      // multiples of the beat

        [Header("Coupling")]
        [Tooltip("Robot's peer-coupling to nearby crowd (its own phase pull).")]
        public float robotPeerCoupling = 1.2f;
        [Tooltip("Robot's coupling to the beat (should be strong — machines lock).")]
        public float robotBeatCoupling = 2.8f;
        [Tooltip("Mutual coupling strength during Partnering (dominates crowd K_s).")]
        public float pairCoupling = 4.5f;
        [Tooltip("Radius over which the Partnering broadcast pulls the crowd.")]
        public float pairBroadcastRadius = 8f;
        [Tooltip("Coupling strength of the Partnering broadcast to the crowd.")]
        public float pairBroadcastCoupling = 2.4f;

        [Header("Player accept thresholds")]
        [Tooltip("How close (m) the player must stand to accept.")]
        public float acceptRadius = 2.2f;
        [Tooltip("Continuous still-and-close seconds required to accept.")]
        public float acceptDwellSeconds = 1.5f;
        [Tooltip("Player speed (m/s) below which they count as 'still'.")]
        public float stillSpeedThreshold = 0.4f;
        [Tooltip("Minimum tap count during Asking to count as engaged.")]
        public int acceptMinTaps = 2;

        [Header("Look (silhouette-then-glow, matches crowd aesthetic)")]
        public Color chassisColor = new Color(0.10f, 0.10f, 0.12f);
        public Color visorColor = new Color(0.35f, 0.85f, 1.0f);
        public Color partneringVisorColor = new Color(1.0f, 0.55f, 0.20f);
        [Range(0f, 8f)] public float visorEmission = 4.2f;

        public RobotState State { get; private set; } = RobotState.Observing;
        public float StateElapsed { get; private set; }
        public float PartnerDwellSeconds { get; private set; }
        public float Phase { get; private set; }
        public Vector3 Position => transform.position;
        public event Action<RobotState, RobotState> StateChanged;

        // Public single influence broadcast into the crowd via
        // DanceFloor.RobotInfluence — only Active when Partnering.
        public KuramotoMath.ExternalInfluence CrowdInfluence { get; private set; }

        // Bookkeeping.
        float nextObserveTargetSeconds;
        float acceptDwellAccum;
        int tapsAtAskStart;
        Vector3 lastPlayerPos;
        Vector3 approachTarget;
        Renderer visorRenderer;
        MaterialPropertyBlock mpb;
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        System.Random rng;

        void Awake()
        {
            if (floor == null) floor = FindFirstObjectByType<DanceFloor>();
            if (music == null) music = FindFirstObjectByType<MusicBeat>();
            if (player == null) player = FindFirstObjectByType<PlayerRig>();
            rng = new System.Random(unchecked(gameObject.GetInstanceID() * 397));
            mpb = new MaterialPropertyBlock();
            visorRenderer = FindVisorRenderer();
            nextObserveTargetSeconds = SampleObserveDuration();
            lastPlayerPos = player != null ? player.transform.position : Vector3.zero;
        }

        void Update()
        {
            if (floor == null || music == null || player == null) return;

            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            StateElapsed += dt;

            // Robot phase advances via its own tiny Kuramoto ODE — beat drives
            // strongly, plus partner pull when Partnering.
            float dtheta = 0f;
            dtheta += robotBeatCoupling * Mathf.Sin(music.BeatPhase - Phase);

            // Partner term: when Partnering, mutually lock to the player's tap
            // phase. If the player isn't tapping, they default-lock to the beat.
            if (State == RobotState.Partnering)
            {
                player.Tap.Sample(Time.time, out float pp, out _, out bool pActive);
                float target = pActive ? pp : music.BeatPhase;
                dtheta += pairCoupling * Mathf.Sin(target - Phase);
            }

            Phase = KuramotoMath.WrapTo4Pi(Phase + dtheta * dt);

            switch (State)
            {
                case RobotState.Observing:   TickObserving(dt); break;
                case RobotState.Approaching: TickApproaching(dt); break;
                case RobotState.Asking:      TickAsking(dt); break;
                case RobotState.Partnering:  TickPartnering(dt); break;
                case RobotState.Recovering:  TickRecovering(dt); break;
            }

            UpdateVisuals();
            UpdateBroadcast();
            lastPlayerPos = player.transform.position;
        }

        // ---- STATES ------------------------------------------------------

        void TickObserving(float dt)
        {
            // Sway in place, roughly on the beat.
            transform.position = SnapToGround(transform.position);
            if (StateElapsed >= nextObserveTargetSeconds)
            {
                approachTarget = player.transform.position;
                Transition(RobotState.Approaching);
            }
        }

        void TickApproaching(float dt)
        {
            // Head toward the player; if they wander, the target updates.
            approachTarget = player.transform.position;
            Vector3 delta = approachTarget - transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist <= approachStopDistance)
            {
                Transition(RobotState.Asking);
                return;
            }
            Vector3 dir = delta / Mathf.Max(dist, 1e-3f);
            transform.position += dir * walkSpeed * dt;
            transform.position = SnapToGround(transform.position);
            FaceTarget(approachTarget);
        }

        void TickAsking(float dt)
        {
            FaceTarget(player.transform.position);

            if (StateElapsed >= askTimeoutSeconds)
            {
                Transition(RobotState.Recovering);
                return;
            }

            Vector3 pDelta = player.transform.position - transform.position;
            pDelta.y = 0f;
            float pDist = pDelta.magnitude;
            float pSpeed = (player.transform.position - lastPlayerPos).magnitude / Mathf.Max(dt, 1e-4f);

            bool close = pDist <= acceptRadius;
            bool still = pSpeed <= stillSpeedThreshold;
            bool tapping = player.Tap.TapCount - tapsAtAskStart >= acceptMinTaps;

            if (close && still && tapping)
            {
                acceptDwellAccum += dt;
                if (acceptDwellAccum >= acceptDwellSeconds)
                {
                    Transition(RobotState.Partnering);
                    return;
                }
            }
            else
            {
                acceptDwellAccum = Mathf.Max(0f, acceptDwellAccum - dt * 0.5f);
            }
        }

        void TickPartnering(float dt)
        {
            PartnerDwellSeconds += dt;

            // Orbit around the player at a comfortable dance radius.
            float omega = 2f * Mathf.PI / Mathf.Max(0.1f, orbitSecondsPerRevolution);
            Vector3 anchor = player.transform.position;
            Vector3 offset = transform.position - anchor;
            offset.y = 0f;
            float currentAngle = Mathf.Atan2(offset.z, offset.x);
            float target = currentAngle + omega * dt;
            float x = anchor.x + Mathf.Cos(target) * orbitRadius;
            float z = anchor.z + Mathf.Sin(target) * orbitRadius;
            transform.position = SnapToGround(new Vector3(x, transform.position.y, z));
            FaceTarget(anchor);

            // Player wandered off → release.
            Vector3 pDelta = anchor - transform.position;
            pDelta.y = 0f;
            if (pDelta.magnitude > acceptRadius * 1.6f)
            {
                Transition(RobotState.Recovering);
            }
        }

        void TickRecovering(float dt)
        {
            // Walk a couple of meters away from the player, cool down, resume.
            Vector3 away = transform.position - player.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = new Vector3(1f, 0f, 0f);
            Vector3 dir = away.normalized;
            transform.position += dir * (walkSpeed * 0.5f) * dt;
            transform.position = SnapToGround(transform.position);
            if (StateElapsed >= recoverSeconds)
            {
                PartnerDwellSeconds = 0f;
                nextObserveTargetSeconds = SampleObserveDuration();
                Transition(RobotState.Observing);
            }
        }

        // ---- HELPERS -----------------------------------------------------

        void Transition(RobotState next)
        {
            var prev = State;
            State = next;
            StateElapsed = 0f;
            if (next == RobotState.Asking)
            {
                acceptDwellAccum = 0f;
                tapsAtAskStart = player != null ? player.Tap.TapCount : 0;
            }
            if (next == RobotState.Recovering)
            {
                PartnerDwellSeconds = 0f;
            }
            StateChanged?.Invoke(prev, next);
        }

        float SampleObserveDuration()
        {
            return Mathf.Lerp(observeSeconds.x, observeSeconds.y, (float)rng.NextDouble());
        }

        void FaceTarget(Vector3 targetPos)
        {
            Vector3 look = targetPos - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(look, Vector3.up),
                6f * Time.deltaTime);
        }

        Vector3 SnapToGround(Vector3 p)
        {
            p.y = 0f;
            return p;
        }

        void UpdateVisuals()
        {
            if (visual == null) return;
            // Bob to the beat — mechanical but visible.
            float bob = 0.5f * bobHeight * (1f - Mathf.Cos(Phase * bobFrequency));
            visual.localPosition = new Vector3(0f, bob, 0f);

            if (visorRenderer != null)
            {
                var c = State == RobotState.Partnering ? partneringVisorColor : visorColor;
                float pulse = 0.5f + 0.5f * Mathf.Cos(Phase);
                var emit = c * (visorEmission * (0.6f + 0.4f * pulse));
                visorRenderer.GetPropertyBlock(mpb);
                if (visorRenderer.sharedMaterial.HasProperty(EmissionColorId))
                    mpb.SetColor(EmissionColorId, emit);
                if (visorRenderer.sharedMaterial.HasProperty(BaseColorId))
                    mpb.SetColor(BaseColorId, c * 0.4f);
                if (visorRenderer.sharedMaterial.HasProperty(ColorId))
                    mpb.SetColor(ColorId, c * 0.4f);
                visorRenderer.SetPropertyBlock(mpb);
            }
        }

        void UpdateBroadcast()
        {
            var influence = new KuramotoMath.ExternalInfluence
            {
                X = transform.position.x,
                Z = transform.position.z,
                Phase = Phase,
                Coupling0 = pairBroadcastCoupling,
                Radius = pairBroadcastRadius,
                Active = State == RobotState.Partnering,
            };
            CrowdInfluence = influence;
        }

        Renderer FindVisorRenderer()
        {
            if (visual == null) return null;
            var t = visual.Find("Visor");
            return t != null ? t.GetComponent<Renderer>() : null;
        }
    }
}
