using UnityEngine;
using Interactions;

/// <summary>
/// 둥둥 떠있는 비주얼 모델에 부착되어, 에임 상호작용을 SupplyStation으로 토스해주는 컴포넌트입니다.
/// </summary>
public class FloatingItemInteractable : MonoBehaviour, IInteractable
{
    private SupplyStation _station;

    public void Init(SupplyStation station)
    {
        _station = station;
    }

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (_station == null) return false;
        return _station.CanInteract(player, type);
    }

    public void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (_station != null)
        {
            _station.Interact(player, type);
        }
    }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (_station == null) return "";
        return _station.GetInteractionLabel(player, type);
    }

    public void OnFocus(CookingMasterHandsManager player)
    {
        if (_station != null) _station.OnFocus(player);
    }

    public void OnFocusLost()
    {
        if (_station != null) _station.OnFocusLost();
    }
}
