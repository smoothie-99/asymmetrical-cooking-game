using UnityEngine;
using Fusion;

/// <summary>
/// 슬라임 — 플레이어가 가까이 오면 점프하며 도망가는 적.
///
/// [네트워크 구조]
/// - StateAuthority: FixedUpdateNetwork()에서 물리/로직 처리 후 위치·회전 동기화
/// - 관찰자 클라이언트: Render()에서 NetworkedPosition으로 보간
/// - 로컬(Sandbox): Update()에서 기존 물리 로직 동작
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FleeingEnemy : NetworkBehaviour
{
    [Header("Flee Settings")]
    public float fleeDistance = 5f;
    public float jumpForce    = 4f;
    public float bounceHeight = 4f;
    public float jumpInterval = 0.8f;

    // ── 네트워크 동기화 상태 ──────────────────────────────────────
    [Networked] private Vector3    NetworkedPosition { get; set; }
    [Networked] private Quaternion NetworkedRotation { get; set; }
    [Networked] private double     NextJumpTime      { get; set; }

    // ── 로컬 ─────────────────────────────────────────────────────
    private Rigidbody _rb;
    private Vector3   _renderPosition;
    private float     _localNextJumpTime;

    private bool IsNetworkReady => Object != null && Object.IsValid;

    // ── Unity 생명주기 ────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    // ── Fusion 생명주기 ───────────────────────────────────────────

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            NetworkedPosition = transform.position;
            NetworkedRotation = transform.rotation;
            NextJumpTime      = Runner.SimulationTime;
        }
        else
        {
            // 관찰자: 물리 비활성화 (Render에서 위치 직접 지정)
            _rb.isKinematic = true;
        }

        _renderPosition = transform.position;
    }

    /// <summary>
    /// StateAuthority 전용: 도망 로직 + 위치 동기화
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        Transform nearest = FindNearestPlayer();
        if (nearest != null && Runner.SimulationTime >= NextJumpTime)
        {
            float dist = Vector3.Distance(transform.position, nearest.position);
            if (dist < fleeDistance)
            {
                Vector3 dirAway = (transform.position - nearest.position).normalized;
                dirAway.y = 0f;

                _rb.linearVelocity = Vector3.zero;
                _rb.AddForce(dirAway * jumpForce + Vector3.up * bounceHeight, ForceMode.Impulse);

                if (dirAway != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(dirAway);

                NextJumpTime = Runner.SimulationTime + jumpInterval;
            }
        }

        // 매 틱 위치/회전 동기화
        NetworkedPosition = transform.position;
        NetworkedRotation = transform.rotation;
    }

    /// <summary>
    /// 모든 클라이언트: 네트워크 위치로 보간
    /// </summary>
    public override void Render()
    {
        base.Render();
        if (!IsNetworkReady) return;

        _renderPosition     = Vector3.Lerp(_renderPosition, NetworkedPosition, Time.deltaTime * 15f);
        transform.position  = _renderPosition;
        transform.rotation  = NetworkedRotation;
    }

    // ── 로컬(Sandbox) 폴백 ───────────────────────────────────────

    private void Update()
    {
        if (IsNetworkReady) return;

        Transform nearest = FindNearestPlayer();
        if (nearest == null) return;

        if (Time.time >= _localNextJumpTime)
        {
            float dist = Vector3.Distance(transform.position, nearest.position);
            if (dist < fleeDistance)
            {
                Vector3 dirAway = (transform.position - nearest.position).normalized;
                dirAway.y = 0f;

                _rb.linearVelocity = Vector3.zero;
                _rb.AddForce(dirAway * jumpForce + Vector3.up * bounceHeight, ForceMode.Impulse);

                if (dirAway != Vector3.zero)
                    transform.rotation = Quaternion.LookRotation(dirAway);

                _localNextJumpTime = Time.time + jumpInterval;
            }
        }
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────

    /// <summary>
    /// 씬 내 가장 가까운 플레이어 Transform을 반환합니다.
    /// </summary>
    private Transform FindNearestPlayer()
    {
        CookingMasterMovement[] players =
            FindObjectsByType<CookingMasterMovement>(FindObjectsSortMode.None);

        Transform nearest  = null;
        float     minDist  = float.MaxValue;

        foreach (CookingMasterMovement p in players)
        {
            if (p == null) continue;
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < minDist)
            {
                minDist = d;
                nearest = p.transform;
            }
        }

        return nearest;
    }
}
