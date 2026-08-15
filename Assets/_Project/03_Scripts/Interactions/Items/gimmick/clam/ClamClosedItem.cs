using UnityEngine;
using Fusion;
using Interactions;
using System.Collections;

/// <summary>
/// 닫힌 수면 보석 조개.
/// 초기 상태는 오염됨. 슬라임으로 청소하면 수면 가스가 분출되고 청소한 플레이어만 잠듦.
/// 청소 후 E키로 열면 ClamOpenItem 으로 교체됩니다.
/// 불에 올리면 탄화되고, 오염 상태에서는 열 수 없습니다.
/// </summary>
public class ClamClosedItem : PickableItem, ICookable, IInteractable, IWashable
{
    [Header("Prefab")]
    [SerializeField] private NetworkObject clamOpenPrefab;

    [Header("Visual")]
    [SerializeField] private MeshRenderer shellRenderer;
    [SerializeField] private Material      contaminatedMaterial;

    [Header("Direct Heat")]
    [SerializeField, Min(0.1f)] private float shellBurnSeconds = 30f;

    [Header("Cleaning / Sleep Gas")]
    [SerializeField] private GameObject sleepPowderEffect;
    [SerializeField, Min(0.1f)] private float sleepDuration       = 5f;
    [SerializeField, Min(0.1f)] private float powderEffectDuration = 3f;

    // ── 네트워크 상태 ──────────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnVisualChanged))]
    public bool IsContaminatedNetworked { get; set; }

    [Networked, OnChangedRender(nameof(OnVisualChanged))]
    public CookState CookStateNetworked { get; set; }

    [Networked] public float CookProgressNetworked  { get; set; }
    [Networked] public int   JewelCountNetworked    { get; set; }
    [Networked] public int   LastRemovedColorIndex  { get; set; }

    [Networked, Capacity(8)]
    public NetworkArray<int> JewelSlots => default;

    [Networked] public bool IsBeingCookedNetworked { get; set; }

    [Networked] private float WashProgressNetworked { get; set; }

    // ── 로컬 폴백 ─────────────────────────────────────────────────

    private CookState _localCookState    = CookState.Raw;
    private bool      _localContaminated = false;
    private float     _localWashProgress = 0f;
    private float     _localCookProgress = 0f;
    private int       _localJewelCount   = 8;
    private int       _localLastColor    = -1;
    private readonly int[] _localSlots   = new int[8];
    private float     _queuedHeat        = 0f;

    private Material  _originalMaterial;
    private Coroutine _powderRoutine;
    private static readonly int ColorId    = Shader.PropertyToID("_BaseColor");
    private static readonly Color BurnColor = new Color(0.15f, 0.12f, 0.08f, 1f);
    private MaterialPropertyBlock _mpb;

    // 8슬롯: Yellow×2, Green×2, Cyan×2, Red×1, Navy×1
    private static readonly int[] InitialColors =
        { (int)JewelColor.Yellow, (int)JewelColor.Green, (int)JewelColor.Cyan, (int)JewelColor.Red,
          (int)JewelColor.Navy,   (int)JewelColor.Yellow, (int)JewelColor.Green, (int)JewelColor.Cyan };

    private bool IsNetworkReady => Object != null && Object.IsValid;

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

    public bool IsContaminated =>
        IsNetworkReady ? IsContaminatedNetworked : _localContaminated;

    /// <summary>샌드박스(비네트워크) 환경에서 오염 상태를 직접 설정합니다.</summary>
    public void SetContaminatedLocal(bool value) => _localContaminated = value;

    // ── IWashable ──────────────────────────────────────────────────

    public float CleanRatio => IsNetworkReady ? WashProgressNetworked : _localWashProgress;

    public void Wash(float amount)
    {
        if (IsNetworkReady)
        {
            if (!HasStateAuthority) return;
            float next = Mathf.Min(WashProgressNetworked + amount, 100f);
            WashProgressNetworked = next;
            if (next >= 100f) { IsContaminatedNetworked = false; RecordWashed(); }
        }
        else
        {
            _localWashProgress = Mathf.Min(_localWashProgress + amount, 100f);
            if (_localWashProgress >= 100f) { _localContaminated = false; RecordWashed(); }
        }
    }

