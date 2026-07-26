using UnityEngine;
using Playa.Verification;

namespace Playa
{
    // Minimal HUD — phase ring, sync meter, one-line hint. IMGUI so no prefab
    // wiring is required; the whole world stays procedural.
    public sealed class HUD : MonoBehaviour
    {
        public DanceFloor floor;
        public PlayerRig player;
        public IgnitionController ignition;
        public MusicBeat music;
        public RobotDancer robot;
        public FeasibilityAuditor feasibility;
        public SongPicker songPicker;
        public string hint = "walk in · pick a song at the booth · watch the robot dance";

        Texture2D ringTex;
        GUIStyle hintStyle;
        GUIStyle numStyle;
        GUIStyle songTitleStyle;
        GUIStyle askStyle;

        void Awake()
        {
            if (floor == null) floor = FindAnyObjectByType<DanceFloor>();
            if (player == null) player = FindAnyObjectByType<PlayerRig>();
            if (ignition == null) ignition = FindAnyObjectByType<IgnitionController>();
            if (music == null) music = FindAnyObjectByType<MusicBeat>();
            if (robot == null) robot = FindAnyObjectByType<RobotDancer>();
            if (feasibility == null) feasibility = FindAnyObjectByType<FeasibilityAuditor>();
            if (songPicker == null) songPicker = FindAnyObjectByType<SongPicker>();
            ringTex = new Texture2D(1, 1);
            ringTex.SetPixel(0, 0, Color.white);
            ringTex.Apply();
        }

        void OnGUI()
        {
            EnsureStyles();
            if (floor == null) return;

            float pad = 20f;
            float meterW = 320f, meterH = 14f;

            // Sync meter (top-left)
            var meterRect = new Rect(pad, pad, meterW, meterH);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(meterRect, ringTex);
            float r = Mathf.Clamp01(floor.RLocal);
            GUI.color = Color.Lerp(new Color(0.35f, 0.4f, 0.9f),
                                   floor.hotPlayerColor, r);
            GUI.DrawTexture(new Rect(meterRect.x, meterRect.y, meterRect.width * r, meterRect.height), ringTex);

            // Ignition threshold tick
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            float tickX = meterRect.x + meterRect.width * 0.80f;
            GUI.DrawTexture(new Rect(tickX - 1f, meterRect.y - 3f, 2f, meterRect.height + 6f), ringTex);
            GUI.color = Color.white;

            GUI.Label(new Rect(pad, pad + meterH + 4, 500, 20),
                $"R_local {floor.RLocal:F2}   n_local {floor.NLocal}   R_global {floor.RGlobal:F2}",
                numStyle);

            // Dwell readout
            if (ignition != null && !ignition.Ignited && ignition.DwellSeconds > 0f)
            {
                GUI.Label(new Rect(pad, pad + meterH + 24, 500, 20),
                    $"holding {ignition.DwellSeconds:F1}s / {ignition.dwellSeconds:F0}s",
                    numStyle);
            }

            // Song banner intentionally removed — HUD stays quieter. The
            // booth-nearby prompt shows the current song title when relevant.

            // Robot state (top-left, below meter)
            if (robot != null)
            {
                GUI.Label(new Rect(pad, pad + meterH + 44, 500, 20),
                    $"robot: {robot.State}"
                    + (robot.State == RobotState.Partnering
                        ? $"  ({robot.PartnerDwellSeconds:F1}s / {ignition?.robotPartnerDwellSeconds ?? 8f:F0}s)"
                        : ""),
                    numStyle);
            }

            // Ask prompt (center) when the robot wants to dance with you
            if (robot != null && robot.State == RobotState.Asking)
            {
                var ask = "🤖 wants to dance — hold still + tap SPACE to accept";
                var aSize = askStyle.CalcSize(new GUIContent(ask));
                GUI.Label(
                    new Rect((Screen.width - aSize.x) * 0.5f, Screen.height * 0.62f, aSize.x, aSize.y),
                    ask, askStyle);
            }

            // Song picker prompt (center) when the player is near the booth
            if (songPicker != null && songPicker.PlayerNearby)
            {
                var s = music != null ? music.CurrentSong.title : "";
                var msg = $"♪  ←  {s}  →   (arrow keys or scroll to change)";
                var mSize = askStyle.CalcSize(new GUIContent(msg));
                GUI.Label(
                    new Rect((Screen.width - mSize.x) * 0.5f, Screen.height * 0.62f, mSize.x, mSize.y),
                    msg, askStyle);
            }

            // Hint (bottom center)
            var size = hintStyle.CalcSize(new GUIContent(hint));
            GUI.Label(
                new Rect((Screen.width - size.x) * 0.5f, Screen.height - 42f, size.x, size.y),
                hint, hintStyle);

            // Phase ring (top-right)
            DrawPhaseRing(new Rect(Screen.width - 96 - pad, pad, 96, 96));

            // Feasibility panel (right side, below phase ring)
            if (feasibility != null && feasibility.IsBound)
                DrawFeasibilityPanel(new Rect(Screen.width - 260 - pad, pad + 108, 260, 130));
        }

