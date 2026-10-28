using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TimeTheft
{
    // 카메라 구도와 Overlay 좌표계를 함께 맞춘다. 씬/Prefab 연결 없이 타이틀과 게임에 적용한다.
    public sealed class GameViewport : MonoBehaviour
    {
        private sealed class ScreenUi
        {
            public Canvas Canvas;
            public CanvasScaler Scaler;
            public RectTransform Content;
            public Vector2 Reference;
        }
        private readonly List<ScreenUi> screens = new List<ScreenUi>();
        private Camera[] cameras = new Camera[0];
        private Canvas bars;
        private readonly RectTransform[] edges = new RectTransform[4];
        private float nextDiscovery;
        private static GameViewport instance;
        public static Rect ScreenRect => CalculateRect(Screen.width, Screen.height);

        public static Rect CalculateRect(int width, int height)
        {
            if (width <= 0 || height <= 0) return new Rect(0, 0, 1, 1);
            float aspect = width / (float)height;
            const float target = 16f / 9f;
            if (aspect > target)
            {
                float w = target / aspect;
                return new Rect((1 - w) * .5f, 0, w, 1);
            }
            float h = aspect / target;
            return new Rect(0, (1 - h) * .5f, 1, h);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance) return;
            instance = new GameObject("16x9 Game Viewport").AddComponent<GameViewport>();
            DontDestroyOnLoad(instance.gameObject);
        }

        private static bool IsGameScene(Scene scene) => scene.path == "Assets/Scenes/TimeTheftGame.unity" ||
            scene.path == "Assets/TimeTheft/TitleScreen/Scenes/TimeTheftTitle.unity";

        private void OnEnable()
        {
            SceneManager.sceneLoaded += SceneLoaded;
            Canvas.preWillRenderCanvases += Apply;
        }
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            Canvas.preWillRenderCanvases -= Apply;
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { Discover(); Apply(); }
        private void Update()
        {
            if (Time.unscaledTime >= nextDiscovery) { Discover(); nextDiscovery = Time.unscaledTime + .5f; }
            Apply();
        }

        private void Discover()
        {
            if (!bars)
            {
                bars = new GameObject("Black Letterbox", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                bars.transform.SetParent(transform, false);
                bars.renderMode = RenderMode.ScreenSpaceOverlay;
                bars.sortingOrder = 32760;
                for (int i = 0; i < edges.Length; i++)
                {
                    var image = new GameObject("Black Margin " + i, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                    image.transform.SetParent(bars.transform, false);
                    image.color = Color.black;
                    edges[i] = image.rectTransform;
                }
            }
            cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            screens.RemoveAll(s => !s.Canvas || !s.Content);
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay || !IsGameScene(canvas.gameObject.scene)) continue;
                bool known = false;
                foreach (var screen in screens) if (screen.Canvas == canvas) { known = true; break; }
                if (known) continue;
                var scaler = canvas.GetComponent<CanvasScaler>();
                Vector2 reference = scaler && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                    ? scaler.referenceResolution : new Vector2(1920, 1080);
                var root = new GameObject("16x9 UI Content", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
                root.SetParent(canvas.transform, false);
                screens.Add(new ScreenUi { Canvas = canvas, Scaler = scaler, Content = root, Reference = reference });
            }
        }

        private void Apply()
        {
            Rect viewport = ScreenRect;
            bool active = IsGameScene(SceneManager.GetActiveScene());
            if (bars) bars.gameObject.SetActive(active);
            if (!active) return;
            foreach (var camera in cameras)
                if (camera && IsGameScene(camera.gameObject.scene) && camera.cameraType == CameraType.Game && !camera.targetTexture)
                    camera.rect = viewport;
            foreach (var screen in screens)
            {
                if (!screen.Canvas || !screen.Content) continue;
                float scale = Mathf.Min(Screen.width * viewport.width / screen.Reference.x, Screen.height * viewport.height / screen.Reference.y);
                if (screen.Scaler)
                {
                    screen.Scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    screen.Scaler.scaleFactor = scale;
                }
                screen.Canvas.scaleFactor = scale;
                var content = screen.Content;
                content.anchorMin = viewport.min; content.anchorMax = viewport.max;
                content.offsetMin = content.offsetMax = Vector2.zero;
                // 나중에 같은 Canvas에 추가되는 안내도 동일한 좌표와 클립 영역에 넣는다.
                if (screen.Canvas.transform.childCount > 1)
                {
                    content.SetAsLastSibling();
                    while (screen.Canvas.transform.childCount > 1)
                        screen.Canvas.transform.GetChild(0).SetParent(content, false);
                }
            }
            if (!bars) return;
            SetEdge(0, Vector2.zero, new Vector2(viewport.xMin, 1));
            SetEdge(1, new Vector2(viewport.xMax, 0), Vector2.one);
            SetEdge(2, new Vector2(viewport.xMin, 0), new Vector2(viewport.xMax, viewport.yMin));
            SetEdge(3, new Vector2(viewport.xMin, viewport.yMax), new Vector2(viewport.xMax, 1));
        }
        private void SetEdge(int index, Vector2 min, Vector2 max)
        {
            edges[index].anchorMin = min; edges[index].anchorMax = max;
            edges[index].offsetMin = edges[index].offsetMax = Vector2.zero;
            edges[index].gameObject.SetActive(max.x > min.x && max.y > min.y);
        }
    }
}
