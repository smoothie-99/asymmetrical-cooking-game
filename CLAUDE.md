# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

Unity 멀티플레이 2인 협동 요리 시뮬레이션 게임.

- **Unity 버전**: 6000.3.9f1
- **P1 (RecipeMaster)**: 주문서 해독 + 레시피/재료 정보 브리핑 (별도 화면, 조리 안 함)
- **P2 (CookingMaster)**: 실제 조리 + 특수 재료 기믹 처리 (1인칭 물리 인터랙션)
- 개발 씬: `Assets/_Project/02_Develop/P1_Chef/P1_Sandbox.unity`
- 프로덕션 씬: `Assets/_Project/01_Production/Scenes/00_Main_Game.unity`
- 스크립트 루트: `Assets/_Project/03_Scripts/`

> **주의**: `README.md`는 초기 기획 문서로 Unity NGO로 기재되어 있으나, 실제 구현은 **Photon Fusion 2 Shared Mode** 기반입니다.

---

## 기술 스택

- **Photon Fusion 2 Shared Mode**: 항상 네트워크로 테스트. 로컬 fallback 코드는 제거 대상.
- **Photon Voice**: 음성채팅 + 푸시투톡 통합 (`NetworkLauncher`에서 `FusionVoiceClient` + `Recorder` 런타임 추가). `DeafnessAudioManager`가 귀마개 착용 시 청각 효과 담당.
- **StateAuthority** = 해당 오브젝트를 조작 중인 플레이어
- **`NetworkLauncher.SelectedJob`**: 로컬 플레이어의 역할(RecipeMaster / CookingMaster)을 저장하는 정적 변수. `GlobalNetworkState.IsLocalRecipeMaster()`가 이를 참조.
- UI: UI Toolkit, TextMeshPro — UI 담당 팀원 영역, 건드리지 않음

---

## 스크립트 모듈 구조

```
03_Scripts/
  Core/           — GlobalNetworkState, SystemManager, SoundManager (전역 싱글턴)
  Interactions/
    Interfaces/   — IInteractable, ICookable, ICuttable, ITool, IWashable, IWearable 등
    Items/        — PickableItem (베이스), KnifeItem, LadleItem, PastaItem, FleeingEnemy, 특수 재료(Special/gimmick/)
    Stations/     — CuttingStation, FireStation, ServingStation, PotStation, GeneralStation, BellInteractable, TrashCan, FloatingItemInteractable, SpecialEquipmentHolder
  Managers/       — GamePlayManager (요리 세션 타이머·판정)
  Map/            — MapGenerator, MapSystem, TileCatalog (절차적 맵 생성), FloatingPoint (재료 디스펜서)
  Mob/            — SlimeMob (네트워크 몹)
  Network/        — NetworkLauncher, PlayerSpawner, SandboxLauncher, LobbyPlayer
  Player/
    CookingMaster/ — CookingMasterHandsManager, CookingMasterMovement, CookingMasterCamera, CookingMasterHUD
    RecipeMaster/  — RecipeMasterController (P1 관전·오더 전달)
  RecipeMaster/   — P1 UI 로직: RecipeBookManager, IngredientBookManager, OrderSlide 등
  Score/          — PlateItem, SoupBowlItem, PotStation, DishData, ScoringEngine, RecipeRequirementSO, GuestDataSO
  Server/         — AuthManager, ServerConnection, AuthDTOs (백엔드 인증)
  UI/             — 각종 UI 패널 (EvaluationUI, TimerUI, MainMenuUI 등)
```

---

## 게임 흐름 (MetaState → CookingState)

```
SystemManager (로컬 MonoBehaviour) — 전체 라이프사이클 총괄
  └─ MetaState: Lobby → StageSelection → ReadyConfirmation
              → OrderDialogue → Cooking → Feedback → Result

GlobalNetworkState (NetworkBehaviour) — 네트워크 동기화 레이어
  └─ [Networked] CurrentMetaState / CurrentCookingState / CookTimer / IsSuccess / IsPaused

GamePlayManager (로컬 MonoBehaviour) — 요리 세션 시간·판정 담당
  └─ CookingState: Ready(카운트다운) → Cooking(타이머) → Submit(판정)
```

