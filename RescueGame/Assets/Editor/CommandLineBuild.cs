using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds without the editor window (for automation):
///   Unity -batchmode -quit -projectPath RescueGame -executeMethod CommandLineBuild.MacDev -logFile build.log
/// Optional: -rebuildWorld also re-runs the level/city/audio setup menus first;
/// -menu "Tools/..." runs editor menus first; -buildPath &lt;.app&gt; builds somewhere else (e.g. while a playtest is using the usual build).
/// </summary>
public static class CommandLineBuild
{
    public static void MacDev()
    {
        var args = System.Environment.GetCommandLineArgs();
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Prototype.unity");
        // -menu "Tools/Rescue Heli/..." (repeatable): run those editor menus first, in order
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-menu")
            {
                bool ok = EditorApplication.ExecuteMenuItem(args[i + 1]);
                Debug.Log($"[CommandLineBuild] menu '{args[i + 1]}': {(ok ? "ran" : "NOT FOUND")}");
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            }
        if (System.Array.IndexOf(args, "-rebuildWorld") >= 0)
        {
            EditorApplication.ExecuteMenuItem("Tools/Rescue Heli/Wire Helicopter Audio");
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        }
        string path = "/Users/samwudel/Game/Builds/Mac/RescueGame.app";
        int at = System.Array.IndexOf(args, "-buildPath");
        if (at >= 0 && at + 1 < args.Length) path = args[at + 1];
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Prototype.unity" },
            locationPathName = path,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.Development,
        });
        Debug.Log($"[CommandLineBuild] {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / 1e6:F0} MB");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
