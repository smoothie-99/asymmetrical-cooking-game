# 🚀 프로젝트 네트워크 개발 및 리팩토링 가이드 (Antigravity 용)

본 문서는 Photon Fusion 2 Shared Mode 표준 패턴 및 프로젝트의 2차 리팩토링 지침을 담고 있습니다. Antigravity는 향후 코드를 작성하거나 수정할 때 본 가이드의 규칙을 최우선으로 준수해야 합니다.

---

## 1. 씬 검색 지침 (Anti-Search Policy)
멀티플레이 환경에서 씬 전체를 검색하는 방식은 성능 저하 및 엉뚱한 플레이어 참조 버그를 유발합니다.
- **❌ 금지**: `FindFirstObjectByType<T>()`, `GameObject.FindWithTag("Player")`
- **✅ 권장**: 
  - `[SerializeField]`를 통한 직접 참조 전달.
  - `Interact(player)`와 같이 매개변수를 통한 객체 주입.
  - 다수 객체 중 로컬 플레이어 검색 시: `FindObjectsByType<T>(...).FirstOrDefault(x => x.HasStateAuthority)`

## 2. 객체 삭제 지침 (Despawn Policy)
네트워크 객체(NetworkObject)를 일반 `Destroy()`로 삭제하면 타 클라이언트에서 동기화가 깨집니다.
- **❌ 금지**: `Destroy(gameObject)` (네트워크 객체 대상)
- **✅ 권장**: 
  ```csharp
  if (Object != null && Object.IsValid && Runner != null)
      Runner.Despawn(Object);
  else
      Destroy(gameObject); // 네트워크 미연결(Sandbox) 시 fallback
  ```

## 3. 스폰 중계 지침 (Spawn Hub Pattern)
비권한(Client) 객체가 직접 `Runner.Spawn()`을 호출하면 중복 생성되거나 실패합니다.
- **❌ 금지**: 각 객체에서의 독립적인 `Runner.Spawn()` 호출
- **✅ 권장**: **MapGenerator**를 스폰 허브로 활용
  - 예: `MapGenerator.Instance.RequestItemSpawn(prefab, position, playerId)`
  - Master Client(권한자)가 RPC를 수신하여 중앙에서 생성하는 패턴 준수.

## 4. 실행 루프 분리 (Logic Separation)
입력 처리와 데이터 초기화/전송 루프를 엄격히 분리합니다.
- **Update()**: 로컬 입력 처리 및 즉각적인 시각 피드백(Local UI 등) 담당.
- **FixedUpdateNetwork()**: 네트워크 상태([Networked] 변수) 갱신 담당.

## 5. 도메인 언어 통일 (Naming Convention)
아이템 및 스테이션 이름에서 테마 수식어를 제거하고 기능적 의미만 남깁니다.
- `SleepyClamItem` → **ClamItem**
- `CloudTomatoItem` → **TomatoItem**
- `MimicTrashCan` → **TrashCan**
- `PurifyingSlime` → **WashingStation**

## 6. 절단 시스템 (Slicing Policy)
- **❌ 금지**: EzySlice 등을 이용한 실시간 메시 절단 (NetworkObject 비생성으로 인한 동기화 불가)
- **✅ 권장**: 미리 제작된 choppedPrefab을 `Runner.Spawn()`하는 방식으로 대체.
- ISliceable 인터페이스 사용 시 `bool CanChop` 프로퍼티 활용.

---
**Antigravity 지침**: 위 가이드를 숙지하고, 모든 코드 제안 시 이 패턴을 벗어나지 않도록 주의할 것.
