using System;
using System.IO;
using Harvestline.Unity.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Harvestline.Editor
{
    /// <summary>
    /// Batchmode entry point that builds the Android APK with zero hand-authored assets:
    /// it configures URP, creates a one-object scene carrying <see cref="SceneComposer"/>
    /// (which builds the rest of the scene at runtime), sets Player settings, and builds.
    /// Invoked by CI as <c>-buildMethod Harvestline.Editor.HarvestlineBuild.PerformAndroidBuild</c>.
    /// </summary>
    public static class HarvestlineBuild
    {
        private const string BundleId = "com.harvestline.game";
        private const string ScenePath = "Assets/Scenes/Main.unity";

        public static void PerformAndroidBuild()
        {
            string outputPath = OutputPath();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

            ConfigureUrp();
            ConfigurePlayer();
            CreateScene();

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.buildAppBundle = false; // APK, sideloadable for testing

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                locationPathName = outputPath,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[Harvestline] Build {summary.result}: {summary.totalSize} bytes -> {outputPath}");
            if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
            EditorApplication.Exit(0);
        }

        private static void ConfigureUrp()
        {
            // Create a default URP asset in-memory and make it the active pipeline so the
            // Harvestline/Palette shader (URP) renders. No pipeline asset is committed.
            var urp = UniversalRenderPipelineAsset.Create();
            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Harvestline";
            PlayerSettings.productName = "Harvestline";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);

            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.colorSpace = ColorSpace.Linear;
        }

        private static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Harvestline");
            go.AddComponent<SceneComposer>();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>
        /// Resolve the output apk path from CI args: game-ci passes <c>-customBuildPath</c>;
        /// we also accept <c>-outputPath</c>. Falls back to <c>build/Harvestline.apk</c>.
        /// </summary>
        private static string OutputPath()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-customBuildPath" || args[i] == "-outputPath")
                {
                    string p = args[i + 1];
                    if (!p.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                        p = Path.Combine(p, "Harvestline.apk");
                    return p;
                }
            }
            return "build/Harvestline.apk";
        }
    }
}
