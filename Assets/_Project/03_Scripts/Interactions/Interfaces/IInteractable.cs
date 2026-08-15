using UnityEngine;

namespace Interactions
{
    /// <summary>
    /// 인터페이스를 통한 상호작용 통합 관리.
    /// 구체적인 상호작용 로직(들고 있는 템 체크, 가이드 문구 등)을 스테이션이 직접 관리하도록 합니다.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>
        /// 해당 타입의 상호작용이 현재 가능한지 확인합니다.
        /// </summary>
        bool CanInteract(CookingMasterHandsManager player, InteractionType type);

        /// <summary>
        /// 실제 상호작용을 수행합니다.
        /// </summary>
        void Interact(CookingMasterHandsManager player, InteractionType type);

        /// <summary>
        /// 화면 UI에 표시할 상호작용 가이드 문구를 반환합니다.
        /// </summary>
        string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type);

        /// <summary>
        /// 플레이어가 바라보는 동안 매 프레임 호출됩니다.
        /// </summary>
        void OnFocus(CookingMasterHandsManager player) { }

        /// <summary>
        /// 플레이어 시선이 벗어날 때 호출됩니다.
        /// </summary>
        void OnFocusLost() { }
    }
}
