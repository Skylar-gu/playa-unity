using UnityEngine;

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
        public string hint = "walk in · pick a song at the booth · watch the robot dance";

        Texture2D ringTex;
        GUIStyle hintStyle;
        GUIStyle numStyle;
        GUIStyle songTitleStyle;
        GUIStyle askStyle;

        void Awake()
        {
            if (floor == null) floor = FindFirstObjectByType<DanceFloor>();
            if (player == null) player = FindFirstObjectByType<PlayerRig>();
            if (ignition == null) ignition = FindFirstObjectByType<IgnitionController>();
            if (music == null) music = FindFirstObjectByType<MusicBeat>();
            if (robot == null) robot = FindFirstObjectByType<RobotDancer>();
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

            // Song banner (top center)
            if (music != null)
            {
                var s = music.CurrentSong;
                var songText = $"♪ {s.title} · {s.vibe} · {SongLibrary.DemoBPM:F0} BPM";
                var sSize = songTitleStyle.CalcSize(new GUIContent(songText));
                var prev = GUI.color;
                GUI.color = s.accent;
                GUI.Label(
                    new Rect((Screen.width - sSize.x) * 0.5f, pad, sSize.x, sSize.y),
                    songText, songTitleStyle);
                GUI.color = prev;
            }

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

            // Hint (bottom center)
            var size = hintStyle.CalcSize(new GUIContent(hint));
            GUI.Label(
                new Rect((Screen.width - size.x) * 0.5f, Screen.height - 42f, size.x, size.y),
                hint, hintStyle);

            // Phase ring (top-right)
            DrawPhaseRing(new Rect(Screen.width - 96 - pad, pad, 96, 96));
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
