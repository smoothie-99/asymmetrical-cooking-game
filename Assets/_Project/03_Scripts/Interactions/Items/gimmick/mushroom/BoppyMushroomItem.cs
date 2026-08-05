using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 통통 버섯 본체.
/// - 플레이어를 감지하면 도망감
/// - 벽/낭떠러지 회피
/// - 도마 위에 올리면 정지
/// - 칼 가이드라인에 따라 바로 슬라이스 통통버섯으로 분리됨
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BoppyMushroomItem : PickableItem, ICuttable
{
    [Header("Mushroom Settings")]
    [SerializeField] private float moveSpeed = 4.0f;
    [SerializeField] private float detectionRadius = 5.0f;
    [SerializeField] private float turnAcceleration = 10f;
    [SerializeField] private float wallCheckDistance = 1.5f;
    [SerializeField] private float edgeCheckDistance = 1.2f;
    [SerializeField] private float groundCheckDistance = 0.3f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Hop Settings")]
    [SerializeField] private float hopInterval = 0.55f;
    [SerializeField] private float hopForce = 5.0f;

    [Header("Wander Settings")]
    [SerializeField] private float wanderChangeInterval = 2.0f;
    [SerializeField] private float wanderAngleRange = 70f;

    [Header("Cutting")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new CutGuidelineConfig() };
    private CutGuidelineVisual[] _guidelineVisuals;

    [Header("Optional Visuals")]
    [SerializeField] private GameObject wholeModel;

    private Rigidbody _rb;
    private Vector3 _moveDirection = Vector3.forward;

    private float _hopTimer;
    private float _wanderTimer;
    private Vector3 _wallAvoidance = Vector3.zero;
    private float _wallAvoidTimer = 0f;

    // 샌드박스용 플레이어 등록 리스트
    private static readonly HashSet<Transform> _localPlayers = new HashSet<Transform>();

    private static readonly float[] _avoidAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

    private void Start()
    {
        itemName = "통통 버섯";
        metadata["ingredientID"] = "통통 버섯";

        _rb = GetComponent<Rigidbody>();

        if (obstacleLayer == 0)
            obstacleLayer = LayerMask.GetMask("Default", "Interactable");

        Vector3 forward = transform.forward;
        forward.y = 0f;
        _moveDirection = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

        _hopTimer = Random.Range(0f, hopInterval);
        _wanderTimer = Random.Range(0f, wanderChangeInterval);

        CreateGuidelineVisuals();
        UpdateVisuals();
    }

    public override void Spawned()
    {
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();

        Vector3 forward = transform.forward;
        forward.y = 0f;
        _moveDirection = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;

        _hopTimer = Random.Range(0f, hopInterval);
        _wanderTimer = Random.Range(0f, wanderChangeInterval);

        CreateGuidelineVisuals();
        UpdateVisuals();
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsNetworkReady)
            return;

        // StateAuthority만 실제 이동을 계산
        if (HasStateAuthority)
        {
            float deltaTime = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;
            ProcessMovementStep(deltaTime);
        }

        // 중요:
        // PickableItem의 기본 위치/상태 네트워크 동기화를 반드시 태워야 함
        base.FixedUpdateNetwork();
    }

    private void FixedUpdate()
    {
        if (IsNetworkReady)
            return;

        ProcessMovementStep(Time.fixedDeltaTime);
    }

    private void ProcessMovementStep(float deltaTime)
    {
        if (_rb == null)
            return;

        // 손에 들려 있거나 스테이션 위면 이동하지 않음
        if (isHeld || IsOnStation)
        {
            ZeroRigidMotion();
            return;
        }

        ApplyMovingPhysics();
        HandleMovement(deltaTime);
    }

    private void HandleMovement(float deltaTime)
    {
        if (_wallAvoidTimer > 0f)
            _wallAvoidTimer -= deltaTime;

        _wanderTimer -= deltaTime;

        Vector3 fleeVector = GetFleeVector(out bool hasPlayer);

        // 벽에 부딪힌 직후에는 벽 법선 방향으로 강제 회피
        if (_wallAvoidTimer > 0f && _wallAvoidance.sqrMagnitude > 0.001f)
            fleeVector = _wallAvoidance;

        // 플레이어 없을 때 주기적으로 랜덤 방향 전환 (방랑)
        if (!hasPlayer && _wanderTimer <= 0f)
        {
            float randomAngle = Random.Range(-wanderAngleRange, wanderAngleRange);
            _moveDirection = (Quaternion.Euler(0f, randomAngle, 0f) * _moveDirection).normalized;
            _wanderTimer = wanderChangeInterval;
        }

        Vector3 adjustedVector = AdjustVectorForObstacles(fleeVector);
        if (adjustedVector.sqrMagnitude <= 0.001f)
            adjustedVector = _moveDirection.sqrMagnitude > 0.001f ? _moveDirection : Vector3.forward;

        float turnT = 1f - Mathf.Exp(-turnAcceleration * deltaTime);
        _moveDirection = Vector3.Slerp(_moveDirection, adjustedVector.normalized, turnT).normalized;

        Vector3 velocity = _moveDirection * moveSpeed;
        velocity.y = _rb.linearVelocity.y;
        _rb.linearVelocity = velocity;

        HandleHop(deltaTime);

        if (_moveDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(_moveDirection, Vector3.up);
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRotation, turnT));
        }
    }

    private void HandleHop(float deltaTime)
    {
        _hopTimer -= deltaTime;
        if (_hopTimer > 0f)
            return;

        // 지면에 닿아 있을 때만 점프
        bool grounded = Physics.Raycast(
            transform.position + Vector3.up * 0.15f,
            Vector3.down,
            groundCheckDistance + 0.15f,
            obstacleLayer,
            QueryTriggerInteraction.Ignore);

        if (!grounded)
            return;

        _rb.AddForce(Vector3.up * hopForce, ForceMode.Impulse);
        _hopTimer = hopInterval;
    }

    // 벽 충돌 감지 — 레이캐스트로 못 잡는 케이스를 물리 콜백으로 보완
    private void OnCollisionEnter(Collision collision) => HandleWallContact(collision);
    private void OnCollisionStay(Collision collision) => HandleWallContact(collision);

    private void HandleWallContact(Collision collision)
    {
        if (isHeld || IsOnStation)
            return;

        Vector3 normal = Vector3.zero;
        int count = 0;

        foreach (ContactPoint contact in collision.contacts)
        {
            // y 성분이 작은 법선 = 수직 벽
            if (Mathf.Abs(contact.normal.y) < 0.7f)
            {
                normal += contact.normal;
                count++;
            }
        }

        if (count == 0)
            return;

        normal.y = 0f;
        if (normal.sqrMagnitude > 0.001f)
        {
            _wallAvoidance = normal.normalized;
            _wallAvoidTimer = 0.5f;
        }
    }

    private Vector3 GetFleeVector(out bool hasPlayer)
    {
        Vector3 totalFleeVector = Vector3.zero;
        int detectedCount = 0;
        float detectionRadiusSqr = detectionRadius * detectionRadius;

        foreach (Transform playerTransform in GetPlayerTargets())
        {
            if (playerTransform == null)
                continue;

            Vector3 diff = transform.position - playerTransform.position;
            diff.y = 0f;

            float sqrDist = diff.sqrMagnitude;
            if (sqrDist <= 0.0001f || sqrDist > detectionRadiusSqr)
                continue;

            float weight = detectionRadius / (Mathf.Sqrt(sqrDist) + 0.1f);
            totalFleeVector += diff.normalized * weight;
            detectedCount++;
        }

        if (detectedCount > 0 && totalFleeVector.sqrMagnitude > 0.001f)
        {
            hasPlayer = true;
            return totalFleeVector.normalized;
        }

        hasPlayer = false;
        return _moveDirection.sqrMagnitude > 0.001f ? _moveDirection : Vector3.forward;
    }

    private IEnumerable<Transform> GetPlayerTargets()
    {
        if (IsNetworkReady && Runner != null)
        {
            foreach (PlayerRef playerRef in Runner.ActivePlayers)
            {
                if (Runner.TryGetPlayerObject(playerRef, out NetworkObject playerObject) && playerObject != null)
                {
                    CookingMasterHandsManager hands = playerObject.GetComponentInChildren<CookingMasterHandsManager>(true);
                    if (hands != null)
                        yield return hands.transform;
                }
            }

            yield break;
        }

        foreach (Transform t in _localPlayers)
        {
            if (t != null)
                yield return t;
        }
    }

    private Vector3 AdjustVectorForObstacles(Vector3 desiredVector)
    {
        if (desiredVector.sqrMagnitude <= 0.001f)
            return _moveDirection.sqrMagnitude > 0.001f ? _moveDirection : Vector3.forward;

        Vector3 baseDir = desiredVector.normalized;
        Vector3 rayOrigin = transform.position + Vector3.up * 0.35f;

        foreach (float angle in _avoidAngles)
        {
            Vector3 candidate = Quaternion.Euler(0f, angle, 0f) * baseDir;

            // 벽 감지
            if (Physics.Raycast(rayOrigin, candidate, wallCheckDistance, obstacleLayer, QueryTriggerInteraction.Ignore))
                continue;

            // 낭떠러지 감지
            Vector3 edgeOrigin = transform.position + candidate * edgeCheckDistance + Vector3.up * 0.35f;
            bool hasGround = Physics.Raycast(
                edgeOrigin,
                Vector3.down,
                groundCheckDistance + 0.5f,
                obstacleLayer,
                QueryTriggerInteraction.Ignore);

            if (!hasGround)
                continue;

            return candidate;
        }

        return _moveDirection.sqrMagnitude > 0.001f ? _moveDirection : baseDir;
    }

    private void ApplyMovingPhysics()
    {
        if (_rb == null)
            return;

        if (CurrentState == ItemState.Free)
        {
            if (_rb.isKinematic)
                _rb.isKinematic = false;

            _rb.useGravity = true;
        }
    }

    private void ZeroRigidMotion()
    {
        if (_rb == null)
            return;

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    // ─────────────────────────────────────────
    // ICuttable
    // ─────────────────────────────────────────

    public override bool CanChop => !isHeld && HasValidCutResults();

    private bool HasValidCutResults()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0)
            return false;

        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            if (_guidelineConfigs[i].resultPrefabs != null &&
                _guidelineConfigs[i].resultPrefabs.Length > 0)
                return true;
        }

        return false;
    }

    private void CreateGuidelineVisuals()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0)
            return;

        if (_guidelineVisuals != null && _guidelineVisuals.Length == _guidelineConfigs.Length)
            return;

        _guidelineVisuals = new CutGuidelineVisual[_guidelineConfigs.Length];

        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _guidelineConfigs[i].localPosition;
            go.transform.localEulerAngles = _guidelineConfigs[i].localEulerAngles;

            CutGuidelineVisual vis = go.AddComponent<CutGuidelineVisual>();
            vis.toolType = "Knife";
            vis.cutName = _guidelineConfigs[i].cutName;
            vis.resultPrefabs = _guidelineConfigs[i].resultPrefabs;
            vis.resultCounts = _guidelineConfigs[i].resultCounts;

            _guidelineVisuals[i] = vis;
        }
    }

    public override void Chop(CuttingStation board)
    {
        if (!CanChop)
            return;

        const string toolType = "Knife";

        if (IsNetworkReady)
            Rpc_Cut(0, toolType);
        else
            LocalCut(0, toolType);
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType)
    {
        if (!CanChop)
            return System.Array.Empty<CutGuidelineVisual>();

        return _guidelineVisuals != null
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();
    }

    public override void TransferStateTo(PickableItem target)
    {
        // base Rpc_Cut / LocalCut가 metadata를 원본에서 복사하므로,
        // 결과물의 ingredientID가 슬라이스 버섯으로 남도록 원본 metadata를 바꿔 준다.
        if (target is SlicedBoppyMushroomItem sliced)
        {
            metadata["ingredientID"] = "통통 버섯";
            sliced.metadata["ingredientID"] = "통통 버섯";
        }
    }

    private void UpdateVisuals()
    {
        if (wholeModel != null)
            wholeModel.SetActive(true);
    }

    // ─────────────────────────────────────────
    // 샌드박스용 플레이어 등록
    // ─────────────────────────────────────────

    public static void RegisterLocalPlayer(Transform player)
    {
        if (player != null)
            _localPlayers.Add(player);
    }

    public static void UnregisterLocalPlayer(Transform player)
    {
        if (player != null)
            _localPlayers.Remove(player);
    }
}