// WebComm.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public class WebComm : MonoBehaviour
{
    [Header("Server Settings")]
    public string serverURL = "ws://localhost:5000/ws";  // 기본값
    public TMP_Text currIP;

    private ClientWebSocket ws;
    private Uri serverUri;
    private CancellationTokenSource cts;
    private bool isPaused = false;

    // ==== 메인 스레드 실행용 큐 ====
    private readonly Queue<Action> mainThreadActions = new Queue<Action>();

    // ---------------------------------------------------
    // 데이터 전송/수신용 DTO
    // (app_moon.py의 프로토콜에 맞춘 구조)
    // ---------------------------------------------------

    [Serializable]
    public class Vec3Dto
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public class SkyCoordDto
    {
        public float u;
        public float v;
    }

    [Serializable]
    public class WorryDto
    {
        public int id;
        public string text;
        public string resolved_text;
        public string time;
        public string resolved_at;
        public string location;
        public string color;      // "white", "black", "blue", "yellow", "red"
        public bool is_resolved;
        public Vec3Dto jar_position;
        public SkyCoordDto sky_coord;
    }

    [Serializable]
    private class MessageHeader
    {
        public string type;
    }

    [Serializable]
    private class SnapshotMessage
    {
        public string type;
        public WorryDto[] worries;
    }

    [Serializable]
    private class WorryCreatedMessage
    {
        public string type;
        public WorryDto worry;
    }

    [Serializable]
    private class WorryResolvedMessage
    {
        public string type;
        public WorryDto worry;
    }

    [Serializable]
    private class WorryDetailMessage
    {
        public string type;
        public WorryDto worry; // null 일 수도 있음
    }

    // ---- 전송용 메시지 구조 ----

    [Serializable]
    private class CreateWorryMessage
    {
        public string type = "create_worry";
        public string text;
        public string time;
        public string location;
        public Vec3Dto jar_position;
    }

    [Serializable]
    private class ResolveWorryMessage
    {
        public string type = "resolve_worry";
        public int worry_id;
        public string resolved_text;
        public string time;
    }

    [Serializable]
    private class SelectStarMessage
    {
        public string type = "select_star";
        public SkyCoordDto sky_coord;
    }

    // ---------------------------------------------------
    // 다른 스크립트에서 구독할 이벤트들
    // ---------------------------------------------------

    /// <summary>
    /// 서버에 처음 접속했을 때 받는 전체 스냅샷.
    /// worries 리스트를 받아 백자 크랙/별을 한 번에 재구성할 때 사용.
    /// </summary>
    public event Action<List<WorryDto>> OnSnapshotReceived;

    /// <summary>
    /// 새 고민이 생성되었을 때 (어떤 유저가 create_worry를 보내면
    /// 서버가 worry_created를 브로드캐스트).
    /// </summary>
    public event Action<WorryDto> OnWorryCreated;

    /// <summary>
    /// 기존 고민이 해결되었을 때 (resolve_worry → worry_resolved).
    /// </summary>
    public event Action<WorryDto> OnWorryResolved;

    /// <summary>
    /// 월 디스플레이에서 별을 선택했을 때, 서버가 돌려주는 상세 정보.
    /// (HMD에서 좌표 보내고 worry_detail 수신 → UI에 내용 보여주기용)
    /// worry가 null일 수도 있음.
    /// </summary>
    public event Action<WorryDto> OnWorryDetail;

    // ---------------------------------------------------
    // 메인 스레드 큐 유틸
    // ---------------------------------------------------

    public void EnqueueOnMainThread(Action action)
    {
        lock (mainThreadActions)
        {
            mainThreadActions.Enqueue(action);
        }
    }

    private void Update()
    {
        // 메인 스레드에서 큐에 쌓인 작업 실행
        lock (mainThreadActions)
        {
            while (mainThreadActions.Count > 0)
            {
                var a = mainThreadActions.Dequeue();
                try
                {
                    a?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError("[WebComm] Main-thread action error: " + e);
                }
            }
        }
    }

    // ---------------------------------------------------
    // WebSocket 연결
    // ---------------------------------------------------

    private async void ConnectWebSocket()
    {
        try
        {
            // 기존 소켓 정리
            if (ws != null)
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnect", CancellationToken.None);
                    }
                }
                catch { }
                ws.Dispose();
            }

            ws = new ClientWebSocket();
            cts = new CancellationTokenSource();

            serverUri = new Uri(serverURL);
            Debug.Log("[WebComm] Connecting to " + serverURL);
            await ws.ConnectAsync(serverUri, cts.Token);

            _ = Task.Run(ReceiveLoop); // 수신 루프 비동기 실행
            Debug.Log("[WebComm] WebSocket connected.");
        }
        catch (Exception e)
        {
            Debug.LogError("[WebComm] WebSocket connect error: " + e);
        }
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[4096];

        while (ws != null && ws.State == WebSocketState.Open)
        {
            if (isPaused)
            {
                await Task.Delay(100);
                continue;
            }

            WebSocketReceiveResult result = null;
            var segment = new ArraySegment<byte>(buffer);

            try
            {
                result = await ws.ReceiveAsync(segment, cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogError("[WebComm] Receive error: " + e);
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Debug.Log("[WebComm] Server closed connection.");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleServerMessage(msg);
            }
        }
    }

    // ---------------------------------------------------
    // 서버 -> 클라이언트 메시지 처리
    // ---------------------------------------------------

    private void HandleServerMessage(string msg)
    {
        Debug.Log("[WebComm] Server says: " + msg);

        try
        {
            // 먼저 type만 확인
            MessageHeader header = JsonUtility.FromJson<MessageHeader>(msg);
            if (header == null || string.IsNullOrEmpty(header.type))
            {
                Debug.LogWarning("[WebComm] Message has no type");
                return;
            }

            switch (header.type)
            {
                case "snapshot":
                    {
                        SnapshotMessage snapshot = JsonUtility.FromJson<SnapshotMessage>(msg);
                        if (snapshot != null && snapshot.worries != null)
                        {
                            EnqueueOnMainThread(() =>
                            {
                                Debug.Log("[WebComm] Snapshot received: " + snapshot.worries.Length + " worries");
                                OnSnapshotReceived?.Invoke(new List<WorryDto>(snapshot.worries));
                            });
                        }
                        break;
                    }

                case "worry_created":
                    {
                        WorryCreatedMessage created = JsonUtility.FromJson<WorryCreatedMessage>(msg);
                        if (created != null && created.worry != null)
                        {
                            EnqueueOnMainThread(() =>
                            {
                                Debug.Log("[WebComm] Worry created: id=" + created.worry.id);
                                OnWorryCreated?.Invoke(created.worry);
                            });
                        }
                        break;
                    }

                case "worry_resolved":
                    {
                        WorryResolvedMessage resolved = JsonUtility.FromJson<WorryResolvedMessage>(msg);
                        if (resolved != null && resolved.worry != null)
                        {
                            EnqueueOnMainThread(() =>
                            {
                                Debug.Log("[WebComm] Worry resolved: id=" + resolved.worry.id);
                                OnWorryResolved?.Invoke(resolved.worry);
                            });
                        }
                        break;
                    }

                case "worry_detail":
                    {
                        WorryDetailMessage detail = JsonUtility.FromJson<WorryDetailMessage>(msg);
                        // worry가 null일 수도 있음
                        EnqueueOnMainThread(() =>
                        {
                            Debug.Log("[WebComm] Worry detail received" +
                                      (detail != null && detail.worry != null ? $" id={detail.worry.id}" : " (none)"));
                            OnWorryDetail?.Invoke(detail != null ? detail.worry : null);
                        });
                        break;
                    }

                default:
                    Debug.LogWarning("[WebComm] Unknown message type: " + header.type);
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WebComm] Error while handling server message: " + e);
        }
    }

    // ---------------------------------------------------
    // 클라이언트 -> 서버 메시지 전송
    // ---------------------------------------------------

    /// <summary>
    /// "고민 남기기" – 백자 상의 위치 + 텍스트/위치 정보 전송.
    /// 서버는 색/sky 좌표를 정하고 worry_created를 브로드캐스트합니다.
    /// </summary>
    public async void SendCreateWorry(string text, string location, Vector3 jarWorldPosition)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            Debug.LogWarning("[WebComm] WebSocket is not open. Cannot send create_worry.");
            return;
        }

        var msg = new CreateWorryMessage
        {
            text = text ?? "",
            location = string.IsNullOrEmpty(location) ? "Unknown" : location,
            time = DateTime.UtcNow.ToString("o"),
            jar_position = new Vec3Dto
            {
                x = jarWorldPosition.x,
                y = jarWorldPosition.y,
                z = jarWorldPosition.z
            }
        };

        string json = JsonUtility.ToJson(msg);
        ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));

        try
        {
            await ws.SendAsync(bytesToSend, WebSocketMessageType.Text, true, cts.Token);
            Debug.Log("[WebComm] Sent create_worry: " + json);
        }
        catch (Exception e)
        {
            Debug.LogError("[WebComm] Send create_worry error: " + e);
        }
    }

    /// <summary>
    /// "고민 해결" – 이미 존재하는 worry_id에 대해 해결 내용을 전송.
    /// 서버는 worry_resolved를 브로드캐스트합니다.
    /// </summary>
    public async void SendResolveWorry(int worryId, string resolvedText)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            Debug.LogWarning("[WebComm] WebSocket is not open. Cannot send resolve_worry.");
            return;
        }

        var msg = new ResolveWorryMessage
        {
            worry_id = worryId,
            resolved_text = resolvedText ?? "",
            time = DateTime.UtcNow.ToString("o")
        };

        string json = JsonUtility.ToJson(msg);
        ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));

        try
        {
            await ws.SendAsync(bytesToSend, WebSocketMessageType.Text, true, cts.Token);
            Debug.Log("[WebComm] Sent resolve_worry: " + json);
        }
        catch (Exception e)
        {
            Debug.LogError("[WebComm] Send resolve_worry error: " + e);
        }
    }

    /// <summary>
    /// HMD로 월 디스플레이 상의 별을 선택했을 때,
    /// normalized 좌표(0~1)를 서버로 보냅니다.
    /// 서버는 가장 가까운 worry를 찾아 worry_detail을 돌려줍니다.
    /// </summary>
    public async void SendSelectStar(Vector2 skyCoord01)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            Debug.LogWarning("[WebComm] WebSocket is not open. Cannot send select_star.");
            return;
        }

        var msg = new SelectStarMessage
        {
            sky_coord = new SkyCoordDto
            {
                u = Mathf.Clamp01(skyCoord01.x),
                v = Mathf.Clamp01(skyCoord01.y)
            }
        };

        string json = JsonUtility.ToJson(msg);
        ArraySegment<byte> bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));

        try
        {
            await ws.SendAsync(bytesToSend, WebSocketMessageType.Text, true, cts.Token);
            Debug.Log("[WebComm] Sent select_star: " + json);
        }
        catch (Exception e)
        {
            Debug.LogError("[WebComm] Send select_star error: " + e);
        }
    }

    // ---------------------------------------------------
    // Unity 생명주기
    // ---------------------------------------------------

    private void Start()
    {
        // PlayerPrefs에서 서버 URL 로드
        if (PlayerPrefs.HasKey("serverURL"))
        {
            serverURL = PlayerPrefs.GetString("serverURL", serverURL);
            if (currIP != null)
            {
                currIP.text = "Current IP: " +
                              serverURL.Replace("ws://", "").Replace(":5000/ws", "");
            }
            Debug.Log("[WebComm] Loaded server URL from PlayerPrefs: " + serverURL);
        }
        else
        {
            if (currIP != null)
                currIP.text = "Current IP: localhost";
        }

        ConnectWebSocket();
    }

    /// <summary>
    /// UI InputField 등에서 서버 IP를 입력받을 때 사용.
    /// 예: field.text = "192.168.0.10" 이면 ws://192.168.0.10:5000/ws 로 세팅
    /// </summary>
    public void SetServerURL(TMP_InputField field)
    {
        Debug.Log("[WebComm] Setting server URL to: " + field.text);
        serverURL = "ws://" + field.text + ":5000/ws";
        ConnectWebSocket();

        if (currIP != null)
            currIP.text = "Current IP: " + field.text;

        PlayerPrefs.SetString("serverURL", serverURL);
        PlayerPrefs.Save();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        isPaused = pauseStatus;
        if (pauseStatus)
        {
            Debug.Log("[WebComm] Application paused - ReceiveLoop will skip processing.");
        }
        else
        {
            Debug.Log("[WebComm] Application resumed.");
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        isPaused = !hasFocus;
        if (!hasFocus)
        {
            Debug.Log("[WebComm] Application lost focus - ReceiveLoop will skip processing.");
        }
        else
        {
            Debug.Log("[WebComm] Application gained focus.");
        }
    }

    private async void OnApplicationQuit()
    {
        if (ws != null)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }
            catch { }
            ws.Dispose();
        }
    }
}
