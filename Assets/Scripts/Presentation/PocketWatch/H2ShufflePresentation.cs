using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimeTheft
{
    [DisallowMultipleComponent]
    public sealed class H2ShufflePresentation : MonoBehaviour
    {
        private const float GatherEnd = .28f, MixEnd = .72f;
        [SerializeField] private AnimationClip leftSwap, rightSwap;
        [Header("셔플 · Settings.Shuffle Duration은 최소 전체 시간")]
        [Min(.01f)] public float SecondsPerPass = 10f / 24;
        [Min(.1f)] public float DepthScale = 1;
        [SerializeField] private AnimationCurve cameraReturnEase = AnimationCurve.EaseInOut(0,0,1,1);
        private sealed class Pass { public int[] before, pairs; }
        private struct Curve { public Vector3 start, end; public Quaternion rotation; }
        private readonly List<Pass> passes = new List<Pass>();
        private ClockTokenView[] sources;
        private Vector3[] slots, motionPositions, clusterSlots;
        private float clusterDepth;
        private Vector3 clusterCenter;
        private Renderer[][] pileRenderers;
        private bool[][] rendererStates;
        private Quaternion[] motionRotations;
        private Curve leftCurve, rightCurve;
        private TimeTheftGameView view;
        private TabletopFocusCamera focus;
        private Camera presentationCamera;
        private Vector3 cameraStart;
        private Quaternion cameraRotation;
        private float cameraFov;
        public bool IsRunning { get; private set; }
        public ClockShuffle Permutation { get; private set; }
        public int PassCount => passes.Count;
        public float Progress { get; private set; }
        public ClockTokenView[] PhysicalSources => sources == null ? new ClockTokenView[0] : (ClockTokenView[])sources.Clone();
        public AnimationClip LeftSwap => leftSwap;
        public AnimationClip RightSwap => rightSwap;
        public event Action<ClockShuffle> Started, Completed;

        public bool CanPlay(TimeTheftGameView gameView)
        {
            if (!isActiveAndEnabled || !leftSwap || !rightSwap || !gameView || !gameView.FocusCamera) return false;
            var clocks = gameView.Clocks;
            foreach (var clock in clocks)
                if (!clock || !clock.H2Presentation || !clock.H2Presentation.isActiveAndEnabled || clock.transform.parent != clocks[0].transform.parent) return false;
            return clocks.Length == TimeTheftMatch.ClocksPerBatch;
        }
        public float Duration(float minimum) => Mathf.Max(.05f, minimum, 7 * SecondsPerPass);

        public void Prepare(TimeTheftGameView gameView, ClockShuffle permutation)
        {
            if (ReferenceEquals(Permutation, permutation) && view == gameView) return;
            Cancel(); view = gameView; Permutation = permutation;
            sources = view.Clocks; int count = sources.Length;
            slots = new Vector3[count]; motionPositions = new Vector3[count]; motionRotations = new Quaternion[count];
            for (int i=0;i<count;i++)
            {
                var token = sources[i]; token.CloseFrame(1); token.SetSelected(false); token.SetSelectable(false);
                slots[i] = token.transform.position;
                motionPositions[i] = token.H2Presentation.MotionRoot.localPosition;
                motionRotations[i] = token.H2Presentation.MotionRoot.localRotation;
            }
            BuildPasses();
            leftCurve = ReadCurve(leftSwap); rightCurve = ReadCurve(rightSwap);
            BuildCluster();
            focus = view.FocusCamera; presentationCamera = focus.GetComponent<Camera>();
            cameraStart = presentationCamera.transform.position; cameraRotation = presentationCamera.transform.rotation; cameraFov = presentationCamera.fieldOfView;
            focus.BeginPresentationCamera(); IsRunning = true; Progress = 0; Frame(0); Started?.Invoke(Permutation);
        }
        private Curve ReadCurve(AnimationClip clip)
        {
            var model=sources[0].H2Presentation; var root=model.MotionRoot;
            model.SampleBatchClip(clip,0); var result=new Curve{start=root.localPosition,rotation=root.localRotation};
            model.SampleBatchClip(clip,1); result.end=root.localPosition;
            root.localPosition=motionPositions[0];root.localRotation=motionRotations[0];return result;
        }
        private void BuildPasses()
        {
            passes.Clear(); int[] order={0,1,2,3,4};
            // 홀짝 인접 정렬망: 목표 슬롯 순위만 비교한다. 추가 난수/Face 조회가 없다.
            for(int phase=0;phase<5;phase++)
            {
                var before=(int[])order.Clone();var pairs=new List<int>();
                for(int i=phase%2;i<4;i+=2)
                    if(Permutation.SourceToDestination[order[i]]>Permutation.SourceToDestination[order[i+1]])
                    {pairs.Add(i);int s=order[i];order[i]=order[i+1];order[i+1]=s;}
                if(pairs.Count>0)passes.Add(new Pass{before=before,pairs=pairs.ToArray()});
            }
        }
        private void BuildCluster()
        {
            clusterCenter = Vector3.zero;
            foreach (var slot in slots) clusterCenter += slot / slots.Length;
            clusterDepth = Vector3.Distance(slots[0], slots[slots.Length - 1]) * .075f;
            clusterSlots = new Vector3[sources.Length];
            pileRenderers = new Renderer[sources.Length][]; rendererStates = new bool[sources.Length][];
            for (int s = 0; s < sources.Length; s++)
            {
                clusterSlots[s] = clusterCenter;
                pileRenderers[s] = sources[s].H2Presentation.Watch.GetComponentsInChildren<Renderer>(true);
                rendererStates[s] = new bool[pileRenderers[s].Length];
                for (int part = 0; part < pileRenderers[s].Length; part++) rendererStates[s][part] = pileRenderers[s][part].enabled;
            }
        }
        public void Frame(float progress)
        {
            if(!IsRunning)return;Progress=Mathf.Clamp01(progress);
            if (Progress < GatherEnd) { Arrange(Progress / GatherEnd, true); ShowPileRepresentative(-1); }
            else if (Progress >= MixEnd)
            { Arrange((Progress - MixEnd) / (1 - MixEnd), false); ShowPileRepresentative(Progress == MixEnd ? 0 : -1); }
            else
            {
                float mix = (Progress - GatherEnd) / (MixEnd - GatherEnd);
                for (int s = 0; s < sources.Length; s++) SetRest(s, clusterCenter);
                int representative = 0;
                if (passes.Count == 0)
                    Move(0, 0, 0, leftSwap, leftCurve, mix, 1);
                else
                {
                    float p=mix*passes.Count;int index=Mathf.Min(passes.Count-1,Mathf.FloorToInt(p));var pass=passes[index];
                    float u=Mathf.Clamp01((p-index)/.9f); // 원본 9프레임 이동 + 1프레임 정착
                    representative = pass.before[pass.pairs[0]];
                    foreach(int left in pass.pairs)
                    {
                        float mirror=left==3?-1:1;
                        Move(pass.before[left],left,left+1,leftSwap,leftCurve,u,mirror);
                        Move(pass.before[left+1],left+1,left,rightSwap,rightCurve,u,mirror);
                    }
                }
                // 다섯 실물은 동일 XYZ에 두고, 겹친 동안의 중복 렌더링만 줄여 Z-fighting을 막는다.
                ShowPileRepresentative(representative);
            }
            float t=Mathf.Clamp01(cameraReturnEase.Evaluate(Progress));
            presentationCamera.transform.SetPositionAndRotation(Vector3.Lerp(cameraStart,focus.OverviewPosition,t),Quaternion.Slerp(cameraRotation,focus.OverviewRotation,t));
            presentationCamera.fieldOfView=Mathf.Lerp(cameraFov,focus.OverviewFieldOfView,t);
            Physics.SyncTransforms();
        }
        private void Arrange(float u, bool gather)
        {
            float travel = Mathf.SmoothStep(0, 1, Mathf.Clamp01(u));
            for (int s = 0; s < sources.Length; s++)
            {
                int slot = gather ? s : Permutation.SourceToDestination[s];
                var position = Vector3.Lerp(gather ? slots[slot] : clusterSlots[slot], gather ? clusterSlots[slot] : slots[slot], travel)
                    + Vector3.forward * (Mathf.Sin(travel * Mathf.PI) * clusterDepth * (slot % 2 == 0 ? 1 : -1) * DepthScale);
                SetRest(s, position);
            }
        }
        private void ShowPileRepresentative(int representative)
        {
            if (pileRenderers == null) return;
            for (int s = 0; s < pileRenderers.Length; s++)
                for (int part = 0; part < pileRenderers[s].Length; part++)
                    if (pileRenderers[s][part]) pileRenderers[s][part].enabled = rendererStates[s][part] && (representative < 0 || representative == s);
        }
        private void SetRest(int source,Vector3 position)
        {
            var token=sources[source];token.transform.position=position;
            token.H2Presentation.MotionRoot.localPosition=motionPositions[source];token.H2Presentation.MotionRoot.localRotation=motionRotations[source];
        }
        private void Move(int source,int from,int to,AnimationClip clip,Curve curve,float u,float mirror)
        {
            var token=sources[source];var model=token.H2Presentation;var root=model.MotionRoot;
            model.SampleBatchClip(clip,u);var q=root.localRotation;
            token.transform.position=clusterCenter;
            var rotationDelta=Quaternion.Inverse(curve.rotation)*q;
            if(mirror<0)rotationDelta=Quaternion.Inverse(rotationDelta);
            root.localPosition=motionPositions[source];root.localRotation=motionRotations[source]*rotationDelta;
        }
        public void Complete()
        {
            if(!IsRunning)return;
            var destinations=new ClockTokenView[5];
            for(int destination=0;destination<5;destination++)
            {
                int source=Permutation.DestinationToSource[destination];SetRest(source,slots[destination]);destinations[destination]=sources[source];
            }
            view.BindShuffleSlots(destinations);Progress=1;Release();Completed?.Invoke(Permutation);
        }
        public void Cancel()
        {
            if(!IsRunning)return;
            var controller=GetComponent<TimeTheftGameController>();
            if(gameObject.activeInHierarchy&&controller&&controller.isActiveAndEnabled&&controller.Match!=null&&
                controller.Match.Stage==TurnStage.Shuffling&&ReferenceEquals(controller.Match.LastShuffle,Permutation))
            {Complete();return;}
            for(int i=0;i<sources.Length;i++)SetRest(i,slots[i]);
            view.BindShuffleSlots(sources);Release();
        }
        private void Release(){ShowPileRepresentative(-1);IsRunning=false;if(focus)focus.EndPresentationCamera();}
        private void OnDisable()=>Cancel();
    }
}