- `GlobalNetworkState`는 Fusion에 등록된 네트워크 오브젝트. `SystemManager`와 `GamePlayManager`는 이를 Proxy로 참조.
- `IsProxyValid` 패턴 (`Object != null && Object.IsValid`) — 아직 남아있는 Sandbox 분기, 제거 대상.

---

## 인터페이스 구조

```
IInteractable   — CanInteract / Interact / GetInteractionLabel / OnFocus / OnFocusLost
                  모든 상호작용 가능 오브젝트(스테이션, 아이템) 공통
ICookable       — CurrentCookState / CookInFire(heat)
                  불/냄비에서 익힐 수 있는 아이템
ICuttable       — GetGuidelineVisuals / ShowGuidelines / HideGuidelines / SelectedGuidelineIndex
                  도마에서 칼로 처리 가능한 아이템 (가이드라인 시각화 포함)
ITool           — ToolType (string)
                  도구 아이템 (KnifeItem 등)
IWashable       — CleanRatio / Wash(amount)
                  세척 가능 아이템
IWearable       — (마커 인터페이스, 구현 내용 없음)
                  착용 아이템 (EarplugsItem 등) — SpecialEquipmentHolder가 감지해 WearItem() 호출
IServable       — PlatedIngredients / IsEmpty / DishType / ClearDish()
                  ServingStation에 제출 가능한 요리 컨테이너 (PlateItem, SoupBowlItem)
```

> **실질적으로 교체된 인터페이스**: `ISliceable` → `PickableItem.CanChop` / `Chop(CuttingStation)` virtual 메서드. `ICutResultProvider` → `CutGuidelineVisual.resultPrefabs` / `resultCounts`. 파일은 아직 존재하나 신규 코드에서는 사용하지 않음.

---

## 핵심 클래스 위치

| 클래스 | 경로 |
|--------|------|
| `PickableItem` | `Interactions/Items/PickableItem.cs` |
| `PlateItem` | `Score/PlateItem.cs` |
| `SoupBowlItem` | `Score/SoupBowlItem.cs` |
| `PotStation` | `Score/PotStation.cs` |
| `DishRecord`, `IngredientRecord`, `CookingResult` | `Score/DishData.cs` |
| `PlatedIngredient` | `Score/PlatedIngredient.cs` |
| `ScoringEngine` | `Score/ScoringEngine.cs` |
| `RecipeRequirementSO` | `Score/RecipeRequirementSO.cs` |
| `GuestDataSO` | `Score/GuestDataSO.cs` |
| `ServingStation` | `Interactions/Stations/ServingStation.cs` |
| `CuttingStation` | `Interactions/Stations/CuttingStation.cs` |
| `FireStation` | `Interactions/Stations/FireStation.cs` |
| `SpecialEquipmentHolder` | `Interactions/Stations/SpecialEquipmentHolder.cs` |
| `GamePlayManager` | `Managers/GamePlayManager.cs` |
| `SystemManager` | `Core/SystemManager.cs` |
| `GlobalNetworkState` | `Core/GlobalNetworkState.cs` |
| `CookingMasterHandsManager` | `Player/CookingMaster/CookingMasterHandsManager.cs` |

---

## 요리 완성 흐름

```
재료 아이템 (PickableItem + ICookable 등)
  ├─ PlateItem.TryAddIngredient()  → PlatedIngredient 로 캡처 → 원본 Despawn
  └─ PotStation (자동 흡수)        → SoupBowlItem.FillFromPot() → PlatedIngredient 캡처

ServingStation
  └─ [E] 올려두기 / [F] 제출 (IServable 체크)
  └─ BuildDishRecord() → ScoringEngine.Evaluate(dish, recipe, guest)
  └─ GamePlayManager.FinishCookingSession(success, feedbackMessage)
```

