using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 로컬 플레이어 전용 청력 마비 매니저.
/// 
/// 역할:
/// - 일정 시간 동안 게임 전체 재생 음량을 차단
/// - 필요하면 특정 AudioSource들도 강제로 mute
/// - Photon Voice 송신은 건드리지 않음 (입력 마이크는 유지)
///
/// 배치 권장:
/// - CookingMasterHandsManager와 같은 오브젝트 또는 그 부모
/// - AudioListener는 자식 카메라에 있어도 됨
/// </summary>
public class DeafnessAudioManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CookingMasterHandsManager handsManager;
    [SerializeField] private AudioListener targetListener;

    [Header("Deafness Output")]
    [SerializeField, Range(0f, 1f)] private float deafenedListenerVolume = 0f;
    [SerializeField] private bool restoreVolumeOnDisable = true;

    [Header("Optional Forced Mute Sources")]
    [SerializeField] private List<AudioSource> extraSourcesToMute = new List<AudioSource>();

    private readonly HashSet<AudioSource> _registeredMuteSources = new HashSet<AudioSource>();

    private float _remainingTime = 0f;
    private float _normalListenerVolume = 1f;
    private bool _bootstrapped = false;
    private bool _muteApplied = false;

    public bool IsDeafened => _remainingTime > 0f;
    public float RemainingTime => Mathf.Max(0f, _remainingTime);

    private void Awake()
    {
        Bootstrap();
    }

    private void OnEnable()
    {
        Bootstrap();
        ApplyCurrentOutputState();
    }

    private void Update()
    {
        if (!IsLocalOwner())
            return;

        if (!IsDeafened)
        {
            // 평상시 외부 옵션/슬라이더가 볼륨을 바꾸면 기준값 갱신
            if (!_muteApplied)
                _normalListenerVolume = AudioListener.volume;

            return;
        }

        _remainingTime = Mathf.Max(0f, _remainingTime - Time.deltaTime);
        ApplyCurrentOutputState();
    }

    /// <summary>
    /// 만드라고라 등에서 호출.
    /// 여러 번 맞으면 더 긴 시간으로 갱신.
    /// </summary>
    public void ApplyDeafEffect(float duration)
    {
        if (duration <= 0f)
            return;

        if (!IsLocalOwner())
            return;

        _remainingTime = Mathf.Max(_remainingTime, duration);
        ApplyCurrentOutputState();
    }

    public void ClearDeafEffect()
    {
        if (!IsLocalOwner())
            return;

        _remainingTime = 0f;
        ApplyCurrentOutputState();
    }

    /// <summary>
    /// ignoreListenerVolume를 쓰는 특수 AudioSource나
    /// 별도로 강제 음소거하고 싶은 소스를 등록.
    /// </summary>
    public void RegisterMuteSource(AudioSource source)
    {
        if (source == null)
            return;

        _registeredMuteSources.Add(source);
        ApplyCurrentOutputState();
    }

    public void UnregisterMuteSource(AudioSource source)
    {
        if (source == null)
            return;

        _registeredMuteSources.Remove(source);

        if (!IsDeafened)
            source.mute = false;
    }

    private void Bootstrap()
    {
        if (_bootstrapped)
            return;

        if (handsManager == null)
            handsManager = GetComponent<CookingMasterHandsManager>();

        if (handsManager == null)
            handsManager = GetComponentInParent<CookingMasterHandsManager>();

        if (targetListener == null)
            targetListener = GetComponentInChildren<AudioListener>(true);

        _normalListenerVolume = AudioListener.volume;

        for (int i = 0; i < extraSourcesToMute.Count; i++)
        {
            if (extraSourcesToMute[i] != null)
                _registeredMuteSources.Add(extraSourcesToMute[i]);
        }

        _bootstrapped = true;
    }

    private bool IsLocalOwner()
    {
        if (handsManager == null)
            return true;

        if (handsManager.Object == null || !handsManager.Object.IsValid)
            return true; // 샌드박스

        return handsManager.HasStateAuthority;
    }

    private void ApplyCurrentOutputState()
    {
        if (!IsLocalOwner())
            return;

        bool shouldMute = IsDeafened;

        if (!shouldMute)
            _normalListenerVolume = Mathf.Clamp01(_normalListenerVolume);

        // AudioListener.volume은 static이므로 targetListener가 없어도 동작
        AudioListener.volume = shouldMute ? deafenedListenerVolume : _normalListenerVolume;
        _muteApplied = shouldMute;

        foreach (AudioSource source in _registeredMuteSources)
        {
            if (source != null)
                source.mute = shouldMute;
        }
    }

    private void RestoreOutputNow()
    {
        if (!IsLocalOwner())
            return;

        _remainingTime = 0f;
        AudioListener.volume = _normalListenerVolume;
        _muteApplied = false;

        foreach (AudioSource source in _registeredMuteSources)
        {
            if (source != null)
                source.mute = false;
        }
    }

    private void OnDisable()
    {
        if (restoreVolumeOnDisable)
            RestoreOutputNow();
    }

    private void OnDestroy()
    {
        if (restoreVolumeOnDisable)
            RestoreOutputNow();
    }
}