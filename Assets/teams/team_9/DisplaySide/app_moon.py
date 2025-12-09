# app_moon.py
import json
import datetime
import random
import math
from sentence_analyze import classify_sentence

from flask import Flask, jsonify, render_template
from flask_sock import Sock

# static: /static, templates: /templates
app = Flask(__name__, static_folder="static", template_folder="templates")
sock = Sock(app)

# 월 디스플레이 중앙의 달 영역 (별이 피해야 하는 영역)
MOON_CENTER_U = 0.5
MOON_CENTER_V = 0.5
MOON_RADIUS = 0.18  # 필요하면 조절 (0~0.5 정도)

# ============================================================
# 데이터 모델 (메모리 상 관리; 필요하면 DB로 교체)
# ============================================================

# worries: 서버가 관리하는 "단 하나의 진실" 리스트
# 각 원소는 다음 키를 가집니다.
# {
#   "id": int,
#   "text": str,
#   "resolved_text": str or None,
#   "time": str (ISO8601),
#   "resolved_at": str or None,
#   "location": str,
#   "color": str ("white", "black", "blue", "yellow", "red"),
#   "is_resolved": bool,
#   "jar_position": {"x": float, "y": float, "z": float},
#   "sky_coord": {"u": float, "v": float},
# }

# ============================================================
# 디버깅 or 데모용 초기 고민 20개 (5개 미해결 + 15개 해결됨)
# ============================================================
def make_time(offset_hours):
    """현재에서 -offset_hours 만큼 과거 시간 만들어줌."""
    t = datetime.datetime.utcnow() - datetime.timedelta(hours=offset_hours)
    return t.isoformat() + "Z"

def random_sky():
    """
    달이 있는 중앙 원형 영역을 피해서 랜덤 좌표 생성.
    (0 <= u, v <= 1)
    """
    while True:
        u = random.random()
        v = random.random()

        # 달 중심과 거리 계산
        du = u - MOON_CENTER_U
        dv = v - MOON_CENTER_V
        dist2 = du * du + dv * dv

        # 달의 반경 영역 내부라면 다시 뽑기
        if dist2 < (MOON_RADIUS * MOON_RADIUS):
            continue

        return {"u": u, "v": v}

def random_pos():
    """백자 주변의 적당한 랜덤 위치"""
    return {
        "x": round(random.uniform(-0.3, 0.3), 3),
        "y": round(random.uniform( 0.0, 0.6), 3),
        "z": round(random.uniform(-0.3, 0.3), 3),
    }

COLORS = ["white","black","blue","yellow","red"]

init_worries = []

# 해결된 고민 20개 자동 생성
resolved_examples = [
    ("I couldn’t sleep well for days.", "I rested properly over the weekend."),
    ("I had a serious argument with my family.", "We talked it out and made peace."),
    ("My assignment schedule felt overwhelming.", "I broke it into smaller tasks."),
    ("I was stressed because I wasn’t exercising.", "I adjusted my routine and started again."),
    ("Work pressure was becoming too much.", "I talked with my supervisor and reorganized tasks."),
    ("I was anxious about an upcoming presentation.", "Repeated practice boosted my confidence."),
    ("My physical condition felt worse lately.", "Regular exercise improved my stamina."),
    ("My team project was full of conflicts.", "We redefined roles and resolved issues."),
    ("I was worried about my low savings.", "I started managing my expenses strictly."),
    ("I feared I wouldn’t meet the deadline.", "I finished everything on time."),
    ("My room was a mess and stressed me out.", "I cleaned everything in one go."),
    ("I had ongoing trouble with a close friend.", "We met and cleared misunderstandings."),
    ("I was anxious about my health condition.", "Medical checkup showed everything was fine."),
    ("Job hunting was exhausting and stressful.", "I finally received an offer."),
    ("My class schedule was too packed.", "I rearranged it for better balance."),
    ("I felt lost about my future career path.", "I set clear short-term goals."),
    ("I kept comparing myself to others.", "I learned to focus on my own pace."),
    ("I felt lonely after moving to a new city.", "I slowly built new friendships."),
    ("I was afraid of failing an important exam.", "Consistent studying paid off."),
    ("I lost motivation for everything.", "Taking a short break helped me reset."),
]

# 6~20번 자동 생성
for i in range(20):

    is_resolved = True
    
    if i > 15:
        is_resolved = False

    idx = i
    text, resolved_text = resolved_examples[i]
    init_worries.append({
        "id": idx,
        "text": text,
        "resolved_text": resolved_text,
        "time": make_time(100 - i * 2),
        "resolved_at": make_time(50 - i),   # 해결은 더 최근 시간
        "location": random.choice([
            "Seoul, Korea", "Busan, Korea", "Tokyo, Japan",
            "New York, USA", "Paris, France",
            "London, UK", "Berlin, Germany",
            "Sydney, Australia", "Taipei, Taiwan"
        ]),
        "color": random.choice(COLORS),
        "is_resolved": is_resolved,
        "jar_position": random_pos(),
        "sky_coord": random_sky(),
    })



worries = []
next_worry_id = 1

