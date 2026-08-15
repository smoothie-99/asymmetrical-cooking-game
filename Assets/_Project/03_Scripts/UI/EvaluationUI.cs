using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 도입부 대화, 피드백, 최종 결과를 통합 관리하는 UI 매니저
/// </summary>
public class EvaluationUI : MonoBehaviour
{
    [Header("Panels")]
    public GameObject dialoguePanel;   // 게임 시작 전 요구사항 패널
    [SerializeField] private GameObject feedbackPanel;   // 요리 후 손님 대사 패널
    [SerializeField] private GameObject resultPanel;     // 최종 성공/실패 패널
    public GameObject startButton;     // [Dialogue] 단계의 '요리 시작' 버튼

    [Header("Restart Controls")]
    [SerializeField] private Button restartButton;
    [SerializeField] private GameObject otherPlayerLeftMsg; // "다른 플레이어가 메인으로 이동하였습니다" 메시지

    [Header("Text References")]
    public TextMeshProUGUI dialogueText;    // 도입부 요구사항 텍스트
    public TextMeshProUGUI feedbackText;    // "면이 다 타버렸잖아!" 등
    public TextMeshProUGUI resultTitleText; // SUCCESS / FAILED

    [Header("Character Portraits")]
    public UnityEngine.UI.Image dialoguePortrait;  // [수정] 대화창용 포트레이트
    public UnityEngine.UI.Image feedbackPortrait;  // [수정] 피드백창용 포트레이트
    
    private bool isResultSent = false; // [신규] 결과 전적 서버 전송 중복 방지 플래그

    // [중요] 기존 악마 이미지는 삭제하고, 현재 주문에서 손님 정보를 가져오도록 수정합니다.
    private GuestDataSO GetCurrentGuestData()
    {
        if (OrderManager.Instance != null && OrderManager.Instance.currentOrder != null)
        {
            return OrderManager.Instance.currentOrder.guestData;
        }
        return null;
    }

    void OnEnable()
    {
        SystemManager.SubscribeWhenReady(UpdateUI);
    }

    void OnDisable()
    {
        SystemManager.Unsubscribe(UpdateUI);
    }

    public void RefreshUI()
    {
        if (SystemManager.Instance != null)
            UpdateUI(SystemManager.Instance.CurrentMetaState);
    }

    public void UpdateUI(MetaState state)
    {
        // 모든 패널 활성화/비활성화
        if (dialoguePanel != null) dialoguePanel.SetActive(state == MetaState.OrderDialogue);
        if (feedbackPanel != null) feedbackPanel.SetActive(state == MetaState.Feedback);

        if (resultPanel != null) 
        {
            resultPanel.SetActive(state == MetaState.Result || state == MetaState.ReadyConfirmation);
        }

        // 초상화 활성화 제어
        if (dialoguePortrait != null) dialoguePortrait.gameObject.SetActive(state == MetaState.OrderDialogue);
        if (feedbackPortrait != null) feedbackPortrait.gameObject.SetActive(state == MetaState.Feedback);

        if (state == MetaState.OrderDialogue || state == MetaState.Feedback || state == MetaState.Result)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            
            if (state == MetaState.Result) SyncResultText();
        }

        if (startButton != null)
        {
            bool isHost = SystemManager.Instance != null && SystemManager.Instance.HasStateAuthority;
            startButton.SetActive(state == MetaState.OrderDialogue && isHost);
        }

        bool success = false;
        string feedback = "";
        if (GamePlayManager.Instance != null)
        {
            success = GamePlayManager.Instance.IsSuccess;
            feedback = GamePlayManager.Instance.FeedbackMessage;
        }

        // 현재 손님 데이터 가져오기 (이미지 및 데이터 반영)
        GuestDataSO guest = GetCurrentGuestData();

