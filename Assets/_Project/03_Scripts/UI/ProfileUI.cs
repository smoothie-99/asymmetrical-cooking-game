using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 각 스테이지별로 클리어(흑백)와 퍼펙트(컬러) 아이콘 세트를 관리합니다.
/// </summary>
[System.Serializable]
public struct StageBadgeIcons
{
    public Sprite clearedIcon;    // 1: 흑백 이미지 (제작해오신 것)
    public Sprite perfectIcon;    // 2: 컬러 이미지
}

/// <summary>
/// 내 정보 패널(MyInfoPanel)의 닉네임과 12개 스테이지 뱃지 상태를 시각적으로 업데이트합니다.
/// </summary>
public class ProfileUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text nicknameText;          // 닉네임 표시 텍스트
    [SerializeField] private Image[] badgeImages;           // 하이어라키의 12개 Image 컴포넌트

    [Header("Common Sprite")]
    [SerializeField] private Sprite commonLockedIcon;       // 0: 공용 잠금 실루엣 (선택 사항)

    [Header("Stage Specifically Sprites")]
    [SerializeField] private StageBadgeIcons[] stageBadgeSets; // 1~12 스테이지별 아이콘 세트 (배열 크기 12 권장)

    // 스테이지 달성 데이터 (0: 미클리어, 1: 클리어, 2: 퍼펙트)
    // 실제 서버 데이터 연동 전까지는 Mock 데이터를 사용합니다.
    private int[] _stageClearStatuses = new int[12];

    /// <summary>
    /// 패널이 열릴 때 최신 정보로 UI를 갱신합니다.
    /// </summary>
    public void RefreshProfile()
    {
        // 1. 닉네임 갱신
        if (nicknameText != null)
        {
            nicknameText.text = MainMenuUI.CurrentSessionNickname ?? "GUEST";
        }

        // 2. 뱃지 시나리오 업데이트
        UpdateBadgeVisuals();
    }

    private void UpdateBadgeVisuals()
    {
        if (badgeImages == null || stageBadgeSets == null) return;

        // [수정] 가짜 데이터 대신 AuthManager의 진짜 전적 장부를 읽어옵니다.
        int[] records = AuthManager.StageRecords;

        for (int i = 0; i < 12; i++)
        {
            // 인덱스 범위 초과 방지
            if (i >= badgeImages.Length || i >= stageBadgeSets.Length) break;
            if (badgeImages[i] == null) continue;

            // [수정] 실시간 전적 반영 (기록이 없으면 0)
            int status = (records != null && i < records.Length) ? records[i] : 0;
            StageBadgeIcons iconSet = stageBadgeSets[i];

            // 0: 잠금 - 공용 잠금 아이콘 우선 적용 🔒
            if (status == 0)
            {
                if (commonLockedIcon != null)
                {
                    badgeImages[i].sprite = commonLockedIcon;
                    badgeImages[i].color = Color.white; 
                }
                else
                {
                    // 공용 이미지가 없으면 제작해오신 흑백 이미지에 검정 칠하기
                    badgeImages[i].sprite = iconSet.clearedIcon;
                    badgeImages[i].color = Color.black;
                }
            }
            // 1: 클리어 - 은색/흑백 이미지 노출 🛡️
            else if (status == 1)
            {
                badgeImages[i].sprite = iconSet.clearedIcon;
                badgeImages[i].color = Color.white;
            }
            // 2: 퍼펙트 클리어 - 컬러/금색 이미지 노출 🏆
            else if (status == 2)
            {
                badgeImages[i].sprite = iconSet.perfectIcon;
                badgeImages[i].color = Color.white;
            }
        }
    }
}