    // ── Unity / Fusion 생명주기 ────────────────────────────────────

    private void Start()
    {
        itemName = "수면 보석 조개";
        metadata["ingredientID"] = "수면 보석 조개";
        _mpb = new MaterialPropertyBlock();

        if (shellRenderer == null)
            shellRenderer = GetComponentInChildren<MeshRenderer>(true);
        if (shellRenderer != null)
            _originalMaterial = shellRenderer.material;

        if (!IsNetworkReady)
            InitLocal();

        UpdateVisuals();
    }

    public override void Spawned()
    {
        _mpb ??= new MaterialPropertyBlock();

        if (shellRenderer == null)
            shellRenderer = GetComponentInChildren<MeshRenderer>(true);
        if (shellRenderer != null && _originalMaterial == null)
            _originalMaterial = shellRenderer.material;

        if (HasStateAuthority)
            InitNetwork();

        UpdateVisuals();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
        {
            if (_queuedHeat <= 0f) { isBeingCooked = false; }
            else if (CurrentCookState == CookState.Burned) { _queuedHeat = 0f; }
            else
            {
                isBeingCooked = true;
                CookProgressNetworked += _queuedHeat;
                _queuedHeat = 0f;
                if (CookProgressNetworked >= shellBurnSeconds)
                    CurrentCookState = CookState.Burned;
            }
        }
        base.FixedUpdateNetwork();
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        if (_queuedHeat <= 0f) return;
        if (CurrentCookState == CookState.Burned) { _queuedHeat = 0f; return; }

        _localCookProgress += _queuedHeat;
        _queuedHeat = 0f;

        if (_localCookProgress >= shellBurnSeconds)
        {
            CurrentCookState = CookState.Burned;
            UpdateVisuals();
        }
    }

    // ── ICookable ──────────────────────────────────────────────────

    public void CookInFire(float heat)
    {
        if (CurrentCookState == CookState.Burned) return;
        if (IsNetworkReady && !HasStateAuthority) return;
        _queuedHeat += heat;
    }

