using UnityEngine;
using Fusion;
using System;
using System.Reflection;

/// <summary>
/// 수면 보석 조개살.
/// ClamOpenItem 을 썰면 스폰됩니다.
///
/// - SetFlavor(JewelColor): 조개를 썰 때 flavor 지정 (리플렉션 호출)
/// - ICookable: Raw → NotCooked → Cooked → TooCooked → Burned
/// - ISliceable: 도마에서 자름
///   · 가이드라인 A (포썰기)  → SlicedClamMeatItem
///   · 가이드라인 B (깍뚝썰기) → DicedClamMeatItem
/// </summary>
public class ClamMeatItem : PickableItem, ICookable, ICuttable
{
    [Header("Cut Guidelines")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = new CutGuidelineConfig[]
    {
        new CutGuidelineConfig { resultCounts = new int[] { 2 } }, // 포썰기
        new CutGuidelineConfig { resultCounts = new int[] { 4 } }, // 깍뚝썰기
    };

    [Header("Visual")]
    [SerializeField] private MeshRenderer[] _renderers;

    [Header("Cook Thresholds (seconds)")]
    [SerializeField, Min(0.1f)] private float notCookedAt =  8f;
    [SerializeField, Min(0.1f)] private float cookedAt    = 20f;
    [SerializeField, Min(0.1f)] private float tooCookedAt = 35f;
    [SerializeField, Min(0.1f)] private float burnedAt    = 50f;

    // ── 네트워크 상태 ──────────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }

    [Networked] public float CookProgressNetworked  { get; set; }
    [Networked] public bool  IsBeingCookedNetworked { get; set; }

    /// <summary>남은 보석 색별 카운트 (0=Yellow~4=Navy). 썰 때 flavor 결정에 사용.</summary>
    [Networked, Capacity(5)]
    public NetworkArray<int> JewelCounts => default;

    // ── 로컬 폴백 ─────────────────────────────────────────────────

    private CookState _localCookState    = CookState.Raw;
    private float     _localCookProgress = 0f;
    private readonly int[] _localJewelCounts = new int[5];
    private float     _queuedHeat        = 0f;

    private MaterialPropertyBlock _mpb;
    private static readonly int   ColorId      = Shader.PropertyToID("_BaseColor");
    private static readonly Color ColorRaw       = new Color(0.85f, 0.75f, 0.65f, 1f);
    private static readonly Color ColorNotCooked = new Color(0.80f, 0.55f, 0.40f, 1f);
    private static readonly Color ColorCooked    = new Color(0.65f, 0.38f, 0.22f, 1f);
    private static readonly Color ColorTooCooked = new Color(0.40f, 0.25f, 0.12f, 1f);
    private static readonly Color ColorBurned    = new Color(0.15f, 0.12f, 0.08f, 1f);

    private CutGuidelineVisual[] _guidelineVisuals;

    private bool IsNetworkReady => Object != null && Object.IsValid;

    // ── ICookable ──────────────────────────────────────────────────

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState _prev = CurrentCookState;
            if (IsNetworkReady) { if (HasStateAuthority) CookStateNetworked = value; }
            else _localCookState = value;
            RecordCookStateChange(_prev, value);
            UpdateVisuals();
            UpdateItemName();
        }
    }

    public bool isBeingCooked
    {
        get => IsNetworkReady ? IsBeingCookedNetworked : false;
        set { if (IsNetworkReady && HasStateAuthority) IsBeingCookedNetworked = value; }
    }

    // ── ISliceable ────────────────────────────────────────────────

    public override bool CanChop => CurrentCookState != CookState.Burned;

    // ── 생명주기 ──────────────────────────────────────────────────

    private void Start()
    {
        _mpb = new MaterialPropertyBlock();
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<MeshRenderer>(true);

        if (!IsNetworkReady) { _localCookState = CookState.Raw; _localCookProgress = 0f; }

        CreateGuidelineVisuals();
        UpdateVisuals();
        UpdateItemName();
    }

    public override void Spawned()
    {
        _mpb ??= new MaterialPropertyBlock();
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<MeshRenderer>(true);

        if (_guidelineVisuals == null) CreateGuidelineVisuals();
        UpdateVisuals();
        UpdateItemName();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
            ProcessHeat();
        else
            _queuedHeat = 0f;
        base.FixedUpdateNetwork();
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessHeat();
    }

    // ── 가이드라인 생성 ───────────────────────────────────────────

    private void CreateGuidelineVisuals()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0) return;

        _guidelineVisuals = new CutGuidelineVisual[_guidelineConfigs.Length];
        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform);
            go.transform.localPosition    = _guidelineConfigs[i].localPosition;
            go.transform.localEulerAngles = _guidelineConfigs[i].localEulerAngles;

            CutGuidelineVisual vis = go.AddComponent<CutGuidelineVisual>();
            vis.toolType      = "Knife";
            vis.cutName       = _guidelineConfigs[i].cutName;
            // ICutResultProvider 자동 읽기를 덮어씀 — 가이드라인별 결과물 직접 지정
            vis.resultPrefabs = _guidelineConfigs[i].resultPrefabs;
            vis.resultCounts  = _guidelineConfigs[i].resultCounts;
            _guidelineVisuals[i] = vis;
        }
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType)
    {
        if (_guidelineVisuals == null) return null;
        var list = new System.Collections.Generic.List<CutGuidelineVisual>();
        foreach (var v in _guidelineVisuals)
            if (v != null && v.toolType == toolType) list.Add(v);
        return list.ToArray();
    }

    // ── 조리 ──────────────────────────────────────────────────────

    public void CookInFire(float heat)
    {
        if (heat <= 0f) return;
        if (CurrentCookState == CookState.Burned) return;
        if (IsNetworkReady && !HasStateAuthority) return;
        _queuedHeat += heat;
    }

    private void ProcessHeat()
    {
        if (_queuedHeat <= 0f) { isBeingCooked = false; return; }
        if (CurrentCookState == CookState.Burned) { _queuedHeat = 0f; return; }

        isBeingCooked = true;
        if (IsNetworkReady) CookProgressNetworked += _queuedHeat;
        else _localCookProgress += _queuedHeat;
        _queuedHeat = 0f;

        float p = IsNetworkReady ? CookProgressNetworked : _localCookProgress;
        CookState next = ProgressToState(p);
        if (next != CurrentCookState) CurrentCookState = next;
    }

    private CookState ProgressToState(float p)
    {
        if (p >= burnedAt)    return CookState.Burned;
        if (p >= tooCookedAt) return CookState.TooCooked;
        if (p >= cookedAt)    return CookState.Cooked;
        if (p >= notCookedAt) return CookState.NotCooked;
        return CookState.Raw;
    }

    // ── 보석 카운트 초기화 ────────────────────────────────────────

    /// <summary>네트워크 환경: onBeforeSpawned 에서 호출.</summary>
    public void SetJewelCountsNetworked(int[] counts)
    {
        if (counts == null) return;
        for (int i = 0; i < 5 && i < counts.Length; i++)
            JewelCounts.Set(i, counts[i]);
    }

    /// <summary>로컬 환경: Instantiate 후 호출.</summary>
    public void InitializeWithJewelCounts(int[] counts)
    {
        if (counts == null) return;
        for (int i = 0; i < 5 && i < counts.Length; i++)
            _localJewelCounts[i] = counts[i];
    }

    private int GetJewelCount(int colorIndex)
    {
        if (colorIndex < 0 || colorIndex >= 5) return 0;
        return IsNetworkReady ? JewelCounts[colorIndex] : _localJewelCounts[colorIndex];
    }

    // ── 썰기 ──────────────────────────────────────────────────────

    public override void Chop(CuttingStation board)
    {
        if (!CanChop) return;
        if (IsNetworkReady && !HasStateAuthority) { RPC_RequestSlice(); return; }
        ExecuteSlice();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestSlice() { if (CanChop) ExecuteSlice(); }

    private void ExecuteSlice()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0) return;

        // 선택된 가이드라인 인덱스 (PickableItem.SelectedGuidelineIndex)
        int guideIdx = SelectedGuidelineIndex >= 0 ? SelectedGuidelineIndex : 0;
        if (guideIdx >= _guidelineConfigs.Length) guideIdx = 0;

        CutGuidelineConfig config = _guidelineConfigs[guideIdx];
        if (config.resultPrefabs == null || config.resultPrefabs.Length == 0) return;

        // 절단 기록
        if (!string.IsNullOrEmpty(config.cutName))
        {
            cookingSeq.Add(config.cutName);
            metadata["cutMethod"] = config.cutName;
        }

        // flavor 결정
        int[] counts = new int[5];
        for (int i = 0; i < 5; i++) counts[i] = GetJewelCount(i);
        JewelColor flavor = DetermineFlavor(counts);

        CookState cs = CurrentCookState;
        float     cp = IsNetworkReady ? CookProgressNetworked : _localCookProgress;

        Debug.Log($"[ClamMeat] 썰기. 가이드라인[{guideIdx}] flavor={flavor}");

        for (int p = 0; p < config.resultPrefabs.Length; p++)
        {
            if (config.resultPrefabs[p] == null) continue;
            int count = (config.resultCounts != null && p < config.resultCounts.Length)
                        ? config.resultCounts[p] : 1;

            for (int n = 0; n < count; n++)
            {
                Vector3 offset = transform.position + UnityEngine.Random.insideUnitSphere * 0.1f;

                if (IsNetworkReady && Runner != null && Object != null && Object.IsValid)
                {
                    JewelColor capturedFlavor = flavor;
                    CookState  capturedCs     = cs;
                    float      capturedCp     = cp;
                    var seqCopy  = new System.Collections.Generic.List<string>(cookingSeq);
                    var metaCopy = new System.Collections.Generic.Dictionary<string, string>(metadata);
                    Runner.Spawn(config.resultPrefabs[p], offset, transform.rotation,
                        onBeforeSpawned: (runner, obj) =>
                        {
                            obj.GetComponent<SlicedClamMeatItem>()?.SetFlavor(capturedFlavor);
                            obj.GetComponent<DicedClamMeatItem>()?.SetFlavor(capturedFlavor);
                            // cookState 전달
                            if (obj.TryGetComponent(out SlicedClamMeatItem s))
                            { s.CookStateNetworked = capturedCs; s.CookProgressNetworked = capturedCp; }
                            else if (obj.TryGetComponent(out DicedClamMeatItem d))
                            { d.CookStateNetworked = capturedCs; d.CookProgressNetworked = capturedCp; }
                            // cookingSeq / metadata 전달
                            var meat = obj.GetComponent<PickableItem>();
                            if (meat != null)
                            {
                                meat.cookingSeq = seqCopy;
                                foreach (var kv in metaCopy) meat.metadata[kv.Key] = kv.Value;
                            }
                        });
                }
                else
                {
                    GameObject go = Instantiate(config.resultPrefabs[p].gameObject, offset, transform.rotation);
                    go.GetComponent<SlicedClamMeatItem>()?.InitializeFrom((int)flavor, cs, cp);
                    go.GetComponent<DicedClamMeatItem>()?.InitializeFrom((int)flavor, cs, cp);
                    var meat = go.GetComponent<PickableItem>();
                    if (meat != null)
                    {
                        meat.cookingSeq = new System.Collections.Generic.List<string>(cookingSeq);
                        foreach (var kv in metadata) meat.metadata[kv.Key] = kv.Value;
                    }
                }
            }
        }

        SafeRemoveSelf();
    }

    /// <summary>
    /// 남은 보석 색별 카운트로 맛을 결정합니다.
    /// 한 색만 남아있으면 그 색의 맛, 두 색 이상이면 Astringent(떫은맛).
    /// </summary>
    public static JewelColor DetermineFlavor(int[] counts)
    {
        int survivingColor = -1;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] <= 0) continue;
            if (survivingColor != -1) return JewelColor.Astringent;
            survivingColor = i;
        }
        return survivingColor >= 0 ? (JewelColor)survivingColor : JewelColor.Astringent;
    }

    // ── 비주얼 ────────────────────────────────────────────────────

    private void OnCookStateChanged() { UpdateVisuals(); UpdateItemName(); }

    private void UpdateVisuals()
    {
        if (_renderers == null) return;
        _mpb ??= new MaterialPropertyBlock();

        Color c = CurrentCookState switch
        {
            CookState.NotCooked => ColorNotCooked,
            CookState.Cooked    => ColorCooked,
            CookState.TooCooked => ColorTooCooked,
            CookState.Burned    => ColorBurned,
            _                   => ColorRaw,
        };
        _mpb.SetColor(ColorId, c);
        foreach (MeshRenderer r in _renderers)
            if (r != null) r.SetPropertyBlock(_mpb);
    }

    private void UpdateItemName()
    {
        itemName = $"조개살 [{CurrentCookState.ToKoreanString()}]";
        metadata["ingredientID"] = "수면 보석 조개";
    }

    private void SafeRemoveSelf()
    {
        if (Object != null && Object.IsValid && Runner != null) Runner.Despawn(Object);
        else Destroy(gameObject);
    }
}
