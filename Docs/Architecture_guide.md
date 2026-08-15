# 게임 시스템 아키텍처 가이드 (Single Scene & Prefab Container)
=====================================================

본 프로젝트는 씬(Scene) 전환 없이 하나의 글로벌 씬에서 프리팹(Prefab)을 교체하며 진행되는 구조를 가집니다. 각 시스템 간의 결합도를 낮추고 유지보수성을 높이기 위해 **계층형 매니저 구조**, **이벤트 기반(옵저버 패턴) 통신**, 그리고 **독립적인 컨테이너 관리 패턴**을 채택했습니다.

1\. 핵심 설계 원칙
------------

*   **단일 책임 원칙:** 각 매니저와 시스템은 자신에게 할당된 명확한 하나의 역할만 수행합니다.
    
*   **이벤트 기반 통신 (No God Class):** 특정 매니저가 다른 시스템을 직접 호출(Direct Call)하여 지시하지 않습니다. 오직 '상태 변경'을 방송(Broadcast)하고, 각 시스템이 이를 수신하여 스스로 동작합니다.
    
*   **독립적인 공간 관리 (Container Pattern):** 매니저(GM, RoundManager)는 내부적으로 물리적인 씬이나 공간을 소유하지 않습니다. 각 하위 실무 시스템이 글로벌 씬(Scene)에 마련된 전용 빈 오브젝트(Container)를 직접 관리합니다.
    

* * *

2\. 매니저 및 시스템 역할 분담
-------------------

| **분류** | **컴포넌트 이름** | **역할 및 책임** | **비유** |
| --- | --- | --- | --- |
| **최상위 관리자** | `SystemBootstrap` (GM) |   **게임 전체의 수명과 상태 통제**      • 시스템 프리팹 로드 및 초기화      • 로비 ↔ 인게임 상태 전환 결정 및 방송   | 총괄 디렉터 |
| **인게임 관리자** | `RoundManager` |   **인게임 내부의 타이밍과 진행 전담**      • 라운드 시작/종료, 제한 시간 관리      • **공간을 소유하지 않음!** 오직 상태(Event)만 방송   | 현장 진행 요원 |
| **실무 시스템** | `UI_System` |   **화면 출력 및 유저 입력 수신**      • 캔버스(Canvas) 하위의 UI 요소 ON/OFF 관리   | 전광판 관리팀 |
|  | `Map_System` |   **무대 환경(프리팹) 생성 및 제거**      • 방송 수신 시 `[Map_Container]` 하위에 맵 프리팹 Instantiate/Destroy   | 무대 세팅팀 |
|  | `Network_System` |   **플레이어 스폰 및 통신 동기화**      • 방송 수신 시 `[Player_Container]` 하위에 2인 플레이어 캐릭터 프리팹 생성 및 위치 동기화   | 배우 관리팀 |

* * *

3\. Hierarchy 공간(Container) 관리 구조
---------------------------------

`RoundManager` 내부에서 맵이나 캐릭터가 생성되는 것이 아닙니다. Unity의 **글로벌 Scene**에 각 시스템을 위한 빈 오브젝트(Container)를 미리 만들어두고, 각 시스템(Map, Network 등)이 방송을 들으면 자기 구역을 스스로 세팅합니다.

Plaintext

    ▼ [Global_Scene]
        ▼ [System_Containers]
            - SystemBootstrap (GM)
            - RoundManager
            - Map_System
            - Network_System
            - UI_System
        ▼ [Environment_Container]
            ▼ [Map_Container]  <-- Map_System이 판타지 주방 맵 프리팹을 소환하는 구역
                - (Spawned) Fantasy_Kitchen_Round1
        ▼ [Entity_Container]
            ▼ [Player_Container] <-- Network_System이 2명의 요리사 플레이어 프리팹을 소환하는 구역
                - (Spawned) Chef_Player_1
                - (Spawned) Chef_Player_2

* * *

4\. 핵심 작동 흐름 (프리팹 스폰 시나리오)
--------------------------

시스템 간의 의존성을 없애기 위해 명령이 아닌 \*\*'신호(Event)'와 '자율적 행동'\*\*으로 스폰 흐름이 이어집니다.

1.  **\[상태 방송\] RoundManager:** "타이머 세팅 완료! 1라운드 주방 세팅 시작!" (이벤트 방송 발생)
    
2.  **\[실무 수행\] Map\_System:** 방송을 듣고 이전 라운드의 맵을 지운 뒤, 1라운드용 판타지 주방 맵 프리팹을 Instantiate 하여 `[Map_Container]` 아래에 넣습니다.
    
3.  **\[실무 수행\] Network\_System:** 방송을 듣고 2명의 플레이어 캐릭터 프리팹을 Instantiate 하여 `[Player_Container]` 아래에 넣고 정해진 스폰 위치로 이동시킵니다.
    
4.  **\[실무 수행\] UI\_System:** 방송을 듣고 'ROUND 1' 텍스트와 타이머 UI를 화면에 켭니다.
    
