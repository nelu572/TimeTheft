#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TimeTheft.Editor
{
    // 4-E 독립 Player 검증용. Editor 설정은 빌드 종료/실패 모두 복구한다.
    public static class H2PlayerReviewBuild
    {
        public static string Status { get; private set; } = "Idle";
        public static void Begin(string executable, string reportPath)
        {
            if (Status == "Building") return;
            Status = "Building";
            EditorApplication.CallbackFunction build = null;
            build = () =>
            {
                EditorApplication.update -= build;
                bool timing = PlayerSettings.enableFrameTimingStats;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(executable));
                    PlayerSettings.enableFrameTimingStats = true;
                    var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                        scenes = new[] { "Assets/Scenes/TimeTheftGame.unity" },
                        locationPathName = executable, target = BuildTarget.StandaloneWindows64,
                        options = BuildOptions.None
                    });
                    Status = report.summary.result.ToString();
                    File.WriteAllText(reportPath, JsonUtility.ToJson(new Result {
                        result = Status, errors = report.summary.totalErrors, warnings = report.summary.totalWarnings,
                        seconds = report.summary.totalTime.TotalSeconds, bytes = report.summary.totalSize,
                        messages = Array.ConvertAll(report.steps, step => step.name + ": " +
                            string.Join("\n", Array.ConvertAll(step.messages, m => m.type + " " + m.content)))
                    }, true));
                }
                catch (Exception e) { Status = "Failed"; File.WriteAllText(reportPath, e.ToString()); Debug.LogException(e); }
                finally { PlayerSettings.enableFrameTimingStats = timing; }
            };
            EditorApplication.update += build;
            EditorApplication.QueuePlayerLoopUpdate();
        }
        [Serializable] private sealed class Result
        {
            public string result; public int errors, warnings; public double seconds; public ulong bytes;
            public string[] messages;
        }
    }
}
#endif
