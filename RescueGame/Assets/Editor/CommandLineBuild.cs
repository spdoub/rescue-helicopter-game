using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds without the editor window (for automation):
///   Unity -batchmode -quit -projectPath RescueGame -executeMethod CommandLineBuild.MacDev -logFile build.log
/// Optional: -rebuildWorld also re-runs the level/city/audio setup menus first.
/// </summary>
public static class CommandLineBuild
{
    public static void MacDev()
    {
        var args = System.Environment.GetCommandLineArgs();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype.unity");
        if (System.Array.IndexOf(args, "-rebuildWorld") >= 0)
        {
            EditorApplication.ExecuteMenuItem("Tools/Rescue Heli/Wire Helicopter Audio");
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        }
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Prototype.unity" },
            locationPathName = "/Users/samwudel/Game/Builds/Mac/RescueGame.app",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.Development,
        });
        Debug.Log($"[CommandLineBuild] {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / 1e6:F0} MB");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
