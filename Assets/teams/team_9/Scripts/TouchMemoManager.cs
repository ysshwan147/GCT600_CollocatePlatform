using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Meta.WitAi.Dictation;
using Oculus.Voice.Dictation;
using TMPro;
using UnityEngine.UI;
using Meta.WitAi.Dictation.Data;

public class TouchMemoManager : MonoBehaviour
{
    [Serializable]
    public class TouchMemo
    {
        public Vector3 position;
        public DateTime time;
        public string text;
        public GameObject decalProjector;  // 이 메모와 연결된 데칼
        public bool isSolved;
    }

    public AppDictationExperience dictation;   // Dictation 빌딩 블록
    public GameObject jar;
    public GameObject buttons;
    public GameObject loadingIcon;
    public HandIndexTipTracker tipTracker;
    public float processingDelay = 3f;
    public AudioSource audioSource;
    public List<TouchMemo> memos = new List<TouchMemo>();
    public List<GameObject> decals = new List<GameObject>();
    public int currentIndex = 0;

    public GameObject markerPrefab;            // 표시용 오브젝트 (작은 구, 아이콘 등)

    public TextMeshProUGUI debugText;          // 선택: 현재 텍스트 확인용

    [Header("Memo Display UI")]
    public GameObject memoCanvas;              // 켜고 끌 Canvas (World/Screen 아무거나)
    public TextMeshProUGUI memoText;          // 메모 내용을 띄울 TMP 텍스트
    public float memoMatchMaxDistance = 0.05f; // 마커와 메모 위치 매칭 허용 거리

    private Vector3 currentPosition;
    private bool hasPosition = false;
    private bool isRecording = false;
    private string currentText = "";



    public GameObject needleObject;               // 바늘 오브젝트
    public GameObject interactionMarkerPrefab;

    private Vector3 lastShownMemoPosition;
    private bool memoInteractionActive = false;
    private Transform cachedIndexTip;
    private GameObject activeInteractionMarker;
    public Transform needleTip;



    void Awake()
    {
        // 음성 인식 결과 이벤트 연결
        dictation.DictationEvents.OnPartialTranscription.AddListener(OnPartialTranscription);
        dictation.DictationEvents.OnFullTranscription.AddListener(OnFullTranscription);
        dictation.DictationEvents.OnDictationSessionStopped.AddListener(OnDictationSessionStopped);
    }

    private void Start()
    {
        for (int i = 0; i < decals.Count; i++)
        {
            decals[i].SetActive(false);
        }

        buttons.SetActive(false);
        needleObject.SetActive(false);

        // 메모 캔버스는 기본적으로 꺼두기
        if (memoCanvas != null)
            memoCanvas.SetActive(false);
    }

    /// <summary>
    /// 백자 터치 시, 해당 위치로 메모를 준비만 함 (자동 녹음 X)
    /// </summary>
    public void PrepareMemoAtPosition(Vector3 worldPos)
    {
        currentPosition = worldPos;
        hasPosition = true;
        Debug.Log($"[Memo] Position selected: {worldPos}");

        //if (debugText != null)
            //debugText.text = $"Ready at: {worldPos}";

        buttons.SetActive(true);
    }


    public void FinishMemoAtPosition()
    {
        ClearData();
    }

    /// <summary>
    /// Start 버튼
    /// </summary>
    public void OnStartButton()
    {
        if (!hasPosition)
        {
            Debug.LogWarning("[Memo] No position selected yet.");
            if (debugText != null)
                debugText.text = "Tap the Baekja first!";
            return;
        }

        if (isRecording)
        {
            Debug.Log("[Memo] Already recording.");
            return;
        }

        currentText = "";
        isRecording = true;

        Debug.Log("[Memo] Dictation started.");
        if (debugText != null)
            debugText.text = "Listening...";

        dictation.Activate();
    }

    /// <summary>
    /// Stop 버튼
    /// </summary>
    public void OnStopButton()
    {
        if (!isRecording)
        {
            Debug.Log("[Memo] Not recording.");
            return;
        }

        Debug.Log("[Memo] Dictation stopping...");
        dictation.Deactivate();
        isRecording = false;
    }

    // --- Dictation 이벤트 핸들러 ---

    private void OnPartialTranscription(string text)
    {
        currentText = text;

        if (debugText != null)
            debugText.text = $"(Partial)\n{text}";
    }

    private void OnFullTranscription(string text)
    {
        currentText = text;

        if (debugText != null)
            debugText.text = $"(Full)\n{text}";
    }

    private void OnDictationSessionStopped(DictationSession session)
    {
        SaveCurrentMemo();
    }

    // --- 메모 저장 ---

    private void SaveCurrentMemo()
    {
        if (string.IsNullOrWhiteSpace(currentText))
        {
            Debug.Log("[Memo] Empty text, not saving.");
            if (debugText != null)
                debugText.text = "No text.";

            currentText = "Developement is so difficult";
            //return;
        }

        StartCoroutine(SaveCurrentMemoRoutine());
    }

