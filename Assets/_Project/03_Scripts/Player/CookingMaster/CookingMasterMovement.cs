using UnityEngine;
using Fusion;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(AudioSource))]
public class CookingMasterMovement : NetworkBehaviour
{
    public float moveSpeed = 1.0f;
    public float gravity = -9.81f;
    public AudioClip[] footstepClips;
    public Transform headBone;
    public float danceDuration = 10f;

    private CharacterController controller;
    private Animator animator;
    private AudioSource audioSource;
    private Vector3 velocity;
    private int clipIndex = 0;

    // 비틀비틀 양배추 디버프용 (로컬 전용)
    private float _directionInvertRemaining = 0f;
    private bool _invertForwardInput = false;
    private bool _invertStrafeInput = false;

    // Update()에서 계산한 값을 FixedUpdateNetwork()로 전달
    private bool _isWalking;

    [Networked] public Vector3 NetworkedPosition { get; set; }
    [Networked] public float NetworkedYRotation { get; set; }
    [Networked] public float NetworkedXRotation { get; set; }
    [Networked] public NetworkBool NetworkedIsWalking { get; set; }
    [Networked] public NetworkBool NetworkedIsSleeping { get; set; }
    [Networked] public NetworkBool NetworkedIsDancing { get; set; }
    [Networked] public float StunTimer { get; set; }
    [Networked] public float DanceTimer { get; set; }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();
    }

    public override void Spawned()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponent<Animator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        if (!HasStateAuthority) return;

        NetworkedPosition = transform.position;
        NetworkedYRotation = transform.eulerAngles.y;
    }

    public override void Despawned(NetworkRunner runner, bool hasState) { }

    public void ApplySleepEffect(float duration)
    {
        StunTimer = Mathf.Max(StunTimer, duration);
    }

    public void FailClamPrep()
    {
        ApplySleepEffect(10f);
    }

    public void ApplyDirectionInvertEffect(float duration, bool invertForward, bool invertStrafe)
    {
        if (duration <= 0f) return;

        _directionInvertRemaining = Mathf.Max(_directionInvertRemaining, duration);
        _invertForwardInput = invertForward;
        _invertStrafeInput = invertStrafe;

        // 최소 한 축은 반전되도록 보정
        if (!_invertForwardInput && !_invertStrafeInput)
            _invertForwardInput = true;
    }

    private void TickDirectionInvert(float dt)
    {
        if (_directionInvertRemaining <= 0f) return;

        _directionInvertRemaining = Mathf.Max(0f, _directionInvertRemaining - dt);

        if (_directionInvertRemaining <= 0f)
        {
            _invertForwardInput = false;
            _invertStrafeInput = false;
        }
    }

    private Vector2 ApplyDirectionInvertToInput(Vector2 input)
    {
        if (_directionInvertRemaining <= 0f)
            return input;

        if (_invertStrafeInput)
            input.x = -input.x;

        if (_invertForwardInput)
            input.y = -input.y;

        return input;
    }

    // ── 네트워크 틱: 상태 동기화 전용 ────────────────────────────────────────
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        float dt = Runner.DeltaTime;

        // StunTimer 감소 (네트워크 정확도를 위해 틱에서 처리)
        if (StunTimer > 0)
        {
            StunTimer = Mathf.Max(0, StunTimer - dt);
            NetworkedIsSleeping = true;
            return;
        }
        NetworkedIsSleeping = false;

        // Update()에서 이동한 결과를 네트워크에 기록
        NetworkedIsWalking = _isWalking;
        NetworkedPosition = transform.position;
        NetworkedYRotation = transform.eulerAngles.y;
    }

    // ── 로컬 이동 (매 프레임, 부드러운 입력 처리) ────────────────────────────
    private void Update()
    {
        if (controller == null) return;

        bool isNetworkMode = Object != null && Object.IsValid;

        // 타인 클라이언트는 이동 처리 안 함
        if (isNetworkMode && !HasStateAuthority) return;

        float dt = Time.deltaTime;
        TickDirectionInvert(dt);

        // 기절 중 이동 불가
        if (StunTimer > 0)
        {
            // Sandbox에서는 FixedUpdateNetwork가 없으므로 여기서 감소
            if (!isNetworkMode)
                StunTimer = Mathf.Max(0, StunTimer - dt);

            _isWalking = false;
            return;
        }

        // 이동 불가 조건 체크
        if (SystemManager.Instance != null)
        {
            if (InGameMenuUI.IsMenuOpen || SystemManager.Instance.CurrentMetaState != MetaState.Cooking)
            {
                _isWalking = false;
                return;
            }
        }

        if (GamePlayManager.Instance != null)
        {
            if (GamePlayManager.Instance.CurrentCookingState != CookingState.Cooking)
            {
                _isWalking = false;
                return;
            }
        }

        // 이동 입력
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector2 input = new Vector2(h, v).normalized;
        input = ApplyDirectionInvertToInput(input);
        _isWalking = input.magnitude >= 0.1f;

        if (_isWalking)
        {
            Vector3 move = transform.right * input.x + transform.forward * input.y;
            controller.Move(move * moveSpeed * dt);
        }

        // 중력
        if (controller.isGrounded && velocity.y < 0)
            velocity.y = -2f;

        velocity.y += gravity * dt;
        controller.Move(velocity * dt);
    }

    // ── 렌더 (비주얼 전용) ────────────────────────────────────────────────────
    public override void Render()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) return;

        animator.SetBool("IsWalking", (bool)NetworkedIsWalking);
        animator.SetBool("IsSleeping", (bool)NetworkedIsSleeping);

        // 타인 클라이언트: 위치/회전 보간
        if (!HasStateAuthority)
        {
            transform.position = NetworkedPosition;
            Vector3 euler = transform.eulerAngles;
            euler.y = NetworkedYRotation;
            transform.eulerAngles = euler;
        }
    }

    private void LateUpdate()
    {
        if (headBone == null) return;

        bool isNativeNetwork = (Object != null);
        bool isSleeping = isNativeNetwork ? (bool)NetworkedIsSleeping : false;
        bool isDancing = isNativeNetwork ? (bool)NetworkedIsDancing : false;
        float xRot = isNativeNetwork ? NetworkedXRotation : 0f;

        if (!isSleeping && !isDancing && StunTimer <= 0)
            headBone.Rotate(xRot, 0, 0, Space.Self);
    }

    public void PlayFootstep()
    {
        if (!HasStateAuthority) return;
        if (footstepClips == null || footstepClips.Length == 0) return;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) return;

        audioSource.pitch = Random.Range(0.9f, 1.1f);
        audioSource.PlayOneShot(footstepClips[clipIndex]);
        clipIndex = (clipIndex + 1) % footstepClips.Length;
    }
}