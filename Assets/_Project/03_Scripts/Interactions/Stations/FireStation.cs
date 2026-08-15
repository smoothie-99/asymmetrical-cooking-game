using System;
using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using Fusion;
using YourNamespace; // FullOpaqueFire 에셋의 VFX_FireController 네임스페이스
using Interactions;

// MVP 기획: "화구 스테이션" (이전 이름: 용의 숨결 풀무)
// 플레이어가 상호작용(연타)할 때마다 온도를 높여주는 장치이며, 모든 ICookable 아이템을 직접 구울 수 있는 화구 역할을 합니다.
public class FireStation : NetworkBehaviour, IInteractable
{
    [Header("Temperature Settings")]
    [Networked] public float currentTemperature { get; set; } = 1.0f;
    private float _localTemperature = 1.0f; // 네트워크 버그 대용 (Sandbox)
    
    public float CurrentTemperature => (Object != null && Object.IsValid) ? currentTemperature : _localTemperature;
    public float maxTemperature = 5.0f;     // 최대 화력 (5.0배속)
    public float pumpPower = 0.5f;          // 상호작용 1회당 오르는 온도
    public float coolingRate = 0.2f;        // 1초당 식어버리는 온도

    [Header("Visual Feedback - Basic")]
    public Transform visualHandle;          // 상단 손잡이 모델
    public ParticleSystem fireParticles;    // 단일 불 파티클

    [Header("Visual Feedback - Full Opaque Fire")]
    [Tooltip("VFX_FullOpaqueFire 프리팹 내부의 VFX_FireController를 끌어서 넣으세요.")]
    public VFX_FireController vfxFireController;
    [Tooltip("기본 화력(1.0) 일 때 부드러운 불 색상 (예: 붉은색)")]
    public Color minFireColor = new Color(1f, 0.3f, 0f, 1f);
    [Tooltip("최대 화력(5.0) 일 때 뜨거운 불 색상 (예: 푸른 백열광)")]
    public Color maxFireColor = new Color(0.2f, 0.6f, 1f, 1f);
    [Tooltip("비활성/초기 불꽃 조명 강도")]
    public float minFireIntensity = 0.8f;
    [Tooltip("연타 시 최대 폭발 조명 강도")]
    public float maxFireIntensity = 4.0f;
    [Tooltip("불 파티클이 충돌(가로막힘)해야 할 레이어 (예: 냄비 레이어)")]
    public LayerMask fireCollisionLayer;

    [Header("Physical Feedback")]
    [Tooltip("상호작용 시 눌릴 상단 껍데기/손잡이 트랜스폼")]
    public Transform stationTopShell;       
    [Tooltip("클릭 1회당 아래로 눌리는 정도 (로컬 Y축)")]
    public float pressAmount = 0.15f;       
    [Tooltip("원래 자리로 돌아오는 스프링 탄성 속도")]
    public float animationSpeed = 15f;      
    
    // 물체의 원통형 복귀를 위한 위치값 저장
    private Vector3 originalShellPos;
    private Vector3 targetShellPos;

    // 이벤트: 온도가 변할 때 다른 스크립트(가령 냄비 또는 가마솥 리스너)에 알려줌
    public UnityEvent<float> OnTemperatureChanged;    // 화구 제어용 (용광로 기능)
    

    [Header("Item Placement Configuration")]
    [Tooltip("화구 안에서 아이템들이 놓일 위치들의 기준점 (예: 모델 내부 중앙 바닥)")]
    public Transform placementCenter;
    [Tooltip("화구 안에 들어갈 수 있는 최대 아이템 수 (너무 많으면 겹침)")]
    public int maxItems = 5;
    [Tooltip("아이템들이 배치될 때 사이 간격")]
    public float placementSpacing = 0.3f;
    
    // 현재 화구 안에 들어있는 모든 아이템 (배치 추적용)
    private List<PickableItem> allContainedItems = new List<PickableItem>();
    private Predicate<PickableItem> _isItemInvalid;

    private void Awake()
    {
        if (placementCenter == null)
            placementCenter = transform;

        if (stationTopShell != null)
        {
            originalShellPos = stationTopShell.localPosition;
            targetShellPos = originalShellPos;
        }

        _isItemInvalid = item =>
            item == null ||
            !item.IsOnStation ||
            (Object != null && Object.IsValid ? item.StationId != Object.Id : item.CurrentStationId != default(NetworkId));
    }

    public override void Spawned()
    {
        if (vfxFireController != null && fireCollisionLayer != 0)
        {
            vfxFireController.EnableCollision(fireCollisionLayer);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null || !Object.IsValid) return;

        // 권한자(Host)만 스테이트 관리
        if (HasStateAuthority)
        {
            // 1. 온도 감소
            if (currentTemperature > 1.0f)
            {
                currentTemperature -= coolingRate * Runner.DeltaTime;
                if (currentTemperature < 1.0f) currentTemperature = 1.0f;
            }

            // 2. 리스트 관리
            CleanupContainedItemsList();

            // 3. 조리 로직
            UpdateCookingLogic(Runner.DeltaTime);

            // 4. 아이템 위치 동기화 (Networked WorldPosition 업데이트)
            UpdateItemPositionsNetworked();
        }
    }

