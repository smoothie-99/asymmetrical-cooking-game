using UnityEngine;
using Fusion;
using Interactions;
using System.Collections;

/// <summary>
/// 열린 수면 보석 조개.
/// ClamClosedItem 에서 E키로 열면 이 프리팹으로 교체됩니다.
///
/// [규칙]
/// - 집게(TongsItem)로 보석 적출
/// - 같은 색 연속 적출 금지 → 함정 발동
/// - 남은 보석 수 3개 이하 → 함정 발동
/// - 함정: 조개 오염 + 수면 가루 분출
/// - 썰면 남은 보석 비율로 맛(flavor) 결정
/// </summary>
public class ClamOpenItem : PickableItem, ICookable, IInteractable
{
    [Header("Prefabs")]
    [SerializeField] private NetworkObject clamMeatPrefab;
    [Tooltip("0=Yellow, 1=Green, 2=Cyan, 3=Red, 4=Navy — JewelColor 순서와 일치")]
    [SerializeField] private NetworkObject[] jewelPrefabsByColor = new NetworkObject[5];

    [Header("Visual")]
    [SerializeField] private GameObject[] jewelModels; // 0~7 고정 슬롯 모델
    [SerializeField] private GameObject meatModel;     // 조개살 시각적 오브젝트
    [SerializeField] private MeshRenderer shellRenderer;
    [SerializeField] private Material contaminatedMaterial;

    [Header("Trap Settings")]
    [SerializeField, Min(0.1f)] private float powderRadius        = 4.0f;
    [SerializeField, Min(0.1f)] private float sleepDuration       = 5.0f;
    [SerializeField, Min(0.1f)] private float powderEffectDuration = 3.0f;
    [SerializeField] private GameObject sleepPowderEffect;

    [Header("Direct Heat")]
    [SerializeField, Min(0.1f)] private float shellBurnSeconds = 30f;

    // ── 네트워크 상태 ──────────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnVisualChanged))]
    public bool IsContaminatedNetworked { get; set; }

    [Networked, OnChangedRender(nameof(OnVisualChanged))]
    public CookState CookStateNetworked { get; set; }

    [Networked] public float CookProgressNetworked         { get; set; }
    [Networked] public int   JewelCountNetworked           { get; set; }
    [Networked] public int   LastRemovedColorIndexNetworked { get; set; }
    [Networked] public bool  IsBeingCookedNetworked        { get; set; }

    [Networked, Capacity(8)]
    public NetworkArray<int> JewelSlots => default;

    // ── 로컬 폴백 ─────────────────────────────────────────────────

    private bool      _localContaminated  = false;
    private CookState _localCookState     = CookState.Raw;
    private float     _localCookProgress  = 0f;
    private int       _localJewelCount    = 8;
    private int       _localLastColor     = -1;
    private readonly int[] _localSlots    = new int[8];

    private float _queuedHeat = 0f;

    private bool                       _extractRequested   = false;
    private int                        _pendingExtractSlot = -1;
    private NetworkId                  _pendingExtractorId = default;
    private CookingMasterHandsManager  _pendingExtractorLocal;

    private bool _sliceRequested = false;

    private Coroutine _localPowderRoutine;
    private Material  _originalMaterial;
    private MaterialPropertyBlock _mpb;
    private static readonly int   ColorId    = Shader.PropertyToID("_BaseColor");
    private static readonly Color BurnColor  = new Color(0.15f, 0.12f, 0.08f, 1f);

    // 8슬롯: Yellow×2, Green×2, Cyan×2, Red×1, Navy×1
    private static readonly int[] InitialColors =
        { (int)JewelColor.Yellow, (int)JewelColor.Green, (int)JewelColor.Cyan, (int)JewelColor.Red,
          (int)JewelColor.Navy,   (int)JewelColor.Yellow, (int)JewelColor.Green, (int)JewelColor.Cyan };

    private bool IsNetworkReady => Object != null && Object.IsValid;

