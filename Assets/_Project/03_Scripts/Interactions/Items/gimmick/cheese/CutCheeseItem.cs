using UnityEngine;
using Fusion;

/// <summary>
/// 잘린 용암 치즈 조각.
/// LavaCheeseItem 이 썰렸을 때 Spawn 되는 결과물입니다.
///
/// - ICookable: 조각도 불에 올리면 익힘 / 탄화 가능
/// - IsVented: 조각은 이미 열기가 빠진 상태 (항상 안전하게 집을 수 있음)
/// - TransferStateTo: 다른 CutCheeseItem 으로 조리 상태 전달
/// - itemName / ingredientID: 레시피 채점 시 "용암 치즈 조각" 으로 식별
/// </summary>
public class CutCheeseItem : PickableItem, ICookable
{
    [Header("Cooking Settings")]
    [SerializeField, Min(0.1f)] private float cookSeconds = 60f;
    [SerializeField, Min(0.1f)] private float burnSeconds = 90f;

    [Header("Visual Settings")]
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private Color rawColor    = new Color(1f, 0.4f, 0f);
    [SerializeField] private Color cookedColor = new Color(0.8f, 0.2f, 0f);
    [SerializeField] private Color burnedColor = Color.black;

    // ─────────────────────────────────────────
    // ICookable 네트워크 상태
    // ─────────────────────────────────────────

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

    // ─────────────────────────────────────────
    // 초기화 (부모 치즈 상태 상속용)
    // ─────────────────────────────────────────

    private bool _initializedFromParent = false;

    // ─────────────────────────────────────────
    // Unity / Fusion 생명주기
    // ─────────────────────────────────────────

    private void Start()
    {
        itemName = "용암 치즈 조각";
        metadata["ingredientID"] = "용암 치즈";
        UpdateVisuals();
    }

    public override void Spawned()
    {
        if (HasStateAuthority && !_initializedFromParent)
            CurrentCookState = CookState.Raw;

        UpdateVisuals();
    }

    private void OnCookStateChanged() => UpdateVisuals();

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();
    }

    // ─────────────────────────────────────────
    // ICookable — CookInFire
    // ─────────────────────────────────────────

    public void CookInFire(float heat)
    {
        if (IsNetworkReady && !HasStateAuthority) return;
        if (CurrentCookState == CookState.Burned) return;

        float burnAt = Mathf.Max(burnSeconds, cookSeconds);

        cookTime += heat;

        if (cookTime >= burnAt)
        {
            CurrentCookState = CookState.Burned;
        }
        else if (cookTime >= cookSeconds)
        {
            CurrentCookState = CookState.Cooked;
        }
    }

    // ─────────────────────────────────────────
    // 부모 상태 상속
    // ─────────────────────────────────────────

    /// <summary>
    /// LavaCheeseItem.SliceIntoCubesNow()에서 호출.
    /// 부모 치즈의 조리 상태를 그대로 이어받습니다.
    /// </summary>
    public void ApplyInheritedState(CookState inheritedState, float inheritedCookTime)
    {
        _initializedFromParent = true;
        _localCookState = inheritedState;
        cookTime = inheritedCookTime;

        // 네트워크 권한 있을 때만 networked 변수에 반영
        if (IsNetworkReady && HasStateAuthority)
            CookStateNetworked = inheritedState;

        UpdateVisuals();
    }

    // ─────────────────────────────────────────
    // 상태 전달 (Rpc_Cut / LocalCut 경로)
    // ─────────────────────────────────────────

    /// <summary>
    /// 썰기 결과로 Spawn 된 다른 CutCheeseItem 에 조리 상태를 복사합니다.
    /// PickableItem.Rpc_Cut() 이 자동 호출합니다.
    /// </summary>
    public override void TransferStateTo(PickableItem target)
    {
        if (target is CutCheeseItem other)
        {
            other.CurrentCookState = CurrentCookState;
            other.cookTime = cookTime;
            other.UpdateVisuals();
        }
    }

    // ─────────────────────────────────────────
    // 비주얼
    // ─────────────────────────────────────────

    public void UpdateVisuals()
    {
        UpdateItemName();

        if (targetRenderer == null) return;

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
        switch (CurrentCookState)
        {
            case CookState.Cooked:
                itemName = "익은 용암 치즈 조각";
                break;
            case CookState.Burned:
                itemName = "타버린 용암 치즈 조각";
                break;
            default:
                itemName = "용암 치즈 조각";
                break;
        }
    }
}
