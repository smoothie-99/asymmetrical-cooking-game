using UnityEngine;
using Fusion;

/// <summary>
/// 냉기 주머니.
/// 기본 동작:
/// - 손에 들고 있고
/// - 그 주머니가 holderRoot(보통 플레이어 루트) 아래에 매달려 있으면
///   용암 치즈를 안전하게 다룰 수 있다고 판정.
/// </summary>
public class ColdPouchItem : PickableItem, ITool
{
    public string ToolType => "ColdPouch";

    [Header("Cold Protection")]
    [SerializeField] private bool protectionActiveWhileHeld = true;

    void Start()
    {
        if (string.IsNullOrEmpty(itemName) || itemName == "Object")
        {
            itemName = "냉기 주머니";
        }
    }

    /// <summary>
    /// 현재 냉기 보호 기능이 켜져 있는지.
    /// PickableItem의 isHeld를 사용.
    /// </summary>
    public bool IsProvidingProtection => protectionActiveWhileHeld && isHeld;

    /// <summary>
    /// 이 주머니가 특정 holderRoot를 보호 중인지 판정.
    /// 
    /// 전제:
    /// - 아이템을 손에 들면 해당 손이 플레이어 루트의 자식이 된다.
    /// - 그래서 transform.IsChildOf(holderRoot.transform)로 판정 가능.
    /// - (추가) 멀티플레이 환경의 네트워크 지연을 대비하여 HolderId 비교 로직을 최우선으로 검사합니다.
    /// </summary>
    public bool IsProvidingProtectionFor(GameObject holderRoot)
    {
        if (!IsProvidingProtection || holderRoot == null)
            return false;

        // 멀티플레이어 동기화 환경을 위한 안전한 Holder 판정 로직
        if (IsNetworkReady && Object != null && Object.IsValid)
        {
            NetworkObject rootNetObj = holderRoot.GetComponent<NetworkObject>();
            if (rootNetObj != null)
            {
                return rootNetObj.Id == this.HolderId;
            }
        }

        // 기존 싱글/로컬 환경 대응 (물리적 계층 검사) 로직
        return transform.IsChildOf(holderRoot.transform);
    }
}