        void DrawFeasibilityPanel(Rect area)
        {
            var report = feasibility.Report;
            // Background
            GUI.color = new Color(0, 0, 0, 0.35f);
            GUI.DrawTexture(area, ringTex);
            GUI.color = Color.white;

            float x = area.x + 10f, y = area.y + 8f;
            GUI.Label(new Rect(x, y, area.width, 18),
                $"robot feasibility · {report.Overall}", numStyle);
            y += 18;

            // Feasibility score bar
            float barW = area.width - 20f;
            var barRect = new Rect(x, y, barW, 8);
            GUI.color = new Color(1, 1, 1, 0.15f);
            GUI.DrawTexture(barRect, ringTex);
            GUI.color = StatusColor(report.Overall);
            GUI.DrawTexture(new Rect(x, y, barW * report.FeasibilityScore, 8), ringTex);
            GUI.color = Color.white;
            y += 14;

            // Balance line (only if it's meaningful — i.e. legged robot).
            GUI.Label(new Rect(x, y, area.width, 16),
                $"balance {report.BalanceStatus}   margin {report.BalanceMarginNormalized:F2}",
                numStyle);
            y += 14;

            // Worst joint callout.
            if (!string.IsNullOrEmpty(report.WorstJointName))
            {
                GUI.Label(new Rect(x, y, area.width, 16),
                    $"tightest: {report.WorstJointName}", numStyle);
                y += 14;
            }

            // Quick counts.
            int viol = 0, warn = 0;
            for (int i = 0; i < report.Joints.Count; i++)
            {
                var w = report.Joints[i].Worst;
                if (w == FeasibilityStatus.Violation) viol++;
                else if (w == FeasibilityStatus.Warn) warn++;
            }
            GUI.Label(new Rect(x, y, area.width, 16),
                $"{viol} violations · {warn} warnings · {report.Joints.Count} joints",
                numStyle);
        }

        Color StatusColor(FeasibilityStatus s)
        {
            switch (s)
            {
                case FeasibilityStatus.Violation: return new Color(1f, 0.35f, 0.30f);
                case FeasibilityStatus.Warn: return new Color(1f, 0.85f, 0.35f);
                default: return new Color(0.35f, 0.9f, 0.55f);
            }
        }

        void DrawPhaseRing(Rect area)
        {
            var c = area.center;
            float r = area.width * 0.45f;
            // Outer dim ring
            const int segs = 48;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * (Mathf.PI * 2f) / segs;
                float a1 = (i + 1) * (Mathf.PI * 2f) / segs;
                var p0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r;
                var p1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r;
                Line(p0, p1);
            }
            // Player-phase pip
            if (player != null)
            {
                player.Tap.Sample(Time.time, out float phase, out _, out bool active);
                if (active)
                {
                    // Phase is in [0, 4π); rewrap to [0, 2π) for a normal ring.
                    float phi = phase % (2f * Mathf.PI);
                    var p = c + new Vector2(Mathf.Cos(phi), Mathf.Sin(phi)) * r;
                    GUI.color = floor.hotPlayerColor;
                    GUI.DrawTexture(new Rect(p.x - 4, p.y - 4, 8, 8), ringTex);
                }
            }
            GUI.color = Color.white;
        }

        void Line(Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float len = d.magnitude;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - 1, len, 2), ringTex);
            GUI.matrix = m;
        }

        void EnsureStyles()
        {
            if (hintStyle == null)
            {
                hintStyle = new GUIStyle(GUI.skin.label);
                hintStyle.fontSize = 16;
                hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                hintStyle.alignment = TextAnchor.MiddleCenter;
            }
            if (numStyle == null)
            {
                numStyle = new GUIStyle(GUI.skin.label);
                numStyle.fontSize = 12;
                numStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            }
            if (songTitleStyle == null)
            {
                songTitleStyle = new GUIStyle(GUI.skin.label);
                songTitleStyle.fontSize = 20;
                songTitleStyle.fontStyle = FontStyle.Bold;
                songTitleStyle.alignment = TextAnchor.MiddleCenter;
            }
            if (askStyle == null)
            {
                askStyle = new GUIStyle(GUI.skin.label);
                askStyle.fontSize = 22;
                askStyle.fontStyle = FontStyle.Bold;
                askStyle.alignment = TextAnchor.MiddleCenter;
                askStyle.normal.textColor = new Color(1f, 0.65f, 0.25f);
            }
        }
    }
}
