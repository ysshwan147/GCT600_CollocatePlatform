import pygame
import random
import math

# 화면 크기
WIDTH, HEIGHT = 1280, 720

BRIGHT_MAX = 255
BRIGHT_INIT = 100          # 새로 생긴 별 밝기
BRIGHT_THRESHOLD = 220     # 이 이상이면 "밝은 별"로 취급 (조금 낮춰줌)

class Star:
    def __init__(self, x, y, idx, brightness=BRIGHT_INIT, radius=12):
        self.x = x
        self.y = y
        self.idx = idx          # 몇 번째 별인지 (그리진 않음)

        self.base_brightness = float(brightness)   # 키 입력으로 조절되는 기본 밝기
        self.current_brightness = float(brightness)

        self.radius = radius

        # 반짝임 파라미터
        self.flicker_amp = 35.0                        # 반짝임 진폭
        self.flicker_speed = random.uniform(1.0, 3.0)  # 속도 (rad/sec)
        self.flicker_phase = random.uniform(0.0, 2.0 * math.pi)

    def brighten_to_max(self):
        # 완전 255로 올리면 flicker가 덜 보이니까 살짝 여유 두기
        self.base_brightness = 220.0

    def update(self, dt):
        # flicker용 위상 증가
        self.flicker_phase += self.flicker_speed * dt

        # -1~1 사인파
        flicker = math.sin(self.flicker_phase) * self.flicker_amp

        # base + flicker 를 0~255로 클램핑
        val = self.base_brightness + flicker
        val = max(0.0, min(255.0, val))
        self.current_brightness = val

    def draw(self, surface):
        # 팔각별(8-point star) 폴리곤 그리기
        # -> 16개의 점 (바깥/안쪽 반지름 번갈아)
        b = int(self.current_brightness)
        color = (b, b, b)
        points = []

        outer_r = self.radius
        inner_r = self.radius * 0.35  # 작게 해서 끝을 더 날카롭게

        # 8개의 팔 → 16개 점, 각도 간격 = 2π/16 = π/8
        for i in range(16):
            angle = i * (math.pi / 8) - math.pi / 2  # 위쪽에서 시작
            r = outer_r if i % 2 == 0 else inner_r
            px = int(self.x + math.cos(angle) * r)
            py = int(self.y + math.sin(angle) * r)
            points.append((px, py))

        pygame.draw.polygon(surface, color, points)


def load_moon_phases(path: str, target_size=400):
    """
    moon_full.png를 4x4 그리드라고 가정하고,
    '위쪽 2줄(=전체 이미지의 상단 절반)'만 사용해서 8단계 위상으로 만듦.
    """
    sheet = pygame.image.load(path).convert_alpha()
    sheet_w, sheet_h = sheet.get_width(), sheet.get_height()
    cols, rows = 4, 4
    frame_w = sheet_w // cols
    frame_h = sheet_h // rows

    phases = []
    for row in range(rows // 2):       # 위쪽 절반만 사용
        for col in range(cols):
            rect = pygame.Rect(col * frame_w, row * frame_h, frame_w, frame_h)
            sub = sheet.subsurface(rect).copy()
            # 정사각형으로 리사이즈
            sub = pygame.transform.smoothscale(sub, (target_size, target_size))
            phases.append(sub)

    return phases  # [0] = 가장 어두운, [7] = 가장 밝은 위상이라고 가정


def compute_moon_phase_value(stars, num_phases):
    """
    전체 별 중 '밝은 별' 비율에 따라 달 위상 실수값(0 ~ num_phases-1)을 반환.
    flicker에 안 휘둘리게 base_brightness 기준으로 판단.
    """
    if not stars or num_phases <= 1:
        return 0.0

    bright_count = sum(1 for s in stars if s.base_brightness >= BRIGHT_THRESHOLD)
    ratio = bright_count / len(stars)  # 0.0 ~ 1.0
    ratio = max(0.0, min(1.0, ratio))

    return ratio * (num_phases - 1)


def draw_moon(screen, moon_phases, phase_value):
    """phase_value(실수)를 이용해 두 프레임을 cross-fade로 그리기."""
    num_phases = len(moon_phases)
    if num_phases == 0:
        return

    # 0 ~ num_phases-1 범위로 제한
    phase_value = max(0.0, min(num_phases - 1, phase_value))

    idx0 = int(math.floor(phase_value))
    idx1 = min(idx0 + 1, num_phases - 1)
    t = phase_value - idx0  # 0~1 사이

    moon0 = moon_phases[idx0]
    moon_rect = moon0.get_rect(center=(WIDTH // 2, HEIGHT // 2))

    # 첫 번째 phase는 항상 불투명
    moon0.set_alpha(255)
    screen.blit(moon0, moon_rect)

    # 다음 phase를 t 비율로 섞기
    if idx1 != idx0 and t > 0.0:
        moon1 = moon_phases[idx1]
        moon1.set_alpha(int(255 * t))
        screen.blit(moon1, moon_rect)
        # 다음 프레임을 위해 알파 복구
        moon1.set_alpha(255)


def main():
    pygame.init()
    screen = pygame.display.set_mode((WIDTH, HEIGHT))
    pygame.display.set_caption("Star Wall Display with Smooth Moon Phases")
    clock = pygame.time.Clock()

    # 달 이미지 로드 & 위상 분할
    moon_phases = load_moon_phases("moon_full.png", target_size=360)
    num_phases = len(moon_phases)

    stars = []

    # 달 현재 위상 (실수 인덱스)
    moon_phase_value = 0.0

    running = True
    while running:
        # dt: 초 단위
        dt = clock.tick(60) / 1000.0

        # ----- 이벤트 처리 -----
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                running = False

            elif event.type == pygame.KEYDOWN:
                if event.key == pygame.K_ESCAPE:
                    running = False

                # 'a' 키: 별 하나 추가
                elif event.key == pygame.K_a:
                    x = random.randint(40, WIDTH - 40)
                    y = random.randint(40, HEIGHT - 40)
                    idx = len(stars)
                    stars.append(Star(x, y, idx))

                # 숫자 키 0~9: 해당 번호의 별 밝기 최대로 (있으면)
                elif pygame.K_0 <= event.key <= pygame.K_9:
                    num = event.key - pygame.K_0  # 0~9
                    if 0 <= num < len(stars):
                        stars[num].brighten_to_max()

        # ----- 업데이트 -----
        for s in stars:
            s.update(dt)

        # 달 위상의 목표값 계산
        target_phase = compute_moon_phase_value(stars, num_phases)
        # 서서히 따라가도록 lerp
        moon_phase_value += (target_phase - moon_phase_value) * 0.05

        # ----- 화면 그리기 -----
        screen.fill((0, 0, 0))   # 밤하늘

        # 팔각별들 그리기
        for s in stars:
            s.draw(screen)

        # 달 그리기 (부드럽게 phase 변경)
        draw_moon(screen, moon_phases, moon_phase_value)

        pygame.display.flip()

    pygame.quit()


if __name__ == "__main__":
    main()