    public override void Render()
    {
        // 시각적 피드백
        UpdateVisuals();

        // 물리적 애니메이션 (탄성)
        if (stationTopShell != null)
        {
            targetShellPos = Vector3.Lerp(targetShellPos, originalShellPos, Time.deltaTime * animationSpeed * 0.5f);
            stationTopShell.localPosition = Vector3.Lerp(stationTopShell.localPosition, targetShellPos, Time.deltaTime * animationSpeed);
        }
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid)
        {
            // Sandbox 모드 업데이트
            CleanupContainedItemsList();
            UpdateCookingLogic(Time.deltaTime);
            UpdateItemPositionsNetworked();

            // 물리적 애니메이션 (탄성)
            if (stationTopShell != null)
            {
                targetShellPos = Vector3.Lerp(targetShellPos, originalShellPos, Time.deltaTime * animationSpeed * 0.5f);
                stationTopShell.localPosition = Vector3.Lerp(stationTopShell.localPosition, targetShellPos, Time.deltaTime * animationSpeed);
            }
        }
    }

    private void UpdateCookingLogic(float deltaTime)
    {
        // 조리 가능 아이템
        foreach (var item in allContainedItems)
        {
            if (item is ICookable cookable)
            {
                if (cookable.CurrentCookState == CookState.Burned) continue;
                cookable.CookInFire(deltaTime * CurrentTemperature);
            }
        }

    }

    private void UpdateItemPositionsNetworked()
    {
        for (int i = 0; i < allContainedItems.Count; i++)
        {
            PickableItem item = allContainedItems[i];
            if (item == null) continue;

            float timeValue = (Object != null && Object.IsValid) ? (float)Runner.SimulationTime : Time.time;
            float baseAngle = i * (360f / Mathf.Max(1, allContainedItems.Count));
            float currentAngle = baseAngle + (timeValue * 30f);
            
            Vector3 offset = new Vector3(Mathf.Sin(currentAngle * Mathf.Deg2Rad), 0, Mathf.Cos(currentAngle * Mathf.Deg2Rad)) * placementSpacing;
            float baseFloatY = 0.4f + (CurrentTemperature * 0.1f); // 화력이 높을수록 높이 뜸
            float floatY = baseFloatY + Mathf.Sin(timeValue * 2f + i) * 0.1f;
            
            Vector3 targetPos = placementCenter.position + offset + new Vector3(0, floatY, 0);
            item.SetStationPosition(targetPos);

            // 자전은 시각적이므로 PickableItem이 Render에서 처리하거나 여기서 Transform 직접 건드림
            // (권한자가 건드려도 SyncTransform이 켜져있어야 함. 여기서는 렌더링용으로 Render에서 하는게 좋음)
        }
    }

    private void UpdateVisuals()
    {
        float normalizedTemp = (CurrentTemperature - 1.0f) / (maxTemperature - 1.0f);
        normalizedTemp = Mathf.Clamp01(normalizedTemp);

        if (vfxFireController != null)
        {
            Color currentColor = Color.Lerp(minFireColor, maxFireColor, normalizedTemp);
            vfxFireController.SetFireColor(currentColor);
            float currentIntensity = Mathf.Lerp(minFireIntensity, maxFireIntensity, normalizedTemp);
            vfxFireController.SetFireIntensity(currentIntensity);
        }

        if (fireParticles != null)
        {
            var main = fireParticles.main;
            main.startSizeMultiplier = CurrentTemperature;
            var emission = fireParticles.emission;
            emission.rateOverTime = CurrentTemperature * 10f;
        }

        OnTemperatureChanged?.Invoke(CurrentTemperature);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_Interact()
    {
        if (Object != null && Object.IsValid)
        {
            currentTemperature += pumpPower;
            if (currentTemperature > maxTemperature) currentTemperature = maxTemperature;
            Rpc_OnPumpVisuals();
        }
        else
        {
            _localTemperature += pumpPower;
            if (_localTemperature > maxTemperature) _localTemperature = maxTemperature;
            LocalOnPumpVisuals();
        }
    }

    private void LocalOnPumpVisuals()
    {
        if (stationTopShell != null)
        {
            targetShellPos = originalShellPos - new Vector3(0, pressAmount, 0); 
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_OnPumpVisuals()
    {
        if (stationTopShell != null)
        {
            targetShellPos = originalShellPos - new Vector3(0, pressAmount, 0); 
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (Object != null && Object.IsValid && !HasStateAuthority) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        if (item == null || item.IsPhysicallyHeld || allContainedItems.Contains(item) || !item.CanBePickedUpByStation) return;
        if (item is not ICookable) return;
        if (allContainedItems.Count >= maxItems) return;

        Vector3 stationPos = transform.position + Vector3.up * 0.5f;
        if (Object != null && Object.IsValid)
            item.Rpc_PutOnStation(Object.Id, stationPos);
        else
            item.LocalPutOnStation(stationPos); // Sandbox fallback
            item.LocalStationRef = this;

        allContainedItems.Add(item);

    }

    public PickableItem TakeOutItem(PickableItem targetItem)
    {
        if (targetItem == null || !allContainedItems.Contains(targetItem)) return null;

        if (Object == null || !Object.IsValid || HasStateAuthority)
        {
            allContainedItems.Remove(targetItem);

            Vector3 dropPos = transform.position + transform.forward * 0.8f + Vector3.up * 0.3f;
            if (Object != null && Object.IsValid)
                targetItem.Rpc_Drop(dropPos);
            else
                targetItem.transform.position = dropPos; // Sandbox fallback
        }

        return targetItem;
    }

    #region IInteractable Implementation

    public bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        return type == InteractionType.UseItem;
    }

    public void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem)
        {
            if (Object != null && Object.IsValid)
                Rpc_Interact();
            else
            {
                _localTemperature += pumpPower;
                if (_localTemperature > maxTemperature) _localTemperature = maxTemperature;
                LocalOnPumpVisuals();
            }
        }
    }

    public string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem)
            return "[화구]\n[L-Click] 화력 높이기 (연타!)";
        return "";
    }

    #endregion

    private void CleanupContainedItemsList()
    {
        allContainedItems.RemoveAll(_isItemInvalid);
    }
}
