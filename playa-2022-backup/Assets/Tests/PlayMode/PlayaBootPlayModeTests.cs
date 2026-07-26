using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Playa.Tests
{
    public class PlayaBootPlayModeTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator PlayaBoot_SpawnsCrowdAndSubsystems()
        {
            root = new GameObject("Boot");
            root.AddComponent<PlayaBoot>();
            yield return null; // let Awake / Start settle

            var floor = Object.FindFirstObjectByType<DanceFloor>();
            Assert.NotNull(floor, "DanceFloor should have been spawned.");
            Assert.NotNull(floor.Simulator, "Simulator should be initialised.");
            Assert.AreEqual(80, floor.Simulator.Count);

            var telemetry = Object.FindFirstObjectByType<CrowdTelemetry>();
            Assert.NotNull(telemetry);
            Assert.IsFalse(telemetry.ConsentGiven,
                "Telemetry must remain inactive until Consent() is called.");

            var ign = Object.FindFirstObjectByType<IgnitionController>();
            Assert.NotNull(ign);
            Assert.IsFalse(ign.Ignited);

            // New subsystems: music, robot, and DJ song picker.
            var music = Object.FindFirstObjectByType<MusicBeat>();
            Assert.NotNull(music, "MusicBeat should have been spawned.");
            var robot = Object.FindFirstObjectByType<RobotDancer>();
            Assert.NotNull(robot, "RobotDancer should have been spawned.");
            Assert.AreEqual(RobotState.Observing, robot.State,
                "Robot should start in Observing state.");
            var picker = Object.FindFirstObjectByType<SongPicker>();
            Assert.NotNull(picker, "SongPicker should have been spawned on the booth.");

            // Cross-wiring: floor knows about robot + music.
            Assert.AreSame(robot, floor.Robot);
            Assert.AreSame(music, floor.Music);
        }

        [UnityTest]
        public IEnumerator DanceFloor_UpdatesPhasesAndR()
        {
            var go = new GameObject("Floor");
            go.SetActive(false);
            var floor = go.AddComponent<DanceFloor>();
            floor.count = 32;
            floor.floorRadius = 8f;
            floor.seed = 7;
            go.SetActive(true);
            yield return null; // Awake / Rebuild

            float initialTheta = floor.Simulator.Theta[0];
            for (int i = 0; i < 8; i++) yield return null;

            Assert.AreNotEqual(initialTheta, floor.Simulator.Theta[0],
                "Simulator should have advanced phases over several frames.");
            Assert.GreaterOrEqual(floor.RGlobal, 0f);
            Assert.LessOrEqual(floor.RGlobal, 1f);
        }
    }
}
