using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DownRange.Editor
{
    public static class BuildGame
    {
        public static void PerformBuild()
        {
            ValidateGame.PerformValidation();
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Tactical.unity");
            PlayerSettings.companyName = "Down Range Campaign Command";
            PlayerSettings.productName = "Down Range Tactical Resolver";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth = 1540; PlayerSettings.defaultScreenHeight = 980;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            Directory.CreateDirectory("Build");
            var options = new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Tactical.unity" }, locationPathName = "Build/DownRangeTactical.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Unity build failed: " + report.summary.result);
            Debug.Log("Built Down Range Tactical Resolver: " + Path.GetFullPath(options.locationPathName));
        }
    }
}
