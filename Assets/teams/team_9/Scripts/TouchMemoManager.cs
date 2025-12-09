using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

public class TouchMemoManager : MonoBehaviour
{
    [Serializable]
    public class TouchMemo
    {
        public int worryId;                // 서버에서 관리하는 고민 ID
        public Vector3 position;          // 백자 상 위치 (jar_position)
        public DateTime time;
        public string location;
        public string text;

        public GameObject decalProjector; // 이 메모와 연결된 데칼(크랙)
        public GameObject markerObject;   // 표시용 마커 (MarkerTouchDetector 붙어 있는 오브젝트)

        public bool isSolved;
    }

    public WebComm comm;
    public GameObject jar;
    public GameObject buttons;
    public GameObject loadingIcon;
    public HandIndexTipTracker tipTracker;
    public float processingDelay = 3f;
    public AudioSource audioSource;
    public List<TouchMemo> memos = new List<TouchMemo>();

    [Header("Decal Prefab & Materials")]
    public GameObject decalPrefab;                      // 공통으로 쓸 Decal Projector 프리팹 하나

    [Tooltip("white 색상 크랙 머티리얼들")]
    public List<Material> whiteCrackMaterials = new List<Material>();

    [Tooltip("black 색상 크랙 머티리얼들")]
    public List<Material> blackCrackMaterials = new List<Material>();

    [Tooltip("blue 색상 크랙 머티리얼들")]
    public List<Material> blueCrackMaterials = new List<Material>();

    [Tooltip("yellow 색상 크랙 머티리얼들")]
    public List<Material> yellowCrackMaterials = new List<Material>();

    [Tooltip("red 색상 크랙 머티리얼들")]
    public List<Material> redCrackMaterials = new List<Material>();

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

    public GameObject solveStartButton;
    public GameObject solveStopButton;

    private Vector3 lastShownMemoPosition;
    private bool memoInteractionActive = false;
    private Transform cachedIndexTip;
    private GameObject activeInteractionMarker;
    public Transform needleTip;

    // 간단히 고정 위치 정보 (나중에 별도 UI/설정으로 바꿔도 됨)
    private string defaultLocation = "Seoul, Korea";

    void Awake()
    {

    }

    private void OnEnable()
    {
        if (comm != null)
        {
            // 서버 전체 스냅샷 / 생성 / 해결 이벤트 구독
            comm.OnSnapshotReceived += HandleSnapshot;
            comm.OnWorryCreated += HandleWorryCreated;
            comm.OnWorryResolved += HandleWorryResolved;
        }
    }

    private void OnDisable()
    {
        if (comm != null)
        {
            comm.OnSnapshotReceived -= HandleSnapshot;
            comm.OnWorryCreated -= HandleWorryCreated;
            comm.OnWorryResolved -= HandleWorryResolved;
        }
    }

    private void Start()
    {
        buttons.SetActive(false);

        if (needleObject != null)
            needleObject.SetActive(false);

        // 메모 캔버스는 기본적으로 꺼두기
        if (memoCanvas != null)
            memoCanvas.SetActive(false);
    }

    // =========================================================
    // 서버 → Unity 이벤트 처리
    // =========================================================

    /// <summary>
    /// 서버에서 현재까지의 전체 고민(snapshots)을 받았을 때 호출.
    /// 기존 로컬 메모/크랙/마커를 모두 지우고 새로 구성합니다.
    /// </summary>
    private void HandleSnapshot(List<WebComm.WorryDto> worries)
    {
        ClearAllMemos();

        if (worries == null)
            return;

        foreach (var w in worries)
        {
            CreateMemoFromDto(w);
        }

        Debug.Log($"[Memo] Snapshot applied: {memos.Count} memos.");
    }

    /// <summary>
    /// 서버에서 새 고민(worry_created)을 브로드캐스트했을 때 호출.
    /// </summary>
    private void HandleWorryCreated(WebComm.WorryDto w)
    {
        if (w == null)
            return;

        CreateMemoFromDto(w);

        Debug.Log($"[Memo] Worry created from server: id={w.id}, color={w.color}");
    }

    /// <summary>
    /// 서버에서 고민 해결(worry_resolved)을 브로드캐스트했을 때 호출.
    /// 해당 고민의 크랙 데칼을 제거합니다.
    /// </summary>
    private void HandleWorryResolved(WebComm.WorryDto w)
    {
        if (w == null)
            return;

        var memo = memos.Find(m => m.worryId == w.id);
        if (memo == null)
        {
            Debug.LogWarning($"[Memo] Resolved worry id={w.id} not found in local memos.");
            return;
        }

        memo.isSolved = w.is_resolved;

        // 크랙(데칼) 제거
        if (memo.decalProjector != null)
        {
            Destroy(memo.decalProjector);
            memo.decalProjector = null;
        }

        Debug.Log($"[Memo] Worry resolved (local update): id={w.id}");
    }