    public bool IsContaminated =>
        IsNetworkReady ? IsContaminatedNetworked : _localContaminated;

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState _prev = CurrentCookState;
            if (IsNetworkReady) { if (HasStateAuthority) CookStateNetworked = value; }
            else _localCookState = value;
            RecordCookStateChange(_prev, value);
        }
    }

    public bool isBeingCooked
    {
        get => IsNetworkReady ? IsBeingCookedNetworked : false;
        set { if (IsNetworkReady && HasStateAuthority) IsBeingCookedNetworked = value; }
    }

    public override bool CanChop =>
        clamMeatPrefab != null && !IsContaminated && CurrentCookState != CookState.Burned;

    // ── Unity / Fusion 생명주기 ────────────────────────────────────

    private void Start()
    {
        itemName = "수면 보석 조개 (열림)";
        metadata["ingredientID"] = "수면 보석 조개";

        if (shellRenderer == null)
            shellRenderer = GetComponentInChildren<MeshRenderer>(true);
        if (shellRenderer != null)
            _originalMaterial = shellRenderer.material;

        if (!IsNetworkReady)
            InitLocal();

        BuildChildOutlines();
        UpdateVisuals();
        UpdateItemName();
        SetPowderEffect(false);
    }

    public override void Spawned()
    {
        if (shellRenderer == null)
            shellRenderer = GetComponentInChildren<MeshRenderer>(true);
        if (shellRenderer != null && _originalMaterial == null)
            _originalMaterial = shellRenderer.material;

        BuildChildOutlines();

        // InitializeFrom (onBeforeSpawned) 으로 설정된 값을 그대로 사용하므로
        // HasStateAuthority 이더라도 초기화를 덮어쓰지 않습니다.

        UpdateVisuals();
        UpdateItemName();
        SetPowderEffect(false);
    }

    public override void Render()
    {
        base.Render();
        if (IsNetworkReady)
        {
            UpdateVisuals();
            UpdateItemName();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
            ProcessSimulationStep();
        else
            _queuedHeat = 0f;
        base.FixedUpdateNetwork();
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessSimulationStep();
    }

    // ── 초기화 ────────────────────────────────────────────────────

    private void InitLocal()
    {
        _localContaminated = false;
        _localCookState    = CookState.Raw;
        _localCookProgress = 0f;
        _localJewelCount   = 8;
        _localLastColor    = -1;
        for (int i = 0; i < 8; i++) _localSlots[i] = InitialColors[i];
    }

    /// <summary>
    /// ClamClosedItem 의 SpawnOpenClamLocal() 에서 로컬 환경에서 상태를 복사할 때 사용합니다.
    /// </summary>
    public void InitializeFrom(int jewelCount, int lastColor,
                               CookState cookState, float cookProgress,
                               bool contaminated, int[] slots)
    {
        _localJewelCount   = jewelCount;
        _localLastColor    = lastColor;
        _localCookState    = cookState;
        _localCookProgress = cookProgress;
        _localContaminated = contaminated;
        if (slots != null)
            for (int i = 0; i < 8 && i < slots.Length; i++)
                _localSlots[i] = slots[i];

        UpdateVisuals();
        UpdateItemName();
    }

    // ── ICookable ──────────────────────────────────────────────────

    public void CookInFire(float heat)
    {
        if (heat <= 0f) return;
        if (CurrentCookState == CookState.Burned) return;
        if (IsNetworkReady && !HasStateAuthority) return;
        _queuedHeat += heat;
    }

    // ── IInteractable ─────────────────────────────────────────────
    // 집게(TongsItem)를 들고 있는 경우에만 상호작용 가능.
    // 카메라 레이캐스트로 바라보고 있는 보석 슬롯을 적출합니다.

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem)
        {
            if (!PlayerHoldsKnife(player)) return false; // 칼 없이 UseItem 완전 차단
            if (IsContaminated || CurrentCookState == CookState.Burned) return false;
            int slot = FindLookedAtSlot(player);
            if (slot != -1 && GetJewelColor(slot) != -1) return true;
            if (IsLookingAtMeat(player)) return true;
            return false;
        }
        return base.CanInteract(player, type);
    }

    public override void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem)
        {
            if (!PlayerHoldsKnife(player)) return;
            int slot = FindLookedAtSlot(player);
            if (slot != -1 && GetJewelColor(slot) != -1)
            {
                ExtractJewel(slot, player);
                return;
            }
            if (IsLookingAtMeat(player))
            {
                if (IsNetworkReady)
                {
                    if (!HasStateAuthority) { RPC_RequestSlice(); return; }
                    _sliceRequested = true;
                }
                else
                {
                    _sliceRequested = true;
                }
                return;
            }
            return;
        }
        base.Interact(player, type);
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem)
        {
            if (!PlayerHoldsKnife(player)) return "";
            if (IsContaminated || CurrentCookState == CookState.Burned) return "";
            int slot = FindLookedAtSlot(player);
            if (slot != -1 && GetJewelColor(slot) != -1) return "[L-Click] 젬 분리";
            if (IsLookingAtMeat(player)) return "[L-Click] 조개살 분리";
            return "";
        }
        return base.GetInteractionLabel(player, type);
    }

    // ── ISliceable ────────────────────────────────────────────────

    public override void Chop(CuttingStation board)
    {
        if (!CanChop) return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority) { RPC_RequestSlice(); return; }
            _sliceRequested = true;
            return;
        }
        _sliceRequested = true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSlice()
    {
        if (CanChop) _sliceRequested = true;
    }

    // ── 보석 적출 ──────────────────────────────────────────────────

    public void ExtractJewel(int slotIndex, CookingMasterHandsManager player)
    {
        if (slotIndex < 0 || slotIndex >= 8) return;
        if (IsContaminated || CurrentCookState == CookState.Burned) return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                NetworkId pid = (player != null && player.Object != null && player.Object.IsValid)
                    ? player.Object.Id : default;
                RPC_RequestExtractJewel(slotIndex, pid);
                return;
            }
            QueueExtractRequest(slotIndex, player);
            return;
        }
        QueueExtractRequest(slotIndex, player);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestExtractJewel(int slotIndex, NetworkId playerId)
    {
        if (slotIndex < 0 || slotIndex >= 8) return;
        CookingMasterHandsManager player = null;
        if (Runner != null)
        {
            NetworkObject obj = Runner.FindObject(playerId);
            if (obj != null) player = obj.GetComponent<CookingMasterHandsManager>();
        }
        QueueExtractRequest(slotIndex, player);
    }

    private void QueueExtractRequest(int slotIndex, CookingMasterHandsManager player)
    {
        _extractRequested   = true;
        _pendingExtractSlot = slotIndex;
        _pendingExtractorLocal = player;
        _pendingExtractorId = (player != null && player.Object != null && player.Object.IsValid)
            ? player.Object.Id : default;
    }

    private void ProcessPendingExtract()
    {
        _extractRequested = false;

        CookingMasterHandsManager actor = ResolvePendingExtractor();
        _pendingExtractorLocal = null;
        _pendingExtractorId    = default;

        if (actor == null) return;
        if (!PlayerHoldsKnife(actor))
        {
            Debug.Log("[ClamOpen] 칼 없이 보석을 뽑으려 했습니다.");
            return;
        }
        if (IsContaminated || CurrentCookState == CookState.Burned) return;

        int slot = _pendingExtractSlot;
        _pendingExtractSlot = -1;
        if (slot < 0 || slot >= 8) return;

        int color = GetJewelColor(slot);
        if (color == -1) return;

        int lastColor = IsNetworkReady ? LastRemovedColorIndexNetworked : _localLastColor;

        // 같은 색 연속 적출 → 함정
        if (color == lastColor)
        {
            Debug.Log($"[ClamOpen] 같은 색상 연속 적출 실패: {(JewelColor)color}");
            TriggerTrap(actor);
            return;
        }

        // 정상 적출
        SetJewelColor(slot, -1);
        SetLastColor(color);
        SetJewelCount(Mathf.Max(0, GetJewelCount() - 1));
        SpawnExtractedJewel(color);

        // 남은 보석 수 3개 이하 → 함정
        if (GetJewelCount() <= 3)
        {
            Debug.Log($"[ClamOpen] 남은 보석이 {GetJewelCount()}개 → 함정 발동.");
            TriggerTrap(actor);
        }
        else
        {
            Debug.Log($"[ClamOpen] 보석 적출 성공. 남은: {GetJewelCount()}");
        }

        UpdateVisuals();
        UpdateItemName();
    }

    private CookingMasterHandsManager ResolvePendingExtractor()
    {
        if (!IsNetworkReady) return _pendingExtractorLocal;
        if (Runner == null || _pendingExtractorId == default) return _pendingExtractorLocal;
        NetworkObject obj = Runner.FindObject(_pendingExtractorId);
        return obj != null ? obj.GetComponent<CookingMasterHandsManager>() : _pendingExtractorLocal;
    }

    // ── 슬롯 헬퍼 ────────────────────────────────────────────────

    public int GetJewelColor(int slot)
    {
        if (slot < 0 || slot >= 8) return -1;
        return IsNetworkReady ? JewelSlots[slot] : _localSlots[slot];
    }

    private void SetJewelColor(int slot, int value)
    {
        if (slot < 0 || slot >= 8) return;
        if (IsNetworkReady) { if (HasStateAuthority) JewelSlots.Set(slot, value); }
        else _localSlots[slot] = value;
    }

    private int GetJewelCount()
        => IsNetworkReady ? JewelCountNetworked : _localJewelCount;

    private void SetJewelCount(int v)
    {
        if (IsNetworkReady) { if (HasStateAuthority) JewelCountNetworked = v; }
        else _localJewelCount = v;
    }

    private void SetLastColor(int v)
    {
        if (IsNetworkReady) { if (HasStateAuthority) LastRemovedColorIndexNetworked = v; }
        else _localLastColor = v;
    }

    /// <summary>
    /// 플레이어 카메라 레이캐스트로 바라보고 있는 보석 슬롯 인덱스를 반환합니다.
    /// 보석 모델에 Collider가 있어야 합니다.
    /// 레이가 어떤 보석에도 닿지 않으면 -1을 반환합니다.
    /// </summary>
    private int FindLookedAtSlot(CookingMasterHandsManager player)
    {
        if (player == null || player.mainCamera == null) return -1;

        Ray ray = new Ray(player.mainCamera.transform.position,
                          player.mainCamera.transform.forward);

        // ~0 = 모든 레이어, QueryTriggerInteraction.Collide = 트리거 콜라이더도 감지
        RaycastHit[] hits = Physics.RaycastAll(ray, player.interactionDistance, ~0, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            GameObject hitObj = hit.collider.gameObject;
            if (jewelModels == null) continue;
            for (int i = 0; i < jewelModels.Length; i++)
            {
                if (jewelModels[i] == null) continue;
                if (hitObj == jewelModels[i] ||
                    hitObj.transform.IsChildOf(jewelModels[i].transform))
                {
                    if (GetJewelColor(i) != -1) return i;
                }
            }
        }
        return -1;
    }

    // ── 보석 스폰 ─────────────────────────────────────────────────

    private void SpawnExtractedJewel(int colorIndex)
    {
        if (jewelPrefabsByColor == null || colorIndex < 0 || colorIndex >= jewelPrefabsByColor.Length) return;
        NetworkObject prefab = jewelPrefabsByColor[colorIndex];
        if (prefab == null) return;

        Vector3 pos = transform.position + Vector3.up * 0.3f;
        if (IsNetworkReady && Runner != null && Object != null && Object.IsValid)
            Runner.Spawn(prefab, pos, Quaternion.identity);
        else
            Instantiate(prefab, pos, Quaternion.identity);
    }

    // ── 살 떼어내기 (도마 + 칼) ───────────────────────────────────

    private void ProcessPendingSlice()
    {
        _sliceRequested = false;
        if (!CanChop) return;

        // 남은 보석 색별 카운트를 조개살에 전달
        int[] counts = new int[5]; // Yellow/Green/Cyan/Red/Navy
        for (int i = 0; i < 8; i++)
        {
            int c = GetJewelColor(i);
            if (c >= 0 && c < counts.Length) counts[c]++;
        }

        Debug.Log($"[ClamOpen] 살 떼어냄. Y({counts[0]}) G({counts[1]}) C({counts[2]}) R({counts[3]}) N({counts[4]})");

        if (clamMeatPrefab != null)
        {
            if (IsNetworkReady && Runner != null && Object != null && Object.IsValid)
            {
                int[] captured = counts;
                Runner.Spawn(clamMeatPrefab, transform.position, transform.rotation,
                    onBeforeSpawned: (runner, obj) =>
                    {
                        ClamMeatItem meat = obj.GetComponent<ClamMeatItem>();
                        meat?.SetJewelCountsNetworked(captured);
                    });
            }
            else
            {
                GameObject go = Instantiate(clamMeatPrefab.gameObject, transform.position, transform.rotation);
                go.GetComponent<ClamMeatItem>()?.InitializeWithJewelCounts(counts);
            }
        }

        SafeRemoveSelf();
    }

    // ── 함정 ─────────────────────────────────────────────────────

    private void TriggerTrap(CookingMasterHandsManager actor)
    {
        if (IsNetworkReady) { if (HasStateAuthority) IsContaminatedNetworked = true; }
        else { _localContaminated = true; }

        UpdateVisuals();
        UpdateItemName();

        Vector3 origin = transform.position;
        if (IsNetworkReady)
            RPC_PlayTrapFeedback(origin, powderRadius, sleepDuration, powderEffectDuration);
        else
            PlayTrapFeedbackLocal(origin, powderRadius, sleepDuration, powderEffectDuration);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayTrapFeedback(Vector3 origin, float radius, float sleep, float effect)
        => PlayTrapFeedbackLocal(origin, radius, sleep, effect);

    private void PlayTrapFeedbackLocal(Vector3 origin, float radius, float sleep, float effect)
    {
        if (_localPowderRoutine != null) StopCoroutine(_localPowderRoutine);
        _localPowderRoutine = StartCoroutine(PowderRoutine(effect));
        TryApplySleepLocal(origin, radius, sleep);
    }

    private IEnumerator PowderRoutine(float duration)
    {
        SetPowderEffect(true);
        yield return new WaitForSeconds(duration);
        SetPowderEffect(false);
        _localPowderRoutine = null;
    }

    private void SetPowderEffect(bool active)
    {
        if (sleepPowderEffect == null) return;
        sleepPowderEffect.SetActive(active);
        if (active)
        {
            ParticleSystem ps = sleepPowderEffect.GetComponentInChildren<ParticleSystem>(true);
            ps?.Play(true);
        }
    }

    private void TryApplySleepLocal(Vector3 origin, float radius, float duration)
    {
        CookingMasterHandsManager local = FindLocalHands();
        if (local == null) return;
        if (Vector3.Distance(origin, local.transform.position) > radius) return;
        if (PlayerHasHeldItem(local, "BellItem", "종"))
        {
            Debug.Log("[ClamOpen] 종이 있어 수면 가루를 막아냈습니다.");
            return;
        }
        local.ApplySleepEffect(duration);
        Debug.Log($"[ClamOpen] 수면 가루! {duration}초 기절.");
    }

    private CookingMasterHandsManager FindLocalHands()
    {
        CookingMasterHandsManager[] all =
            FindObjectsByType<CookingMasterHandsManager>(FindObjectsSortMode.None);
        foreach (var h in all)
        {
            if (h == null) continue;
            if (IsNetworkReady) { if (h.HasStateAuthority) return h; }
            else { if (h.isActiveAndEnabled) return h; }
        }
        return null;
    }

    // ── 시뮬레이션 ────────────────────────────────────────────────

    private void ProcessSimulationStep()
    {
        if (_extractRequested) ProcessPendingExtract();
        if (_sliceRequested)   { ProcessPendingSlice(); return; }

        if (_queuedHeat <= 0f) { isBeingCooked = false; return; }
        if (CurrentCookState == CookState.Burned) { _queuedHeat = 0f; return; }

        isBeingCooked = true;
        if (IsNetworkReady) CookProgressNetworked += _queuedHeat;
        else _localCookProgress += _queuedHeat;
        _queuedHeat = 0f;

        float progress = IsNetworkReady ? CookProgressNetworked : _localCookProgress;
        if (progress >= shellBurnSeconds)
        {
            CurrentCookState = CookState.Burned;
            UpdateVisuals();
            UpdateItemName();
        }
    }

    // ── 비주얼 ────────────────────────────────────────────────────

    private void OnVisualChanged() { UpdateVisuals(); UpdateItemName(); }

    private void UpdateVisuals()
    {
        // 보석 모델 표시
        for (int i = 0; i < 8; i++)
        {
            if (jewelModels == null || i >= jewelModels.Length || jewelModels[i] == null) continue;
            int c = GetJewelColor(i);
            jewelModels[i].SetActive(c != -1 && !IsContaminated);
        }

        // 껍질 재질
        if (shellRenderer != null)
        {
            if (IsContaminated && contaminatedMaterial != null)
            {
                shellRenderer.material = contaminatedMaterial;
                shellRenderer.SetPropertyBlock(null);
            }
            else
            {
                if (_originalMaterial != null)
                    shellRenderer.material = _originalMaterial;

                if (CurrentCookState == CookState.Burned)
                {
                    _mpb ??= new MaterialPropertyBlock();
                    _mpb.SetColor(ColorId, BurnColor);
                    shellRenderer.SetPropertyBlock(_mpb);
                }
                else
                {
                    shellRenderer.SetPropertyBlock(null);
                }
            }
        }
    }

    private void UpdateItemName()
    {
        if (IsContaminated)     { itemName = "오염된 수면 보석 조개"; return; }
        if (CurrentCookState == CookState.Burned) { itemName = "타버린 수면 보석 조개"; return; }
        itemName = "수면 보석 조개 (열림)";
    }

    // ── 유틸 ──────────────────────────────────────────────────────

    private bool PlayerHoldsKnife(CookingMasterHandsManager player)
        => PlayerHasHeldItem(player, "KnifeItem", "칼");

    private bool IsLookingAtMeat(CookingMasterHandsManager player)
    {
        if (meatModel == null || player?.mainCamera == null) return false;
        Ray ray = new Ray(player.mainCamera.transform.position,
                          player.mainCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, player.interactionDistance, ~0, QueryTriggerInteraction.Collide);
        foreach (var hit in hits)
        {
            if (hit.collider.gameObject == meatModel ||
                hit.collider.gameObject.transform.IsChildOf(meatModel.transform))
                return true;
        }
        return false;
    }

    private bool PlayerHasHeldItem(CookingMasterHandsManager player,
                                   string typeName, string nameKeyword)
    {
        if (player == null) return false;
        foreach (PickableItem item in player.GetComponentsInChildren<PickableItem>(true))
        {
            if (item == null || !item.isHeld) continue;
            if (item.GetType().Name == typeName) return true;
            if (!string.IsNullOrEmpty(item.itemName) && item.itemName.Contains(nameKeyword)) return true;
        }
        return false;
    }

    // ── 자식 아웃라인 ─────────────────────────────────────────────

    private void BuildChildOutlines()
    {
        // HandsManager의 UpdateOutline이 GetComponentInChildren로 루트 Outline을 우선 찾도록
        // 루트에 Outline이 없으면 직접 추가 (PickableItem.Start가 이미 추가했을 수도 있음)
        if (GetComponent<Outline>() == null)
        {
            Outline ol = gameObject.AddComponent<Outline>();
            ol.OutlineMode  = Outline.Mode.OutlineAll;
            ol.OutlineColor = Color.yellow;
            ol.OutlineWidth = 3f;
            ol.enabled = false;
        }

        // 보석/살 자식 Outline: MeshRenderer가 있는 오브젝트에 추가
        if (jewelModels != null)
            foreach (var go in jewelModels)
                if (go != null) AddOutlineToMeshObject(go);
        if (meatModel != null) AddOutlineToMeshObject(meatModel);
    }

    private void AddOutlineToMeshObject(GameObject go)
    {
        // MeshRenderer가 있는 가장 가까운 오브젝트에 Outline 추가
        MeshRenderer mr = go.GetComponent<MeshRenderer>() ?? go.GetComponentInChildren<MeshRenderer>();
        GameObject target = mr != null ? mr.gameObject : go;

        Outline ol = target.GetComponent<Outline>();
        if (ol == null)
        {
            ol = target.AddComponent<Outline>();
            ol.OutlineMode  = Outline.Mode.OutlineAll;
            ol.OutlineColor = Color.yellow;
            ol.OutlineWidth = 3f;
        }
        ol.enabled = false;
    }

    public override void OnFocus(CookingMasterHandsManager player)
    {
        base.OnFocus(player);
        if (!PlayerHoldsKnife(player)) return;

        int slot = FindLookedAtSlot(player);
        bool lookingAtMeat = (slot == -1) && IsLookingAtMeat(player);
        bool hasChildTarget = (slot != -1 && GetJewelColor(slot) != -1) || lookingAtMeat;

        // 특정 자식(젬/살)을 겨냥 중일 때만 루트 아웃라인 숨김
        // 겨냥 대상 없으면 쉘 아웃라인 유지 (아무 피드백도 없는 상태 방지)
        Outline shellOl = GetComponent<Outline>();
        if (shellOl != null) shellOl.enabled = !hasChildTarget;

        for (int i = 0; jewelModels != null && i < jewelModels.Length; i++)
        {
            if (jewelModels[i] == null) continue;
            Outline ol = jewelModels[i].GetComponentInChildren<Outline>();
            if (ol != null) ol.enabled = (i == slot && GetJewelColor(i) != -1);
        }
        if (meatModel != null)
        {
            Outline ol = meatModel.GetComponentInChildren<Outline>();
            if (ol != null) ol.enabled = lookingAtMeat;
        }
    }

    public override void OnFocusLost()
    {
        base.OnFocusLost();
        if (jewelModels != null)
            foreach (var go in jewelModels)
                if (go != null) { Outline ol = go.GetComponentInChildren<Outline>(); if (ol != null) ol.enabled = false; }
        if (meatModel != null)
        {
            Outline ol = meatModel.GetComponentInChildren<Outline>();
            if (ol != null) ol.enabled = false;
        }
    }

    private void SafeRemoveSelf()
    {
        if (Object != null && Object.IsValid && Runner != null)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }
}
