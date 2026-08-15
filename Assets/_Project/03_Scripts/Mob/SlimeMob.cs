using UnityEngine;
using Fusion;

/// <summary>
/// 수면 보석 슬라임 몬스터.
/// 이동 AI로 배회하며 트리거 내 아이템을 포획합니다.
/// - IWashable 아이템: Wash() 적용. CleanRatio >= 100 이면 방출.
///   (ClamClosedItem 도 IWashable 구현 → 세척 완료 시 오염 자동 해제)
/// - HP 0 → 포획 아이템 + 드롭 아이템 방출 후 Despawn
///
/// [칼 공격 연동 미구현]
/// KnifeItem 등에서 TakeDamage(int) 호출 예정.
/// </summary>
public class SlimeMob : NetworkBehaviour
{
    // ── HP ────────────────────────────────────────────────────────

    [Header("HP")]
    public int maxHp = 3;

    [Networked, OnChangedRender(nameof(OnHpChanged))]
    public int Hp { get; set; }

    [Header("Drop on Death")]
    public NetworkObject[] dropPrefabs;
    public float dropScatterRadius = 0.5f;

    // ── 세척/포획 ─────────────────────────────────────────────────

    [Header("Slime Properties")]
    [Tooltip("IWashable 아이템 초당 세척량 (CleanRatio 0→100 기준, 10 = 10초 소요)")]
    public float washSpeedPerSecond = 10f;
    [Tooltip("이 시간(초) 초과 시 미세척 아이템 녹임")]
    public float meltThresholdTime = 15f;

    [Networked] private float TrappedTime { get; set; }
    private float _localTrappedTime = 0f;
    private PickableItem trappedItem = null;

    // ── 이동 AI ───────────────────────────────────────────────────

    [Header("Movement AI")]
    public float moveSpeed = 1.5f;
    public float changeDirectionTime = 3f;

    [Networked] public Vector3 MoveDirection { get; set; }

    // ── 위치/회전 동기화 (BoppyMushroom 패턴) ─────────────────────
    // StateAuthority가 매 틱 실제 위치를 기록 → 비권한 클라이언트가 Render()에서 Lerp
    [Networked] private Vector3 SyncedPosition { get; set; }
    [Networked] private Quaternion SyncedRotation { get; set; }

    private float _directionTimer = 0f;
    private Rigidbody _rb;

    // ── 편의 프로퍼티 ─────────────────────────────────────────────

    private bool IsNetworkActive => Object != null && Object.IsValid;
    private float CurrentTrappedTime => IsNetworkActive ? TrappedTime : _localTrappedTime;

    // ── Fusion 생명주기 ────────────────────────────────────────────

