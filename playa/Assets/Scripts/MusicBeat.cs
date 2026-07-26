using System;
using UnityEngine;

namespace Playa
{
    // The music-driven beat source. Fixed BPM (SongLibrary.DemoBPM) — swapping
    // songs only changes the *title* the crowd is dancing to, not β_omega.
    // Optional AudioSource: if `defaultTrack` is assigned (or if the current
    // Song has a clip), it plays looped and drives no timing — the beat is
    // still math-based so sync doesn't drift on audio buffer jitter.
    [DefaultExecutionOrder(-60)]
    [RequireComponent(typeof(AudioSource))]
    public sealed class MusicBeat : MonoBehaviour
    {
        [Tooltip("Fallback track played when the current Song has no clip. Drag any audio file (~124 BPM ideal) here.")]
        public AudioClip defaultTrack;
        [Range(0f, 1f)] public float volume = 0.7f;

        public float BpmOmega { get; private set; } = SongLibrary.DemoOmega;
        public float BeatPhase { get; private set; }   // wrapped to [0, 4π)
        public int BeatCount { get; private set; }     // integer beats since start
        public int CurrentSongIndex { get; private set; } = 0;

        public Song CurrentSong => SongLibrary.Presets[CurrentSongIndex];

        public event Action<Song> SongChanged;
        public event Action OnDownbeat;

        AudioSource source;
        int lastWholeBeat;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // 2D — plays regardless of camera position
            RefreshTrack();
        }

        void Update()
        {
            source.volume = volume;
            RefreshTrack();   // cheap no-op unless clip changed at runtime
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

        void RefreshTrack()
        {
            var clip = CurrentSong.clip != null ? CurrentSong.clip : defaultTrack;
            if (source.clip == clip) return;
            source.clip = clip;
            if (clip != null) source.Play();
            else source.Stop();
        }

        public void CycleSong(int direction)
        {
            int n = SongLibrary.Presets.Length;
            CurrentSongIndex = ((CurrentSongIndex + direction) % n + n) % n;
            SongChanged?.Invoke(CurrentSong);
            RefreshTrack();
        }

        public void SetSong(int index)
        {
            int n = SongLibrary.Presets.Length;
            CurrentSongIndex = ((index % n) + n) % n;
            SongChanged?.Invoke(CurrentSong);
            RefreshTrack();
        }
    }
}