worries = init_worries.copy()
next_worry_id = len(worries) + 1

# 현재 연결된 WebSocket 클라이언트들
clients = set()


# ============================================================
# 유틸 함수들
# ============================================================


def now_iso():
    return datetime.datetime.utcnow().isoformat() + "Z"


def choose_color(text: str, location: str) -> str:
    """
    디버깅용: 텍스트/위치와 상관없이
    white / black / blue / yellow / red 중 하나를 랜덤 반환.
    """
    _, color, _, _ = classify_sentence(text)
    return color


def random_sky_coord() -> dict:
    """
    월 디스플레이에서 사용할 랜덤 별 좌표 (0~1 정규화).
    중앙의 달 영역(MOON_CENTER, MOON_RADIUS) 안에는 별을 배치하지 않습니다.
    """
    while True:
        u = random.random()
        v = random.random()

        du = u - MOON_CENTER_U
        dv = v - MOON_CENTER_V
        dist2 = du * du + dv * dv

        if dist2 < MOON_RADIUS * MOON_RADIUS:
            # 달이 있는 영역이면 다시 뽑음
            continue

        return {"u": u, "v": v}


def worry_to_public(w: dict) -> dict:
    """
    클라이언트에게 보낼 때 사용할 공개용 딕셔너리.
    """
    return {
        "id": w["id"],
        "text": w["text"],
        "resolved_text": w["resolved_text"],
        "time": w["time"],
        "resolved_at": w["resolved_at"],
        "location": w["location"],
        "color": w["color"],
        "is_resolved": w["is_resolved"],
        "jar_position": w["jar_position"],
        "sky_coord": w["sky_coord"],
    }


def get_counts():
    """
    전체 개수와 아직 해결되지 않은 고민 개수를 반환.
    (달 위상 계산용)
    """
    total = len(worries)
    unresolved = sum(1 for w in worries if not w["is_resolved"])
    return total, unresolved


def broadcast(message: dict, exclude_ws=None):
    """
    모든 WebSocket 클라이언트에 메시지 브로드캐스트.
    (Unity, 웹 브라우저 모두 포함)
    """
    if not clients:
        return

    payload = json.dumps(message, ensure_ascii=False)
    dead_clients = []

    for ws in list(clients):
        if ws is exclude_ws:
            continue
        try:
            ws.send(payload)
        except Exception as e:
            print(f"[WS] Send failed: {e}")
            dead_clients.append(ws)

    for ws in dead_clients:
        clients.discard(ws)


def find_worry_by_id(worry_id: int):
    for w in worries:
        if w["id"] == worry_id:
            return w
    return None


def find_nearest_worry_by_sky_coord(u: float, v: float):
    """
    월 디스플레이 상의 좌표(u, v)에 가장 가까운 worry를 찾습니다.
    """
    best = None
    best_dist2 = None

    for w in worries:
        wu = w["sky_coord"]["u"]
        wv = w["sky_coord"]["v"]
        du = wu - u
        dv = wv - v
        d2 = du * du + dv * dv

        if best is None or d2 < best_dist2:
            best = w
            best_dist2 = d2

    return best


# ============================================================
# HTTP 라우트
# ============================================================


@app.route("/")
def index():
    """
    메인 페이지: 월 디스플레이 화면 (달 + 별 + 고민 시각화).
    """
    return render_template("index_moon.html")


# ============================================================
# WebSocket 핸들러
# ============================================================


