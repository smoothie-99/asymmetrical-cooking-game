# Unity Editor 작업 목록

코드 변경 후 Unity Inspector/Editor에서 직접 해야 하는 작업들을 누적 관리합니다.

---

## 🔴 필수 (기능 동작에 필요)

### [PlateItem] 시각적 재료 쌓기 설정
- 재료 프리팹 완성 후 진행
- Plate 프리팹 열기
- 접시 표면 위치에 빈 자식 오브젝트 생성 → 이름: `StackAnchor`
- PlateItem 컴포넌트 → **Visual Stacking** 섹션:
  - `Stack Anchor` 에 `StackAnchor` 오브젝트 할당
  - `Default Stack Layer Height`: `0.05`
  - `Ingredient Visuals` 리스트에 재료별 항목 추가

### [ServingStation] Recipe/Guest 연결
- 스테이지 작업 시 진행
- `Recipe Requirement`: 해당 스테이지의 `RecipeRequirementSO` 할당
- `Guest Data`: 해당 손님의 `GuestDataSO` 할당

---

## 🟡 남은 Inspector 설정

### [PotStation] PotBox 프리팹 설정
- ⏳ **모델 완성 후:**
  - 냄비 안에 Cylinder 프리미티브로 액체 메시 생성 → `Liquid Transform` 할당
  - `Liquid Min/Max Scale Y` 조정
  - `Ingredient Visuals` 리스트 채우기 (재료별 시각 프리팹)
  - `Fire Effect` 파티클 오브젝트 할당

### [SoupBowlItem] 국그릇 프리팹 신규 생성
- ⏳ **아트 에셋(그릇 메시) 준비 후:**
  - 그릇 메시 + `Rigidbody` + `Collider` 구성
  - `NetworkObject` + `NetworkTransform` 추가
  - `SoupBowlItem` 스크립트 부착
  - `dishType` Inspector 설정 (기본값: `Soup`)
  - Fusion NetworkPrefabTable에 등록

### [ClamOpenItem] Sleep Powder Effect
- ClamOpenItem 프리팹 → **Trap Settings** 섹션:
  - `Sleep Powder Effect`: 파티클 오브젝트 할당

---

## ✅ 완료

- [EarplugsItem] 프리팹 생성 + `EarplugsItem.cs` + `NetworkObject` 추가
- [SpecialEquipmentHolder] 귀마개 거치대 설정
- [ManualBookItem / ColdPouchItem] 거치대 불필요 확인
- [CookingMasterHandsManager] WearPoint 할당
- [CutTomatoItem] 프리팹 생성 + `CutTomatoItem.cs` + `NetworkObject` 추가
- [ServingStation] Placement Point / Height Offset / IsTrigger 설정
- [TomatoItem] Cut Result Prefabs[0] = CutTomatoItem, Counts[0] = 2
- [SoupBowlItem] PotBox 재설정 완료 (PickableItem 제거, NetworkBehaviour 스테이션으로 전환)
- [SleepyJewelClamItem] → ClamClosedItem / ClamOpenItem 으로 분리 대체
- [조개 시스템] JewelItem×5, SlicedClamMeatItem, DicedClamMeatItem, ClamMeatItem, ClamOpenItem, ClamClosedItem 프리팹 생성 완료
- [SlimeMob] 프리팹 생성 + NetworkTransform/Rigidbody 설정 완료
- [Fusion NetworkPrefabTable] 11개 등록 완료
