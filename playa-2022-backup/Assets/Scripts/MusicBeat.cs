using System;
using UnityEngine;

namespace Playa
{
    // The music-driven beat source. Fixed BPM (SongLibrary.DemoBPM) — swapping
    // songs only changes the *title* the crowd is dancing to, not β_omega.
    // That's an intentional demo-stability property (per user brief): all
    // downstream sim behavior is validated at one BPM.
    //
    // Also stays lightweight so it can be swapped for an audio-file-backed
    // source (P1) without changes elsewhere.
    [DefaultExecutionOrder(-60)]
    public sealed class MusicBeat : MonoBehaviour
    {
        public float BpmOmega { get; private set; } = SongLibrary.DemoOmega;
        public float BeatPhase { get; private set; }   // wrapped to [0, 4π)
        public int BeatCount { get; private set; }     // integer beats since start
        public int CurrentSongIndex { get; private set; } = 0;

        public Song CurrentSong => SongLibrary.Presets[CurrentSongIndex];

        public event Action<Song> SongChanged;
        public event Action OnDownbeat;

        int lastWholeBeat;

        void Update()
        {
            float prev = BeatPhase;
            BeatPhase = KuramotoMath.WrapTo4Pi(BeatPhase + BpmOmega * Time.deltaTime);

            // 2π corresponds to one beat (θ progresses at ω = 2π·BPM/60).
            int whole = Mathf.FloorToInt(BeatPhase / KuramotoMath.TwoPi);
            if (whole != lastWholeBeat)
            {
                BeatCount++;
                OnDownbeat?.Invoke();
                lastWholeBeat = whole;
            }
        }

        public void CycleSong(int direction)
        {
            int n = SongLibrary.Presets.Length;
            CurrentSongIndex = ((CurrentSongIndex + direction) % n + n) % n;
            SongChanged?.Invoke(CurrentSong);
        }

        public void SetSong(int index)
        {
            int n = SongLibrary.Presets.Length;
            CurrentSongIndex = ((index % n) + n) % n;
            SongChanged?.Invoke(CurrentSong);
        }
    }
}
