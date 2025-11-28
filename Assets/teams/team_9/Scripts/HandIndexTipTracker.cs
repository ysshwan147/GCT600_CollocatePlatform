using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandIndexTipTracker : MonoBehaviour
{
    public OVRSkeleton skeleton;          // 오른손/왼손 OVRSkeleton 참조
    public Transform indexTip;           // 디버깅용으로 보고싶으면 public

    void Start()
    {
        StartCoroutine(WaitForSkeleton());
    }

    IEnumerator WaitForSkeleton()
    {
        // Skeleton이 초기화될 때까지 대기
        yield return new WaitUntil(() => skeleton != null && skeleton.IsInitialized);

        // Bones 리스트에서 IndexTip 찾아서 캐싱
        foreach (var bone in skeleton.Bones)
        {
            if (bone.Id == OVRSkeleton.BoneId.Hand_IndexTip ||
                bone.Id == OVRSkeleton.BoneId.XRHand_IndexTip)
            {
                indexTip = bone.Transform;
                break;
            }
        }

        if (indexTip == null)
        {
            Debug.LogError("IndexTip bone not found!");
        }
    }

    void Update()
    {
        if (indexTip == null) return;

        // 예: 이 스크립트가 붙은 오브젝트를 손가락 끝 위치에 붙여놓고 싶을 때
        transform.position = indexTip.position;
        transform.rotation = indexTip.rotation;
    }
}