### DishRecord / IngredientRecord

```csharp
// Score/DishData.cs
DishRecord       — { dishType, List<IngredientRecord> ingredients }
IngredientRecord — { itemType, category, containerType, contents[재귀],
                     cookState, cookingSeq[], cookingTime, cutMethod, flavor }
CookingResult    — { outcome(Perfect/Clear/Fail), feedbackMessage, deductionReasons[] }
```

- `cutMethod`: 절단 방법 문자열 ("포썰기", "깍뚝썰기" 등). `CutGuidelineVisual.cutName` 에서 옵니다.
- `cookingSeq`: 조리 단계 순서 목록. 절단 시 자동 기록, 나머지(가열·세척 등)는 아이템이 직접 추가.

### dishType

`PlateItem.dishType` / `SoupBowlItem.dishType` Inspector 필드에서 설정 (예: "Salad", "Soup").

---

## PickableItem

`Interactions/Items/PickableItem.cs` — 모든 집을 수 있는 아이템의 베이스 클래스.

- `metadata: Dictionary<string, string>` — 아이템별 추가 정보 저장 (`ingredientID`, `cutMethod` 등)
- `cookingSeq: List<string>` — 조리 단계 기록 (채점에 사용). 아이템이 직접 `Add()`.
- `CanChop / Chop(board)` — virtual. 도마 절단 가능 아이템은 override.
- `ICuttable` 기본 구현 내장 — 서브클래스가 `ICuttable` 선언 시 ShowGuidelines 등 자동 충족.

### 절단 기록 흐름

```
Rpc_Cut / LocalCut
  1. guide.cutName → cookingSeq.Add(cutName), metadata["cutMethod"] = cutName
  2. 결과물 Spawn → TransferStateTo() + cookingSeq / metadata 복사
  3. 원본 Despawn
```

`CutGuidelineVisual.cutName` 을 Inspector에서 설정해야 절단 방법이 기록됩니다.

---

## PlateItem (접시)

`Score/PlateItem.cs` — `PickableItem` + `IServable` 구현.

- 재료 ID 해결 우선순위: `metadata["ingredientID"]` → `ingredientID` 프로퍼티 → 필드 → `itemName` → `name`
- 시각적 쌓기: `stackAnchor` 기준, `ingredientVisuals` 매핑 → `RefreshStackVisuals()`
- 네트워크: `[Networked] NetworkArray<NetworkPlatedIngredient>` (PlateSlots, 최대 16)
- `consumeSourceObjectOnAdd = true` → 추가 즉시 원본 Despawn
- **StateAuthority는 Render()에서 SyncListFromNetworkState 호출 안 함** (cookingSeq 보존)

---

## PotStation (고정 냄비)

`Score/PotStation.cs` — `NetworkBehaviour` + `IInteractable`. 고정 가구.

- 재료가 트리거 안에 드롭되면 자동 흡수 (SoupBowlItem 제외)
- **[F]**: 젓기 → 조리 속도 UP
- **[E]**: `SoupBowlItem` 들고 있을 때 → 국물 전체 퍼냄 + 냄비 초기화
- 액체 시각화: `liquidTransform` (Cylinder). 피벗이 중심이므로 `p.y = liquidBottomY + scaleY`

---

## SoupBowlItem (국그릇)

`Score/SoupBowlItem.cs` — `PickableItem` + `IServable`. 들고 다니는 그릇.

- `FillFromPot(ingredients, soupState)` — PotStation에서 호출, 재료 일괄 담기
- 시각적 쌓기 없음 (PlateItem과 달리)
- `ClearDish()` → 내용물 초기화
- **프리팹 신규 생성 필요** (UNITY_TODO.md 참조)

---

## SpecialEquipmentHolder (특수장비 거치대)

`Interactions/Stations/SpecialEquipmentHolder.cs` — 이미 구현 완료.

