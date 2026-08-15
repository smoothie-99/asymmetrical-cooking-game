using System.Linq;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 투명 호박 본체.
/// - 방치하면 순간이동하고 투명해짐
/// - 레시피 마스터는 투명 상태여도 볼 수 있음
/// - 쿠킹 마스터는 안 보이지만 집을 수 있고, 집으면 다시 보이게 됨
/// - 익거나 타거나 썰리면 순간이동 종료
/// - 칼로 자르면 knife guideline config에 설정된 결과물로 교체
/// </summary>
public class InvisiblePumpkinItem : PumpkinCookableBaseItem, ICuttable
{
    [Header("Teleport Settings")]
    [SerializeField, Min(0.1f)] private float teleportInterval = 10f;
    [SerializeField] private Collider safeAreaBounds;
    [SerializeField, Min(0.5f)] private float fallbackTeleportRadius = 4f;
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField, Min(1)] private int teleportPositionTryCount = 8;
    [SerializeField] private float teleportGroundOffset = 0.2f;
    [SerializeField] private GameObject teleportEffect;

    [Header("Cutting")]
    [SerializeField] private CutGuidelineConfig[] knifeGuidelineConfigs = { new CutGuidelineConfig() };
    private CutGuidelineVisual[] _guidelineVisuals;

    [Header("Sandbox Fallback")]
    [SerializeField] private bool assumeChefIfRoleUnknownInSandbox = false;

    [Networked, OnChangedRender(nameof(OnInvisibleChanged))]
    public bool IsInvisibleNetworked { get; set; }

    private bool _localIsInvisible;
    private float _teleportTimer;
    private Rigidbody _rb;

    // PlayerSpawner 기준:
    // 1 = RecipeMaster
    // 2 = CookingMaster
    private static int _cachedLocalRoleInt;
    private static float _nextRoleResolveAttemptTime = -1f;

    protected override string RawItemName => "투명 호박";
    protected override string CookedItemName => "익은 투명 호박";
    protected override string BurnedItemName => "타버린 투명 호박";
    protected override string IngredientId => "투명 호박";

    private bool IsInvisible
    {
        get => IsNetworkReady ? IsInvisibleNetworked : _localIsInvisible;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                IsInvisibleNetworked = value;
            }

            _localIsInvisible = value;
            RefreshWholeVisualState();
        }
    }

    public override bool CanChop => !isHeld && CurrentCookState != CookState.Burned && HasValidCutResults();

    protected override bool ShouldRenderForLocalViewer()
    {
        if (!IsInvisible)
            return true;

        // 투명 상태일 때만 쿠킹 마스터에게 숨김
        bool isLocalCookingMaster = IsLocalViewerCookingMaster();
        return !isLocalCookingMaster;
    }

    protected override string GetRawDisplayName()
    {
        return IsInvisible ? "투명한 호박" : "투명 호박";
    }

    protected override void Start()
    {
        base.Start();
        _rb = GetComponent<Rigidbody>();
        EnsureGuidelineVisuals();
        RefreshWholeVisualState();
    }

    public override void Spawned()
    {
        base.Spawned();
        _rb = GetComponent<Rigidbody>();
        EnsureGuidelineVisuals();

        if (HasStateAuthority)
        {
            IsInvisible = false;
            _teleportTimer = 0f;
        }
        else if (IsNetworkReady)
        {
            _localIsInvisible = IsInvisibleNetworked;
        }

        RefreshWholeVisualState();
    }

    public override void Render()
    {
        base.Render();

        if (!IsNetworkReady)
            return;

        if (_localIsInvisible != IsInvisibleNetworked)
        {
            _localIsInvisible = IsInvisibleNetworked;
            RefreshWholeVisualState();
        }
        else if (IsInvisible)
        {
            // 역할이 바뀌거나 로컬 역할 판별이 늦게 들어오는 경우를 위해 갱신 유지
            RefreshWholeVisualState();
        }
    }

    protected override void OnAfterCookStep(float deltaTime)
    {
        if (isHeld && IsInvisible)
        {
            IsInvisible = false;
            _teleportTimer = 0f;
        }

        bool teleportAllowed = CurrentCookState != CookState.Cooked && CurrentCookState != CookState.Burned;

        if (!teleportAllowed)
        {
            if (IsInvisible)
                IsInvisible = false;

            _teleportTimer = 0f;
            return;
        }

        if (isHeld)
        {
            _teleportTimer = 0f;
            return;
        }

        _teleportTimer += deltaTime;

        if (_teleportTimer >= teleportInterval)
        {
            bool teleported = TryExecuteTeleport();
            _teleportTimer = teleported ? 0f : teleportInterval * 0.75f;
        }
    }

    private bool HasValidCutResults()
    {
        if (knifeGuidelineConfigs == null || knifeGuidelineConfigs.Length == 0)
            return false;

        for (int i = 0; i < knifeGuidelineConfigs.Length; i++)
        {
            if (knifeGuidelineConfigs[i].resultPrefabs != null &&
                knifeGuidelineConfigs[i].resultPrefabs.Length > 0)
                return true;
        }

        return false;
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType)
    {
        if (!CanChop)
            return System.Array.Empty<CutGuidelineVisual>();

        EnsureGuidelineVisuals();

        return _guidelineVisuals != null
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();
    }

    public override void TransferStateTo(PickableItem target)
    {
        if (target is InvisiblePumpkinSliceItem sliced)
        {
            // 절단 이후에도 투명 호박 가문 유지
            metadata["ingredientID"] = "투명 호박";
            sliced.metadata["ingredientID"] = "투명 호박";
            sliced.ApplyInheritedState(CurrentCookState, CookProgress);
            return;
        }

        base.TransferStateTo(target);
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Primary && IsInvisible && IsLocalViewerCookingMaster())
            return "";

        return base.GetInteractionLabel(player, type);
    }

    private void EnsureGuidelineVisuals()
    {
        if (knifeGuidelineConfigs == null || knifeGuidelineConfigs.Length == 0)
        {
            ClearGuidelineVisuals();
            _guidelineVisuals = System.Array.Empty<CutGuidelineVisual>();
            return;
        }

        bool isValid =
            _guidelineVisuals != null &&
            _guidelineVisuals.Length == knifeGuidelineConfigs.Length;

        if (isValid)
        {
            for (int i = 0; i < _guidelineVisuals.Length; i++)
            {
                if (_guidelineVisuals[i] == null)
                {
                    isValid = false;
                    break;
                }
            }
        }

        if (isValid)
            return;

        ClearGuidelineVisuals();
        _guidelineVisuals = new CutGuidelineVisual[knifeGuidelineConfigs.Length];

        for (int i = 0; i < knifeGuidelineConfigs.Length; i++)
        {
            CutGuidelineConfig config = knifeGuidelineConfigs[i];

            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = config.localPosition;
            go.transform.localEulerAngles = config.localEulerAngles;

            CutGuidelineVisual vis = go.AddComponent<CutGuidelineVisual>();
            vis.toolType = "Knife";
            vis.cutName = string.IsNullOrEmpty(config.cutName) ? "썰기" : config.cutName;
            vis.resultPrefabs = config.resultPrefabs ?? System.Array.Empty<NetworkObject>();
            vis.resultCounts = BuildResultCounts(config);

            _guidelineVisuals[i] = vis;
        }
    }

    private int[] BuildResultCounts(CutGuidelineConfig config)
    {
        NetworkObject[] prefabs = config.resultPrefabs ?? System.Array.Empty<NetworkObject>();
        if (prefabs.Length == 0)
            return System.Array.Empty<int>();

        if (config.resultCounts != null && config.resultCounts.Length == prefabs.Length)
        {
            int[] counts = new int[config.resultCounts.Length];
            for (int i = 0; i < counts.Length; i++)
                counts[i] = Mathf.Max(1, config.resultCounts[i]);
            return counts;
        }

        int[] defaultCounts = new int[prefabs.Length];
        for (int i = 0; i < defaultCounts.Length; i++)
            defaultCounts[i] = 1;

        return defaultCounts;
    }

    private void ClearGuidelineVisuals()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("CutGuideline_"))
                continue;

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    private bool TryExecuteTeleport()
    {
        if (!TryFindTeleportPosition(out Vector3 targetPosition))
            return false;

        Vector3 fromPosition = transform.position;

        SyncFreePosition(targetPosition);
        IsInvisible = true;

        if (IsNetworkReady)
            RPC_PlayTeleportEffects(fromPosition, targetPosition);
        else
        {
            SpawnTeleportEffect(fromPosition);
            SpawnTeleportEffect(targetPosition);
        }

        return true;
    }

    private bool TryFindTeleportPosition(out Vector3 targetPosition)
    {
        if (safeAreaBounds != null)
            return TryFindPositionInBounds(out targetPosition);

        return TryFindPositionInFallbackRadius(out targetPosition);
    }

    private bool TryFindPositionInBounds(out Vector3 targetPosition)
    {
        targetPosition = transform.position;
        Bounds bounds = safeAreaBounds.bounds;

        for (int i = 0; i < teleportPositionTryCount; i++)
        {
            float randomX = Random.Range(bounds.min.x, bounds.max.x);
            float randomZ = Random.Range(bounds.min.z, bounds.max.z);
            Vector3 rayOrigin = new Vector3(randomX, bounds.max.y + 0.5f, randomZ);

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, bounds.size.y * 3f, groundLayer, QueryTriggerInteraction.Ignore))
            {
                targetPosition = hit.point + Vector3.up * teleportGroundOffset;
                return true;
            }

            // 레이캐스트 실패 시에도 이동은 일어나게 보조
            targetPosition = new Vector3(randomX, transform.position.y, randomZ);
        }

        return true;
    }

    private bool TryFindPositionInFallbackRadius(out Vector3 targetPosition)
    {
        Vector3 origin = transform.position;
        targetPosition = origin;

        for (int i = 0; i < teleportPositionTryCount; i++)
        {
            Vector2 circle = Random.insideUnitCircle * fallbackTeleportRadius;
            Vector3 rayOrigin = origin + new Vector3(circle.x, 5f, circle.y);

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 15f, groundLayer, QueryTriggerInteraction.Ignore))
            {
                targetPosition = hit.point + Vector3.up * teleportGroundOffset;
                return true;
            }

            targetPosition = origin + new Vector3(circle.x, 0f, circle.y);
        }

        return true;
    }

    private void SyncFreePosition(Vector3 targetPosition)
    {
        // 손/스테이션 상태를 해제한 뒤 원하는 위치로 즉시 순간이동.
        LocalDrop(targetPosition);

        if (Object != null && Object.IsValid)
            Rpc_Drop(targetPosition);

        if (_rb == null)
            _rb = GetComponent<Rigidbody>();

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = targetPosition;
            _rb.Sleep();
        }
        else
        {
            transform.position = targetPosition;
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayTeleportEffects(Vector3 fromPosition, Vector3 toPosition)
    {
        SpawnTeleportEffect(fromPosition);
        SpawnTeleportEffect(toPosition);
    }

    private void SpawnTeleportEffect(Vector3 position)
    {
        if (teleportEffect == null)
            return;

        GameObject fx = Instantiate(teleportEffect, position, Quaternion.identity);
        Destroy(fx, 2f);
    }

    private void OnInvisibleChanged()
    {
        _localIsInvisible = IsInvisibleNetworked;
        RefreshWholeVisualState();
    }

    private void RefreshWholeVisualState()
    {
        RefreshVisualState();
    }

    private bool IsLocalViewerCookingMaster()
    {
        if (Time.unscaledTime >= _nextRoleResolveAttemptTime)
        {
            _nextRoleResolveAttemptTime = Time.unscaledTime + 0.5f;
            _cachedLocalRoleInt = ResolveLocalRoleInt();
        }

        return _cachedLocalRoleInt == 2;
    }

    private int ResolveLocalRoleInt()
    {
        int roleInt = 0;

        roleInt = (int)NetworkLauncher.SelectedJob;

        if (roleInt == 0 && IsNetworkReady && Runner != null && Runner.IsRunning)
        {
            LobbyPlayer lp = LobbyPlayer.Get(Runner.LocalPlayer);
            if (lp != null)
                roleInt = lp.SelectedRole;
        }

        if (roleInt == 0 && (!IsNetworkReady || Runner == null || !Runner.IsRunning))
            roleInt = assumeChefIfRoleUnknownInSandbox ? 2 : 1;

        return roleInt;
    }
}