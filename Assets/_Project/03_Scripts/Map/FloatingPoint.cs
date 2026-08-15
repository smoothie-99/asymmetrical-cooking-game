using UnityEngine;
using Fusion;
using Interactions;

public class FloatingPoint : NetworkBehaviour
{
    public GameObject ingredientPrefab;        // 비주얼 프리뷰 및 Sandbox Instantiate용
    public NetworkPrefabRef networkPrefabRef;  // 네트워크 Spawn용 (Inspector에서 동일 프리팹 지정)
    public Transform spawnPoint;
    public float targetSize = 0.2f;
    public float interactionRange = 2.5f;
    public float floatAmplitude = 0.1f;
    public float floatFrequency = 1.0f;
    public float interactionCooldown = 1.0f;

    [Header("Pickup Settings")]
    public bool infiniteSupply = true;
    // true  → 재료 박스: 무한 복제 (기존 동작 유지)
    // false → 조리도구/특수 아이템: 1회 픽업 후 비주얼 끄고 상호작용 잠금

    [Tooltip("이 박스의 아이템을 꺼내려면 냉기 주머니(ColdPouch)가 필요합니까? (예: 용암 치즈).")]
    public bool requiresColdPouch = false;

    // ── 네트워크 동기화 ────────────────────────────────────────────
    // StateAuthority가 true로 설정하면 Fusion이 모든 클라이언트에 자동 전파.
    // Render()에서 감지하여 비주얼을 비활성화합니다.
    [Networked] public bool IsEmpty { get; set; }
    private bool _localIsEmpty = false; // Sandbox(비네트워크) fallback

    bool IsNetworkReady => Object != null && Object.IsValid;
    public bool CurrentIsEmpty => IsNetworkReady ? IsEmpty : _localIsEmpty;

    // ── 로컬 상태 ─────────────────────────────────────────────────
    private GameObject visualPreview;
    // [수정] 월드 기준 수직 이동을 위해 월드 시작 위치를 저장합니다.
    private Vector3 startPosWorld; 
    private float lastInteractTime = -1f;

    // 정적 프레임 카운터: 같은 프레임에 여러 FloatingPoint가 동시 발동하는 것을 막습니다.
    //private static int _lastInteractionFrame = -1;

