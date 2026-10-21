using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fyp.CI
{
    public static class BuildScript
    {
        // Jenkins supplies a unique output directory for each build.
        public static void BuildWindows64()
        {
            try
            {
                if (Application.unityVersion != "6000.0.79f1")
                    throw new InvalidOperationException("Expected Unity 6000.0.79f1.");

                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                    throw new InvalidOperationException("Windows 64-bit build support is unavailable.");

                string output = Environment.GetEnvironmentVariable("CI_BUILD_OUTPUT");
                if (string.IsNullOrWhiteSpace(output))
                    throw new InvalidOperationException("CI_BUILD_OUTPUT is required.");

                output = Path.GetFullPath(output);
                if (Directory.Exists(output) || File.Exists(output))
                    throw new InvalidOperationException("Output already exists: " + output);

                string[] scenes = EditorBuildSettings.scenes
                    .Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
                if (scenes.Length == 0 || scenes.Any(scene => !File.Exists(scene)))
                    throw new InvalidOperationException("Enabled build scenes are missing or empty.");

                Directory.CreateDirectory(output);
                string executable = Path.Combine(output, "SuperFinalFuse.exe");
                Debug.Log("CI build scenes: " + string.Join(", ", scenes));
                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = executable,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                });

                Debug.Log($"CI build result: {report.summary.result}; errors: {report.summary.totalErrors}; duration: {report.summary.totalTime}");
                if (report.summary.result != BuildResult.Succeeded || !File.Exists(executable))
                    throw new InvalidOperationException("Windows build failed. See BuildReport messages above.");

                var info = new BuildInfo
                {
                    unityVersion = Application.unityVersion,
                    productVersion = PlayerSettings.bundleVersion,
                    target = "StandaloneWindows64",
                    commit = Environment.GetEnvironmentVariable("GIT_COMMIT") ?? "unknown",
                    buildNumber = Environment.GetEnvironmentVariable("BUILD_NUMBER") ?? "unknown",
                    builtAtUtc = DateTime.UtcNow.ToString("o"),
                    scenes = scenes
                };
                File.WriteAllText(Path.Combine(output, "build-info.json"), JsonUtility.ToJson(info, true));
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        [Serializable]
        private class BuildInfo
        {
            public string unityVersion;
            public string productVersion;
            public string target;
            public string commit;
            public string buildNumber;
            public string builtAtUtc;
            public string[] scenes;
        }
    }
}
