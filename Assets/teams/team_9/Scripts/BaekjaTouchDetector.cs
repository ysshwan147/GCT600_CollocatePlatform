using UnityEngine;

public class BaekjaTouchDetector : MonoBehaviour
{
    public HandIndexTipTracker tipTracker;   // 위에서 만든 스크립트
    public Collider baekjaCollider;          // 백자에 붙은 Collider
    public TouchMemoManager memoManager;     // (뒤에서 설명할 매니저)

    public float touchDistance = 0.01f;      // 어느 정도 거리 이하면 "터치"로 볼지
    private bool isTouching = false;
    public float toggleCooldown = 3f;

    private bool isOn = false;          // ON/OFF 상태
    private float lastToggleTime = -999f;

    private void Start()
    {
        
    }

    void Update()
    {
        if (tipTracker == null || tipTracker.indexTip == null) return;
        if (baekjaCollider == null) return;

        Vector3 tipPos = tipTracker.indexTip.position;
        Vector3 closestPoint = baekjaCollider.ClosestPoint(tipPos);
        float dist = Vector3.Distance(tipPos, closestPoint);

        bool isNear = dist < touchDistance;

        if (!isTouching && isNear)
        {
            // 터치 시작
            isTouching = true;

            if (Time.time - lastToggleTime >= toggleCooldown)
            {
                ToggleAction(closestPoint);
                lastToggleTime = Time.time;
            }

        }
        else if (isTouching && dist >= touchDistance)
        {
            // 손가락이 떨어짐
            isTouching = false;
        }
    }

    private void ToggleAction(Vector3 point)
    {
        if (!isOn)
        {
            // OFF → ON
            isOn = true;
            Debug.Log("[Baekja] TOGGLE ON");

            // 예: 메모 준비, UI 띄우기 등
            memoManager.PrepareMemoAtPosition(point);
        }
        else
        {
            // ON → OFF
            isOn = false;
            Debug.Log("[Baekja] TOGGLE OFF");

            // 예: 메모 UI 닫기 또는 기타 종료 로직
            memoManager.FinishMemoAtPosition();
        }
    }
}
