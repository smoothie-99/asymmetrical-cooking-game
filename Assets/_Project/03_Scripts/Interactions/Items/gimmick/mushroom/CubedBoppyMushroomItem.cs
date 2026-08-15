using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 큐브 통통버섯 최종 결과물.
/// - 더 이상 자를 수 없음
/// - 도망가지 않음
/// </summary>
public class CubedBoppyMushroomItem : PickableItem, ICookable
{
    [Header("Cooking Settings")]
    [SerializeField, Min(0.1f)] private float cookSeconds = 60f;
    [SerializeField, Min(0.1f)] private float burnSeconds = 90f;

    [Networked, OnChangedRender(nameof(OnCookStateChanged))]
    public CookState CookStateNetworked { get; set; }

    [Networked]
    public bool IsBeingCookedNetworked { get; set; }

    [Networked]
    public float CookProgressNetworked { get; set; }

    private CookState _localCookState = CookState.Raw;
    private bool _localIsBeingCooked = false;
    private float _localCookProgress = 0f;

    private float _queuedHeat = 0f;

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
            UpdateVisuals();
        }
    }

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

    private void Start()
    {
        itemName = "큐브 통통버섯";
        metadata["ingredientID"] = "통통 버섯";
        UpdateVisuals();
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentCookState = CookState.Raw;
            CookProgressNetworked = 0f;
        }
        UpdateVisuals();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
        {
            _queuedHeat = 0f;
            return;
        }

        if (_queuedHeat > 0f && CurrentCookState != CookState.Burned)
        {
            isBeingCooked = true;
            CookProgressNetworked += _queuedHeat;
            
            if (CookProgressNetworked >= burnSeconds)
                CurrentCookState = CookState.Burned;
            else if (CookProgressNetworked >= cookSeconds)
                CurrentCookState = CookState.Cooked;
        }
        else
        {
            isBeingCooked = false;
        }

        _queuedHeat = 0f;
    }

    public void CookInFire(float heat)
    {
        if (heat <= 0f || CurrentCookState == CookState.Burned) return;
        _queuedHeat += heat;
    }

    private void OnCookStateChanged() => UpdateVisuals();

    private void UpdateVisuals()
    {
        // 비주얼 로직 (필요 시 색상 변경 등 추가 가능)
        if (CurrentCookState == CookState.Burned) itemName = "타버린 큐브 통통버섯";
        else if (CurrentCookState == CookState.Cooked) itemName = "익은 큐브 통통버섯";
    }

    public override bool CanChop => false;
}
