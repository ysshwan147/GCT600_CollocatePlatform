# Team 9

## 팀원
- 김병준
- 강한결
- 양석환
- 우지은

## 개요
본 폴더는 Team 9의 Unity 콘텐츠를 포함합니다.


## 실행 방법

### 로컬 PC 설정 (app_moon.py)

`Assets/teams/team_9/setup` 디렉토리에 requirements.txt 파일이 있습니다.
```bash
cd Assets/teams/team_9
pip install -r setup/requirements.txt

cd DisplaySide
python app_moon.py
```

### 월 디스플레이 실행 방법

웹 브라우저에서 아래 주소로 접속합니다.

로컬 PC에서 확인할 경우:
http://localhost:5000

HMD 또는 외부 디바이스에서 확인할 경우:
http://<PC_IP>:5000

### Unity 설정

#### [1] Whisper (음성 인식) 설정

본 프로젝트는 [whisper.unity](https://github.com/Macoron/whisper.unity) 패키지를 사용합니다.

Unity Package Manager에서 다음 주소로 설치:
```bash
https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity
```

[여기](https://huggingface.co/ggerganov/whisper.cpp)에서 모델 파일을 다운로드한 후, `Assets/StreamingAssets/Whisper`에 복사합니다.

#### [2] IP 주소 설정

Inspector에서 ```NetworkManager``` 오브젝트의 다음 항목을 수정합니다.
```
Server URL: ws://<PC_IP>:5000/ws
```


#### [3] URP Decal Renderer 설정

`Assets/Settings/PC_Renderer`와 `Assets/Settings/Mobile_Renderer`에 Decal Renderer Feature를 추가합니다. (Add Renderer Feature → Decal)



#### [4] 메인 씬 실행

`Assets/Teams/Team_9/Scenes/Main_AR.unity`을 실행합니다.


## 사용한 에셋 및 라이선스

[백자 3D 모델]

Unity Asset Store:
https://assetstore.unity.com/packages/3d/props/interior/korea-craft-design-recommend-goods-for-korean-restaurant-252231

------------------------------------------------------------

[깨지는 효과음 (크랙 효과)]

Thanks to, Mike Koenig  
From: http://soundbible.com/1658-Mirror-Breaking.html  
Distributor: Stop worrying about music & free sound effects with Mewpot’  
https://www.mewpot.com  
추천인 코드: mewc.at/ref/4gn5  
위의 URL을 통해 구독하시면 2개월 추가 혜택이 제공됩니다.

------------------------------------------------------------

[로딩 아이콘]

https://www.flaticon.com/kr/free-icons/  
로딩 아이콘 제작자: rikhwan fatih - Flaticon

------------------------------------------------------------

[바늘 3D 모델]

https://www.blenderkit.com/asset-gallery-detail/bbc7c51b-d4e7-4211-ac54-88da78054855/

------------------------------------------------------------
