using System.Linq;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 무한 재료 공급 스테이션 (Supply Station)
/// - 상자 위에 시각적 전용 모델을 둥둥 띄웁니다.
/// - 플레이어가 가져갈 때 복제본을 생성하여 플레이어에게 지급합니다.
/// - 여러 재료가 설정된 경우 스폰 시 랜덤하게 하나를 선택합니다.
/// - 선택된 재료가 ISupplyDispenseResolver를 구현하면 실제 지급 직전에 다른 프리팹으로 해석할 수 있습니다.
///   (예: 미믹 상자 프록시 -> 금/은/동 미믹 상자 중 하나)
/// </summary>
public class SupplyStation : NetworkBehaviour
{
    [System.Serializable]
    public struct StageIngredientSet
    {
        [Tooltip("스테이지 번호 (예: 1, 2, 3...)")]
        public int stageIndex;
        [Tooltip("해당 스테이지에서 스폰될 수 있는 재료 프리팹 목록")]
        public PickableItem[] ingredients;
    }

    [Header("Supply Settings")]
    [Tooltip("스테이지별 재료 설정 목록 (단일 씬 대응)")]
    public StageIngredientSet[] stageSpecificIngredients;

    [Tooltip("재료가 나타날 위치 (직접 지정 시 사용. 없으면 자동 높이 적용)")]
    public Transform spawnPoint;

    [Tooltip("spawnPoint가 없을 때의 자동 높이 오프셋 (기본 1.0f)")]
    public float heightOffset = 1.0f;

    private PickableItem[] availableIngredients;

    [Header("Visual Effects")]
    public float floatSpeed = 1f;
    public float floatHeight = 0.1f;
    public float rotateSpeed = 30f;
    public float visualScale = 1f;

    [Networked] private int _selectedIndex { get; set; } = -1;

    private PickableItem _selectedPrefab;
    private GameObject _floatingVisual;
    private Vector3 _startPosition;

    bool IsNetworkReady => Object != null && Object.IsValid;

    private void Awake()
    {
        if (spawnPoint == null) spawnPoint = transform;
    }

    private void InitializeStageIngredients()
    {
        int currentStage = 0;
        if (SystemManager.Instance != null)
        {
            currentStage = SystemManager.Instance.SelectedStage;
        }

        availableIngredients = null;
        if (stageSpecificIngredients != null)
        {
            foreach (var set in stageSpecificIngredients)
            {
                if (set.stageIndex == currentStage)
                {
                    availableIngredients = set.ingredients;
                    break;
                }
            }
        }

        if ((availableIngredients == null || availableIngredients.Length == 0) && stageSpecificIngredients != null && stageSpecificIngredients.Length > 0)
        {
            availableIngredients = stageSpecificIngredients[0].ingredients;
        }
    }

    public override void Spawned()
    {
        InitializeStageIngredients();

        if (HasStateAuthority)
        {
            if (availableIngredients != null && availableIngredients.Length > 0)
            {
                _selectedIndex = Random.Range(0, availableIngredients.Length);
            }
        }
    }

    public override void Render()
    {
        if (!gameObject.scene.IsValid()) return;

        if (_selectedIndex >= 0 && _floatingVisual == null)
        {
            UpdateVisualModel();
        }

        UpdateFloatingAnimation();
    }

    private void Update()
    {
        if (IsNetworkReady) return;
        if (_selectedIndex >= 0 && _floatingVisual == null)
        {
            UpdateVisualModel();
        }
        UpdateFloatingAnimation();
    }

    private void UpdateVisualModel()
    {
        if (_selectedIndex < 0 || availableIngredients == null || _selectedIndex >= availableIngredients.Length)
        {
            return;
        }

        _selectedPrefab = availableIngredients[_selectedIndex];
        if (_selectedPrefab == null) return;

        if (_floatingVisual != null) Destroy(_floatingVisual);

        Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
        Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        if (spawnPoint == null || spawnPoint == transform)
        {
            pos += Vector3.up * heightOffset;
        }

        _floatingVisual = new GameObject("FloatingVisualWrapper");
        _floatingVisual.transform.position = pos;
        _floatingVisual.transform.rotation = rot;

        GameObject model = Instantiate(_selectedPrefab.gameObject, _floatingVisual.transform);
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale = Vector3.one * visualScale;

        MeshFilter[] meshFilters = model.GetComponentsInChildren<MeshFilter>();
        if (meshFilters.Length > 0 && meshFilters[0] != null && meshFilters[0].sharedMesh != null)
        {
            Bounds combinedBounds = meshFilters[0].sharedMesh.bounds;
            model.transform.localPosition -= combinedBounds.center;
        }

        _floatingVisual.layer = gameObject.layer;
        foreach (Transform child in _floatingVisual.GetComponentsInChildren<Transform>())
            child.gameObject.layer = gameObject.layer;

        var rb = model.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        var cols = model.GetComponentsInChildren<Collider>();
        foreach (var c in cols) c.isTrigger = true;

        var interactableCol = _floatingVisual.AddComponent<BoxCollider>();
        if (interactableCol != null)
        {
            interactableCol.isTrigger = true;
            interactableCol.size = new Vector3(0.3f, 0.3f, 0.3f);
            interactableCol.center = Vector3.zero;
        }

        var networkObj = model.GetComponent<NetworkObject>();
        if (networkObj != null) Destroy(networkObj);

        var itemScript = model.GetComponent<PickableItem>();
        if (itemScript != null) Destroy(itemScript);

        var interactable = _floatingVisual.AddComponent<FloatingItemInteractable>();
        if (interactable != null) interactable.Init(this);

        _startPosition = pos;
    }

