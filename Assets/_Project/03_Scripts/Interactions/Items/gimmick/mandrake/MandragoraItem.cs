using UnityEngine;
using Fusion;
using Interactions;
using System.Collections;
using System.Reflection;

/// <summary>
/// 만드라고라 식재료 스크립트.
///
/// [규칙]
/// - 처음 줍기에서는 비명을 지르지 않음
/// - 칼로 손질(Chop)할 때만 비명을 지름
/// - 비명을 들어도 손질 자체는 정상 진행됨
/// - 귀마개가 없으면 15초간 청력 마비
/// - 삶기만 정상 조리 가능
/// - 직화는 즉시 풍미 상실(망가짐)
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MandragoraItem : PickableItem, ICookable
{
    [Header("Mandragora Settings")]
    [SerializeField, Min(0.1f)] private float screamRadius = 8.0f;
    [SerializeField, Min(0.1f)] private float deafDuration = 15.0f;
    [SerializeField, Min(0.1f)] private float screamVisualDuration = 2.0f;
    [SerializeField] private AudioClip screamSound;

    [Header("Optional Chop Result")]
    [SerializeField] private NetworkObject choppedPrefab; // 있으면 손질 결과물 생성, 없으면 현재 오브젝트만 손질 완료 처리
    [SerializeField] private bool destroyOnChopIfNoPrefab = false;

    [Header("Cooking Settings")]
    [SerializeField, Min(0.1f)] private float cookSeconds = 90f;   // 1:30
    [SerializeField, Min(0.1f)] private float burnSeconds = 120f;  // 2:00

    [Header("Visuals")]
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private Color rawColor = new Color(0.7f, 0.5f, 0.3f);
    [SerializeField] private Color cookedColor = new Color(0.9f, 0.8f, 0.6f);
    [SerializeField] private Color burnedColor = Color.black;
    [SerializeField] private GameObject screamEffect;

    // ─────────────────────────────────────────
    // 조리 상태
    // ─────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }

    private CookState _localCookState = CookState.Raw;

    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState _prev = CurrentCookState;
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookStateNetworked = value;
            }

            _localCookState = value;
            RecordCookStateChange(_prev, value);
            RefreshVisualState();
        }
    }

    [Networked]
    public bool IsBeingCookedNetworked { get; set; }

    private bool _localIsBeingCooked = false;

    public bool isBeingCooked
    {
        get => IsNetworkReady ? IsBeingCookedNetworked : _localIsBeingCooked;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                IsBeingCookedNetworked = value;
            }

            _localIsBeingCooked = value;
        }
    }

    [Networked]
    public float CookProgressNetworked { get; set; }

    private float _localCookProgress = 0f;

    public float CookProgress
    {
        get => IsNetworkReady ? CookProgressNetworked : _localCookProgress;
        private set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                CookProgressNetworked = value;
            }

            _localCookProgress = value;
        }
    }

    // ─────────────────────────────────────────
    // 추가 상태
    // ─────────────────────────────────────────

    [Networked, OnChangedRender(nameof(OnFireRuinStateChanged))]
    public bool RuinedByDirectFireNetworked { get; set; }

    private bool _localRuinedByDirectFire = false;

    private bool RuinedByDirectFire
    {
        get => IsNetworkReady ? RuinedByDirectFireNetworked : _localRuinedByDirectFire;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                RuinedByDirectFireNetworked = value;
            }

            _localRuinedByDirectFire = value;
            RefreshVisualState();
        }
    }

    [Networked, OnChangedRender(nameof(OnTrimmedStateChanged))]
    public bool IsTrimmedNetworked { get; set; }

    private bool _localIsTrimmed = false;

    private bool IsTrimmed
    {
        get => IsNetworkReady ? IsTrimmedNetworked : _localIsTrimmed;
        set
        {
            if (IsNetworkReady)
            {
                if (!HasStateAuthority) return;
                IsTrimmedNetworked = value;
            }

            _localIsTrimmed = value;
            RefreshVisualState();
        }
    }

    // ─────────────────────────────────────────
    // 내부 버퍼
    // ─────────────────────────────────────────

    private AudioSource _audioSource;
    private Coroutine _localScreamEffectRoutine;

    private float _queuedBoilHeat = 0f;
    private float _queuedDirectFireHeat = 0f;
    private bool _sliceRequested = false;

    private bool _localInitApplied = false;
    private bool _initializedFromParent = false;

    public override bool CanChop => !isHeld && CurrentCookState != CookState.Burned && !IsTrimmed;

    private void Start()
    {
        itemName = "만드라고라";
        metadata["ingredientID"] = "만드라고라";
        _audioSource = GetComponent<AudioSource>();
        ConfigureAudioSource();

        if (!IsNetworkReady && !_localInitApplied)
        {
            _localCookState = CookState.Raw;
            _localCookProgress = 0f;
            _localIsBeingCooked = false;
            _localRuinedByDirectFire = false;
            _localIsTrimmed = false;
            _localInitApplied = true;
        }

        RefreshVisualState();
        SetScreamEffect(false);
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady) return;
        ProcessSimulationStep();
    }

    private void OnValidate()
    {
        if (burnSeconds < cookSeconds)
            burnSeconds = cookSeconds;

        if (screamRadius < 0.1f)
            screamRadius = 0.1f;

        if (deafDuration < 0.1f)
            deafDuration = 0.1f;
    }

    public override void Spawned()
    {
        _audioSource = GetComponent<AudioSource>();
        ConfigureAudioSource();

        if (HasStateAuthority && !_initializedFromParent)
        {
            CurrentCookState = CookState.Raw;
            CookProgress = 0f;
            isBeingCooked = false;
            RuinedByDirectFire = false;
            IsTrimmed = false;
        }

        RefreshVisualState();
        SetScreamEffect(false);
    }

    public override void Render()
    {
        base.Render();

        if (!IsNetworkReady)
            return;

        bool dirty = false;

        if (_localCookState != CookStateNetworked)
        {
            _localCookState = CookStateNetworked;
            dirty = true;
        }

        if (_localRuinedByDirectFire != RuinedByDirectFireNetworked)
        {
            _localRuinedByDirectFire = RuinedByDirectFireNetworked;
            dirty = true;
        }

        if (_localIsTrimmed != IsTrimmedNetworked)
        {
            _localIsTrimmed = IsTrimmedNetworked;
            dirty = true;
        }

        if (dirty)
            RefreshVisualState();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
            ProcessSimulationStep();
        else
        {
            _queuedBoilHeat = 0f;
            _queuedDirectFireHeat = 0f;
        }
        base.FixedUpdateNetwork();
    }

    private void ConfigureAudioSource()
    {
        if (_audioSource == null)
            return;

        _audioSource.spatialBlend = 1.0f;
        _audioSource.playOnAwake = false;

        if (screamSound != null)
            _audioSource.clip = screamSound;
    }

    // ─────────────────────────────────────────
    // 조리
    // ─────────────────────────────────────────

    /// <summary>
    /// 직화. 만드라고라는 직화되면 즉시 풍미를 잃습니다.
    /// </summary>
    public void CookInFire(float heat)
    {
        if (heat <= 0f)
            return;

        if (CurrentCookState == CookState.Burned)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
                return;

            _queuedDirectFireHeat += heat;
            return;
        }

        _queuedDirectFireHeat += heat;
    }

    /// <summary>
    /// 삶기 전용. 보일링 스테이션에서 호출.
    /// </summary>
    public void BoilInWater(float heat)
    {
        if (heat <= 0f)
            return;

        if (CurrentCookState == CookState.Burned)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
                return;

            _queuedBoilHeat += heat;
            return;
        }

        _queuedBoilHeat += heat;
    }

    private void ProcessSimulationStep()
    {
        if (CurrentCookState == CookState.Burned)
        {
            _queuedBoilHeat = 0f;
            _queuedDirectFireHeat = 0f;
            _sliceRequested = false;
            isBeingCooked = false;
            return;
        }

        bool hasDirectFireThisStep = _queuedDirectFireHeat > 0f;
        bool hasBoilHeatThisStep = _queuedBoilHeat > 0f;

        isBeingCooked = hasDirectFireThisStep || hasBoilHeatThisStep;

        // 1) 직화는 즉시 풍미 상실
        if (hasDirectFireThisStep)
        {
            RuinByDirectFireNow();
            _queuedDirectFireHeat = 0f;
            _queuedBoilHeat = 0f;
            _sliceRequested = false;
            return;
        }

        // 2) 삶기 진행
        if (hasBoilHeatThisStep)
        {
            CookProgress += _queuedBoilHeat;
            EvaluateBoilState();
        }

        _queuedBoilHeat = 0f;
        _queuedDirectFireHeat = 0f;

        if (_sliceRequested)
        {
            _sliceRequested = false;

            if (CanChop)
            {
                ExecuteTrim();
                return;
            }
        }

        if (CurrentCookState == CookState.Burned)
            isBeingCooked = false;
    }

    private void EvaluateBoilState()
    {
        if (CookProgress >= burnSeconds)
        {
            RuinedByDirectFire = false;
            CurrentCookState = CookState.Burned;
            isBeingCooked = false;
            return;
        }

        if (CookProgress >= cookSeconds)
        {
            CurrentCookState = CookState.Cooked;
        }
    }

    private void RuinByDirectFireNow()
    {
        RuinedByDirectFire = true;
        CookProgress = Mathf.Max(CookProgress, burnSeconds);
        CurrentCookState = CookState.Burned;
        isBeingCooked = false;

        Debug.Log("[Mandragora] 불에 직접 닿아 풍미가 사라졌습니다.");
    }

    // ─────────────────────────────────────────
    // 손질
    // ─────────────────────────────────────────

    public override void Chop(CuttingStation board)
    {
        if (!CanChop)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                RPC_RequestChop();
                return;
            }

            FireScreamEventNow();
            _sliceRequested = true;
            return;
        }

        FireScreamEventNow();
        _sliceRequested = true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestChop()
    {
        if (!CanChop)
            return;

        FireScreamEventNow();
        _sliceRequested = true;
    }

    private void ExecuteTrim()
    {
        // choppedPrefab이 있으면 교체 생성
        if (choppedPrefab != null)
        {
            SpawnTrimmedResult();
            return;
        }

        // 프리팹이 없으면 현재 오브젝트만 손질 완료 처리
        IsTrimmed = true;
        itemName = "손질된 만드라고라";
        metadata["ingredientID"] = "손질된 만드라고라";

        if (destroyOnChopIfNoPrefab)
        {
            SafeRemoveSelf();
        }
    }

    private void SpawnTrimmedResult()
    {
        Vector3 spawnPos = transform.position;
        Quaternion spawnRot = transform.rotation;

        if (IsNetworkReady && Runner != null && Object != null && Object.IsValid)
        {
            NetworkObject spawnedObj = Runner.Spawn(choppedPrefab, spawnPos, spawnRot);
            MandragoraItem trimmed = spawnedObj != null ? spawnedObj.GetComponent<MandragoraItem>() : null;

            if (trimmed != null)
            {
                trimmed.ApplyInheritedState(true, CurrentCookState, CookProgress, RuinedByDirectFire);
            }
        }
        else
        {
            NetworkObject spawnedObj = Instantiate(choppedPrefab, spawnPos, spawnRot);
            MandragoraItem trimmed = spawnedObj != null ? spawnedObj.GetComponent<MandragoraItem>() : null;

            if (trimmed != null)
            {
                trimmed.ApplyInheritedState(true, CurrentCookState, CookProgress, RuinedByDirectFire);
            }
        }

        SafeRemoveSelf();
    }

    public void ApplyInheritedState(bool trimmed, CookState cookState, float cookProgress, bool ruinedByFire)
    {
        _initializedFromParent = true;
        _localInitApplied = true;

        IsTrimmed = trimmed;
        CurrentCookState = cookState;
        CookProgress = cookProgress;
        RuinedByDirectFire = ruinedByFire;
        isBeingCooked = false;

        RefreshVisualState();
    }

    // ─────────────────────────────────────────
    // 비명 이벤트
    // ─────────────────────────────────────────

    private void FireScreamEventNow()
    {
        Vector3 screamOrigin = transform.position;

        if (IsNetworkReady)
        {
            RPC_PlayScreamFeedback(screamOrigin, screamRadius, deafDuration, screamVisualDuration);
        }
        else
        {
            PlayScreamFeedbackLocal(screamOrigin, screamRadius, deafDuration, screamVisualDuration);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayScreamFeedback(Vector3 screamOrigin, float radius, float deafTime, float visualTime)
    {
        PlayScreamFeedbackLocal(screamOrigin, radius, deafTime, visualTime);
    }

    private void PlayScreamFeedbackLocal(Vector3 screamOrigin, float radius, float deafTime, float visualTime)
    {
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();

        if (_audioSource != null && screamSound != null)
        {
            _audioSource.PlayOneShot(screamSound);
        }

        if (_localScreamEffectRoutine != null)
            StopCoroutine(_localScreamEffectRoutine);

        _localScreamEffectRoutine = StartCoroutine(LocalScreamEffectCoroutine(visualTime));

        TryApplyDeafToLocalPlayerIfNeeded(screamOrigin, radius, deafTime);
    }

    private IEnumerator LocalScreamEffectCoroutine(float duration)
    {
        SetScreamEffect(true);
        yield return new WaitForSeconds(duration);
        SetScreamEffect(false);
        _localScreamEffectRoutine = null;
    }

    private void SetScreamEffect(bool active)
    {
        if (screamEffect != null)
            screamEffect.SetActive(active);
    }

    private void TryApplyDeafToLocalPlayerIfNeeded(Vector3 screamOrigin, float radius, float duration)
    {
        CookingMasterHandsManager localHands = FindLocalHandsManager();
        if (localHands == null)
            return;

        float distance = Vector3.Distance(screamOrigin, localHands.transform.position);
        if (distance > radius)
            return;

        if (PlayerHasEarplugs(localHands))
        {
            Debug.Log("[Mandragora] 귀마개를 가지고 있어 비명을 막아냈습니다.");
            return;
        }

        if (TryInvokeApplyDeafEffect(localHands, duration))
        {
            Debug.Log($"[Mandragora] 비명에 맞아 {duration}초 동안 청력 마비.");
        }
        else
        {
            Debug.LogWarning("[Mandragora] ApplyDeafEffect(float) 수신 메서드를 찾지 못했습니다.");
        }
    }

    private CookingMasterHandsManager FindLocalHandsManager()
    {
        CookingMasterHandsManager[] allHands =
            FindObjectsByType<CookingMasterHandsManager>(FindObjectsSortMode.None);

        if (IsNetworkReady)
        {
            for (int i = 0; i < allHands.Length; i++)
            {
                CookingMasterHandsManager hands = allHands[i];
                if (hands != null && hands.HasStateAuthority)
                    return hands;
            }

            return null;
        }

        for (int i = 0; i < allHands.Length; i++)
        {
            CookingMasterHandsManager hands = allHands[i];
            if (hands != null && hands.isActiveAndEnabled)
                return hands;
        }

        return null;
    }

    private bool PlayerHasEarplugs(CookingMasterHandsManager player)
    {
        if (player == null)
            return false;

        // 착용 슬롯에 귀마개가 있으면 보호
        return player.WornItem is EarplugsItem;
    }

    private bool TryInvokeApplyDeafEffect(CookingMasterHandsManager player, float duration)
    {
        if (player == null)
            return false;

        Component[] candidates = player.GetComponentsInParent<Component>(true);
        for (int i = 0; i < candidates.Length; i++)
        {
            Component c = candidates[i];
            if (c == null)
                continue;

            MethodInfo method = c.GetType().GetMethod(
                "ApplyDeafEffect",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(float) },
                null
            );

            if (method != null)
            {
                method.Invoke(c, new object[] { duration });
                return true;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────
    // 렌더 / 외형
    // ─────────────────────────────────────────

    private void OnCookStateChanged()
    {
        _localCookState = CookStateNetworked;
        RefreshVisualState();
    }

    private void OnFireRuinStateChanged()
    {
        _localRuinedByDirectFire = RuinedByDirectFireNetworked;
        RefreshVisualState();
    }

    private void OnTrimmedStateChanged()
    {
        _localIsTrimmed = IsTrimmedNetworked;
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        UpdateItemName();
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (targetRenderer == null)
            return;

        switch (CurrentCookState)
        {
            case CookState.Raw:
                targetRenderer.material.color = rawColor;
                break;

            case CookState.Cooked:
                targetRenderer.material.color = cookedColor;
                break;

            case CookState.Burned:
                targetRenderer.material.color = burnedColor;
                break;
        }
    }

    private void UpdateItemName()
    {
        if (CurrentCookState == CookState.Cooked)
        {
            itemName = IsTrimmed ? "손질된 잘 삶은 만드라고라" : "잘 삶은 만드라고라";
            return;
        }

        if (CurrentCookState == CookState.Burned)
        {
            if (RuinedByDirectFire)
                itemName = IsTrimmed ? "손질된 풍미를 잃은 만드라고라" : "풍미를 잃은 만드라고라";
            else
                itemName = IsTrimmed ? "손질된 타버린 만드라고라" : "타버린 만드라고라";
            return;
        }

        itemName = IsTrimmed ? "손질된 만드라고라" : "만드라고라";
    }

    private void SafeRemoveSelf()
    {
        if (Object != null && Object.IsValid && Runner != null)
            Runner.Despawn(Object);
        else
            Destroy(gameObject);
    }
}