using UnityEngine;
using Fusion;

/// <summary>
/// 잘린 구름 토마토.
/// TomatoItem 이 썰렸을 때 Spawn 되는 결과물입니다.
///
/// - ICookable: 잘린 후에도 불에 올리면 탄화 가능
/// - TransferStateTo: 다른 CutTomatoItem 으로 조리 상태 전달
/// - itemName / ingredientID: 레시피 채점 시 "잘린 구름 토마토" 로 식별
/// </summary>
public class CutTomatoItem : PickableItem, ICookable
{
    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }
    private CookState _localCookState = CookState.Raw;
    public CookState CurrentCookState
    {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set
        {
            CookState _prev = CurrentCookState;
            if (IsNetworkReady) { if (HasStateAuthority) CookStateNetworked = value; }
            _localCookState = value;
            RecordCookStateChange(_prev, value);
            UpdateVisuals();
        }
    }

    [Networked] public bool IsBeingCookedNetworked { get; set; }
    private bool _localIsBeingCooked = false;
    public bool isBeingCooked
    {
        get => IsNetworkReady ? IsBeingCookedNetworked : _localIsBeingCooked;
        set
        {
            if (IsNetworkReady) { if (HasStateAuthority) IsBeingCookedNetworked = value; }
            _localIsBeingCooked = value;
        }
    }


    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color BurnedColor = new Color(0.15f, 0.12f, 0.08f, 1f);

    private MeshRenderer[]     _renderers;
    private MaterialPropertyBlock _mpb;

    private void Start()
    {
        itemName = "잘린 구름 토마토";
        metadata["ingredientID"] = "구름 토마토";
        CacheRenderers();
        UpdateVisuals();
    }

    public override void Spawned()
    {
        CacheRenderers();
        if (HasStateAuthority)
            CurrentCookState = CookState.Raw;
        UpdateVisuals();
    }

    private void OnCookStateChanged() => UpdateVisuals();

    // ── ICookable ──────────────────────────────────────────────────

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();
    }

    public void CookInFire(float heat)
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (CurrentCookState == CookState.Burned) return;

        cookTime += heat;

        if (cookTime >= 90f)
        {
            CurrentCookState = CookState.Burned;
            itemName = "타버린 구름 토마토 조각";
        }
        else if (cookTime >= 60f)
        {
            CurrentCookState = CookState.Cooked;
        }
    }

    // ── 상태 전달 ──────────────────────────────────────────────────

    /// <summary>
    /// 썰기 결과로 Spawn 된 다른 CutTomatoItem 에 조리 상태를 복사합니다.
    /// PickableItem.Rpc_Cut() 이 자동 호출합니다.
    /// </summary>
    public override void TransferStateTo(PickableItem target)
    {
        if (target is CutTomatoItem other)
        {
            other.CurrentCookState = CurrentCookState;
            other.cookTime = cookTime;
            other.UpdateVisuals();
        }
    }

    // ── 비주얼 ────────────────────────────────────────────────────

    private void CacheRenderers()
    {
        if (_renderers != null) return;
        _renderers = GetComponentsInChildren<MeshRenderer>(true);
        _mpb = new MaterialPropertyBlock();
    }

    public void UpdateVisuals()
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
}
