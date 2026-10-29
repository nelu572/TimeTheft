#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace TimeTheft.Visuals
{
    // One-shot paired captures from the actual Game View, including overlay UI.
    public sealed class VisualStyleCapture : MonoBehaviour
    {
        public string Status { get; private set; } = "Idle";
        public Action<bool> ComparisonStyle { get; set; }
        public string OutputDirectory { get; set; } = "VisualReview";
        public void Record() => StartCoroutine(Capture());
        public void RecordTable() => StartCoroutine(CaptureTable());
        private IEnumerator CaptureTable()
        {
            var controller = FindFirstObjectByType<TimeTheftGameController>();
            var style = FindFirstObjectByType<TimeTheftVisualStyle>();
            string folder = Path.Combine(Application.dataPath, "../" + OutputDirectory); Directory.CreateDirectory(folder);
            Status = "Waiting for table view";
            controller.RestartGame(); float deadline = Time.realtimeSinceStartup + 60;
            while (controller.Match == null || controller.Match.Stage != TurnStage.AwaitingItems || controller.View.BatchReveal.IsRunning)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(Status);
                yield return null;
            }
            controller.StopAllCoroutines(); yield return new WaitForSecondsRealtime(1.5f);
            yield return Pair(style, folder, "01-Table");
            Status = "Complete: table comparison, 1280x720";
        }
        private IEnumerator Capture()
        {
            var controller = FindFirstObjectByType<TimeTheftGameController>();
            var style = FindFirstObjectByType<TimeTheftVisualStyle>();
            string folder = Path.Combine(Application.dataPath, "../" + OutputDirectory);
            Directory.CreateDirectory(folder);
            Status = "Waiting for open watch view";
            controller.RestartGame();
            float deadline = Time.realtimeSinceStartup + 45;
            while (controller.View.BatchReveal.Phase != H2BatchRevealPresentation.RevealPhase.Observing)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(Status);
                yield return null;
            }
            controller.StopAllCoroutines();
            yield return Pair(style, folder, "02-Watch");
            Status = "Waiting for table view";
            controller.RestartGame(); deadline = Time.realtimeSinceStartup + 60;
            while (controller.Match == null || controller.Match.Stage != TurnStage.AwaitingItems || controller.View.BatchReveal.IsRunning)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(Status);
                yield return null;
            }
            controller.StopAllCoroutines();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Pair(style, folder, "01-Table");
            if (ComparisonStyle != null) ComparisonStyle(true); else style.SetStyle(true);
            Status = "Complete: 4 captures, 1280x720";
            Debug.Log(Status);
        }
        private IEnumerator Pair(TimeTheftVisualStyle style, string folder, string label)
        {
            var camera = style.gameCamera;
            var position = camera.transform.position; var rotation = camera.transform.rotation; float fov = camera.fieldOfView;
            foreach (bool enabled in new[] {false, true})
            {
                if (ComparisonStyle != null) ComparisonStyle(enabled); else style.SetStyle(enabled);
                // Let the pipeline and volume stack settle without advancing the stopped presentation.
                for (int i = 0; i < 5; i++) yield return null;
                yield return new WaitForEndOfFrame();
                if (Vector3.Distance(camera.transform.position, position) > .001f || Quaternion.Angle(camera.transform.rotation, rotation) > .01f || camera.fieldOfView != fov)
                    throw new InvalidOperationException("Camera moved between paired captures.");
                Canvas.ForceUpdateCanvases();
                foreach (var text in FindObjectsByType<TMPro.TextMeshProUGUI>(FindObjectsSortMode.None)) text.ForceMeshUpdate();
                yield return null;
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture.width != 1280 || texture.height != 720) throw new InvalidOperationException("Expected 1280x720 Game View, got " + texture.width + "x" + texture.height);
                File.WriteAllBytes(Path.Combine(folder, label + (enabled ? "-After.png" : "-Before.png")), texture.EncodeToPNG());
                Destroy(texture);
            }
            File.WriteAllText(Path.Combine(folder, label + "-camera.txt"), "1280x720; identical camera for before/after\nPosition: " + position.ToString("F5") + "\nRotation: " + rotation.eulerAngles.ToString("F5") + "\nFOV: " + fov);
        }
    }
}
#endif

