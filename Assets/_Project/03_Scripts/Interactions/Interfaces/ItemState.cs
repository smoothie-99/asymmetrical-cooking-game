namespace Interactions
{
    /// <summary>
    /// 아이템의 현재 보유 상태.
    /// Fusion [Networked] 프로퍼티로 동기화됩니다.
    /// </summary>
    public enum ItemState
    {
        Free,       // 바닥에 놓여있음 (Rigidbody 물리 적용)
        Held,       // 플레이어 손에 들려있음
        OnStation,  // 스테이션(화구/도마/가마솥/슬라임) 위에 있음
    }
}