    // ── 로컬/Sandbox 초기화 ────────────────────────────────────────
    void Start()
    {
        if (ingredientPrefab != null && spawnPoint != null)
        {
            if (spawnPoint.childCount > 0)
            {
                visualPreview = spawnPoint.GetChild(0).gameObject; // 그거 그대로 씁니다! (중복 생성 X)
                Debug.Log($"[FloatingPoint] 미리 배치된 껍데기({visualPreview.name})를 사용합니다. 중복 복제 방지!");
            }
            //  만약 미리 달아둔 게 아무것도 없다면? 그때만 코드가 대신 하나 만들어 줍니다!
            else if (ingredientPrefab != null)
            {
                visualPreview = Instantiate(ingredientPrefab, spawnPoint);
                visualPreview.transform.localPosition = Vector3.zero;
                Debug.Log("[FloatingPoint] 껍데기가 비어있어서 코드가 자동으로 하나 만들었습니다.");
            }

            if (visualPreview != null)
            {
                // ── 재귀 방지: 비주얼 프리뷰에 FloatingPoint가 붙어있으면 제거
                foreach (var fp in visualPreview.GetComponentsInChildren<FloatingPoint>())
                    Destroy(fp);

                // ── 비주얼 프리뷰의 PickableItem / NetworkObject 제거
                // 다른 스크립트가 비주얼을 진짜 아이템으로 인식하여 자동 픽업 / 충돌이 발생하는 것을 막습니다.
                foreach (var pi in visualPreview.GetComponentsInChildren<PickableItem>())
                    Destroy(pi);
                foreach (var no in visualPreview.GetComponentsInChildren<NetworkObject>())
                    Destroy(no);

                // ── Proxy 및 Collider 설정 (아이템에만 상호작용 위임)
                var proxy = visualPreview.AddComponent<FloatingPointProxy>();
                proxy.parent = this;

                foreach (var col in visualPreview.GetComponentsInChildren<Collider>())
                {
                    col.enabled = true; // 켭니다!
                    col.isTrigger = true; // 충돌 방지 o
                }
                foreach (var rb in visualPreview.GetComponentsInChildren<Rigidbody>())
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }

                // ── NormalizeScale은 다음 프레임에 실행
                // SkinnedMeshRenderer 등은 첫 렌더 프레임 이후에야 bounds가 초기화됨
                // Start()에서 즉시 호출하면 bounds = (0,0,0) → scale 변환 불가 → 1:1:1 고정
                StartCoroutine(InitScaleNextFrame());
            }
            
        }
    }

    private System.Collections.IEnumerator InitScaleNextFrame()
    {
        yield return null; // 1프레임 대기 (Renderer bounds 초기화 완료 보장)

        if (visualPreview == null) yield break;

        NormalizeScale(visualPreview);

        Vector3 s = visualPreview.transform.localScale;
        if (!float.IsFinite(s.x) || !float.IsFinite(s.y) || !float.IsFinite(s.z))
        {
            Debug.LogError($"[FloatingPoint] '{gameObject.name}'의 scale이 무한대입니다.\n" +
                           $"Ingredient Prefab = [{ingredientPrefab.name}] → 아이템 모델 prefab으로 바꿔주세요.");
            Destroy(visualPreview);
            visualPreview = null;
            yield break;
        }

        // [수정] 스케일 및 위치 조정이 끝난 후의 '월드 위치'를 저장합니다.
        startPosWorld = visualPreview.transform.position;
    }

    // ── 네트워크 초기화 (스폰 직후, 늦은 접속자 포함) ───────────────
    public override void Spawned()
    {
        // 이미 아이템이 픽업된 박스라면 비주얼 즉시 끄기 (늦은 접속자 대응)
        if (!infiniteSupply && IsEmpty && visualPreview != null)
        {
            visualPreview.SetActive(false);
            enabled = false;
        }
    }

    // ── 매 렌더 프레임: IsEmpty 변경을 감지하여 모든 클라이언트 비주얼 동기화 ──
    public override void Render()
    {
        if (!infiniteSupply && CurrentIsEmpty
            && visualPreview != null && visualPreview.activeSelf)
        {
            visualPreview.SetActive(false);
            enabled = false;
        }
    }

    void NormalizeScale(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer ren in renderers)
            bounds.Encapsulate(ren.bounds);

        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

        // NaN <= 0은 C#에서 false → 체크를 통과해 scale이 NaN이 되는 버그 방지
        if (maxDimension <= 0 || !float.IsFinite(maxDimension)) return;

        float scale = targetSize / maxDimension;

        // 계산 결과도 유한값인지 검사 (0에 가까운 maxDimension → 거대한 scale 방지)
        if (!float.IsFinite(scale) || scale > 1000f)
        {
            Debug.LogError($"[FloatingPoint] NormalizeScale: scale 값이 비정상입니다 (scale={scale}, maxDimension={maxDimension}).\n" +
                           $"Ingredient Prefab [{obj.name}] 의 메쉬 크기를 확인하세요.");
            return;
        }

        // Vector3.one을 곱하면 파스타처럼 길쭉한 모델(1,1,20)이 무조건 1:1:1로 찌그러집니다.
        // 원본 모델의 고유 비율(localScale)을 유지하면서 전체 크기만 조절하도록 수정
        obj.transform.localScale = obj.transform.localScale * scale;

        // --- 스케일 조절 후, 메쉬의 중심을 SpawnPoint 중앙에 맞추기 ---
        Bounds newBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            newBounds.Encapsulate(renderers[i].bounds);

        // SpawnPoint의 월드 위치와 현재 메쉬 중심(newBounds.center) 간의 차이를 구해 오프셋 보정
        Vector3 offset = spawnPoint.position - newBounds.center;
        obj.transform.position += offset;
    }

    void Update()
    {
        // 비어있으면 애니메이션/입력 건너뜀
        if (CurrentIsEmpty) return;

        if (visualPreview != null)
        {
            // [수정] 월드 Y 좌표를 계산합니다.
            float newY = startPosWorld.y + Mathf.Sin(Time.time * floatFrequency) * floatAmplitude;
            // [수정] position(월드 좌표)을 직접 수정하여 부모의 회전과 무관하게 절대적인 위아래 운동을 만듭니다.
            visualPreview.transform.position = new Vector3(startPosWorld.x, newY, startPosWorld.z);
        }
    }

    // ── IInteractable 구현 ─────────────────────────────────────────

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return false;
        if (CurrentIsEmpty) return false;
        if (player.IsActiveHandFull()) return false;

        // 냉기 주머니 필요 여부 (용암 치즈 등)
        if (requiresColdPouch && !player.HasItemInEitherHand<ColdPouchItem>()) return false;

        // 거리 확인
        float dist = Vector3.Distance(transform.position, player.transform.position);
        if (dist > interactionRange) return false;

        return Time.time >= lastInteractTime + interactionCooldown;
    }

    public void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return;
        if (!CanInteract(player, type)) return;

        lastInteractTime = Time.time;
        SpawnAndGiveItem(player);
    }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return "";
        if (CurrentIsEmpty) return "";
        if (player.IsActiveHandFull()) return "";

        if (requiresColdPouch && !player.HasItemInEitherHand<ColdPouchItem>())
            return "냉기 주머니가 필요합니다!";

        string itemName = ingredientPrefab != null ? ingredientPrefab.name : "아이템";
        return $"[E] {itemName} 꺼내기";
    }

    /// <summary>
    /// 멀티플레이어에서 이 클라이언트의 로컬 플레이어(Authority 보유)를 찾습니다.
    /// </summary>
    private CookingMasterHandsManager FindLocalPlayerHands()
    {
        foreach (var hands in FindObjectsByType<CookingMasterHandsManager>(FindObjectsSortMode.None))
        {
            NetworkObject netObj = hands.GetComponentInParent<NetworkObject>();
            // 네트워크 없는 Sandbox: 아무나 반환
            if (netObj == null) return hands;
            // 네트워크 있음: 로컬 Authority 플레이어만
            if (netObj.HasStateAuthority) return hands;
        }
        return null;
    }

    void SpawnAndGiveItem(CookingMasterHandsManager hands)
    {
        NetworkObject playerNetObj = hands.GetComponentInParent<NetworkObject>();

        // Sandbox 모드: 직접 Instantiate
        if (playerNetObj == null || playerNetObj.Runner == null)
        {
            GameObject obj = Instantiate(ingredientPrefab, spawnPoint.position, Quaternion.identity);
            PickableItem item = obj.GetComponent<PickableItem>();
            if (item != null)
            {
                // 아이템 자체의 픽업 조건 확인 (예: LavaCheeseItem ColdPouch 필요)
                if (item.CanInteract(hands, Interactions.InteractionType.Primary))
                    hands.PickupItem(item);
                // 불가능하면 아이템은 바닥에 남음
            }

            // 1회성이면 로컬 비활성화
            if (!infiniteSupply)
                DisableLocalStation();
            return;
        }

        // 네트워크 모드: MapGenerator(Master Client)를 통해 스폰
        if (MapGenerator.Instance == null)
        {
            Debug.LogWarning("[FloatingPoint] MapGenerator.Instance가 없습니다.");
            return;
        }

        if (networkPrefabRef == NetworkPrefabRef.Empty)
        {
            Debug.LogWarning("[FloatingPoint] networkPrefabRef가 설정되지 않았습니다.");
            return;
        }

        MapGenerator.Instance.RequestItemSpawn(networkPrefabRef, spawnPoint.position, playerNetObj.Id);

        // 1회성이면 모든 클라이언트에 비활성화 요청
        if (!infiniteSupply)
            Rpc_DisableStation();
    }

    /// <summary>
    /// 어느 클라이언트든 호출 → StateAuthority가 IsEmpty = true 설정
    /// → Fusion이 모든 클라이언트에 전파 → Render()에서 비주얼 끔
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_DisableStation()
    {
        IsEmpty = true;
    }

    /// <summary>
    /// Sandbox(비네트워크) 환경에서 로컬로만 비활성화합니다.
    /// </summary>
    private void DisableLocalStation()
    {
        _localIsEmpty = true;
        if (visualPreview != null) visualPreview.SetActive(false);
        enabled = false;
    }
}

/// <summary>
/// FloatingPoint의 시각적 프리뷰(음식 등)에만 상호작용을 위임하는 프록시 전용 클래스입니다.
/// </summary>
public class FloatingPointProxy : MonoBehaviour, IInteractable
{
    public FloatingPoint parent;

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type) => parent != null && parent.CanInteract(player, type);
    public void Interact(CookingMasterHandsManager player, InteractionType type) => parent?.Interact(player, type);
    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type) => parent != null ? parent.GetInteractionLabel(player, type) : "";
    
    public void OnFocus(CookingMasterHandsManager player) { }
    public void OnFocusLost() { }
}
