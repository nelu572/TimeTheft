#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TimeTheft.Tests
{
    public sealed class H2PocketWatchPlaybackTests
    {
        [UnityTest]
        public IEnumerator SelectActuallyAnimatesAndHoldsWithoutOverwritingIndependentParts()
        {
            yield return EditorSceneManager.LoadSceneInPlayMode("Assets/TimeTheft/PocketWatch/Validation/H2WatchValidation.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var view = Object.FindFirstObjectByType<H2PocketWatchView>();
            var stage = Object.FindFirstObjectByType<H2WatchValidationStage>();
            Assert.That(Object.FindFirstObjectByType<TimeTheftGameController>(), Is.Null);
            view.SetTime(2, 40); view.SetLidAngle(60); view.SetRed(true);
            var hour = view.HourHand.localRotation; var minute = view.MinuteHand.localRotation; var lid = view.LidPivot.localRotation;
            var motion = view.transform.Find("H2_Watch/MotionRoot");
            Vector3 start = motion.localPosition;
            stage.PlaySelect();
            yield return new WaitForSeconds(.45f);
            Assert.That(Vector3.Distance(start, motion.localPosition), Is.GreaterThan(.1f));
            yield return new WaitForSeconds(.6f);
            Assert.That(motion.localPosition.magnitude, Is.GreaterThan(1f));
            Assert.That(view.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).IsName("Select"), Is.True);
            Vector3 end = motion.localPosition;
            yield return new WaitForSeconds(.25f);
            Assert.That(Vector3.Distance(end, motion.localPosition), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(hour, view.HourHand.localRotation), Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(minute, view.MinuteHand.localRotation), Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(lid, view.LidPivot.localRotation), Is.LessThan(.05f));
            Assert.That(motion.Find("VisualScale").localScale.x, Is.EqualTo(.7f));
            view.SetLidAngle(0); stage.PlaySelect();
            yield return new WaitForSeconds(1f);
            Assert.That(Quaternion.Angle(view.LidPivot.localRotation, Quaternion.identity), Is.LessThan(.05f));
        }
    }
}
#endif
