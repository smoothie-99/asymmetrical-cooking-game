using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Audio;

/// <summary>
/// 감도/음량 환경설정 UI 로직. MainMenuUI와 InGameMenuUI 양쪽에서 공유합니다.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("Sliders")]
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;

    [Header("Status Texts")]
    [SerializeField] private TMP_Text sensitivityStatusText;
    [SerializeField] private TMP_Text masterStatusText;
    [SerializeField] private TMP_Text bgmStatusText;
    [SerializeField] private TMP_Text sfxStatusText;

    [Header("Audio")]
    [SerializeField] private AudioMixer mainMixer;

    private float tempSensitivity;
    private float tempMasterVolume;
    private float tempBGMVolume;
    private float tempSFXVolume;

    private static readonly string[] SenseLevels = { "매우 느림", "느림", "보통", "빠름", "매우 빠름" };
    private static readonly float[] SenseValues  = { 1.5f, 2.5f, 4f, 6f, 10f }; // 팀장님의 요청에 따라 감도 범위를 현실적으로 조정 (기존 5~40은 너무 빠름)

    // [추가] 마우스 감도가 변경되었음을 알리는 전역 이벤트
    public static System.Action<float> OnSensitivityUpdated;

    private void Awake()
    {
        if (sensitivitySlider != null)
        {
            sensitivitySlider.minValue = 1;
            sensitivitySlider.maxValue = 5;
            sensitivitySlider.wholeNumbers = true;
            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        }

        if (masterSlider != null) masterSlider.onValueChanged.AddListener(OnMasterChanged);
        if (bgmSlider    != null) bgmSlider.onValueChanged.AddListener(OnBGMChanged);
        if (sfxSlider    != null) sfxSlider.onValueChanged.AddListener(OnSFXChanged);
    }

    private void OnEnable()
    {
        Load();
    }

    /// <summary>
    /// PlayerPrefs로부터 모든 설정을 불러와 UI에 반영하고 시스템에도 적용합니다.
    /// </summary>
    public void Load()
    {
        tempSensitivity  = PlayerPrefs.GetFloat("MouseSensitivityLevel", 3f);
        tempMasterVolume = PlayerPrefs.GetFloat("MasterVolume", 0.75f);
        tempBGMVolume    = PlayerPrefs.GetFloat("BGMVolume", 0.75f);
        tempSFXVolume    = PlayerPrefs.GetFloat("SFXVolume", 0.75f);

        if (sensitivitySlider != null) sensitivitySlider.value = tempSensitivity;
        if (masterSlider      != null) masterSlider.value      = tempMasterVolume;
        if (bgmSlider         != null) bgmSlider.value         = tempBGMVolume;
        if (sfxSlider         != null) sfxSlider.value         = tempSFXVolume;

        RefreshTexts();

        // [중요] 슬라이더 값을 설정하는 것만으로는 onValueChanged가 발동하지 않을 때가 있으므로
        // (예: 같은 0에서 0으로 설정 시) 시스템 설정을 명시적으로 한 번 강제 동기화합니다.
        ApplySystemSettings(mainMixer);
    }

    public void Apply()
    {
        PlayerPrefs.SetFloat("MouseSensitivityLevel", tempSensitivity);
        PlayerPrefs.SetFloat("MasterVolume",          tempMasterVolume);
        PlayerPrefs.SetFloat("BGMVolume",             tempBGMVolume);
        PlayerPrefs.SetFloat("SFXVolume",             tempSFXVolume);

        int idx = Mathf.Clamp((int)tempSensitivity - 1, 0, SenseValues.Length - 1);
        float actualSensitivity = SenseValues[idx];
        PlayerPrefs.SetFloat("MouseSensitivity", actualSensitivity);
        PlayerPrefs.Save();

        // [추가] 실제로 감도가 변경되었음을 알림
        OnSensitivityUpdated?.Invoke(actualSensitivity);

        // 실제로 모든 설정을 믹서에 적용
        ApplySystemSettings(mainMixer);
    }

    /// <summary>
    /// 저장된 정보를 바탕으로 오디오 믹서를 즉시 갱신하는 공용 유틸리티
    /// </summary>
    public static void ApplySystemSettings(AudioMixer mixer)
    {
        if (mixer == null) return;

        float m = PlayerPrefs.GetFloat("MasterVolume", 0.75f);
        float b = PlayerPrefs.GetFloat("BGMVolume", 0.75f);
        float s = PlayerPrefs.GetFloat("SFXVolume", 0.75f);

        SetMixerParameter(mixer, "Master_Vol", m);
        SetMixerParameter(mixer, "BGM_Vol",    b);
        SetMixerParameter(mixer, "SFX_Vol",    s);
    }

    public void Cancel() => Load();

    // --- Slider callbacks ---
    public void OnSensitivityChanged(float value) 
    { 
        tempSensitivity = value; 
        RefreshTexts(); 

        // [추가] 실시간 반영을 원할 경우 여기서도 이벤트를 쏴줍니다.
        int idx = Mathf.Clamp((int)value - 1, 0, SenseValues.Length - 1);
        OnSensitivityUpdated?.Invoke(SenseValues[idx]);
    }
    private void OnMasterChanged(float value)     { tempMasterVolume = value; RefreshTexts(); SetMixerParameter(mainMixer, "Master_Vol", value); }
    private void OnBGMChanged(float value)        { tempBGMVolume    = value; RefreshTexts(); SetMixerParameter(mainMixer, "BGM_Vol",    value); }
    private void OnSFXChanged(float value)        { tempSFXVolume    = value; RefreshTexts(); SetMixerParameter(mainMixer, "SFX_Vol",    value); }

    private void RefreshTexts()
    {
        if (sensitivityStatusText != null)
        {
            int idx = Mathf.Clamp((int)tempSensitivity - 1, 0, SenseLevels.Length - 1);
            sensitivityStatusText.text = SenseLevels[idx];
        }

        SetVolumeText(masterStatusText, tempMasterVolume);
        SetVolumeText(bgmStatusText,    tempBGMVolume);
        SetVolumeText(sfxStatusText,    tempSFXVolume);
    }

    private static void SetVolumeText(TMP_Text label, float value)
    {
        if (label == null) return;
        label.text = value <= 0.0001f ? "음소거" : Mathf.RoundToInt(value * 100) + "%";
    }

    private static void SetMixerParameter(AudioMixer mixer, string param, float volume)
    {
        if (mixer == null) return;
        // 볼륨이 0일 경우 -80dB(완전 무음)으로 처리
        float targetDecibel = volume <= 0.0001f ? -80f : Mathf.Log10(volume) * 20;
        mixer.SetFloat(param, targetDecibel);
    }
}
