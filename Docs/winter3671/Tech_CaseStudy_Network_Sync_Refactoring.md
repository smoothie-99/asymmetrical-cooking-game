# 기술 사례 연구: 네트워크 게임 상태 동기화 및 UI 시스템 리팩토링 (Refactoring Case Study)

본 문서는 Unity와 Photon Fusion 2(Shared Mode)를 활용한 멀티플레이 게임 개발 과정에서 발생한 상태 동기화 문제를 분석하고, 이를 해결하기 위해 도입한 아키텍처 정규화 과정을 기록합니다.

---

## 1. 프로젝트 배경 및 문제 정의 (Context & Problem)

### 1-1. 기술 스택
- **Engine**: Unity 2022.3+
- **Network**: Photon Fusion 2 (Shared Mode)
- **Architecture**: Single Scene Navigation (단일 씬 기반 폴더 전환 방식)

### 1-2. 발생한 주요 문제
1.  **UI 번쩍임 (UI Flickering)**: 게임 시작이나 다시하기 전환 시, 특정 화면이 켜졌다가 0.1초 만에 꺼지거나 로비 화면이 잠깐 노출되는 현상.
2.  **지휘권 중첩 (Multiple Sources of Truth)**: 로비용 상태(`LobbyMenuState`)와 인게임용 상태(`RoundState`)가 분리되어 있어, 상태 전환 시 두 데이터가 일치하지 않는 짧은 'Dead Zone' 발생.
3.  **업데이트 감옥 (Update Jail)**: 다수의 UI 스크립트가 `Update()`문에서 매 프레임 네트워크 변수를 감시(Polling)하여 UI를 갱신함으로써 발생하는 비효율성 및 인종 조건(Race Condition).
4.  **커서 관리 이슈**: 네트워크 상태 전환 시 마우스 커서의 잠금/해제 상태가 동기화되지 않아 조작 불가 상황 발생.

---

## 2. 근본 원인 분석 (Root Cause Analysis)

### 2-1. 솔로 플레이 레거시의 흔적
초기 개발 단계에서 솔로 플레이(Debug Mode)를 위해 작성된 `Update()` 기반의 체크 로직이 멀티플레이 환경에 그대로 이식되면서 문제 발생. 
- **솔로**: 즉각적인 반응을 위해 매 프레임 체크하는 것이 효율적.
- **멀티**: 네트워크 레이턴시로 인해 데이터 변경 시점이 클라이언트마다 다르므로, 매 프레임 체크는 상태 불일치를 심화시킴.

### 2-2. 씬 전환 방식의 잔재
과거 '로비 씬'과 '인게임 씬'이 분리되었던 구조에서는 각 씬의 매니저가 주도권을 가지는 것이 당연했으나, **단일 씬(Single Scene)** 구조로 변경된 후에도 두 지휘관(Manager)이 공존하며 불필요한 '상태 인수인계' 과정에서 버그 유발.

---

## 4. 단계별 구현 상세 (Implementation Steps)

### Phase 1: 기초 공사 - 중앙 지휘소(`RoundManager`) 개편
- **상태 통합**: `LobbyPlayer`의 개별 메뉴 상태를 폐기하고, `RoundManager`의 `RoundState`에 `Waiting`(로비), `Selection`(지도 선택), `ReadyConfirmation`(준비창)을 추가하여 게임의 전구간을 한 곳에서 관리.
- **방송 시스템**: `OnPhaseEntered`라는 `Action<RoundState>` 이벤트를 정의. 네트워크 변수가 변경될 때 `[OnChangedRender]`를 통해 모든 클라이언트의 UI 핸들러를 즉시 호출.

### Phase 2: 권한 위임 - 메신저 역할 정립(`LobbyPlayer`)
- **브릿지 구축**: 기존에 UI를 직접 켜고 끄던 `RPC_RequestLobbyMenuState`가 이제는 `RoundManager.ChangeState()`를 호출하도록 변경.
- **데이터 일원화**: 현재 선택된 스테이지 번호(`SelectedStage`) 등의 핵심 데이터를 `LobbyPlayer`에서 `RoundManager`로 옮겨 모든 인원이 동일한 지도를 보도록 보장.

### Phase 3: 내부 소탕 - UI 업데이트 로직 제거
- **Update 감옥 탈출**: `MainMenuUI`, `RoundSelectionUI`, `InGameMenuUI` 등 모든 UI 스크립트에서 매 프레임 상태를 체크하던 `Update()` 로직을 전면 삭제.
- **이벤트 구독**: 각 UI는 `OnEnable` 시점에 `RoundManager`의 이벤트를 구독하고, 상태 변화 통보가 올 때만 `SetActive`를 실행하여 성능과 시각적 안정성을 확보.

### 보너스: 초기화 안전장치 (Initialization Safety)
- **문제 해결**: 유니티의 스크립트 실행 순서에 따라 매니저보다 UI가 먼저 깨어날 경우 이벤트 구독을 놓치는 현상 발생.
- **대응**: `WaitAndSubscribe` 코루틴을 도입. 매니저의 인스턴스가 생성될 때까지 대기한 후 안전하게 구독하도록 설계하여 실행 순서 버그를 원천 차단.

---

## 5. 최종 개선 결과 (Final Results)

1.  **안정적인 UI 전환**: 상태 머신(FSM)이 정교화되어 UI 간의 충돌 및 번쩍임 현상 해결.
2.  **네트워크 부하 감소**: 매 프레임 네트워크 오브젝트에 접근하던 로직을 제거하여 성능 최적화.
3.  **유지보수 용이성**: 모든 흐름이 `RoundManager` 하나에 정의되어 있어 새로운 단계(Phase) 추가 시 확장이 용이함.
4.  **완전한 동기화**: "하나의 게임, 2개의 시점"이라는 철학에 맞게 모든 플레이어가 물리적으로 동일한 게임 페이즈를 공유함.

---

## 6. 회고 (Retrospective)
네트워크 게임에서 가장 위험한 것은 **"어딘가에서 누군가 매 프레임 값을 감시하고 있는 것"**임을 깨달음. 아키텍처는 환경(단일 씬)에 맞게 단순화되어야 하며, 데이터는 흐르는 것이 아니라 **전파(Broadcast)** 되어야 함을 인지함. 이번 리팩토링을 통해 **"이벤트 기반 아키텍처"**가 멀티플레이 게임의 사용자 경험(UX)에 얼마나 결정적인 역할을 하는지 증명함.

---
**Date**: 2026-03-13  
**Author**: 박정훈