@sock.route("/ws")
def websocket(ws):
    """
    WebSocket 엔드포인트.

    클라이언트 → 서버:
    ---------------------------------------
    1) 새 고민 생성
    {
      "type": "create_worry",
      "text": "...",
      "time": "...(optional)",
      "location": "Seoul, Korea",
      "jar_position": { "x": 1.2, "y": 0.4, "z": -0.8 }
    }

    2) 기존 고민 해결
    {
      "type": "resolve_worry",
      "worry_id": 3,
      "resolved_text": "이제 마음이 편해졌다.",
      "time": "...(optional)"
    }

    3) 월 디스플레이 별 선택
    {
      "type": "select_star",
      "sky_coord": { "u": 0.37, "v": 0.82 }
    }

    서버 → 클라이언트:
    ---------------------------------------
    (1) 처음 접속 시 전체 스냅샷
    {
      "type": "snapshot",
      "worries": [ ... worry_to_public ... ],
      "total_count": int,
      "unresolved_count": int
    }

    (2) 새 고민 생성 브로드캐스트
    {
      "type": "worry_created",
      "worry": { ... worry_to_public ... },
      "total_count": int,
      "unresolved_count": int
    }

    (3) 고민 해결 브로드캐스트
    {
      "type": "worry_resolved",
      "worry": { ... worry_to_public ... },
      "total_count": int,
      "unresolved_count": int
    }

    (4) 별 선택 결과 (선택한 ws 에만 전송)
    {
      "type": "worry_detail",
      "worry": { ... worry_to_public ... }  # 없으면 null
    }
    """
    global next_worry_id

    clients.add(ws)
    print("[WS] Client connected. Total:", len(clients))

    # 1) 새로 접속한 클라이언트에게 현재까지의 전체 상태(snapshot) 전송
    try:
        total, unresolved = get_counts()
        snapshot_payload = {
            "type": "snapshot",
            "worries": [worry_to_public(w) for w in worries],
            "total_count": total,
            "unresolved_count": unresolved,
        }
        ws.send(json.dumps(snapshot_payload, ensure_ascii=False))
    except Exception as e:
        print(f"[WS] Failed to send snapshot: {e}")

    try:
        # 2) 메시지 루프
        while True:
            data = ws.receive()
            if data is None:
                # 클라이언트 연결 종료
                break

            print(f"[WS] Received raw: {data}")

            try:
                msg = json.loads(data)
            except Exception as e:
                print(f"[WS] Invalid JSON: {e}")
                continue

            msg_type = msg.get("type")

            # -----------------------------
            # 1) 새 고민 생성
            # -----------------------------
            if msg_type == "create_worry":
                text = msg.get("text", "") or ""
                location = msg.get("location", "Unknown") or "Unknown"
                time_str = msg.get("time") or now_iso()

                jar_pos = msg.get("jar_position") or {}
                jar_x = float(jar_pos.get("x", 0.0))
                jar_y = float(jar_pos.get("y", 0.0))
                jar_z = float(jar_pos.get("z", 0.0))

                color = choose_color(text, location)
                sky = random_sky_coord()

                worry = {
                    "id": next_worry_id,
                    "text": text,
                    "resolved_text": None,
                    "time": time_str,
                    "resolved_at": None,
                    "location": location,
                    "color": color,
                    "is_resolved": False,
                    "jar_position": {"x": jar_x, "y": jar_y, "z": jar_z},
                    "sky_coord": sky,
                }
                worries.append(worry)
                next_worry_id += 1

                print(f"[WS] Worry created: {worry}")

                total, unresolved = get_counts()
                broadcast(
                    {
                        "type": "worry_created",
                        "worry": worry_to_public(worry),
                        "total_count": total,
                        "unresolved_count": unresolved,
                    }
                )

            # -----------------------------
            # 2) 기존 고민 해결
            # -----------------------------
            elif msg_type == "resolve_worry":
                worry_id = msg.get("worry_id")
                if worry_id is None:
                    print("[WS] resolve_worry: missing worry_id")
                    continue

                try:
                    worry_id = int(worry_id)
                except ValueError:
                    print("[WS] resolve_worry: invalid worry_id")
                    continue

                resolved_text = msg.get("resolved_text") or ""
                time_str = msg.get("time") or now_iso()

                worry = find_worry_by_id(worry_id)
                if worry is None:
                    print(f"[WS] resolve_worry: id={worry_id} not found")
                    continue

                worry["resolved_text"] = resolved_text
                worry["resolved_at"] = time_str
                worry["is_resolved"] = True

                print(f"[WS] Worry resolved: {worry_id}")

                total, unresolved = get_counts()
                broadcast(
                    {
                        "type": "worry_resolved",
                        "worry": worry_to_public(worry),
                        "total_count": total,
                        "unresolved_count": unresolved,
                    }
                )

            # -----------------------------
            # 3) 월 디스플레이 상의 별 선택
            # -----------------------------
            elif msg_type == "select_star":
                sky_coord = msg.get("sky_coord") or {}
                try:
                    u = float(sky_coord.get("u", 0.5))
                    v = float(sky_coord.get("v", 0.5))
                except ValueError:
                    print("[WS] select_star: invalid sky_coord")
                    continue

                worry = find_nearest_worry_by_sky_coord(u, v)
                if worry is None:
                    # 선택할 수 있는 별이 없을 때 (고민이 하나도 없다면)
                    try:
                        ws.send(
                            json.dumps(
                                {
                                    "type": "worry_detail",
                                    "worry": None,
                                },
                                ensure_ascii=False,
                            )
                        )
                    except Exception as e:
                        print(f"[WS] Failed to send empty worry_detail: {e}")
                    continue

                print(f"[WS] select_star -> nearest worry id={worry['id']}")

                try:
                    ws.send(
                        json.dumps(
                            {
                                "type": "worry_detail",
                                "worry": worry_to_public(worry),
                            },
                            ensure_ascii=False,
                        )
                    )
                except Exception as e:
                    print(f"[WS] Failed to send worry_detail: {e}")

            else:
                print(f"[WS] Unknown message type: {msg_type}")
                continue

    except Exception as e:
        print(f"[WS] Error: {e}")
    finally:
        clients.discard(ws)
        print("[WS] Client disconnected. Total:", len(clients))


# ============================================================
# HTTP 라우트 (디버그용)
# ============================================================


@app.route("/worries")
def get_all_worries():
    """
    디버깅/테스트용: 현재 서버에 저장된 고민 상태 전체를 JSON으로 반환.
    """
    return jsonify([worry_to_public(w) for w in worries])


if __name__ == "__main__":
    # 필요에 따라 debug=False, 포트 변경 등 조정
    app.run(debug=True, host="0.0.0.0", port=5000)