    /// <summary>
    /// 서버에서 받은 WorryDto를 기반으로 TouchMemo를 만들고,
    /// 마커 + (미해결이면) 데칼을 생성합니다.
    /// </summary>
    private TouchMemo CreateMemoFromDto(WebComm.WorryDto dto)
    {
        if (dto == null)
            return null;

        var memo = new TouchMemo();
        memo.worryId = dto.id;

        // jar_position → Unity Vector3
        if (dto.jar_position != null)
            memo.position = new Vector3(dto.jar_position.x, dto.jar_position.y, dto.jar_position.z);
        else
            memo.position = Vector3.zero;

        // 시간 파싱
        DateTime parsedTime;
        if (!string.IsNullOrEmpty(dto.time) && DateTime.TryParse(dto.time, out parsedTime))
            memo.time = parsedTime;
        else
            memo.time = DateTime.Now;

        memo.location = dto.location;
        memo.text = dto.text;
        memo.isSolved = dto.is_resolved;

        // 1) 마커 생성
        if (markerPrefab != null)
        {
            var parent = jar.transform;
            GameObject marker = Instantiate(markerPrefab, memo.position, Quaternion.identity, parent);
            memo.markerObject = marker;

            var detector = marker.GetComponent<MarkerTouchDetector>();
            if (detector != null)
            {
                detector.Initialize(
                    tipTracker,  // Index Tip Tracker
                    this         // TouchMemoManager
                );
            }
        }

        // 2) 아직 해결되지 않은 고민이면 크랙(데칼) 생성
        if (!memo.isSolved)
        {
            memo.decalProjector = InstantiateDecalForMemo(memo, dto.color);
        }

        memos.Add(memo);
        return memo;
    }

    /// <summary>
    /// 특정 메모와 색 이름에 맞는 데칼 프리팹을 생성해서 jar에 붙입니다.
    /// (이제는 프리팹 하나 + 색별 랜덤 머티리얼 + 랜덤 사이즈)
    /// </summary>
    private GameObject InstantiateDecalForMemo(TouchMemo memo, string colorName)
    {
        if (memo == null)
            return null;

        if (decalPrefab == null)
        {
            Debug.LogWarning("[Memo] decalPrefab is not assigned.");
            return null;
        }

        Vector3 worldPos = memo.position;
        Vector3 decalPos;
        Quaternion decalRot;
        Transform parent = null;

        if (jar == null)
        {
            // jar가 없으면 그냥 메모 위치에 생성
            decalPos = worldPos;
            decalRot = Quaternion.identity;
            parent = null;
        }
        else
        {
            // 백자 중심에서 방향 벡터를 구해서 살짝 offset
            Vector3 dir = (worldPos - jar.transform.position).normalized;
            if (dir.sqrMagnitude < 1e-6f)
                dir = jar.transform.forward;

            decalPos = jar.transform.position + dir * 0.05f;
            decalRot = Quaternion.LookRotation(dir, Vector3.up);
            parent = jar.transform;   // 백자를 부모로 붙여두면 함께 움직임
        }

        GameObject decalInstance = Instantiate(decalPrefab, decalPos, decalRot, parent);

        float randomZ = UnityEngine.Random.Range(0f, 360f);
        decalInstance.transform.localRotation *= Quaternion.Euler(0f, 0f, randomZ);

        // URP Decal Projector 설정
        var projector = decalInstance.GetComponent<DecalProjector>();
        if (projector != null)
        {
            // 1) 색에 맞는 머티리얼 랜덤 선택
            Material mat = GetRandomMaterialForColor(colorName);
            if (mat != null)
                projector.material = mat;

            // 2) Width/Height를 0.06 ~ 0.09 사이 랜덤
            float size = UnityEngine.Random.Range(0.06f, 0.09f);
            var curSize = projector.size;
            projector.size = new Vector3(size, size, curSize.z);
        }
        else
        {
            Debug.LogWarning("[Memo] DecalPrefab has no DecalProjector component.");
        }

        return decalInstance;
    }



    /// <summary>
    /// 서버 스냅샷을 다시 받을 때, 기존 메모/마커/데칼 싹 정리.
    /// </summary>
    private void ClearAllMemos()
    {
        if (memos != null)
        {
            foreach (var m in memos)
            {
                if (m.decalProjector != null)
                    Destroy(m.decalProjector);
                if (m.markerObject != null)
                    Destroy(m.markerObject);
            }

            memos.Clear();
        }
    }

    // =========================================================
    // Unity → 서버 : 고민 생성/해결 전송
    // =========================================================

    void SendCreateWorryToServer(string text, string locationName, Vector3 jarWorldPosition)
    {
        if (comm != null)
        {
            comm.SendCreateWorry(text, locationName, jarWorldPosition);
        }
        else
        {
            Debug.LogWarning("[Memo] WebComm is null, cannot send create_worry.");
        }
    }

    void SendResolveWorryToServer(TouchMemo memo, string resolvedText)
    {
        if (comm != null && memo != null)
        {
            comm.SendResolveWorry(memo.worryId, resolvedText);
        }
        else
        {
            Debug.LogWarning("[Memo] WebComm or memo is null, cannot send resolve_worry.");
        }
    }