- 벽에 설치, 장비 프리팹을 `displayPoint` 위치에 걸어둠
- **[E]**: 장비 착용(IWearable) 또는 집기 / 반납 시 원래 위치로 복귀

---

## 특수 재료 기믹 목록

`Interactions/Items/gimmick/` 하위에 재료별 폴더로 관리:

| 재료 | 폴더 | 주요 클래스 |
|------|------|------------|
| 베이컨 | `bacon/` | OilBaconItem, OilBaconPieceItem; `bacon/cabbage/`에 CabbageItem 계열 별도 포함 |
| 치즈 | `cheese/` | LavaCheeseItem |
| 조개 | `clam/` | ClamClosedItem, ClamOpenItem, ClamMeatItem, Sliced/DicedClamMeatItem, JewelItem |
| 달걀 | `egg/` | DragonEggItem (Red/Green/Yellow 변종) + 상태 기반 추상 계층 |
| 생선 | `fish/` | PhantomFishItem; 칼 타겟 기반 추상 계층 (`PhantomFishKnifeTargetBaseItem`, `PhantomFishTargetItem`, `PreparedPhantomFishBaseItem`, `DummyPhantomFishItem`) |
| 만드라고라 | `mandrake/` | MandrakeItem → CutMandrakeItem (몸통) / MandrakeHeadItem (머리) / SlicedMandrakeItem; 절단 시 머리·몸통 분리 |
| 미믹 | `mimic/` | MimicBoxItem (Bronze/Silver/Gold 변종, `MimicBoxVariantBaseItem` 상속) — 도구·플레이트를 먹으면 뱉어냄, 요리는 먹음; `MimicBoxSelector`(변종 선택), `MimicBoxSupplyItem`(공급), `MimicCubeMeatItem`(고기 결과물) |
| 버섯 | `mushroom/` | BoppyMushroomItem → Sliced/CubedBoppyMushroomItem |
| 양파 | `onion/` | WholeOnionItem → HalfOnionItem / MincedOnionItem |
| 호박 | `pumpkin/` | InvisiblePumpkinItem → InvisiblePumpkinSliceItem; `PumpkinCookableBaseItem` (추상 베이스) |
| 토마토 | `tomato/` | TomatoItem → CutTomatoItem |

`Special/` 하위에 장비 아이템: `EarplugsItem`, `ColdPouchItem`, `ManualBookItem`

---

## SlimeMob

`Mob/SlimeMob.cs` — `NetworkBehaviour`. 배회 AI 몬스터.

- **이동**: `[Networked] MoveDirection`을 StateAuthority만 갱신 → 모든 클라이언트가 동일 방향으로 Rigidbody 이동. `NetworkTransform` 또는 `NetworkRigidbody` 컴포넌트가 프리팹에 **필수**.
- **포획**: 트리거 내에 비·도구 `PickableItem`이 드롭되면 자신 위치로 끌어당김. 한 번에 1개.
- **IWashable 세척**: 포획 아이템이 `IWashable`이면 매 프레임 `Wash()` — `CleanRatio >= 100` 시 자동 방출 (ClamClosedItem 오염 해제도 이 경로).
- **용해**: 세척 완료 전 `meltThresholdTime` 초 초과 시 `ICookable` 아이템을 `Burned` 상태로 변환.
- **피격**: `TakeDamage(int)` API 존재. HP 0 → 포획 아이템 방출 + `dropPrefabs` 스폰 후 Despawn. KnifeItem 연동은 **미구현**.
- 칼 공격 없이 테스트할 때는 `HitSlime()` 직접 호출.

---

## 맵 시스템

`Map/MapSystem.cs`, `Map/MapGenerator.cs`, `Map/TileCatalog.cs`

