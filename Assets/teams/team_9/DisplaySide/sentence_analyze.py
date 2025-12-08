from transformers import pipeline

# 1) 멀티링구얼 zero-shot 분류용 모델 (NLI 기반)
MODEL_NAME = "joeddav/xlm-roberta-large-xnli"

# 2) zero-shot classification 파이프라인 생성
classifier = pipeline(
    "zero-shot-classification",
    model=MODEL_NAME,
    tokenizer=MODEL_NAME,
    device=0,  # GPU 쓰려면 0으로 바꾸기 (cuda:0)
)

# 3) 우리가 쓰는 5개 카테고리 (설명 문장 형태로)
CANDIDATE_LABELS = [
    "애정 고민",
    "진로·취업·학업 고민",
    "경제적 고민",
    "우울·정신건강·스트레스·정체성 고민",
    "가족 관계 고민",
]

# 4) 라벨 → 색상 매핑
LABEL_TO_COLOR = {
    "애정 고민": "red",   # 애정
    "진로·취업·학업 고민": "blue",   # 진로/취업/학업
    "경제적 고민": "white",   # 경제
    "우울·정신건강·스트레스·정체성 고민": "yellow",
    "가족 관계 고민": "black",   # 가족
}


def classify_sentence(text: str):
    """
    한글 문장을 5개 카테고리 중 하나로 분류.
    반환: (라벨문장, 색이름, 확률, 전체 결과 dict)
    """
    # 한국어 템플릿으로 바꾸면 조금 더 잘 맞을 수 있음
    hypothesis_template = "이 문장은 {}에 관한 내용이다."

    result = classifier(
        text,
        CANDIDATE_LABELS,
        multi_label=False,           # 하나만 고르기
        hypothesis_template=hypothesis_template,
    )

    # result 예시:
    # {
    #   "sequence": "...",
    #   "labels": ["진로·취업·학업 고민", "경제적 고민", ...],
    #   "scores": [0.82, 0.10, ...]
    # }
    best_label = result["labels"][0]
    best_score = float(result["scores"][0])
    color = LABEL_TO_COLOR[best_label]

    return best_label, color, best_score, result


if __name__ == "__main__":
    while True:
        print('고민을 입력하세요')
        s = input()
        label, color, score, _ = classify_sentence(s)
        print(f"문장: {s}")
        print(f" → 라벨: {label} / 색: {color} / score={score:.3f}")
        print("-" * 80)
