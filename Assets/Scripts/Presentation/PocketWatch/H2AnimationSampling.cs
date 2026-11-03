using UnityEngine;

namespace TimeTheft
{
    internal static class H2AnimationSampling
    {
        public static void Sample(AnimationClip clip, GameObject target, float time)
        {
            // Player의 non-Legacy 샘플링에도 Animator가 필요하다. 자동 상태 재생은 하지 않는다.
            if (!target.GetComponent<Animator>())
            {
                var animator = target.AddComponent<Animator>();
                animator.enabled = false;
            }
            clip.SampleAnimation(target, time);
        }
    }
}