    private IEnumerator SaveCurrentMemoRoutine()
    {
        // 로딩 이미지 켜기
        if (loadingIcon != null)
            loadingIcon.SetActive(true);

        if (debugText != null)
            debugText.text = "Processing text...";

        yield return new WaitForSeconds(processingDelay);

        var memo = new TouchMemo
        {
            position = currentPosition,
            time = DateTime.Now,
            text = currentText,
            isSolved = false,           // 처음에는 미해결 상태
            decalProjector = null
        };
        

        Debug.Log($"[Memo Saved] {memo.time} @ {memo.position} : {memo.text}");

        if (markerPrefab != null)
        {
            GameObject marker = Instantiate(markerPrefab, currentPosition, Quaternion.identity);

            // MarkerTouchDetector 추가
            var detector = marker.GetComponent<MarkerTouchDetector>();

            // 자동 연결
            detector.Initialize(
                tipTracker,     // Index Tip Tracker
                this                                          // TouchMemoManager 자신
            );
        }

        if (currentIndex < decals.Count)
        {
            var decal = decals[currentIndex];
            decal.SetActive(true);

            Vector3 dir = (currentPosition - jar.transform.position).normalized;
            Vector3 decalPos = jar.transform.position + dir * 0.05f;

            decal.transform.position = decalPos;
            decal.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            memo.decalProjector = decal;
            currentIndex++;
        }

        memos.Add(memo);

        audioSource.gameObject.transform.position = currentPosition;
        audioSource.Play();

        if (debugText != null)
            debugText.text = $"Saved!\n{memo.text}";

        if (loadingIcon != null)
            loadingIcon.SetActive(false);

        ClearData();
    }

    private void ClearData()
    {
        buttons.SetActive(false);
        hasPosition = false;
        currentText = "";
        currentPosition = transform.position;
        debugText.text = "Record your worry";
    }

    public void ShowMemoForMarkerPosition(Vector3 markerPosition)
    {
        if (memos == null || memos.Count == 0)
        {
            Debug.Log("[Memo] No memos saved yet.");
            return;
        }

        TouchMemo closestMemo = null;
        float closestDist = float.MaxValue;

        foreach (var memo in memos)
        {
            float d = Vector3.Distance(markerPosition, memo.position);
            if (d < closestDist)
            {
                closestDist = d;
                closestMemo = memo;
            }
        }

        if (closestMemo == null || closestDist > memoMatchMaxDistance)
        {
            Debug.Log($"[Memo] Could not find memo near marker. dist={closestDist}");
            return;
        }

        // 메모 위치 저장 (Start 버튼 눌렀을 때 쓸 위치)
        lastShownMemoPosition = closestMemo.position;

        // UI 켜고 텍스트 세팅
        if (memoCanvas != null)
            memoCanvas.SetActive(true);

        if (memoText != null)
            memoText.text = closestMemo.text;

        memoInteractionActive = false;   // 모드 초기화

        Debug.Log($"[Memo] Show memo: {closestMemo.time} @ {closestMemo.position} : {closestMemo.text}");
    }

    // 필요하면 UI 닫는 함수도 하나 만들어두면 편함
    public void HideMemoUI()
    {
        if (memoCanvas != null)
            memoCanvas.SetActive(false);

        if (memoText != null)
            memoText.text = "";
    }






    public void OnMemoInteractionStart()
    {
        if (memoInteractionActive)
            return;

        memoInteractionActive = true;

        // 1) indexTip 잠시 끊기
        if (tipTracker != null)
        {
            cachedIndexTip = tipTracker.indexTip;
            tipTracker.indexTip = null;
        }

        // 2) 바늘 오브젝트 켜기
        if (needleObject != null)
            needleObject.SetActive(true);

        // 3) 메모 위치에 새 프리팹 생성
        if (interactionMarkerPrefab != null)
        {
            activeInteractionMarker = Instantiate(
                interactionMarkerPrefab,
                lastShownMemoPosition,
                Quaternion.identity
            );

            var detector = activeInteractionMarker.GetComponent<NeedleMarkerTouchDetector>();

            // 자동 연결
            detector.Initialize(needleTip, this);
        }

        Debug.Log("[Memo] Interaction started.");
    }


    public void OnMemoInteractionStop()
    {
        if (!memoInteractionActive)
            return;

        memoInteractionActive = false;

        // 1) indexTip 복구
        if (tipTracker != null)
        {
            tipTracker.indexTip = cachedIndexTip;
            cachedIndexTip = null;
        }

        // 2) 바늘 오브젝트 끄기
        if (needleObject != null)
            needleObject.SetActive(false);

        // 3) 생성했던 새 프리팹 제거
        if (activeInteractionMarker != null)
        {
            Destroy(activeInteractionMarker);
            activeInteractionMarker = null;
        }


        Debug.Log("[Memo] Interaction stopped.");
    }




    public void OnNeedleMarkerInteractionComplete(Vector3 markerPosition)
    {
        if (memos == null || memos.Count == 0)
        {
            Debug.Log("[Memo] No memos to solve.");
            return;
        }

        TouchMemo closestMemo = null;
        float closestDist = float.MaxValue;

        foreach (var memo in memos)
        {
            float d = Vector3.Distance(markerPosition, memo.position);
            if (d < closestDist)
            {
                closestDist = d;
                closestMemo = memo;
            }
        }

        if (closestMemo == null || closestDist > memoMatchMaxDistance)
        {
            Debug.Log($"[Memo] No memo found near needle marker. dist={closestDist}");
            return;
        }

        // 연결된 데칼 비활성화
        if (closestMemo.decalProjector != null)
        {
            closestMemo.decalProjector.SetActive(false);
        }

        // 해결 처리
        closestMemo.isSolved = true;  // ← 여기서 true/false 선택: 저는 true=해결로 했어요

        Debug.Log($"[Memo] Solved memo: {closestMemo.text}");

        OnMemoInteractionStop();
        HideMemoUI();
    }
}
