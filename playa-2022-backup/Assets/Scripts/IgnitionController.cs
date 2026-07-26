using System;
using UnityEngine;

namespace Playa
{
    // End state — ignition when the robot successfully holds a partner dance
    // with the player for `robotPartnerDwellSeconds` continuous seconds. Falls
    // back to the original R_local-dwell condition if there's no RobotDancer in
    // the scene (so the old spec-§4 mechanic still works standalone).
    // Fires the dust burst, snaps stage lights to the robot's phase, and
    // shows an outro card with the R_local(t) trace.
    public sealed class IgnitionController : MonoBehaviour
    {
        [Header("Trigger — robot path (primary)")]
        [Tooltip("Continuous partnered seconds required to ignite.")]
        public float robotPartnerDwellSeconds = 8f;

        [Header("Trigger — fallback R_local path")]
        public float rThreshold = 0.80f;
        public int nThreshold = 8;
        public float dwellSeconds = 5.0f;

        [Header("Trace buffer")]
        public int traceCapacitySeconds = 90;

        [Header("References (optional — auto-found)")]
        public DanceFloor floor;
        public RobotDancer robot;
        public Light[] stageLights;
        public ParticleSystem dustBurst;
        public AudioSource subPulse;

        public bool Ignited { get; private set; }
        public float DwellSeconds { get; private set; }
        public float LastIgnitionAt { get; private set; } = -1f;

        // Ring buffer of (t, R_local) sampled per frame — used by the outro card
        // and by tests that want to inspect the whole run.
        public float[] TraceT { get; private set; }
        public float[] TraceR { get; private set; }
        public int TraceCount { get; private set; }
        int traceHead;
        int traceCap;

        public event Action Ignited_Event;

        void Awake()
        {
            if (floor == null) floor = FindFirstObjectByType<DanceFloor>();
            if (robot == null) robot = FindFirstObjectByType<RobotDancer>();
            traceCap = Mathf.Max(60, traceCapacitySeconds * 60);
            TraceT = new float[traceCap];
            TraceR = new float[traceCap];
        }

        void Update()
        {
            if (floor == null || floor.Simulator == null) return;

            float t = floor.Simulator.TimeSeconds;
            float r = floor.RLocal;

            // Append trace sample (ring buffer).
            TraceT[traceHead] = t;
            TraceR[traceHead] = r;
            traceHead = (traceHead + 1) % traceCap;
            if (TraceCount < traceCap) TraceCount++;

            if (Ignited)
            {
                DriveIgnitedLights();
                return;
            }

            // PRIMARY: robot partner-dance dwell.
            if (robot != null)
            {
                DwellSeconds = robot.State == RobotState.Partnering
                    ? robot.PartnerDwellSeconds
                    : 0f;
                if (DwellSeconds >= robotPartnerDwellSeconds)
                {
                    Ignite();
                }
                return;
            }

            // FALLBACK: original R_local dwell (no robot in scene).
            int n = floor.NLocal;
            bool holding = r >= rThreshold && n >= nThreshold;
            DwellSeconds = holding ? DwellSeconds + Time.deltaTime : 0f;
            if (DwellSeconds >= dwellSeconds)
            {
                Ignite();
            }
        }

        public void Ignite()
        {
            if (Ignited) return;
            Ignited = true;
            LastIgnitionAt = floor != null && floor.Simulator != null
                ? floor.Simulator.TimeSeconds
                : Time.timeSinceLevelLoad;
            if (dustBurst != null) dustBurst.Play(true);
            if (subPulse != null) { subPulse.volume = 1f; subPulse.Play(); }
            Ignited_Event?.Invoke();
        }

        void DriveIgnitedLights()
        {
            if (stageLights == null || stageLights.Length == 0) return;
            // Snap to whichever phase authored the ignition — robot's if it
            // was the partnering trigger, player's otherwise.
            float phase = robot != null ? robot.Phase : floor.PlayerPhase;
            float pulse = 0.5f + 0.5f * Mathf.Cos(phase);
            for (int i = 0; i < stageLights.Length; i++)
            {
                if (stageLights[i] == null) continue;
                stageLights[i].intensity = Mathf.Lerp(2f, 12f, pulse);
                stageLights[i].color = floor.hotPlayerColor;
            }
        }

        // Reset for a rerun without rebuilding the scene.
        public void ResetIgnition()
        {
            Ignited = false;
            DwellSeconds = 0f;
            LastIgnitionAt = -1f;
            TraceCount = 0;
            traceHead = 0;
        }

        // Copy the trace out in chronological order. Convenient for tests and
        // for the outro card polyline.
        public int CopyTraceInOrder(float[] outT, float[] outR)
        {
            int n = TraceCount;
            int start = TraceCount < traceCap ? 0 : traceHead;
            for (int i = 0; i < n; i++)
            {
                int src = (start + i) % traceCap;
                outT[i] = TraceT[src];
                outR[i] = TraceR[src];
            }
            return n;
        }

        void OnGUI()
        {
            if (!Ignited) return;
            const int w = 360, h = 140, pad = 16;
            var rect = new Rect(Screen.width - w - pad, pad, w, h);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 8, rect.y + 4, w - 16, 20),
                $"Ignition · session R_local(t)");
            DrawTrace(new Rect(rect.x + 8, rect.y + 26, w - 16, h - 34));
        }

        void DrawTrace(Rect area)
        {
            if (TraceCount < 2) return;
            var buf = new float[TraceCount];
            var tbuf = new float[TraceCount];
            int n = CopyTraceInOrder(tbuf, buf);
            float t0 = tbuf[0], t1 = tbuf[n - 1];
            float span = Mathf.Max(0.001f, t1 - t0);
            var prev = new Vector2(area.x, area.yMax - buf[0] * area.height);
            for (int i = 1; i < n; i++)
            {
                float x = area.x + area.width * (tbuf[i] - t0) / span;
                float y = area.yMax - buf[i] * area.height;
                var cur = new Vector2(x, y);
                Drawing.Line(prev, cur, Color.white);
                prev = cur;
            }
            // Threshold line at rThreshold.
            float ty = area.yMax - rThreshold * area.height;
            Drawing.Line(new Vector2(area.x, ty), new Vector2(area.xMax, ty),
                new Color(1f, 0.6f, 0.2f, 0.7f));
        }

        static class Drawing
        {
            static Texture2D pix;
            public static void Line(Vector2 a, Vector2 b, Color c)
            {
                if (pix == null)
                {
                    pix = new Texture2D(1, 1);
                    pix.SetPixel(0, 0, Color.white);
                    pix.Apply();
                }
                var m = GUI.matrix;
                Color prev = GUI.color;
                GUI.color = c;
                Vector2 d = b - a;
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                float len = d.magnitude;
                GUIUtility.RotateAroundPivot(ang, a);
                GUI.DrawTexture(new Rect(a.x, a.y - 1, len, 2), pix);
                GUI.matrix = m;
                GUI.color = prev;
            }
        }
    }
}
