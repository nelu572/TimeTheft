using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeTheft.CounterStudy
{
    public enum CounterStudyView { Front = 1, Left = 2, Right = 3, Game = 4, TablePlan = 5 }

    // 입력과 시점만 담당한다. 모든 구조는 편집 모드부터 씬에 존재한다.
    public sealed class CounterStructureStudy : MonoBehaviour
    {
        public CounterStudyModel singleCandidate;
        public float singleSideStationOffset = .55f;
        [Tooltip("가장자리 길이 방향 정렬을 유지하며 단일 후보만 테이블 안쪽으로 이동")]
        public float singleInwardOffset;
        [Header("단일 후보 본체 중심을 실제 왼쪽 가장자리 중점에 정렬")]
        public bool centerSingleOnTableEdge;
        public MeshFilter singleBodyShell;
        public CounterStudyModel ActiveModel => singleCandidate;
        public Camera studyCamera;
        public Transform cameraRig;
        public TabletopCameraLook cameraLook;
        [Header("원본 TabletopGame 카메라 값")]
        public Vector3 gamePosition = new Vector3(0, 4.25f, -4.8f);
        public Vector3 gameEuler = new Vector3(34, 0, 0);
        public float gameFov = 58;
        [Header("테이블 왼쪽 가장자리 기준 · 카메라를 향한 회전 없음")]
        public Transform tableSurface;
        public GameObject alignmentGuides;
        public float edgeInset = .95f;
        public float sideStation = .15f;
        public Vector3 DevicePosition => tableSurface.TransformPoint(new Vector3(-.5f, .5f, 0))
            + tableSurface.right * edgeInset + tableSurface.forward * sideStation;
        public Quaternion DeviceRotation => Quaternion.LookRotation(-tableSurface.right, tableSurface.up);

        public void GetLeftTableEdge(out Vector3 near, out Vector3 far)
        {
            var mesh = tableSurface.GetComponent<MeshFilter>().sharedMesh;
            Bounds bounds = mesh.bounds;
            near = tableSurface.TransformPoint(new Vector3(bounds.min.x, bounds.max.y, bounds.min.z));
            far = tableSurface.TransformPoint(new Vector3(bounds.min.x, bounds.max.y, bounds.max.z));
        }
        public CounterStudyView currentView = CounterStudyView.Game;
        public bool parallax;
        public bool showControls = true;

        private void Start() => ShowSingle(currentView);

        public void ShowSingle(CounterStudyView view)
        {
            if (!singleCandidate) return;
            currentView = view;
            cameraLook.enabled = false;
            studyCamera.orthographic = false;
            if (alignmentGuides) alignmentGuides.SetActive(view == CounterStudyView.TablePlan);
            singleCandidate.gameObject.SetActive(true);
            singleCandidate.transform.SetPositionAndRotation(DevicePosition + tableSurface.forward * singleSideStationOffset + tableSurface.right * singleInwardOffset, DeviceRotation);
            singleCandidate.ApplyDimensions();
            if (centerSingleOnTableEdge && singleBodyShell)
            {
                GetLeftTableEdge(out Vector3 near, out Vector3 far);
                Vector3 alongEdge = (far - near).normalized;
                Vector3 bodyCenter = singleBodyShell.transform.TransformPoint(singleBodyShell.sharedMesh.bounds.center);
                singleCandidate.transform.position -= alongEdge * Vector3.Dot(bodyCenter - (near + far) * .5f, alongEdge);
            }
            Vector3 position;
            Quaternion rotation;
            float fov;
            if (view == CounterStudyView.Game)
            {
                position = gamePosition;
                rotation = Quaternion.Euler(gameEuler);
                fov = gameFov;
            }
            else if (view == CounterStudyView.TablePlan)
            {
                Vector3 center = tableSurface.position;
                position = center + tableSurface.up * 12;
                rotation = Quaternion.LookRotation(-tableSurface.up, tableSurface.forward);
                fov = 58;
                studyCamera.orthographic = true;
                studyCamera.orthographicSize = 3.75f;
            }
            else
            {
                Transform point = ActiveModel.inspectionPoint;
                // 깊이 배율로 경사가 바뀌어도 실제 표면의 법선을 정면으로 본다.
                Vector3 outward = point.localToWorldMatrix.inverse.transpose.MultiplyVector(Vector3.back).normalized;
                Vector3 up = point.localToWorldMatrix.MultiplyVector(Vector3.up).normalized;
                float angle = view == CounterStudyView.Left ? -32 : view == CounterStudyView.Right ? 32 : 0;
                outward = Quaternion.AngleAxis(angle, Vector3.up) * outward;
                position = point.position + outward * 4.5f;
                rotation = Quaternion.LookRotation(point.position - position, up);
                fov = 36;
            }
            cameraRig.SetPositionAndRotation(position, rotation);
            studyCamera.transform.localPosition = Vector3.zero;
            studyCamera.transform.localRotation = Quaternion.identity;
            studyCamera.fieldOfView = fov;
            cameraLook.enabled = Application.isPlaying && parallax && view != CounterStudyView.TablePlan;
        }

        public void SetParallax(bool value)
        {
            parallax = value;
            ShowSingle(currentView);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.fKey.wasPressedThisFrame) ShowSingle(CounterStudyView.Front);
            if (keyboard.qKey.wasPressedThisFrame) ShowSingle(CounterStudyView.Left);
            if (keyboard.eKey.wasPressedThisFrame) ShowSingle(CounterStudyView.Right);
            if (keyboard.gKey.wasPressedThisFrame) ShowSingle(CounterStudyView.Game);
            if (keyboard.tKey.wasPressedThisFrame) ShowSingle(CounterStudyView.TablePlan);
            if (keyboard.pKey.wasPressedThisFrame) SetParallax(!parallax);
            if (keyboard.hKey.wasPressedThisFrame) showControls = !showControls;
        }

        private void OnGUI()
        {
            if (!showControls || !singleCandidate) return;
            float scale = Mathf.Clamp(Screen.width / 1600f, .65f, 1.5f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUILayout.BeginArea(new Rect(18, 18, 660, 235), GUI.skin.box);
            GUILayout.Label("COUNTER / VISUAL V1     |     STATIC VALUES / NO GAME LOGIC");
            GUILayout.BeginHorizontal();
            foreach (CounterStudyView view in System.Enum.GetValues(typeof(CounterStudyView)))
                if (GUILayout.Button(view.ToString())) ShowSingle(view);
            GUILayout.EndHorizontal();
            GUILayout.Label(ActiveModel.designName + "  /  " + currentView + "   |   P: mouse parallax " + (parallax ? "ON" : "OFF"));
            if (GUILayout.Button("Toggle mouse parallax (P)")) SetParallax(!parallax);
            GUILayout.Label("Size " + ActiveModel.size.ToString("F2") + "    Depth " + ActiveModel.depth.ToString("F2"));
            float size = GUILayout.HorizontalSlider(ActiveModel.size, .55f, 1.1f);
            float depth = GUILayout.HorizontalSlider(ActiveModel.depth, .65f, 1.25f);
            if (!Mathf.Approximately(size, ActiveModel.size) || !Mathf.Approximately(depth, ActiveModel.depth))
            {
                ActiveModel.size = size;
                ActiveModel.depth = depth;
                ShowSingle(currentView);
            }
            GUILayout.Label("Long axis = table side edge / fixed pose / 00:00 + 30:00 + 30:00");
            GUILayout.Label("F/Q/E: inspect   G: game   T: plan   H: hide");
            GUILayout.EndArea();
            GUI.matrix = old;
        }
    }
}
