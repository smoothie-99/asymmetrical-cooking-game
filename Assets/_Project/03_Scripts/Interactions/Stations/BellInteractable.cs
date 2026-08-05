using Interactions;
using UnityEngine;

/// <summary>
/// ServingStation 위의 벨 오브젝트.
/// [F] 를 누르면 테이블 위의 요리(PlateItem / SoupBowlItem)를 제출합니다.
/// </summary>
public class BellInteractable : MonoBehaviour, IInteractable
{
    [SerializeField] private ServingStation servingStation;

    private void Awake()
    {
        if (servingStation == null)
            servingStation = GetComponentInParent<ServingStation>();

        Outline outline = GetComponent<Outline>();
        if (outline == null) outline = gameObject.AddComponent<Outline>();
        outline.OutlineMode  = Outline.Mode.OutlineAll;
        outline.OutlineColor = Color.yellow;
        outline.OutlineWidth = 5f;
        outline.enabled      = false;
    }

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Secondary) return false;
        if (servingStation == null) return false;
        return servingStation.CurrentDish != null;
    }

    public void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Secondary) return;
        servingStation?.Submit();
    }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.Secondary && CanInteract(player, type))
            return "[주문 벨]\n[F] 요리 제출";
        return "";
    }

    public void OnFocus(CookingMasterHandsManager player)
    {
        Debug.Log($"[Bell] OnFocus — servingStation={servingStation}, currentDish={servingStation?.CurrentDish}, CanInteract={CanInteract(player, InteractionType.Secondary)}");
    }

    public void OnFocusLost() { }
}
