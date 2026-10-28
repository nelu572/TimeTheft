using System.Collections;
using UnityEngine;

namespace TimeTheft.ItemPresentation
{
    public sealed class ItemCutPresentation : MonoBehaviour
    {
        public GameObject Pieces { get; private set; }
        public bool IsBusy => routine != null;
        public bool Failed { get; private set; }
        private string token;
        private GameObject original;
        private bool originalActive;
        private ItemHatchController hatch;
        private string requestId;
        private Coroutine routine;
        private bool completed;

        public bool Impact(string id, GameObject target, GameObject proxyPrefab, Transform targetAnchor, Transform hatchAnchor, ItemHatchController targetHatch)
        {
            if (id == token || target == null || proxyPrefab == null || targetAnchor == null || targetHatch == null || hatchAnchor == null) return false;
            if (!TryBounds(target, out var targetBounds)) return false;
            Cancel();
            token = id; original = target; originalActive = target.activeSelf; hatch = targetHatch;
            completed = false;
            requestId = id + "/cut";
            Pieces = Instantiate(proxyPrefab, target.transform.position, targetAnchor.rotation);
            Pieces.SetActive(true);
            if (!TryBounds(Pieces, out var bounds))
            {
                Destroy(Pieces); Pieces = null; original = null; token = null;
                return false;
            }
            // 실제 보유 모델 크기에 균일 배율만 맞추고 두 조각의 원본 기하는 유지한다.
            float sourceSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float targetSize = Mathf.Max(targetBounds.size.x, targetBounds.size.y, targetBounds.size.z);
            if (sourceSize > .0001f) Pieces.transform.localScale *= targetSize / sourceSize;
            TryBounds(Pieces, out bounds);
            float floor = targetBounds.min.y;
            Pieces.transform.position += new Vector3(targetAnchor.position.x,floor,targetAnchor.position.z)-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
            original.SetActive(false); // 논리적인 ID 제거는 기존 Match가 확정한 상태다.
            routine = StartCoroutine(SeparateThenDrop(hatchAnchor, floor));
            return true;
        }

        private static bool TryBounds(GameObject visual, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        private IEnumerator SeparateThenDrop(Transform location, float floor)
        {
            var filters = Pieces.GetComponentsInChildren<MeshFilter>(true);
            var parts = new Transform[filters.Length];
            var start = new Vector3[filters.Length];
            var rotation = new Quaternion[filters.Length];
            var renderers = new Renderer[filters.Length];
            for (int i = 0; i < parts.Length; i++) { parts[i] = filters[i].transform;renderers[i]=filters[i].GetComponent<Renderer>();start[i] = renderers[i].bounds.center; rotation[i] = parts[i].rotation; }
            float elapsed = 0;
            while (elapsed < .22f)
            {
                float u = Mathf.Clamp01(elapsed / .22f);
                for (int i = 0; i < parts.Length; i++)
                {
                    float sign = i % 2 == 0 ? -1f : 1f;
                    parts[i].rotation = Quaternion.AngleAxis(sign * 8f * u, location.forward) * rotation[i];
                    var desiredCenter = start[i] + location.right * (.055f * sign * u) + location.forward * (.035f * sign * u);
                    parts[i].position += desiredCenter-renderers[i].bounds.center;
                    if (renderers[i].bounds.min.y < floor)
                        parts[i].position += Vector3.up * (floor - renderers[i].bounds.min.y);
                }
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            bool finished = false;
            var request = new ItemHatchRequest(requestId, HatchOperation.SingleDispose, location, new[] { Pieces }, null);
            request.Finished = success => { completed = success; Failed = !success; finished = true; };
            if (hatch.Enqueue(request)) while (!finished) yield return null;
            else Failed = true;
            routine = null;
        }

        public void Cancel()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (hatch != null && requestId != null) hatch.Cancel(requestId);
            if (Pieces != null) Destroy(Pieces);
            Pieces = null;
            if (original != null && !completed) original.SetActive(originalActive);
            original = null; token = null;
            Failed = false;
        }

        private void OnDisable() => Cancel();
    }
}
