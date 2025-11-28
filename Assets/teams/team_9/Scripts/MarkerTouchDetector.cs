using UnityEngine;

public class MarkerTouchDetector : MonoBehaviour
{
    public HandIndexTipTracker tipTracker;   // IndexTip 위치 추적 스크립트
    public Collider markerCollider;          // 이 마커에 붙은 Collider
    public TouchMemoManager memoManager;     // 메모 매니저

    public float touchDistance = 0.01f;      // 어느 정도 거리 이하면 "터치"로 볼지
    private bool isTouching = false;
    public float toggleCooldown = 3f;

    private bool isOn = false;          // ON/OFF 상태
    private float lastToggleTime = -999f;

    public void Initialize(HandIndexTipTracker tracker, TouchMemoManager manager)
    {
        tipTracker = tracker;
        memoManager = manager;

        markerCollider = GetComponent<Collider>();
        if (markerCollider == null)
        {
            markerCollider = gameObject.AddComponent<SphereCollider>();
            ((SphereCollider)markerCollider).radius = 0.01f;
            markerCollider.isTrigger = true;
        }
    }

    void Update()
    {
        if (tipTracker == null || tipTracker.indexTip == null) return;
        if (markerCollider == null) return;
        if (memoManager == null) return;

        Vector3 tipPos = tipTracker.indexTip.position;
        Vector3 closestPoint = markerCollider.ClosestPoint(tipPos);
        float dist = Vector3.Distance(tipPos, closestPoint);

        bool isNear = dist < touchDistance;

        if (!isTouching && isNear)
        {
            // 터치 시작
            isTouching = true;

            if (Time.time - lastToggleTime >= toggleCooldown)
            {
                ToggleAction(transform.position);
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
            Debug.Log("[Marker] TOGGLE ON");

            // 예: 메모 준비, UI 띄우기 등
            memoManager.ShowMemoForMarkerPosition(point);
        }
        else
        {
            // ON → OFF
            isOn = false;
            Debug.Log("[Marker] TOGGLE OFF");

            // 예: 메모 UI 닫기 또는 기타 종료 로직
            memoManager.HideMemoUI();
        }
    }
}