        switch (state)
        {
            case MetaState.OrderDialogue:
                if (dialogueText != null && OrderManager.Instance != null && OrderManager.Instance.currentOrder != null)
                {
                    dialogueText.text = OrderManager.Instance.currentOrder.orderText;
                }
                else if (dialogueText != null)
                {
                    dialogueText.text = "주문을 기다리는 중입니다...";
                }

                // [수정] 주문 시 대화창 사진 출력
                if (dialoguePortrait != null && guest != null && guest.portraitOrder != null)
                {
                    dialoguePortrait.sprite = guest.portraitOrder;
                }
                break;

            case MetaState.Feedback:
                if (feedbackText != null) feedbackText.text = feedback;

                // [정교화] 기획안의 FeedbackType별 표정 매칭 (Angry, Normal, Special)
                if (feedbackPortrait != null && guest != null)
                {
                    FeedbackType primaryError = (GlobalNetworkState.Instance != null) ? 
                                               GlobalNetworkState.Instance.ResultFeedbackType : 
                                               (FeedbackType)(-1);

                    // 1. 특수 기믹 (Special)
                    if (primaryError == FeedbackType.SpecialCondition && guest.portraitSpecial != null)
                    {
                        feedbackPortrait.sprite = guest.portraitSpecial;
                    }
                    // 2. 완벽한 성공 (Happy)
                    else if (success && (GlobalNetworkState.Instance?.ResultOutcome == CookingOutcome.PerfectClear))
                    {
                        feedbackPortrait.sprite = guest.portraitHappy ?? guest.portraitNormal;
                    }
                    // 3. 에러 타입별 상세 매칭
                    else
                    {
                        switch (primaryError)
                        {
                            // [화남(Angry) 그룹]
                            case FeedbackType.EmptyDish:
                            case FeedbackType.WrongDish:
                            case FeedbackType.WrongMain:
                            case FeedbackType.Burned:
                            case FeedbackType.MissingIngredient:
                                feedbackPortrait.sprite = guest.portraitAngry;
                                break;

                            // [보통(Normal) 그룹]
                            case FeedbackType.ExtraIngredient:
                            case FeedbackType.WrongCookState:
                            case FeedbackType.WrongCutting:
                            case FeedbackType.WrongOrder:
                            default:
                                // 성공(Clear) 상태이거나 보통 그룹의 에러는 Normal 사진 사용
                                feedbackPortrait.sprite = (primaryError == (FeedbackType)(-1) && !success) ? 
                                                       guest.portraitAngry : guest.portraitNormal;
                                break;
                        }
                    }
                }
                break;

            case MetaState.Result:
                if (resultTitleText != null)
                {
                    // [1단계] 기본 성공 여부 확인 (IsSuccess가 가장 확실한 데이터)
                    bool isActuallySuccess = success; 
                    if (GamePlayManager.Instance != null) isActuallySuccess = GamePlayManager.Instance.IsSuccess;

                    if (!isActuallySuccess)
                    {
                        resultTitleText.text = "FAILED";
                        resultTitleText.color = Color.red;
                    }
                    else
                    {
                        // [2단계] 성공 상태일 때만 세부 등급 확인
                        CookingOutcome outcome = (GlobalNetworkState.Instance != null) ? 
                                               GlobalNetworkState.Instance.ResultOutcome : 
                                               CookingOutcome.Clear;

                        // 만약 성공인데 Outcome이 Fail(2)로 전송되었다면 보정
                        if (outcome == CookingOutcome.Fail) outcome = CookingOutcome.Clear;

                        switch (outcome)
                        {
                            case CookingOutcome.PerfectClear:
                                resultTitleText.text = "PERFECT CLEAR";
                                resultTitleText.color = new Color(1f, 0.84f, 0f); // Gold
                                break;
                            case CookingOutcome.Clear:
                            default:
                                resultTitleText.text = "CLEAR";
                                resultTitleText.color = Color.green;
                                break;
                        }

                        // [신규] 성공 시 서버에 전적을 기록합니다.
                        if (!isResultSent && AuthManager.Instance != null && SystemManager.Instance != null)
                        {
                            isResultSent = true; // 더 이상 중복 전송하지 않음
                            int roundNum = SystemManager.Instance.SelectedStage;
                            Debug.Log($"💾 [UpdateUI] {roundNum}단계 클리어({outcome}) 기록을 서버로 전송합니다.");
                            StartCoroutine(AuthManager.Instance.UpdateStageRecord(roundNum, outcome));
                        }
                    }
                }
                break;

            default:
                // 결과창 단계가 아니면 전송 플래그를 초기화합니다 (다음 게임 준비)
                if (state != MetaState.Result && state != MetaState.ReadyConfirmation)
                {
                    isResultSent = false;
                }
                break;
        }
    }

    public void OnClickStartReady()
    {
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.ChangeMetaState(MetaState.Cooking);
        }
    }

    public void OnClickNextToResult()
    {
        if (SystemManager.Instance != null)
        {
            SystemManager.Instance.ChangeMetaState(MetaState.Result);
        }
    }

    private void SyncResultText()
    {
        if (resultTitleText != null)
        {
            // [검증] IsSuccess가 최우선
            bool isActuallySuccess = (GamePlayManager.Instance != null) ? GamePlayManager.Instance.IsSuccess : false;

            if (!isActuallySuccess)
            {
                resultTitleText.text = "FAILED";
                resultTitleText.color = Color.red;
            }
            else
            {
                CookingOutcome outcome = (GlobalNetworkState.Instance != null) ? 
                                       GlobalNetworkState.Instance.ResultOutcome : 
                                       CookingOutcome.Clear;

                if (outcome == CookingOutcome.Fail) outcome = CookingOutcome.Clear;

                switch (outcome)
                {
                    case CookingOutcome.PerfectClear:
                        resultTitleText.text = "PERFECT CLEAR";
                        resultTitleText.color = new Color(1f, 0.84f, 0f); // Gold
                        break;
                    case CookingOutcome.Clear:
                    default:
                        resultTitleText.text = "CLEAR";
                        resultTitleText.color = Color.green;
                        break;
                }
            }
        }
    }

    // [Result] 다시하기 버튼 - InGameMenuUI.Instance를 호출하도록 변경
    public void OnClickRestart()
    {
        if (InGameMenuUI.Instance != null)
        {
            InGameMenuUI.Instance.OpenConfirmDialog("정말 이번 라운드를\n다시 시작하시겠습니까?", PerformRestart);
        }
        else
        {
            PerformRestart();
        }
    }

    private void PerformRestart()
    {
        if (LobbyPlayer.Local != null)
        {
            LobbyPlayer.Local.RPC_SetRestartVoting(true);
        }
    }

    // [Result] 메인으로 버튼 - InGameMenuUI.Instance를 호출하도록 변경
    public void OnClickGoToMenu()
    {
        if (InGameMenuUI.Instance != null)
        {
            InGameMenuUI.Instance.OpenConfirmDialog("정말 메인 메뉴로\n이동하시겠습니까?", PerformGoToMenu);
        }
        else
        {
            PerformGoToMenu();
        }
    }

    private void PerformGoToMenu()
    {
        if (LobbyPlayer.Local != null)
        {
            LobbyPlayer.Local.RPC_RequestExitAll();
        }
        else
        {
            NetworkLauncher.Instance?.LeaveSession();
        }
    }

    // [Result] 라운드 선택 버튼 - InGameMenuUI.Instance를 호출하도록 변경
    public void OnClickRoundSelection()
    {
        if (InGameMenuUI.Instance != null)
        {
            InGameMenuUI.Instance.OpenConfirmDialog("정말 라운드 선택 창으로\n이동하시겠습니까?", PerformRoundSelection);
        }
        else
        {
            PerformRoundSelection();
        }
    }

    private void PerformRoundSelection()
    {
        if (LobbyPlayer.Local != null)
        {
            LobbyPlayer.Local.RPC_RequestGlobalRoundSelect();
        }
        else
        {
            NetworkLauncher.Instance?.LeaveSession();
        }
    }
}
