using UnityEngine;

namespace TimeTheft
{
    // H2 단일 모델 검증용 표시부. Match/게임 입력과 연결하지 않는다.
    public sealed class H2PocketWatchView : MonoBehaviour
    {
        [SerializeField] private Transform hourHand, minuteHand, lidPivot;
        [SerializeField] private Renderer dial, lid;
        [SerializeField] private Material normalDial, redDial, normalLid, redLid;
        [SerializeField] private Quaternion hourRest = Quaternion.identity;
        [SerializeField] private Quaternion minuteRest = Quaternion.identity;
        [SerializeField] private Quaternion lidRest = Quaternion.identity;

        public Transform HourHand => hourHand;
        public Transform MinuteHand => minuteHand;
        public Transform LidPivot => lidPivot;

        public void SetTime(int hours, int minutes)
        {
            // FBX의 축 변환은 임포터가 처리했다. 보존한 local 축에서 회전한다.
            hourHand.localRotation = hourRest * Quaternion.AngleAxis(30f * (hours % 12 + minutes / 60f), Vector3.forward);
            minuteHand.localRotation = minuteRest * Quaternion.AngleAxis(6f * minutes, Vector3.forward);
        }

        public void SetLidAngle(float degrees)
        {
            lidPivot.localRotation = lidRest * Quaternion.AngleAxis(Mathf.Clamp(degrees, 0f, 115f), Vector3.up);
        }

        public void SetRed(bool red)
        {
            dial.sharedMaterial = red ? redDial : normalDial;
            lid.sharedMaterial = red ? redLid : normalLid;
        }
    }
}
