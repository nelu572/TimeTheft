using UnityEngine;

namespace TimeTheft
{
    // 기존 검증 조작에 A/B 비교와 정상 속도 동작을 추가한다. 게임 씬에는 연결하지 않는다.
    [DefaultExecutionOrder(-500)]
    public sealed class H2C2ComparisonStage : MonoBehaviour
    {
        [SerializeField] private H2C2MaterialBinding binding;
        [SerializeField] private H2PocketWatchView watch;
        [SerializeField] private Camera reviewCamera;
        [SerializeField] private Light movingLight, fillLight;
        [SerializeField] private Texture2D blenderReference;
        private float startTime;
        private Quaternion restRotation;
        private bool animate;
        private bool previousRealtimeProbes;
        private H2WatchValidationStage modelControls;
        public bool Animate { get => animate; set { animate = value; startTime = Time.time; } }
        public H2C2MaterialBinding Binding => binding;
        public bool HideGui { get; set; }
        public string Segment { get; private set; } = "Static comparison";

        private void OnEnable()
        {
            previousRealtimeProbes = QualitySettings.realtimeReflectionProbes;
            QualitySettings.realtimeReflectionProbes = true;
        }
        private void OnDisable() { QualitySettings.realtimeReflectionProbes = previousRealtimeProbes; }
        private void Start()
        {
            restRotation = watch.transform.rotation;
            modelControls = FindFirstObjectByType<H2WatchValidationStage>();
            var probe = FindFirstObjectByType<ReflectionProbe>();
            if (probe) probe.RenderProbe();
        }
        private void Update() { if (animate) EvaluateDemo((Time.time - startTime) % 24f); }

        public void SetLight(int side)
        {
            movingLight.transform.position = side == 0 ? new Vector3(-1.2f, 1.3f, .3f) : side == 1 ? new Vector3(1.2f, 1.3f, .3f) : new Vector3(0, 1.7f, .15f);
        }

        public void EvaluateDemo(float seconds)
        {
            float t = Mathf.Clamp(seconds, 0f, 24f);
            var motion = watch.transform.Find("H2_Watch/MotionRoot");
            motion.localPosition = Vector3.zero; motion.localRotation = Quaternion.identity;
            watch.transform.rotation = restRotation;
            watch.SetTime(10, 10);
            float opening = 115f;
            movingLight.transform.position = new Vector3(-1.1f, 1.25f, .55f);
            Vector3 target = new Vector3(.15f, .1f, 0);
            Vector3 offset = new Vector3(-1.35f, 1.9f, 1.6f);
            if (t < 8)
            {
                Segment = "Moving point light / 20 degrees per second";
                float a = Mathf.Lerp(-80f, 80f, t / 8) * Mathf.Deg2Rad;
                movingLight.transform.position = new Vector3(Mathf.Sin(a) * 1.5f, 1.2f, -Mathf.Cos(a) * 1.1f);
            }
            else if (t < 16)
            {
                Segment = "Watch rotation / 20 degrees per second";
                watch.transform.rotation = Quaternion.Euler(0, (t - 8) * 20, 0) * restRotation;
            }
            else
            {
                Segment = "Camera orbit / red inner lid / independent hands and hinge";
                offset = Quaternion.Euler(0, (t - 16) * 12, 0) * offset;
                watch.SetTime(2, 40);
                if (t >= 20)
                {
                    opening = Mathf.Abs(Mathf.Cos((t - 20) * Mathf.PI / 4)) * 115;
                    movingLight.transform.position = new Vector3(1.25f, 1.5f, -1.2f);
                }
            }
            if (modelControls) modelControls.SetAppearance(opening, t >= 18f);
            else { watch.SetLidAngle(opening); watch.SetRed(t >= 18f); }
            reviewCamera.transform.position = target + offset;
            reviewCamera.transform.LookAt(target);
        }

        private void OnGUI()
        {
            if (HideGui) return;
            GUILayout.BeginArea(new Rect(Screen.width - 330, 16, 314, 350), GUI.skin.box);
            GUILayout.Label("A / URP LIT   B / LIVE C2   C / BLENDER");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("A: Lit")) binding.SetC2(false);
            if (GUILayout.Button("B: C2")) binding.SetC2(true);
            GUILayout.EndHorizontal();
            GUILayout.Label(binding.C2Enabled ? "B: actual mesh PBR -> UV48 / UV96 -> palette39" : "A: original temporary URP Lit");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Left light")) { animate = false; SetLight(0); }
            if (GUILayout.Button("Right")) { animate = false; SetLight(1); }
            if (GUILayout.Button("Top")) { animate = false; SetLight(2); }
            GUILayout.EndHorizontal();
            if (GUILayout.Button(animate ? "Stop realtime motion" : "Play 24s realtime comparison")) Animate = !animate;
            GUILayout.Label(Segment);
            GUILayout.Label("C: delivered Blender C2 shuffle frame");
            if (blenderReference) GUILayout.Label(blenderReference, GUILayout.Width(296), GUILayout.Height(166));
            GUILayout.Label("C has its original scene/camera. A and B share this Unity scene.");
            GUILayout.EndArea();
        }
    }
}
