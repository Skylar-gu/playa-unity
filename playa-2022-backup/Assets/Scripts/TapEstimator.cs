using System;

namespace Playa
{
    // Tap-tempo estimator per §3.2. Deliberately Unity-free so the test suite
    // can drive it with synthetic timelines.
    //
    //   • Each tap sets θ_p ≡ 0.
    //   • ω_p = 2π / T̄, T̄ = mean of last ≤3 inter-tap intervals, T clamped to
    //     [0.15, 2.0] s (40–400 BPM).
    //   • No tap for 3 s → the player decouples (Active = false, so K_p = 0).
    public sealed class TapEstimator
    {
        public const float MinPeriod = 0.15f;   // 400 BPM ceiling
        public const float MaxPeriod = 2.00f;   //  40 BPM floor
        public const float IdleTimeout = 3.0f;  // decouple after this long

        readonly float[] intervals = new float[3];
        int intervalsFilled;   // 0..3
        int intervalsHead;     // ring buffer write index
        float lastTapTime = float.NegativeInfinity;
        int tapCount;

        public int TapCount => tapCount;
        public float LastTapTime => lastTapTime;

        // Register a tap at `time` (seconds, monotonic).
        public void Tap(float time)
        {
            if (tapCount > 0)
            {
                float interval = time - lastTapTime;
                if (interval > 0f)
                {
                    interval = Clamp(interval, MinPeriod, MaxPeriod);
                    intervals[intervalsHead] = interval;
                    intervalsHead = (intervalsHead + 1) % intervals.Length;
                    if (intervalsFilled < intervals.Length) intervalsFilled++;
                }
            }
            lastTapTime = time;
            tapCount++;
        }

        // Query the estimator at `time`.
        //   phase   — θ_p(t) wrapped to [0, 4π)
        //   omega   — ω_p (rad/s); 0 when inactive
        //   active  — true iff at least one interval has been measured and the
        //             last tap was within IdleTimeout seconds.
        public void Sample(float time, out float phase, out float omega, out bool active)
        {
            active = intervalsFilled > 0 && (time - lastTapTime) <= IdleTimeout;
            if (!active) { phase = 0f; omega = 0f; return; }

            float meanInterval = 0f;
            for (int i = 0; i < intervalsFilled; i++) meanInterval += intervals[i];
            meanInterval /= intervalsFilled;
            omega = KuramotoMath.TwoPi / meanInterval;
            phase = KuramotoMath.WrapTo4Pi(omega * (time - lastTapTime));
        }

        public void Reset()
        {
            intervalsFilled = 0;
            intervalsHead = 0;
            lastTapTime = float.NegativeInfinity;
            tapCount = 0;
            for (int i = 0; i < intervals.Length; i++) intervals[i] = 0f;
        }

        static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