    private void UpdateFloatingAnimation()
    {
        if (_floatingVisual != null)
        {
            float newY = _startPosition.y + Mathf.Sin(Time.time * floatSpeed) * floatHeight;
            _floatingVisual.transform.position = new Vector3(_startPosition.x, newY, _startPosition.z);
            _floatingVisual.transform.Rotate(Vector3.up, Time.deltaTime * rotateSpeed);
            _floatingVisual.transform.localScale = Vector3.one * visualScale;
        }
    }

    private void Start()
    {
        if (!gameObject.scene.IsValid()) return;
        if (IsNetworkReady) return;

        InitializeStageIngredients();

        if (availableIngredients != null && availableIngredients.Length > 0)
        {
            _selectedIndex = Random.Range(0, availableIngredients.Length);
            UpdateVisualModel();
        }
    }

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return false;
        if (_selectedPrefab == null || player.IsActiveHandFull()) return false;

        // 아이템 자체의 픽업 조건 사전 확인 (LavaCheeseItem 냉기 주머니 등)
        // 프리팩 인스턴스를 일시 생성하지 않고, 프리팩의 컴포넌트로만 확인
        if (_selectedPrefab is LavaCheeseItem && !player.HasItemInEitherHand<ColdPouchItem>())
            return false;

        return true;
    }

    public void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary || _selectedPrefab == null) return;
        if (!CanInteract(player, type)) return;

        if (IsNetworkReady)
        {
            SpawnAndGiveIngredient(player);
        }
        else
        {
            PickableItem spawnPrefab = ResolveDispensePrefab(_selectedPrefab);
            if (spawnPrefab == null) return;

            GameObject spawned = Instantiate(spawnPrefab.gameObject, player.GetActiveHandTransform().position, Quaternion.identity);
            PickableItem item = spawned.GetComponent<PickableItem>();
            if (item != null) player.PickupItem(item);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestSpawn(NetworkId playerNetId)
    {
        NetworkObject playerObj = Runner.FindObject(playerNetId);
        if (playerObj != null)
        {
            var hands = playerObj.GetComponentInChildren<CookingMasterHandsManager>();
            if (hands != null) SpawnAndGiveIngredient(hands);
        }
    }

    private void SpawnAndGiveIngredient(CookingMasterHandsManager player)
    {
        if (_selectedPrefab == null) return;

        PickableItem spawnPrefab = ResolveDispensePrefab(_selectedPrefab);
        if (spawnPrefab == null) return;

        Vector3 spawnPos = player.GetActiveHandTransform().position;
        NetworkObject spawned = Runner.Spawn(spawnPrefab.gameObject, spawnPos, Quaternion.identity);
        if (spawned != null)
        {
            PickableItem item = spawned.GetComponent<PickableItem>();
            if (item == null) return;

            // 스폰 직후 아이템 자체의 픽업 조건 확인
            if (item.CanInteract(player, Interactions.InteractionType.Primary))
                player.PickupItem(item);
            // 불가능하면 아이템은 바닥에 떨어짔
        }
    }

    private PickableItem ResolveDispensePrefab(PickableItem sourcePrefab)
    {
        if (sourcePrefab == null)
            return null;

        var resolver = sourcePrefab
            .GetComponents<MonoBehaviour>()
            .OfType<ISupplyDispenseResolver>()
            .FirstOrDefault();

        if (resolver == null)
            return sourcePrefab;

        PickableItem resolved = resolver.ResolveDispensePrefab();
        return resolved != null ? resolved : sourcePrefab;
    }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return "";
        if (player.IsActiveHandFull() || _selectedPrefab == null) return "";

        // 아이템 픽업 조건 불충족 안내
        if (_selectedPrefab is LavaCheeseItem && !player.HasItemInEitherHand<ColdPouchItem>())
            return $"<color=red>[{CookingMasterHandsManager.PickupKey}] {_selectedPrefab.itemName} 꺼내기 불가 (냉기 주머니 필요!)</color>";

        return $"[{CookingMasterHandsManager.PickupKey}] {_selectedPrefab.itemName} 꺼내기";
    }

    private void OnDestroy()
    {
        if (_floatingVisual != null)
        {
            Destroy(_floatingVisual);
        }
    }

    public void OnFocus(CookingMasterHandsManager player) { }
    public void OnFocusLost() { }
}
