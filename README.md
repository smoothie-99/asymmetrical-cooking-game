# 내 요리를 부탁해 (Unity Project)

2인 협동 / 정보 비대칭 요리 시뮬레이션

- P1(레시피 마스터): 주문서 해독 + 레시피/재료 주의사항 브리핑
- P2(쿠킹 마스터): 실제 조리 + 특수 재료 기믹 처리

---

## 개발 환경

- Unity: 6.3 LTS (6000.3.x)
- 3D Low-Poly + Pixel Art Filter Texture
- Physics-based Interaction (RigidBody 기반)
- Multiplayer: Photon Fusion 2 Shared Mode + Photon Voice
- Backend: Java 17, Spring Boot, PostgreSQL, JWT

---

## 프로젝트 열기 (Quick Start)

1. Unity Hub → **Open** → 레포의 Unity 프로젝트 폴더 선택
2. 최초 1회, Unity에서 아래 설정 확인:
   - `Edit > Project Settings > Editor`
     - **Version Control Mode: Visible Meta Files**
     - **Asset Serialization: Force Text**
3. 에셋/씬/프리팹 추가 후에는 `.meta` 포함하여 커밋/체크인(Plastic/Git 동일)

### 백엔드 로컬 실행

1. `Backend/gameserver/.env.example`을 `.env`로 복사하고 실제 값을 입력합니다.
2. 공개 저장소에 있던 기존 JWT 키는 사용하지 말고 32자 이상의 새 키를 발급합니다.
3. `Backend/gameserver`에서 `docker compose up --build`를 실행합니다.
4. 서버 상태는 `http://localhost:8107/actuator/health`에서 확인할 수 있습니다.

Unity의 `AuthManager`에서 `apiBaseUrl`을 실행 환경에 맞게 설정합니다. 데스크톱 개발 환경에서는
`COOKING_GAME_API_URL` 환경변수로 덮어쓸 수 있습니다. 값은 `/api`까지 포함해야 합니다.

```text
http://localhost:8080/api
https://your-domain.example/api
```

### 인증 토큰

- Access Token: 기본 15분, 일반 API 인증에만 사용
- Refresh Token: 기본 7일, `/api/auth/refresh`에서만 사용
- Refresh Token은 서버 DB에 원문 대신 SHA-256 해시로 저장하며 갱신할 때마다 회전
- Unity 클라이언트는 토큰을 `PlayerPrefs`가 아닌 현재 프로세스 메모리에만 보관

### 배포

- `Backend/gameserver/deploy.sh`: 이미지 빌드, Compose 갱신, Health Check 수행
- `Infra/nginx/cooking-game.conf.example`: HTTPS Reverse Proxy 예시
- Jenkins는 애플리케이션 Compose에서 분리했습니다. Docker 소켓을 마운트한 root Jenkins는
  호스트 root 권한과 동일한 위험이 있으므로 별도 Runner 또는 최소 권한 배포 계정을 사용합니다.

---

## 디렉토리 구조 (중요)

> 원칙: **우리 게임 파일은 `Assets/_Project` 아래에만 둔다.**
> 에셋스토어/외부 플러그인은 `Assets/ThirdParty`로 분리한다.

### `Assets/_Project/`

우리 게임의 모든 리소스가 들어가는 루트 폴더입니다.

#### `Assets/_Project/Art/`

그래픽 리소스 모음

- Models: 모델(FBX 등)
- Materials: 머티리얼
- Textures: 텍스처(픽셀/필터 포함)
- Animations / VFX / Shaders 등(필요 시 추가)
  **규칙:** 원본(PSD/BLEND 등)은 용량이 크므로 버전관리 정책에 맞게 관리(LFS 권장)하며, "최종 게임용"과 "원본"을 구분해 두는 것을 권장합니다.

#### `Assets/_Project/Audio/`

사운드 리소스 모음

- BGM / SFX로 구분 권장

#### `Assets/_Project/Data/` ⭐ 데이터 중심 설계 ⭐

**ScriptableObject 기반 게임 데이터**를 보관합니다.  
코드를 고치지 않고도 레시피/재료/타일을 추가하기 위한 “데이터 저장소” 역할입니다.

- `Data/Recipes/` : 레시피 정의(RecipeDefinition)
- `Data/Ingredients/` : 재료 정의(IngredientDefinition)
- `Data/Tiles/` : 맵 타일 정의(TileDefinition) / 절차생성 규칙 데이터

#### `Assets/_Project/Prefabs/`

재사용 가능한 오브젝트 프리팹 모음

- Kitchen: 조리대/도마/화구/제출구 등
- Ingredients: 재료(기본/특수) 프리팹
- UI: UI 프리팹(패널/버튼 등)
- Tools: 칼/냄비/웨어러블(귀마개) 등
  **규칙:** “메인 씬을 여러 명이 동시에 편집”하는 충돌을 줄이기 위해 프리팹 단위 작업을 권장합니다.

#### `Assets/_Project/Scenes/`

씬 파일 모음

