using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Playa.EditorTools
{
    // One-click native builds for itch.io upload.
    //   Playa/Build/Mac (ARM64+Intel Universal)
    //   Playa/Build/Windows (x64)
    //   Playa/Build/Both — builds Mac + Windows and zips each
    //   Playa/Build/Open Output Folder
    //
    // Outputs land in ../PlayaBuilds/{Mac,Windows}/ (sibling of the Unity
    // project folder so they don't get imported as assets) and are also
    // zipped as PlayaBuilds/{Mac,Windows}.zip ready to drag onto itch.io.
    public static class PlayaBuild
    {
        const string ProductName = "Playa";

        [MenuItem("Playa/Build/Mac (Universal)")]
        public static void BuildMac()
        {
            EnsureScene();
            var dir = PlatformDir("Mac");
            var opts = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = Path.Combine(dir, ProductName + ".app"),
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            // Best-effort universal binary; if the module isn't installed this
            // silently falls back to whatever the Build Settings arch is.
            TrySetMacArchitecture();
            var report = BuildPipeline.BuildPlayer(opts);
            if (Report(report, "Mac")) ZipFolder(dir, ZipPath("Mac"));
        }

        [MenuItem("Playa/Build/Windows (x64)")]
        public static void BuildWindows()
        {
            EnsureScene();
            var dir = PlatformDir("Windows");
            var opts = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = Path.Combine(dir, ProductName + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            if (Report(report, "Windows")) ZipFolder(dir, ZipPath("Windows"));
        }

        [MenuItem("Playa/Build/Both (Mac + Windows)")]
        public static void BuildBoth()
        {
            BuildMac();
            BuildWindows();
            EditorUtility.RevealInFinder(RootDir());
        }

        [MenuItem("Playa/Build/Open Output Folder")]
        public static void OpenOutputFolder()
        {
            var root = RootDir();
            Directory.CreateDirectory(root);
            EditorUtility.RevealInFinder(root);
        }

        // ------------------------------------------------------------------

        static string RootDir()
        {
            // Application.dataPath = <project>/Assets → parent's parent is a
            // sibling folder of the Unity project.
            var projectFolder = Directory.GetParent(Application.dataPath).FullName;
            var parent = Directory.GetParent(projectFolder).FullName;
            return Path.Combine(parent, "PlayaBuilds");
        }

        static string PlatformDir(string platform)
        {
            var p = Path.Combine(RootDir(), platform);
            if (Directory.Exists(p))
            {
                // Wipe stale files so old assets don't leak into the zip.
                Directory.Delete(p, recursive: true);
            }
            Directory.CreateDirectory(p);
            return p;
        }

        static string ZipPath(string platform)
        {
            return Path.Combine(RootDir(), platform + ".zip");
        }

        static string[] GetEnabledScenes()
        {
            var list = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && !string.IsNullOrEmpty(s.path)) list.Add(s.path);
            if (list.Count == 0)
            {
                var active = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(active.path)) list.Add(active.path);
            }
            return list.ToArray();
        }

        static void EnsureScene()
        {
            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path))
            {
                // Untitled scene — save it first so BuildPipeline has a scene.
                var scenesDir = Path.Combine(Application.dataPath, "Scenes");
                Directory.CreateDirectory(scenesDir);
                const string savePath = "Assets/Scenes/Playa.unity";
                if (!EditorSceneManager.SaveScene(active, savePath))
                    throw new Exception(
                        "Failed to save the active scene. Open a scene that has a " +
                        "PlayaBoot GameObject on it, save it, then retry the build.");
                AddSceneToBuildSettings(savePath);
                Debug.Log($"[PlayaBuild] Saved untitled scene to {savePath}.");
            }
            else
            {
                if (active.isDirty) EditorSceneManager.SaveScene(active);
                AddSceneToBuildSettings(active.path);
            }
        }

        static void AddSceneToBuildSettings(string scenePath)
        {
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path == scenePath) { EnableScene(scenePath); return; }
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.Insert(0, new EditorBuildSettingsScene(scenePath, enabled: true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void EnableScene(string scenePath)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < list.Count; i++)
                if (list[i].path == scenePath) list[i] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void TrySetMacArchitecture()
        {
            // Old cross-version API. Silently ignored if the OSX module isn't
            // installed for the current Editor.
            try
            {
                EditorUserBuildSettings.SetPlatformSettings(
                    "Standalone", "OSXUniversal", "Architecture", "x64ARM64");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayaBuild] Could not set Mac universal arch: {e.Message}. " +
                                 "Falling back to whatever Build Settings is configured with.");
            }
        }

        static bool Report(BuildReport report, string platform)
        {
            var s = report.summary;
            long mb = (long)(s.totalSize / (1024UL * 1024UL));
            Debug.Log($"[PlayaBuild] {platform}: {s.result} — {mb} MB — {s.outputPath}");
            if (s.result == BuildResult.Succeeded) return true;
            Debug.LogError($"[PlayaBuild] {platform} build did NOT succeed: {s.result}");
            return false;
        }

        static void ZipFolder(string sourceDir, string zipPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(sourceDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
            long mb = new FileInfo(zipPath).Length / (1024L * 1024L);
            Debug.Log($"[PlayaBuild] Zipped → {zipPath} ({mb} MB)");
        }
    }
}