    // =========================================================
    // 백자 터치 → 고민 남기기
    // =========================================================

    /// <summary>
    /// 백자 터치 시, 해당 위치로 메모를 준비만 함 (자동 녹음 X)
    /// </summary>
    public void PrepareMemoAtPosition(Vector3 worldPos)
    {
        currentPosition = worldPos;
        hasPosition = true;
        Debug.Log($"[Memo] Position selected: {worldPos}");

        buttons.SetActive(true);
    }

    public void FinishMemoAtPosition()
    {
        ClearData();
    }

    /// <summary>
    /// Start 버튼 (녹음 시작)
    /// </summary>
    public void OnStartButton()
    {

        if (!isRecording)
        {
            currentText = "";
            isRecording = true;

            if (debugText != null)
                debugText.text = "Listening...";
        }
        else
        {
            isRecording = false;
        }
    }


    // --- 메모 저장 (고민 남기기) ---

    public void SaveCurrentMemo(string recordedText)
    {
        currentText = recordedText;

        if (string.IsNullOrWhiteSpace(currentText))
        {
            Debug.Log("[Memo] Empty text, not saving.");
            if (debugText != null)
                debugText.text = "No text.";

            currentText = "No text";
            //return;
        }

        StartCoroutine(SaveCurrentMemoRoutine());

        var currentLocation = defaultLocation;

        SendCreateWorryToServer(currentText, currentLocation, currentPosition);
    }

    private IEnumerator SaveCurrentMemoRoutine()
    {
        // 로딩 이미지 켜기
        if (loadingIcon != null)
            loadingIcon.SetActive(true);

        if (debugText != null)
            debugText.text = "Processing text...";

        yield return new WaitForSeconds(processingDelay);

        // 사운드 재생
        if (audioSource != null)
        {
            audioSource.gameObject.transform.position = currentPosition;
            audioSource.Play();
        }

        if (debugText != null)
            debugText.text = currentText;

        if (loadingIcon != null)
            loadingIcon.SetActive(false);

        yield return new WaitForSeconds(processingDelay);

        ClearData();
    }

    // =========================================================
    // 메모 UI 보기 / 해결 인터랙션
    // =========================================================

    private void ClearData()
    {
        if (buttons != null)
            buttons.SetActive(false);

        hasPosition = false;
        currentText = "";
        currentPosition = transform.position;

        if (debugText != null)
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

        // 메모 위치 저장 (바늘 인터랙션 시작 시 기준 위치)
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
            var parent = jar.transform;
            activeInteractionMarker = Instantiate(
                interactionMarkerPrefab,
                lastShownMemoPosition,
                Quaternion.identity,
                parent
            );

            var detector = activeInteractionMarker.GetComponent<NeedleMarkerTouchDetector>();

            // 자동 연결
            if (detector != null)
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

        solveStartButton.SetActive(true);
        solveStopButton.SetActive(false);
    }

    /// <summary>
    /// 바늘이 특정 위치의 크랙과 상호작용을 완료했을 때 호출.
    /// 가장 가까운 메모를 찾아 "해결" 요청을 서버로 보냅니다.
    /// </summary>
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

        // 여기서는 임시로 해결 내용을 빈 문자열 또는 고정 문구로 보냅니다.
        // 나중에 "해결 내용" 음성 인식 결과를 받아서 전달하도록 확장 가능.
        string resolvedTextPlaceholder = "Resolved.";

        // 1) 서버에 해결 요청 전송
        SendResolveWorryToServer(closestMemo, resolvedTextPlaceholder);

        // 2) 즉시 로컬에서도 크랙 제거(빠른 피드백용, 서버 브로드캐스트가 한 번 더 정합 맞춰줌)
        if (closestMemo.decalProjector != null)
        {
            Destroy(closestMemo.decalProjector);
            closestMemo.decalProjector = null;
        }

        closestMemo.isSolved = true;

        Debug.Log($"[Memo] Solved memo (local & sent to server): id={closestMemo.worryId}, text={closestMemo.text}");

        OnMemoInteractionStop();
        HideMemoUI();
    }

    /// <summary>
    /// 색 이름에 맞는 머티리얼 리스트에서 랜덤으로 하나 선택.
    /// </summary>
    private Material GetRandomMaterialForColor(string colorName)
    {
        List<Material> list = null;

        switch ((colorName ?? "").ToLowerInvariant())
        {
            case "white":
                list = whiteCrackMaterials;
                break;
            case "black":
                list = blackCrackMaterials;
                break;
            case "blue":
                list = blueCrackMaterials;
                break;
            case "yellow":
                list = yellowCrackMaterials;
                break;
            case "red":
                list = redCrackMaterials;
                break;
            default:
                list = yellowCrackMaterials;   // 기본값은 yellow
                Debug.LogWarning($"[Memo] Unknown color '{colorName}', using yellow materials.");
                break;
        }

        if (list == null || list.Count == 0)
            return null;

        int idx = UnityEngine.Random.Range(0, list.Count);
        return list[idx];
    }
}
