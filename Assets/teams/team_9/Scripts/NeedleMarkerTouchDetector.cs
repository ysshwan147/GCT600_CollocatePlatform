using UnityEngine;

public class NeedleMarkerTouchDetector : MonoBehaviour
{
    public Transform needleTip;               // 바늘 끝 Transform
    public Collider needleMarkerCollider;           // 바늘 Collider (필요하면 자동 지정)
    public TouchMemoManager memoManager;      // 메모 관리자

    public float touchDistance = 0.01f;       // 바늘-마커 간 거리 기준
    public float touchCooldown = 0.5f;        // 최소 터치 간격
    public int requiredTouches = 5;           // 누적 터치 횟수

    private int touchCount = 0;
    private float lastTouchTime = -999f;
    private bool isTouching = false;

    public void Initialize(Transform needleTipTrans, TouchMemoManager manager)
    {
        needleTip = needleTipTrans;
        memoManager = manager;
    }

    void Update()
    {
        if (needleTip == null || memoManager == null)
            return;

        Vector3 needlePos = needleTip.position;
        Vector3 closestPoint = needleMarkerCollider.ClosestPoint(needlePos);

        float dist = Vector3.Distance(needlePos, closestPoint);

        bool isNear = dist < touchDistance;

        // (1) 처음 들어온 순간 감지
        if (!isTouching && isNear)
        {
            isTouching = true;

            // 쿨다운 적용
            if (Time.time - lastTouchTime >= touchCooldown)
            {
                TouchDetected();
                lastTouchTime = Time.time;
            }
        }
        // (2) 손가락/바늘이 멀어진 경우
        else if (isTouching && !isNear)
        {
            isTouching = false;
        }
    }

    private void TouchDetected()
    {
        touchCount++;
        Debug.Log($"[NeedleMarker] Touch {touchCount}/{requiredTouches}");

        if (touchCount >= requiredTouches)
        {
            TriggerFinalAction();
            touchCount = 0; // 필요하면 리셋
        }
    }

    private void TriggerFinalAction()
    {
        Debug.Log("[NeedleMarker] Required touches reached! Triggering MemoManager Action.");

        // 원하는 TouchMemoManager 함수 호출
        memoManager.OnNeedleMarkerInteractionComplete(transform.position);
    }
}