- 그리드 기반 절차적 맵 생성. `TileCatalog` (ScriptableObject)에 타일 정의 등록.
- `SystemManager.useTestMap = true` 시 고정 맵 사용 (개발용).
- **네트워크 모델**: 방장(StateAuthority)만 맵을 생성하고 결과를 `[Networked] SyncedMapData (NetworkString<_256>)`에 직렬화. 비방장은 이 문자열을 받아 동일하게 렌더링.
- **`SeedRandom`**: `MapGenerator` 내부 전용 커스텀 난수 클래스. IL2CPP(빌드)와 Mono(에디터) 간 동일한 시퀀스 보장 — Unity `Random`을 쓰면 플랫폼 간 맵이 달라지므로 반드시 이 클래스를 사용.
- **`TileType` enum**: `Empty`, `General`, `CuttingBoard`, `Fireplace`, `Sink`, `Submission`, `TrashBin`, `Pot` + 재료 디스펜서(`TomatoBox`, `ClamBox` 등 13종). `FloatingPoint` 컴포넌트가 재료를 무한 복제 공급.

---

## 아이템 변환 원칙

| 변환 종류 | 방식 |
|-----------|------|
| 썰기 (분리/분할) | **Prefab 교체** — 원본 Despawn, 새 prefab Spawn |
| 조리 상태 변화 (굽기 등) | **모델 스왑** — 같은 GameObject에서 자식 모델 전환 |

`TransferStateTo(target)` 으로 조리 히스토리 전달 필수.
절단 시 `cookingSeq` / `metadata` 는 `Rpc_Cut`/`LocalCut` 에서 자동 복사.

---

## 채점 로직 (ScoringEngine)

**타 팀원 담당** — 이 코드베이스는 중립적으로 상태를 기록하는 역할만 함.
모든 값은 **string** 으로 통일 (`cookState`, `cutMethod`, `cookingSeq` 모두 문자열).

10단계 우선순위 오류 체크:
`EmptyDish → SpecialCondition → WrongDish → WrongMain → Burned → MissingIngredient → ExtraIngredient → WrongCookState → WrongCutting → WrongOrder`

결과: `PerfectClear / Clear / Fail`

---

## 미구현 항목

- **SoupBowlItem 프리팹 생성**: 아트 에셋 확보 후 Inspector 설정 필요
- **PotStation Inspector 연결**: `liquidTransform`, `floatingParent`, `ingredientVisuals` 등 PotBox.prefab에 할당
- **CutGuidelineVisual.cutName 설정**: TomatoItem, ClamMeatItem 등 기존 가이드라인 오브젝트에 Inspector에서 입력
- **cookingSeq 아이템별 기록**: 가열 완료·세척 등 절단 외 단계는 각 아이템이 직접 `cookingSeq.Add()` 구현 필요
- **ServingStation DespawnDish 안전 처리**: `ClearDish()` 호출 후 즉시 Despawn — RPC 타이밍 검토 필요
- **로컬 fallback 코드 제거**: `Object != null && Object.IsValid` Sandbox 분기 패턴 정리

> 상세 Inspector 체크리스트는 `UNITY_TODO.md` 참조

---

## Editor Tools

`03_Scripts/Editor/` 스크립트는 Unity 메뉴에 등록됩니다 (플레이 없이 씬 설정 자동화):

| 메뉴 항목 | 클래스 | 설명 |
|-----------|--------|------|
| `Tools/Create Chef Player` | `CookingMasterSetupBuilder` | CookingMaster 플레이어 오브젝트 + 1인칭 카메라 + HandsManager 자동 구성 |
| `Tools/Setup Demo Scene as Kitchen` | `KitchenMockupBuilder` | 데모 씬을 주방 레이아웃으로 셋업 |
| `Tools/Physics Fixer` | `PhysicsFixerTool` | 씬 내 Rigidbody 설정 일괄 수정 |
| `Tools/Outline Material Generator` | `OutlineMaterialGenerator` | 아웃라인 머티리얼 자동 생성 |

---

## 커밋 규칙

- 커밋 메시지에 `Co-Authored-By: Claude` footer **절대 금지**