    public override void Spawned()
    {
        _rb = GetComponent<Rigidbody>();
        if (HasStateAuthority)
        {
            Hp = maxHp;
            PickNewDirection();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        UpdateMovementAI(Runner.DeltaTime);
        UpdateTrappedItemLogic(Runner.DeltaTime);

        // 실제 물리 위치/회전을 매 틱 동기화
        SyncedPosition = transform.position;
        SyncedRotation = transform.rotation;
    }

    // 비권한 클라이언트: kinematic 전환 + 위치/회전 부드럽게 보간 (버섯과 동일 패턴)
    public override void Render()
    {
        if (HasStateAuthority) return;

        if (_rb != null && !_rb.isKinematic)
            _rb.isKinematic = true;

        transform.position = Vector3.Lerp(transform.position, SyncedPosition, Time.deltaTime * 15f);
        transform.rotation = Quaternion.Slerp(transform.rotation, SyncedRotation, Time.deltaTime * 15f);
    }

    private void Update()
    {
        // 샌드박스 전용
        if (IsNetworkActive) return;
        UpdateMovementAI(Time.deltaTime);
        UpdateTrappedItemLogic(Time.deltaTime);
    }

    // ── 이동 AI ───────────────────────────────────────────────────

    private void UpdateMovementAI(float dt)
    {
        _directionTimer -= dt;
        if (_directionTimer <= 0f) PickNewDirection();

        if (_rb != null)
        {
            Vector3 vel = MoveDirection * moveSpeed;
            vel.y = _rb.linearVelocity.y;
            _rb.linearVelocity = vel;

            if (MoveDirection != Vector3.zero)
            {
                Quaternion target = Quaternion.LookRotation(MoveDirection);
                _rb.MoveRotation(Quaternion.Slerp(transform.rotation, target, dt * 5f));
            }
        }
    }

    private void PickNewDirection()
    {
        _directionTimer = changeDirectionTime + Random.Range(-1f, 1f);
        if (Random.value < 0.3f)
            MoveDirection = Vector3.zero;
        else
        {
            float a = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            MoveDirection = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)).normalized;
        }
    }

    // ── 포획 / 세척 ───────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (IsNetworkActive && !HasStateAuthority) return;
        if (trappedItem != null) return;

        PickableItem item = other.GetComponentInParent<PickableItem>();
        if (item == null || item.IsPhysicallyHeld) return;
        if (item is ITool) return;
        if (item is not IWashable) return;
        if (!item.CanBePickedUpByStation) return;

        TrapItem(item);
    }

    private void TrapItem(PickableItem item)
    {
        trappedItem = item;
        if (IsNetworkActive) TrappedTime = 0f;
        else _localTrappedTime = 0f;

        Vector3 pos = transform.position + Vector3.up * 0.2f;
        if (IsNetworkActive) item.Rpc_PutOnStation(Object.Id, pos);
        else { item.LocalPutOnStation(pos); item.LocalStationRef = null; }
    }

    private void UpdateTrappedItemLogic(float dt)
    {
        if (trappedItem == null) return;

        // 이미 다른 스테이션으로 간 경우 해제
        if (IsNetworkActive)
        {
            bool stillHere = trappedItem.IsOnStation && trappedItem.StationId == Object.Id;
            if (!stillHere) { trappedItem = null; TrappedTime = 0f; return; }
            TrappedTime += dt;
        }
        else _localTrappedTime += dt;

        // 슬라임 이동에 맞춰 포획 아이템 위치 갱신
        trappedItem.SetStationPosition(transform.position + Vector3.up * 0.2f);

        // IWashable 세척 (ClamClosedItem 포함 — 완료 시 자동 방출)
        if (trappedItem is IWashable washable)
        {
            washable.Wash(washSpeedPerSecond * dt);
            if (washable.CleanRatio >= 100f)
            {
                EjectItem(trappedItem);
                return;
            }
        }

        // 용해 임계치 (세척 완료 전)
        if (CurrentTrappedTime > meltThresholdTime)
            HandleMelting();
    }

    private void HandleMelting()
    {
        if (trappedItem is ICookable cookable && cookable.CurrentCookState != CookState.Burned)
        {
            cookable.CurrentCookState = CookState.Burned;
            trappedItem.itemName = "산성 찌꺼기";
            Debug.Log($"[SlimeMob] {trappedItem.itemName}이 녹아버렸습니다!");
        }
    }

    private void EjectItem(PickableItem item)
    {
        Vector3 pos = transform.position + transform.up * 0.8f + transform.forward * 0.5f;
        if (IsNetworkActive) item.Rpc_Drop(pos);
        else item.transform.position = pos;

        Rigidbody rb = item.GetComponent<Rigidbody>();
        rb?.AddForce(transform.forward * 2f + Vector3.up * 3f, ForceMode.Impulse);

        trappedItem = null;
        if (IsNetworkActive) TrappedTime = 0f;
        else _localTrappedTime = 0f;
    }

    // ── 피격 (칼 연동 구멍) ───────────────────────────────────────

    /// <summary>
    /// 칼로 공격받을 때 호출. KnifeItem 등 공격 로직에서 연동 예정.
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (!IsNetworkActive || HasStateAuthority)
        {
            ApplyDamage(amount);
            return;
        }
        Rpc_RequestDamage(amount);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestDamage(int amount) => ApplyDamage(amount);

    private void ApplyDamage(int amount)
    {
        Hp = Mathf.Max(0, Hp - amount);
        if (Hp <= 0) OnDeath();
    }

    private void OnDeath()
    {
        // 포획 중인 아이템 방출
        if (trappedItem != null) EjectItem(trappedItem);

        // 드롭 아이템 스폰
        if (Runner != null && dropPrefabs != null)
        {
            foreach (var prefab in dropPrefabs)
            {
                if (prefab == null) continue;
                Vector3 pos = transform.position + Random.insideUnitSphere * dropScatterRadius + Vector3.up * 0.3f;
                Runner.Spawn(prefab, pos, Quaternion.identity);
            }
        }

        Runner.Despawn(Object);
    }

    private void OnHpChanged()
    {
        // TODO: HP 변화 시각 피드백 (피격 이펙트 등)
    }

    // ── HitSlime (기존 호환) ───────────────────────────────────────

    /// <summary>슬라임을 때려 아이템을 뱉게 합니다. (칼 미구현 전 임시 테스트용)</summary>
    public void HitSlime()
    {
        if (IsNetworkActive && !HasStateAuthority) { Rpc_RequestHit(); return; }
        if (trappedItem != null) EjectItem(trappedItem);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestHit() => HitSlime();
}
