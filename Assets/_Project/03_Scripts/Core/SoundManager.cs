using UnityEngine;
using System.Collections;
using UnityEngine.Audio;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    public AudioSource bgmSource;
    [SerializeField] private AudioMixer mainMixer; // [추가] 환경설정을 적용할 필터

    public AudioClip[] lobbyBgms;
    public AudioClip[] cookingBgms;
    public AudioClip[] evaluationBgms;

    private Coroutine currentPlaylistCoroutine;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // [추가] 처음 시작 시 저장된 오디오 설정을 시스템에 적용합니다.
            SettingsPanel.ApplySystemSettings(mainMixer);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // [수정] Awake에서 한 번, 실질적으로 소리가 나기 직전인 Start에서 한 번 더 세팅을 적용합니다.
        // 이는 일부 환경에서 믹서가 초기화되기 전에 값이 설정되는 현상을 방지합니다.
        if (mainMixer != null)
        {
            SettingsPanel.ApplySystemSettings(mainMixer);
            Debug.Log("🎵 [SoundManager] 시작 시 오디오 설정을 시스템에 동기화했습니다.");
        }
        else
        {
            Debug.LogWarning("⚠️ [SoundManager] Main Mixer가 할당되지 않아 초기 볼륨 설정을 적용할 수 없습니다.");
        }

        // 로비 BGM 재생 시도
        PlayLobbyBGM();
    }

    public void PlayPlaylist(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return;

        if (currentPlaylistCoroutine != null)
        {
            StopCoroutine(currentPlaylistCoroutine);
        }

        currentPlaylistCoroutine = StartCoroutine(PlaylistRoutine(clips));
    }

    private IEnumerator PlaylistRoutine(AudioClip[] clips)
    {
        int index = 0;
        while (true)
        {
            bgmSource.clip = clips[index];
            bgmSource.Play();

            yield return new WaitForSeconds(bgmSource.clip.length);

            index = (index + 1) % clips.Length;
        }
    }

    public void PlayLobbyBGM() => PlayPlaylist(lobbyBgms);
    public void PlayCookingBGM() => PlayPlaylist(cookingBgms);
    public void PlayEvaluationBGM() => PlayPlaylist(evaluationBgms);

    // ── 청력 마비 ────────────────────────────────────────────────

    private Coroutine _deafRoutine;
    private float _normalVolume = 1f;

    /// <summary>
    /// 로컬 클라이언트의 게임 오디오를 duration초 동안 차단합니다.
    /// 여러 번 맞으면 더 긴 시간으로 갱신됩니다.
    /// </summary>
    public void ApplyDeafEffect(float duration)
    {
        if (duration <= 0f) return;

        if (_deafRoutine != null)
        {
            StopCoroutine(_deafRoutine);
            // 이미 난청 중이면 현재 볼륨을 기준으로 갱신하지 않고 저장된 정상 볼륨 유지
        }
        else
        {
            _normalVolume = AudioListener.volume;
            AudioListener.volume = 0f;
        }

        _deafRoutine = StartCoroutine(DeafRoutine(duration));
    }

    private IEnumerator DeafRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        AudioListener.volume = _normalVolume;
        _deafRoutine = null;
    }
}