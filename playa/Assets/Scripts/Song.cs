using UnityEngine;

namespace Playa
{
    // A song is just a vibe label for the demo — all songs share one BPM so
    // the crowd/robot pipeline is validated for demo (per user brief).
    // Swapping tracks is cosmetic and never destabilises the sim.
    [System.Serializable]
    public struct Song
    {
        public string title;
        public string vibe;
        public Color accent;  // used to tint the booth glow when this song is playing
        public AudioClip clip; // optional; may be null in P0 (silent metronome only)
    }

    // The one BPM everyone dances to. 124 sits inside the "ecstatic-dance"
    // range (roughly 115–128); at 124 the beat is comfortably visible and
    // K_c ≈ 1.12 at σ=0.7 keeps the crowd tunably sub-critical.
    public static class SongLibrary
    {
        public const float DemoBPM = 124f;
        public const float DemoOmega = 2f * Mathf.PI * DemoBPM / 60f; // ≈ 12.98 rad/s

        public static readonly Song[] Presets = new[]
        {
            new Song {
                title = "Meditative",
                vibe = "slow-swell · deep space",
                accent = new Color(0.55f, 0.35f, 0.95f),
            },
            new Song {
                title = "Upbeat",
                vibe = "warm groove · hands up",
                accent = new Color(1.00f, 0.55f, 0.20f),
            },
            new Song {
                title = "Ecstatic",
                vibe = "high energy · full circle",
                accent = new Color(1.00f, 0.25f, 0.45f),
            },
            new Song {
                title = "Sunrise",
                vibe = "ambient · long tail",
                accent = new Color(0.95f, 0.65f, 0.30f),
            },
        };
    }
}
