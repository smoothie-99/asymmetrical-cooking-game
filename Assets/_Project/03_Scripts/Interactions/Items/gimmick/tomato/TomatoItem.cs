using System.Linq;
using UnityEngine;
using Fusion;
using Interactions;

public class TomatoItem : PickableItem, ICookable, ICuttable
{
    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }
    private CookState _localCookState = CookState.Raw;
    public CookState CurrentCookState {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set {
            CookState _prev = CurrentCookState;
            if (IsNetworkReady) { if (HasStateAuthority) CookStateNetworked = value; }
            _localCookState = value;
            RecordCookStateChange(_prev, value);
            UpdateVisuals();
        }
    }

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
        if (target is TomatoItem other)
        {
            other.CurrentCookState = CurrentCookState;
            other.cookTime = cookTime;
        }
        else if (target is CutTomatoItem cut)
        {
            cut.CurrentCookState = CurrentCookState;
            cut.cookTime = cookTime;
        }
    }

    [Header("Cloud Tomato Properties")]
    public float timeIdle = 0f;
    public float floatThreshold = 10f; // 10초 방치 시 떠오름
    public float destroyThreshold = 15f; // 15초(10+5) 방치 시 파괴됨

    public bool isFloating = false;
    private float timeFloating = 0f;
    public override bool CanBePickedUpByStation => !isFloating;
    private Rigidbody myRb;
    
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color BurnedColor = new Color(0.15f, 0.12f, 0.08f, 1f);
    private MeshRenderer[]      _renderers;
    private MaterialPropertyBlock _mpb;

    // 요리 중인지 판별 (CookInFire 호출 여부로 자체 관리)
    private bool _receivedHeatThisFrame = false;

    void Start()
    {
        itemName = "구름 토마토";
        metadata["ingredientID"] = "구름 토마토";
        myRb = GetComponent<Rigidbody>();
        CacheRenderers();
        CreateGuidelineVisuals();
    }

    public override void Spawned()
    {
        CacheRenderers();
        if (HasStateAuthority) CurrentCookState = CookState.Raw;
    }

    private void OnCookStateChanged() => UpdateVisuals();

    private void CacheRenderers()
    {
        if (_renderers != null) return;
        _renderers = GetComponentsInChildren<MeshRenderer>(true);
        _mpb = new MaterialPropertyBlock();
    }

    private void UpdateVisuals()
    {
        if (_renderers == null) CacheRenderers();
        bool burned = CurrentCookState == CookState.Burned;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null) continue;
            if (burned)
            {
                _mpb.SetColor(ColorId, BurnedColor);
                _renderers[i].SetPropertyBlock(_mpb);
            }
            else
            {
                _renderers[i].SetPropertyBlock(null);
            }
        }
    }

    void Update()
    {
        // 이번 프레임 열 수신 여부 기록 후 초기화 (CookInFire 추적용)
        _receivedHeatThisFrame = false;

        // 손에 들고 있을 때만 방치 타이머 리셋
        // beingCooked는 조건에서 제거 → 불 위에 있어도 timeIdle 누적 → 10초 후 부양 가능
        if (isHeld)
        {
            timeIdle = 0f;
            timeFloating = 0f;
            if (isFloating)
            {
                isFloating = false;
                if (myRb != null) myRb.useGravity = true;
            }
            return;
        }

        // 이미 요리가 끝난(소스가 된) 상태면 구름 효과 적용 안 함
        if (CurrentCookState == CookState.Cooked || CurrentCookState == CookState.Burned) return;

        // [추가] 라운드가 '요리(Cook)' 상태가 아닐 때는 방치 타이머를 멈춥니다.
        // Sandbox(비네트워크) 모드에서는 라운드 상태를 무시하고 항상 작동하게 합니다.
        if (SystemManager.Instance != null)
        {
            if (SystemManager.Instance.CurrentMetaState != MetaState.Cooking) return;
        }

        timeIdle += Time.deltaTime;

        if (timeIdle >= floatThreshold && !isFloating)
        {
            // 네트워크 모드에서는 StateAuthority만 트리거
            if (!IsNetworkReady || HasStateAuthority)
                TriggerFloatEffect();
        }

        if (isFloating)
        {
            timeFloating += Time.deltaTime;

            // 네트워크 모드에서는 StateAuthority만 물리 제어
            bool canControlPhysics = !IsNetworkReady || HasStateAuthority;
            if (canControlPhysics && myRb != null)
            {
                myRb.linearVelocity = new Vector3(myRb.linearVelocity.x, 0.5f, myRb.linearVelocity.z);
            }

            if (timeFloating >= destroyThreshold)
            {
                Debug.Log("구름 토마토가 하늘 높이 날아가 버렸습니다!");
                if (IsNetworkReady)
                {
                    if (HasStateAuthority) Runner.Despawn(Object);
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }
    }

    private void TriggerFloatEffect()
    {
        isFloating = true;
        Debug.Log("구름 토마토가 10초 동안 방치되어 공중으로 떠오르기 시작합니다!");

        // 스테이션에 올려져 있으면 탈출
        if (IsOnStation)
        {
            Vector3 escapePos = transform.position;
            if (IsNetworkReady) Rpc_Drop(escapePos);
            else LocalDrop(escapePos);
        }

        if (myRb != null)
        {
            myRb.isKinematic = false;
            myRb.useGravity  = false;
        }
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();
    }

    public void CookInFire(float heat)
    {
        _receivedHeatThisFrame = true;
        if (IsNetworkReady && !HasStateAuthority) return;
        if (CurrentCookState == CookState.Burned) return;

        cookTime += heat;

        if (cookTime >= 60f)
        {
            CurrentCookState = CookState.Burned;
            itemName = "타버린 구름 토마토";
        }
        else if (cookTime >= 30f)
        {
            CurrentCookState = CookState.Cooked;
        }
    }

}
