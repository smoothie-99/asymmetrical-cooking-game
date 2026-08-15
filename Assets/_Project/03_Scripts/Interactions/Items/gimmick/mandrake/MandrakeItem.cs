using System.Linq;
using System.Collections;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 통 맨드레이크. 칼로 자르면 머리(MandrakeHeadItem) + 몸통(CutMandrakeItem)으로 분리됩니다.
/// 절단 시 비명을 질러 범위 내 귀마개 없는 플레이어를 15초 동안 청력 마비시킵니다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MandrakeItem : PickableItem, ICookable, ICuttable
{
    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }
    private CookState _localCookState = CookState.Raw;

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState prev = CurrentCookState;
            if (IsNetworkReady) { if (HasStateAuthority) CookStateNetworked = value; }
            _localCookState = value;
            RecordCookStateChange(prev, value);
            UpdateVisuals();
        }
    }

    [Header("Scream")]
    [SerializeField] private AudioClip screamSound;
    [SerializeField, Min(0.1f)] private float screamRadius = 8f;
    [SerializeField, Min(0.1f)] private float deafDuration = 15f;
    [SerializeField, Min(0.1f)] private float screamVisualDuration = 2f;
    [SerializeField] private GameObject screamEffect;

    private AudioSource _audioSource;
    private Coroutine _screamEffectRoutine;

    [Header("Cutting")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new CutGuidelineConfig() };
    private CutGuidelineVisual[] _guidelineVisuals;

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
            vis.resultPrefabs = _guidelineConfigs[i].resultPrefabs;
            vis.resultCounts  = _guidelineConfigs[i].resultCounts;
            _guidelineVisuals[i] = vis;
        }
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType) =>
        _guidelineVisuals != null
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();

    public override void TransferStateTo(PickableItem target)
    {
        if (target is CutMandrakeItem cut)
            cut.CurrentCookState = CurrentCookState;
        else if (target is MandrakeHeadItem head)
            head.CurrentCookState = CurrentCookState;
    }


    private MeshRenderer[]        _renderers;
    private MaterialPropertyBlock  _mpb;
    private static readonly int    ColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly Color  BurnedColor = new Color(0.15f, 0.12f, 0.08f, 1f);

    private void Start()
    {
        itemName = "맨드레이크";
        metadata["ingredientID"] = "맨드레이크";
        _audioSource = GetComponent<AudioSource>();
        _audioSource.spatialBlend = 1f;
        _audioSource.playOnAwake = false;
        CacheRenderers();
        CreateGuidelineVisuals();
        SetScreamEffect(false);
    }

    public override void Spawned()
    {
        _audioSource = GetComponent<AudioSource>();
        CacheRenderers();
        if (HasStateAuthority) CurrentCookState = CookState.Raw;
        SetScreamEffect(false);
    }

    private void OnCookStateChanged() => UpdateVisuals();

    private void CacheRenderers()
    {
        if (_renderers != null) return;
        _renderers = GetComponentsInChildren<MeshRenderer>(true);
        _mpb       = new MaterialPropertyBlock();
    }

    private void UpdateVisuals()
    {
        if (_renderers == null) CacheRenderers();
        bool burned = CurrentCookState == CookState.Burned;
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            if (burned) { _mpb.SetColor(ColorId, BurnedColor); r.SetPropertyBlock(_mpb); }
            else        { r.SetPropertyBlock(null); }
        }
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();
    }

    public void CookInFire(float heat)
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (CurrentCookState == CookState.Burned) return;
        cookTime += heat;
        if (cookTime >= 30f) CurrentCookState = CookState.Burned;
    }

    // ── 비명 ─────────────────────────────────────────────────────

    protected override void OnCutExecuted(CutGuidelineVisual guide)
    {
        if (IsNetworkReady)
            Rpc_PlayScreamFeedback(transform.position, screamRadius, deafDuration, screamVisualDuration);
        else
            PlayScreamLocal(transform.position, screamRadius, deafDuration, screamVisualDuration);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_PlayScreamFeedback(Vector3 origin, float radius, float deafTime, float visualTime)
    {
        PlayScreamLocal(origin, radius, deafTime, visualTime);
    }

    private void PlayScreamLocal(Vector3 origin, float radius, float deafTime, float visualTime)
    {
        if (_audioSource == null) _audioSource = GetComponent<AudioSource>();
        if (_audioSource != null && screamSound != null)
            _audioSource.PlayOneShot(screamSound);

        if (_screamEffectRoutine != null) StopCoroutine(_screamEffectRoutine);
        _screamEffectRoutine = StartCoroutine(ScreamEffectCoroutine(visualTime));

        TryApplyDeafToLocalPlayer(origin, radius, deafTime);
    }

    private IEnumerator ScreamEffectCoroutine(float duration)
    {
        SetScreamEffect(true);
        yield return new WaitForSeconds(duration);
        SetScreamEffect(false);
        _screamEffectRoutine = null;
    }

    private void SetScreamEffect(bool active)
    {
        if (screamEffect != null) screamEffect.SetActive(active);
    }

    private void TryApplyDeafToLocalPlayer(Vector3 origin, float radius, float duration)
    {
        CookingMasterHandsManager hands = FindLocalHandsManager();
        if (hands == null) return;
        if (Vector3.Distance(origin, hands.transform.position) > radius) return;
        if (hands.WornItem is EarplugsItem)
        {
            Debug.Log("[Mandrake] 귀마개 착용 — 비명 차단");
            return;
        }

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.ApplyDeafEffect(duration);
            Debug.Log($"[Mandrake] 비명 적중 — {duration}초 청력 마비");
        }
        else
        {
            Debug.LogWarning("[Mandrake] SoundManager 인스턴스를 찾지 못했습니다.");
        }
    }

    private CookingMasterHandsManager FindLocalHandsManager()
    {
        CookingMasterHandsManager[] all =
            FindObjectsByType<CookingMasterHandsManager>(FindObjectsSortMode.None);

        foreach (CookingMasterHandsManager h in all)
        {
            if (h == null) continue;
            if (IsNetworkReady ? h.HasStateAuthority : h.isActiveAndEnabled)
                return h;
        }
        return null;
    }
}