- `00_Boot`: 초기화(싱글턴/서비스/네트워크/세이브 등)
- `01_Menu`: 로비/설정/매칭(필요 시)
- `10_Kitchen_Prototype`: 빠른 테스트용 임시 씬
- `20_Gameplay`: 실제 플레이 씬

---

## Scripts 구조 (`Assets/_Project/Scripts/`)

### `Editor/`

Unity Editor 전용 코드 (런타임 빌드에 포함되지 않게 분리)

- 커스텀 인스펙터, 레시피/재료 데이터 생성 툴 등
- **규칙:** `UnityEditor` 네임스페이스 사용 코드는 무조건 이 폴더에 넣어야 빌드 에러가 발생하지 않습니다.

### `Runtime/`

실제 게임 실행(빌드)에 포함되는 핵심 코드

- **`Core/`**: 프로젝트 공통 기반 레이어 (GameState 상태머신, 이벤트, 타이머 등)
  - _목표:_ 다른 폴더가 Core를 “참조”하는 구조로, 특정 기능에 강하게 종속되지 않도록 유지
- **`Interaction/`**: P2의 손/잡기/사용/물리 상호작용 (양손 인벤토리, 웨어러블 슬롯 등)
- **`Cooking/`**: 조리 시스템 (스테이션 + 썰기/끓이기 등 조리행위)
- **`Ingredients/`**: 재료 자체의 성질/기믹/페널티 (만드라고라, 슬라임 젤리 등)
- **`Orders/`**: P1 주문서/레시피 매칭 로직 (주문서 해독, 정답 판정 등)
- **`MapGen/`**: 그리드 기반 절차 생성 타일 배치 및 검증 로직
- **`UI/`**: P1 관전/오더 UI 및 P2 1인칭 HUD 등 화면 표시 로직

---

## 유니티 기본 설정 폴더 (절대 삭제 금지)

### `Assets/Settings/`

- URP Renderer, Quality 설정 등 유니티 프로젝트의 전역 그래픽/렌더링 세팅이 들어있는 폴더입니다.
- **주의:** 이 폴더를 지우거나 임의로 이동하면 프로젝트 전체 그래픽이 깨지므로(분홍색 머티리얼) 절대 건드리지 마세요.

---

## 외부 플러그인 관리

### `Assets/ThirdParty/`

- 에셋스토어에서 다운로드한 외부 플러그인이나 에셋은 임포트 직후 반드시 이 폴더 안으로 이동시켜 주세요.
- 루트(`Assets/`) 경로가 지저분해지는 것을 막고, 우리 코드(`_Project`)와 외부 코드를 완벽히 격리하기 위함입니다.

---

## 네이밍/관리 규칙 (추천)

- C# 스크립트: `PascalCase` (예: `CookingStation.cs`)
- 프리팹: `PF_` 접두사 권장 (예: `PF_CuttingBoard`)
- 머티리얼: `M_` (예: `M_KitchenCounter`)
- 텍스처: `T_` (예: `T_WoodPlank_Albedo`)
- 오디오: `SFX_`, `BGM_`
- ScriptableObject: `SO_` (예: `SO_Recipe_MandrakeSoup`)
- 씬: 숫자 접두사 + 이름 (예: `10_Kitchen_Prototype`)

---

## 협업 시 자주 터지는 문제 예방 ⭐

- `.meta` 파일은 **절대 삭제/누락 금지**
- **멀티플레이 코드 작성 주의:** Photon Fusion의 `[Networked]` 속성을 사용하고, `StateAuthority`와 `InputAuthority`를 명확히 분리하세요.
- 씬/프리팹 충돌이 자주 나면:
  - 프리팹화로 작업 단위 분리
  - (Plastic/Unity VCS 사용 시) 씬 파일 Checkout 및 Lock 기능 적극 활용
- “이번 주 목표” 같은 단기 스프린트에서는 스코프를 크게 키우지 않기
  - 절차 맵 생성, 특수재료 3종, 레시피 20개 같은 확장은 단계적으로 진행합니다.

---

## 콘텐츠 추가 가이드 (초기 기준)

### 레시피 추가

1. `Assets/_Project/Data/Recipes`에서 `RecipeDefinition` 생성
2. 재료 리스트/조리 단계/필수 조건 설정
3. P1 레시피북 UI에서 목록에 노출되는지 확인

### 특수 재료 추가

1. `Assets/_Project/Data/Ingredients`에 `IngredientDefinition` 생성
2. `Assets/_Project/Prefabs/Ingredients`에 재료 프리팹 생성
3. 프리팹에 해당 Behaviour 컴포넌트 부착(예: `MandrakeBehaviour`)
4. 실패 페널티와 점수 판정 조건 연결

---

## TODO (프로젝트가 커지면 고려)

- Assembly Definition(.asmdef)로 Runtime 모듈 분리(컴파일/의존성 관리)
- 데이터 에디터 툴(레시피 단계 편집기) 제작
- 자동 테스트/빌드(CI) 구성
