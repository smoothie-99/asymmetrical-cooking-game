using UnityEngine;
using Fusion;
using Interactions;

// MVP 요구사항: 파스타 면의 조리 상태를 관리하는 스크립트
// PickableItem을 상속받아 들고 다닐 수 있으며, 자체적인 끓임 상태(생면 -> 익음 -> 탐)를 추적합니다.
public class PastaItem : PickableItem, ICookable
{
    [Networked] public CookState CookStateNetworked { get; set; } = CookState.Raw;
    private CookState _localCookState = CookState.Raw;
    public CookState CurrentCookState {
        get => IsNetworkReady ? CookStateNetworked : _localCookState;
        set {
            CookState prev = CurrentCookState;
            if (IsNetworkReady) CookStateNetworked = value;
            _localCookState = value;
            RecordCookStateChange(prev, value);
        }
    }
    public bool isBeingCooked { get; set; }

    [Header("Pasta Status (Networked)")]
    [Networked] public float BoiledTimeNetworked { get; set; }
    private float _localBoiledTime = 0f;
    public float boiledTime {
        get => IsNetworkReady ? BoiledTimeNetworked : _localBoiledTime;
        set {
            if (IsNetworkReady) BoiledTimeNetworked = value;
            _localBoiledTime = value;
        }
    }
    
    public float targetCookTimeMin = 30f;
    public float targetCookTimeMax = 40f;

    [Header("Noodle Components (Auto-collected)")]
    private Rigidbody[] childRigidbodies;
    private Renderer[] childRenderers;

    void Start()
    {
        itemName = "파스타 면";
        
        // 부모(자신)의 Rigidbody는 제외하고 자식들의 물리만 가져옵니다.
        Rigidbody rootRb = GetComponent<Rigidbody>();
        Rigidbody[] allRbs = GetComponentsInChildren<Rigidbody>();
        
        // 자기 자신을 제외한 리스트 만들기
        System.Collections.Generic.List<Rigidbody> filtered = new System.Collections.Generic.List<Rigidbody>();
        foreach(var rb in allRbs)
        {
            if (rb != rootRb) filtered.Add(rb);
        }
        childRigidbodies = filtered.ToArray();

        // 렌더러 캐싱 (색상 변경용)
        childRenderers = GetComponentsInChildren<Renderer>();
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentCookState = CookState.Raw;
            boiledTime = 0f;
        }
        UpdatePhysicalState();
    }

    public override void Render()
    {
        UpdatePhysicalState();
    }

    private void UpdatePhysicalState()
    {
        bool shouldBeFloppy = (CurrentCookState == CookState.Cooked || CurrentCookState == CookState.Burned);

        // 모든 면발의 물리 상태 제어
        if (childRigidbodies != null)
        {
            foreach (var rb in childRigidbodies)
            {
                if (rb == null) continue;
                rb.isKinematic = !shouldBeFloppy;
            }
        }

        // 상태별 이름 및 비주얼 (색상) 처리
        if (CurrentCookState == CookState.Cooked)
        {
            itemName = "익은 파스타 면";
        }
        else if (CurrentCookState == CookState.Burned)
        {
            itemName = "타버린 파스타 면";
            ApplyBurnedVisuals();
        }
        else
        {
            itemName = "파스타 면";
        }
    }

    private void ApplyBurnedVisuals()
    {
        if (childRenderers == null) return;
        
        foreach (Renderer r in childRenderers)
        {
            if (r == null) continue;
            foreach (Material mat in r.materials)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.1f, 0.1f, 0.1f));
                else if (mat.HasProperty("_Color")) mat.color = new Color(0.1f, 0.1f, 0.1f);
            }
        }
    }

    public void CookInFire(float heat)
    {
        // heat = deltaTime * temperature (호출자가 이미 곱해서 전달)
        Boil(heat);
    }

    public void Boil(float deltaTime, float heatMultiplier = 1.0f)
    {
        if (CurrentCookState == CookState.Burned) return;

        float current = boiledTime;
        current += deltaTime * heatMultiplier;
        boiledTime = current;

        CheckCookState();
    }

    private void CheckCookState()
    {
        if (boiledTime < targetCookTimeMin) CurrentCookState = CookState.Raw;
        else if (boiledTime >= targetCookTimeMin && boiledTime <= targetCookTimeMax) CurrentCookState = CookState.Cooked;
        else if (boiledTime > targetCookTimeMax) CurrentCookState = CookState.Burned;
    }
}
