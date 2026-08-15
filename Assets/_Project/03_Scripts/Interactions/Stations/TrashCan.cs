using UnityEngine;
using Fusion;
using Interactions;

public class TrashCan : NetworkBehaviour, IInteractable
{
    [Header("Animation Settings")]
    public Animator trashAnimator;
    public string eatTriggerName  = "Eat";
    public string spitTriggerName = "Spit";

    [Tooltip("팽창 애니메이션을 적용할 시각적 모델 트랜스폼 (미지정시 자기자신)")]
    public Transform visualModel;

    private Vector3 originalScale;
    private Outline _outline;

    private void Awake()
    {
        if (visualModel == null)
            visualModel = this.transform;

        originalScale = visualModel.localScale;

        if (trashAnimator == null)
            trashAnimator = GetComponentInChildren<Animator>();
    }

    public override void Spawned()
    {
        _outline = GetComponent<Outline>();
        if (_outline == null)
        {
            _outline = gameObject.AddComponent<Outline>();
            _outline.OutlineMode  = Outline.Mode.OutlineAll;
            _outline.OutlineColor = Color.yellow;
            _outline.OutlineWidth = 3f;
        }
        _outline.enabled = false;
    }

    public void OnFocus(CookingMasterHandsManager player) => _outline.enabled = true;
    public void OnFocusLost() => _outline.enabled = false;

    private void OnTriggerEnter(Collider other)
    {
        if (Object != null && Object.IsValid && !HasStateAuthority) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        if (item == null || item.IsPhysicallyHeld || !item.CanBePickedUpByStation) return;

        // ITool (칼 등) → 뱉어냄
        if (item is ITool)
        {
            SpitOut(item);
            return;
        }

        // IServable (접시/그릇) → 내용물 비우고 뱉어냄
        if (item is IServable servable)
        {
            if (!servable.IsEmpty)
                servable.ClearDish();
            SpitOut(item);
            return;
        }

        // 일반 재료 → 먹음
        EatItem(item);
    }

    private void SpitOut(PickableItem item)
    {
        Vector3 dir = (item.transform.position - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
        dir = (dir.normalized + Vector3.up * 2f).normalized;

        Vector3 spitPos = transform.position + dir * 0.5f;
        Vector3 velocity = dir * 8f;
        Vector3 angularVelocity = new Vector3(
            Random.Range(-5f, 5f),
            Random.Range(-5f, 5f),
            Random.Range(-5f, 5f));

        PlayAnimation(spitTriggerName);

        item.SetHeldLayer(false);
        item.LocalThrow(spitPos, velocity, angularVelocity);
        if (item.Object != null && item.Object.IsValid)
            item.Rpc_Throw(spitPos, velocity, angularVelocity);
    }

    public void EatItem(PickableItem item)
    {
        if (item == null) return;

        PlayAnimation(eatTriggerName);

        if (Object != null && Object.IsValid)
            Runner.Despawn(item.Object);
        else
        {
            item.gameObject.SetActive(false);
            Destroy(item.gameObject, 0.1f);
        }
    }

    private void PlayAnimation(string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName)) return;
        if (trashAnimator != null) trashAnimator.SetTrigger(triggerName);
        if (Object != null && Object.IsValid) Rpc_PlayAnimation(triggerName);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.Proxies)]
    private void Rpc_PlayAnimation(string triggerName)
    {
        if (trashAnimator != null && !string.IsNullOrEmpty(triggerName))
            trashAnimator.SetTrigger(triggerName);
    }

    #region IInteractable Implementation

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type) => false;

    public void Interact(CookingMasterHandsManager player, InteractionType type) { }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type != InteractionType.Primary) return "";
        PickableItem held = player.GetActiveHandItem();
        if (held == null)
            return "[미믹 쓰레기통]\n[Q] 아이템을 던져서 버리세요";
        if (held is ITool || held is IServable)
            return $"[미믹 쓰레기통]\n[Q] {held.itemName} 던지면 뱉어냄";
        return $"[미믹 쓰레기통]\n[Q] {held.itemName} 던져서 버리기";
    }

    #endregion
}
