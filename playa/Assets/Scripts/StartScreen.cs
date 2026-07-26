using UnityEngine;

namespace Playa
{
    // Start-screen modal that also owns the consent checkbox. §7 says
    // CrowdTelemetry.Consent() is only ever called from this checkbox.
    public sealed class StartScreen : MonoBehaviour
    {
        public bool visible = true;
        public bool consentChecked = false;

        CrowdTelemetry telemetry;
        PlayerRig player;
        GUIStyle titleStyle, bodyStyle, buttonStyle, ctaStyle;
        CursorLockMode wasCursorLock;

        void Awake()
        {
            telemetry = FindAnyObjectByType<CrowdTelemetry>();
            player = FindAnyObjectByType<PlayerRig>();
            // Freeze player until dismissed.
            if (player != null) player.enabled = false;
            wasCursorLock = Cursor.lockState;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnGUI()
        {
            if (!visible) return;
            EnsureStyles();

            // Dim the background.
            var full = new Rect(0, 0, Screen.width, Screen.height);
            GUI.color = new Color(0, 0, 0, 0.72f);
            var pix = Texture2D.whiteTexture;
            GUI.DrawTexture(full, pix);
            GUI.color = Color.white;

            const float w = 520f, h = 320f;
            var box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            // Draw modal as filled colour rect — Unity 6 + URP's default IMGUI
            // GUI.skin.box texture renders as pink here.
            GUI.color = new Color(0.10f, 0.08f, 0.09f, 0.92f);
            GUI.DrawTexture(box, pix);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 24, box.y + 20, w - 48, 40), "PLAYA", titleStyle);
            GUI.Label(new Rect(box.x + 24, box.y + 64, w - 48, 20),
                "tap SPACE on a steady beat · pull the circle into sync", bodyStyle);

            GUI.Label(new Rect(box.x + 24, box.y + 108, w - 48, 100),
                "Motion telemetry (position and tap times) may be written locally for " +
                "research use. VR motion of this kind has been shown to be uniquely " +
                "identifying — the file is treated as pseudo-biometric, stays on this " +
                "machine, and is keyed only by a random session id.",
                bodyStyle);

            var checkRect = new Rect(box.x + 24, box.y + 212, 22, 22);
            var boxCol = consentChecked ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 1f, 1f, 0.3f);
            GUI.color = boxCol;
            GUI.DrawTexture(checkRect, pix);
            GUI.color = Color.white;
            GUI.Label(new Rect(checkRect.xMax + 8, checkRect.y - 2, w - 100, 28),
                "record telemetry locally (opt-in)", bodyStyle);
            if (GUI.Button(new Rect(checkRect.x, checkRect.y, w - 48, 26),
                    GUIContent.none, GUIStyle.none))
            {
                consentChecked = !consentChecked;
            }

            if (GUI.Button(new Rect(box.x + 24, box.y + h - 60, w - 48, 40),
                    "walk onto the playa  →", ctaStyle))
            {
                Dismiss();
            }
        }

        void Dismiss()
        {
            if (consentChecked && telemetry != null) telemetry.Consent();
            visible = false;
            if (player != null) player.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void EnsureStyles()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label);
                titleStyle.fontSize = 34;
                titleStyle.normal.textColor = new Color(1f, 0.62f, 0.22f);
            }
            if (bodyStyle == null)
            {
                bodyStyle = new GUIStyle(GUI.skin.label);
                bodyStyle.fontSize = 14;
                bodyStyle.normal.textColor = new Color(1f, 1f, 1f, 0.86f);
                bodyStyle.wordWrap = true;
            }
            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button);
                buttonStyle.fontSize = 14;
                ClearBackgrounds(buttonStyle);
            }
            if (ctaStyle == null)
            {
                ctaStyle = new GUIStyle(GUI.skin.button);
                ctaStyle.fontSize = 18;
                ctaStyle.normal.textColor = new Color(1f, 0.62f, 0.22f);
                ctaStyle.hover.textColor = new Color(1f, 0.78f, 0.42f);
                ctaStyle.alignment = TextAnchor.MiddleCenter;
                ClearBackgrounds(ctaStyle);
            }
        }

        static void ClearBackgrounds(GUIStyle s)
        {
            // Unity 6 + URP's built-in IMGUI skin button texture renders pink.
            s.normal.background = null;
            s.hover.background = null;
            s.active.background = null;
            s.focused.background = null;
            s.onNormal.background = null;
            s.onHover.background = null;
            s.onActive.background = null;
            s.onFocused.background = null;
        }
    }
}
