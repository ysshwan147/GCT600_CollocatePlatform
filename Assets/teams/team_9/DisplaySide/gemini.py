from google import genai
import os

# 설정
os.environ["GEMINI_API_KEY"] = ""
client = genai.Client()


def summarize_context(text):
    response = client.models.generate_content(
        model="gemini-2.5-flash", contents=f"다음은 고민과 해결의 모음이야. 한줄로 요약하고, 가장 관련있는 명언 한줄 작성해봐. {text}"
    )
    return response.text
