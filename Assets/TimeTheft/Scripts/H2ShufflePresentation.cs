using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimeTheft
{
    [DisallowMultipleComponent]
    public sealed class H2ShufflePresentation : MonoBehaviour
    {
        [SerializeField] private AnimationClip leftSwap, rightSwap;
        [Header("셔플 · Settings.Shuffle Duration은 최소 전체 시간")]
        [Min(.01f)] public float SecondsPerPass = 10f / 24;
        [Min(.1f)] public float DepthScale = 1;
        [SerializeField] private AnimationCurve cameraReturnEase = AnimationCurve.EaseInOut(0,0,1,1);
        private sealed class Pass { public int[] before, pairs; }
        private struct Curve { public Vector3 start, end; public Quaternion rotation; }
        private readonly List<Pass> passes = new List<Pass>();
        private ClockTokenView[] sources;
        private Vector3[] slots, motionPositions;
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
        public float Duration(float minimum) => Mathf.Max(.05f, minimum, PassCount * SecondsPerPass);

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
        public void Frame(float progress)
        {
            if(!IsRunning)return;Progress=Mathf.Clamp01(progress);
            if(passes.Count>0)
            {
                float p=Progress*passes.Count;int index=Mathf.Min(passes.Count-1,Mathf.FloorToInt(p));var pass=passes[index];
                float u=Mathf.Clamp01((p-index)/.9f); // 원본 9프레임 이동 + 1프레임 정착
                for(int slot=0;slot<5;slot++)SetRest(pass.before[slot],slots[slot]);
                foreach(int left in pass.pairs)
                {
                    float mirror=left==3?-1:1;
                    Move(pass.before[left],left,left+1,leftSwap,leftCurve,u,mirror);
                    Move(pass.before[left+1],left+1,left,rightSwap,rightCurve,u,mirror);
                }
            }
            float t=Mathf.Clamp01(cameraReturnEase.Evaluate(Progress));
            presentationCamera.transform.SetPositionAndRotation(Vector3.Lerp(cameraStart,focus.OverviewPosition,t),Quaternion.Slerp(cameraRotation,focus.OverviewRotation,t));
            presentationCamera.fieldOfView=Mathf.Lerp(cameraFov,focus.OverviewFieldOfView,t);
            Physics.SyncTransforms();
        }
        private void SetRest(int source,Vector3 position)
        {
            var token=sources[source];token.transform.position=position;
            token.H2Presentation.MotionRoot.localPosition=motionPositions[source];token.H2Presentation.MotionRoot.localRotation=motionRotations[source];
        }
        private void Move(int source,int from,int to,AnimationClip clip,Curve curve,float u,float mirror)
        {
            var token=sources[source];var model=token.H2Presentation;var root=model.MotionRoot;
            model.SampleBatchClip(clip,u);var sampled=root.localPosition;var q=root.localRotation;
            var delta=curve.end-curve.start;
            float f=Mathf.Clamp01(Vector3.Dot(sampled-curve.start,delta)/delta.sqrMagnitude);
            var sourceAxis=root.parent.TransformVector(delta);sourceAxis.y=0;sourceAxis.Normalize();
            var sourceSide=Vector3.Cross(Vector3.up,sourceAxis);
            float lane=Vector3.Dot(root.parent.TransformVector(sampled-Vector3.Lerp(curve.start,curve.end,f)),sourceSide);
            var targetAxis=slots[to]-slots[from];targetAxis.y=0;targetAxis.Normalize();
            token.transform.position=Vector3.Lerp(slots[from],slots[to],f)+Vector3.Cross(Vector3.up,targetAxis)*(lane*DepthScale*mirror);
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
        private void Release(){IsRunning=false;if(focus)focus.EndPresentationCamera();}
        private void OnDisable()=>Cancel();
    }
}
