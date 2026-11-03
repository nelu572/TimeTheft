using UnityEngine;

namespace TimeTheft
{
    // 전용 검증 씬에서만 쓰는 조작 패널. 기존 카메라/입력을 참조하지 않는다.
    public sealed class H2WatchValidationStage : MonoBehaviour
    {
        [SerializeField] private H2PocketWatchView watch;
        [SerializeField] private Camera reviewCamera;
        private bool red;
        private float lidAngle = 115f;

        private void Start()
        {
            watch.SetTime(10, 10);
            watch.SetLidAngle(lidAngle);
        }

        public void SetView(int view)
        {
            Vector3 target = watch.transform.Find("H2_Watch/MotionRoot").position;
            Vector3 offset = view == 0 ? new Vector3(0, 2.5f, 0) :
                view == 1 ? new Vector3(-2.3f, .35f, .05f) : new Vector3(-1.5f, 1.8f, 1.6f);
            reviewCamera.transform.position = target + offset;
            reviewCamera.transform.rotation = Quaternion.LookRotation(-offset, view == 0 ? Vector3.back : Vector3.up);
        }

        public void PlaySelect()
        {
            Vector3 target = watch.transform.position + new Vector3(.4f, 0f, .4f);
            reviewCamera.transform.position = target + new Vector3(0f, 2.8f, 2.8f);
            reviewCamera.transform.LookAt(target);
            watch.GetComponent<Animator>().Play("Select", 0, 0f);
        }

        public void SetAppearance(float angle, bool isRed)
        {
            lidAngle = Mathf.Clamp(angle, 0f, 115f); red = isRed;
            watch.SetLidAngle(lidAngle); watch.SetRed(red);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, 230, 320), GUI.skin.box);
            GUILayout.Label("H2 / MODEL CONTROLS");
            GUILayout.Label("Standalone asset validation");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("10:10")) watch.SetTime(10, 10);
            if (GUILayout.Button("2:40")) watch.SetTime(2, 40);
            if (GUILayout.Button("6:00")) watch.SetTime(6, 0);
            GUILayout.EndHorizontal();
            GUILayout.Label("Lid: " + Mathf.RoundToInt(lidAngle) + " degrees");
            float next = GUILayout.HorizontalSlider(lidAngle, 0f, 115f);
            if (!Mathf.Approximately(next, lidAngle)) { lidAngle = next; watch.SetLidAngle(lidAngle); }
            GUILayout.BeginHorizontal();
            foreach (int angle in new[] { 0, 60, 115 })
                if (GUILayout.Button(angle.ToString())) { lidAngle = angle; watch.SetLidAngle(angle); }
            GUILayout.EndHorizontal();
            bool nextRed = GUILayout.Toggle(red, "Red dial / inner lid");
            if (nextRed != red) { red = nextRed; watch.SetRed(red); }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Front")) SetView(0);
            if (GUILayout.Button("Side")) SetView(1);
            if (GUILayout.Button("Oblique")) SetView(2);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Select (0.875s)")) PlaySelect();
            if (GUILayout.Button("Reset motion"))
            {
                var animator = watch.GetComponent<Animator>();
                animator.Play("Idle", 0, 0f);
                animator.Update(0f);
                var motion = watch.transform.Find("H2_Watch/MotionRoot");
                motion.localPosition = Vector3.zero;
                motion.localRotation = Quaternion.identity;
            }
            GUILayout.EndArea();
        }
    }
}
