using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using Playa.Verification;

namespace Playa
{
    // §6/§7 — consent-gated, fixed-rate CSV writer. NEVER opens a file before
    // Consent() is called from the start-screen checkbox. Session id is a
    // random 12-hex GUID prefix; no wall-clock, no account, no device id.
    [DefaultExecutionOrder(-10)]
    public sealed class CrowdTelemetry : MonoBehaviour
    {
        [Tooltip("Sample rate in Hz. §6 fixes this at 20.")]
        public float sampleHz = 20f;
        public string subdirectory = "telemetry";

        DanceFloor floor;
        RobotDancer robot;
        MusicBeat music;
        FeasibilityAuditor feasibility;
        StreamWriter writer;
        string sessionId;
        float accumulator;
        int[] localBuffer;

        public bool ConsentGiven { get; private set; }
        public string SessionId => sessionId;
        public string FilePath { get; private set; }

        void Awake()
        {
            floor = FindAnyObjectByType<DanceFloor>();
            robot = FindAnyObjectByType<RobotDancer>();
            music = FindAnyObjectByType<MusicBeat>();
            feasibility = FindAnyObjectByType<FeasibilityAuditor>();
        }

        // Called by the start-screen consent checkbox. Idempotent.
        public void Consent()
        {
            if (ConsentGiven) return;
            ConsentGiven = true;
            sessionId = Guid.NewGuid().ToString("N").Substring(0, 12);

            string dir = Path.Combine(Application.persistentDataPath, subdirectory);
            Directory.CreateDirectory(dir);
            FilePath = Path.Combine(dir, $"playa_{sessionId}.csv");
            writer = new StreamWriter(FilePath, append: false)
            {
                NewLine = "\n"
            };
            writer.WriteLine(
                "session,t,agent,x,z,vx,vz,theta,order_local,order_global," +
                "n_local,player_x,player_z,player_phase,beat_phase," +
                "robot_x,robot_z,robot_theta,robot_state,song_index,song_bpm," +
                "feas_score,feas_overall,feas_balance,worst_joint,n_violations,n_warnings");
        }

        // Public for a "revoke and delete" path; called by the settings screen.
        public void RevokeAndDelete()
        {
            Close();
            if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
                File.Delete(FilePath);
            ConsentGiven = false;
            sessionId = null;
            FilePath = null;
        }

        void OnApplicationQuit() { Close(); }
        void OnDestroy() { Close(); }

        void Close()
        {
            if (writer != null)
            {
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
        }

        void Update()
        {
            if (!ConsentGiven || floor == null || floor.Simulator == null) return;

            accumulator += Time.deltaTime;
            float period = 1f / Mathf.Max(1f, sampleHz);
            while (accumulator >= period)
            {
                accumulator -= period;
                WriteSample();
            }
        }

        void WriteSample()
        {
            var sim = floor.Simulator;
            if (localBuffer == null || localBuffer.Length < sim.Count)
                localBuffer = new int[sim.Count];

            var pos = sim.PosXZ;
            var vel = sim.VelXZ;
            var theta = sim.Theta;
            var pp = floor.PlayerPosition;
            float rGlobal = floor.RGlobal;
            float rLocal = floor.RLocal;
            int nLocal = floor.NLocal;
            float t = sim.TimeSeconds;

            var ci = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder(160);

            float rx = 0f, rz = 0f, rTheta = 0f;
            int rState = -1;
            if (robot != null)
            {
                rx = robot.Position.x; rz = robot.Position.z;
                rTheta = robot.Phase;
                rState = (int)robot.State;
            }
            int songIdx = music != null ? music.CurrentSongIndex : -1;
            float songBpm = SongLibrary.DemoBPM;

            // Feasibility snapshot — same value repeated on every agent row,
            // since it's a per-robot-per-frame quantity, not per-crowd-agent.
            float feasScore = 1f;
            int feasOverall = 0, feasBalance = 0;
            string worstJoint = "";
            int nViol = 0, nWarn = 0;
            if (feasibility != null && feasibility.IsBound)
            {
                var r = feasibility.Report;
                feasScore = r.FeasibilityScore;
                feasOverall = (int)r.Overall;
                feasBalance = (int)r.BalanceStatus;
                worstJoint = r.WorstJointName ?? "";
                for (int k = 0; k < r.Joints.Count; k++)
                {
                    var w = r.Joints[k].Worst;
                    if (w == FeasibilityStatus.Violation) nViol++;
                    else if (w == FeasibilityStatus.Warn) nWarn++;
                }
            }

            for (int i = 0; i < sim.Count; i++)
            {
                sb.Clear();
                sb.Append(sessionId).Append(',')
                  .Append(t.ToString("F3", ci)).Append(',')
                  .Append(i).Append(',')
                  .Append(pos[2 * i].ToString("F3", ci)).Append(',')
                  .Append(pos[2 * i + 1].ToString("F3", ci)).Append(',')
                  .Append(vel[2 * i].ToString("F3", ci)).Append(',')
                  .Append(vel[2 * i + 1].ToString("F3", ci)).Append(',')
                  .Append(theta[i].ToString("F4", ci)).Append(',')
                  .Append(rLocal.ToString("F4", ci)).Append(',')
                  .Append(rGlobal.ToString("F4", ci)).Append(',')
                  .Append(nLocal).Append(',')
                  .Append(pp.x.ToString("F3", ci)).Append(',')
                  .Append(pp.z.ToString("F3", ci)).Append(',')
                  .Append(floor.PlayerPhase.ToString("F4", ci)).Append(',')
                  .Append(sim.BeatPhase.ToString("F4", ci)).Append(',')
                  .Append(rx.ToString("F3", ci)).Append(',')
                  .Append(rz.ToString("F3", ci)).Append(',')
                  .Append(rTheta.ToString("F4", ci)).Append(',')
                  .Append(rState).Append(',')
                  .Append(songIdx).Append(',')
                  .Append(songBpm.ToString("F1", ci)).Append(',')
                  .Append(feasScore.ToString("F3", ci)).Append(',')
                  .Append(feasOverall).Append(',')
                  .Append(feasBalance).Append(',')
                  .Append(worstJoint).Append(',')
                  .Append(nViol).Append(',')
                  .Append(nWarn);
                writer.WriteLine(sb.ToString());
            }
        }
    }
}