    // ── IInteractable ──────────────────────────────────────────────

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Secondary)
        {
            if (CurrentCookState == CookState.Burned) return false;
            if (IsInsideSlime()) return false;
            return true;
        }

        return base.CanInteract(player, type);
    }

    public override void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Secondary)
        {
            if (IsContaminated)
            {
                // 오염 상태에서 열기 시도 → 함정
                NetworkId pid = player != null && player.Object != null && player.Object.IsValid
                    ? player.Object.Id : default;
                if (IsNetworkReady)
                {
                    if (!HasStateAuthority) { Rpc_RequestTrap(pid); return; }
                    ExecuteTrap(pid);
                }
                else ExecuteTrapLocal(player);
                return;
            }

            // 오염 해제 → 정상 열기
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) { Rpc_RequestOpen(); return; }
                SpawnOpenClam();
            }
            else SpawnOpenClamLocal();
            return;
        }

        base.Interact(player, type);
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Secondary && CurrentCookState != CookState.Burned && !IsInsideSlime())
            return "[F] 조개 열기";

        return base.GetInteractionLabel(player, type);
    }

    // ── 함정 (오염 상태에서 열기 시도) ───────────────────────────

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestTrap(NetworkId playerId) => ExecuteTrap(playerId);

    private void ExecuteTrap(NetworkId playerId)
    {
        RPC_PlayTrapEffect(playerId);
    }

    private void ExecuteTrapLocal(CookingMasterHandsManager player)
    {
        if (_powderRoutine != null) StopCoroutine(_powderRoutine);
        _powderRoutine = StartCoroutine(PowderRoutine());
        player?.ApplySleepEffect(sleepDuration);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayTrapEffect(NetworkId victimId)
    {
        if (_powderRoutine != null) StopCoroutine(_powderRoutine);
        _powderRoutine = StartCoroutine(PowderRoutine());

        // 열기를 시도한 플레이어만 잠듦
        if (Runner == null) return;
        NetworkObject obj = Runner.FindObject(victimId);
        CookingMasterHandsManager hands = obj?.GetComponent<CookingMasterHandsManager>();
        if (hands != null && hands.HasStateAuthority)
            hands.ApplySleepEffect(sleepDuration);
    }

    private IEnumerator PowderRoutine()
    {
        SetPowderEffect(true);
        yield return new WaitForSeconds(powderEffectDuration);
        SetPowderEffect(false);
        _powderRoutine = null;
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

    // ── 열기 로직 ──────────────────────────────────────────────────

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestOpen() => SpawnOpenClam();

    private void SpawnOpenClam()
    {
        if (clamOpenPrefab == null) return;

        int   count     = JewelCountNetworked;
        int   lastColor = LastRemovedColorIndex;
        CookState cook  = CookStateNetworked;
        float cookProg  = CookProgressNetworked;
        bool  contam    = IsContaminatedNetworked;
        int[] slots     = new int[8];
        for (int i = 0; i < 8; i++) slots[i] = JewelSlots[i];

        Runner.Spawn(clamOpenPrefab, transform.position, transform.rotation,
            onBeforeSpawned: (runner, obj) =>
            {
                ClamOpenItem open = obj.GetComponent<ClamOpenItem>();
                if (open == null) return;
                open.JewelCountNetworked          = count;
                open.LastRemovedColorIndexNetworked = lastColor;
                open.CookStateNetworked           = cook;
                open.CookProgressNetworked        = cookProg;
                open.IsContaminatedNetworked      = contam;
                for (int i = 0; i < 8; i++) open.JewelSlots.Set(i, slots[i]);
            });

        Runner.Despawn(Object);
    }

    private void SpawnOpenClamLocal()
    {
        if (clamOpenPrefab == null) return;

        GameObject go = Instantiate(clamOpenPrefab.gameObject, transform.position, transform.rotation);
        ClamOpenItem open = go.GetComponent<ClamOpenItem>();
        if (open != null)
        {
            open.InitializeFrom(_localJewelCount, _localLastColor,
                _localCookState, _localCookProgress, _localContaminated, _localSlots);
        }
        Destroy(gameObject);
    }

    // ── 유틸 ──────────────────────────────────────────────────────

    /// <summary>현재 슬라임에 포획된 상태인지 확인합니다.</summary>
    private bool IsInsideSlime()
    {
        if (!IsNetworkReady || !IsOnStation) return false;
        NetworkObject obj = Runner?.FindObject(StationId);
        return obj != null && obj.GetComponent<SlimeMob>() != null;
    }

    // ── 초기화 ────────────────────────────────────────────────────

    private void InitNetwork()
    {
        JewelCountNetworked     = 8;
        LastRemovedColorIndex   = -1;
        CookStateNetworked      = CookState.Raw;
        CookProgressNetworked   = 0f;
        IsContaminatedNetworked = true; // 초기 상태: 오염됨
        for (int i = 0; i < 8; i++) JewelSlots.Set(i, InitialColors[i]);
    }

    private void InitLocal()
    {
        _localJewelCount   = 8;
        _localLastColor    = -1;
        _localCookState    = CookState.Raw;
        _localCookProgress = 0f;
        _localContaminated = true; // 초기 상태: 오염됨
        for (int i = 0; i < 8; i++) _localSlots[i] = InitialColors[i];
    }

    // ── 비주얼 ────────────────────────────────────────────────────

    private void OnVisualChanged() => UpdateVisuals();

    private void UpdateVisuals()
    {
        if (shellRenderer == null) return;

        if (IsContaminated && contaminatedMaterial != null)
        {
            shellRenderer.material = contaminatedMaterial;
            return;
        }

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
