using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [SerializeField] private Button recipeButton;
    [SerializeField] private GameObject recipePanel;
    [SerializeField] private Button ingredientsButton;
    [SerializeField] private GameObject ingredientsPanel;

    [Header("Audio Settings")]
    [SerializeField] private AudioSource uiAudioSource; // UI 전용 오디오 소스
    [SerializeField] private AudioClip panelToggleSound;  // 열고 닫을 때 소리

    [Header("Phase Control")]
    [Tooltip("인게임(Cook 단계)에서만 보여야 할 UI들의 최상위 부모를 연결하세요.")]
    [SerializeField] private GameObject gameplayUIRoot;

    private void Start()
    {
        if (uiAudioSource == null) uiAudioSource = GetComponent<AudioSource>();

        recipeButton.onClick.RemoveAllListeners();
        recipeButton.onClick.AddListener(ToggleRecipePanel);

        ingredientsButton.onClick.RemoveAllListeners();
        ingredientsButton.onClick.AddListener(ToggleIngredientsPanel);

        if (GamePlayManager.Instance != null)
        {
            GamePlayManager.Instance.OnCookingPhaseEntered += OnPhaseEntered;
            RefreshVisibility(GamePlayManager.Instance.CurrentCookingState);
        }
    }

    // 매 프레임마다 키보드 입력을 체크해!
    private void Update()
    {
        // 2번 키를 누르면 레시피 토글
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            ToggleRecipePanel();
        }

        // 3번 키를 누르면 재료창 토글
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            ToggleIngredientsPanel();
        }
    }

    private void OnDestroy()
    {
        if (GamePlayManager.Instance != null)
        {
            GamePlayManager.Instance.OnCookingPhaseEntered -= OnPhaseEntered;
        }
    }

    private void OnPhaseEntered(CookingState state)
    {
        RefreshVisibility(state);
    }

    private void RefreshVisibility(CookingState state)
    {
        if (gameplayUIRoot != null)
        {
            bool shouldShow = (state == CookingState.Cooking);
            gameplayUIRoot.SetActive(shouldShow);
        }

        if (state == CookingState.Ready || state == CookingState.Submit)
        {
            if (recipePanel != null) recipePanel.SetActive(false);
            if (ingredientsPanel != null) ingredientsPanel.SetActive(false);
        }
    }

    private void ToggleRecipePanel()
    {
        // UI가 활성화되어 있을 때만 키보드가 작동하게 하고 싶다면 조건을 추가할 수 있어.
        if (recipePanel != null)
        {
            PlayFlipSound();

            recipePanel.SetActive(!recipePanel.activeSelf);
        }
    }

    private void ToggleIngredientsPanel()
    {
        if (ingredientsPanel != null)
        {
            PlayFlipSound();

            ingredientsPanel.SetActive(!ingredientsPanel.activeSelf);
        }
    }

    private void PlayFlipSound()
    {
        if (uiAudioSource != null && panelToggleSound != null)
        {
            uiAudioSource.PlayOneShot(panelToggleSound);
        }
    }
}