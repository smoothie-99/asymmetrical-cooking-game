using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IngredientBookManager : MonoBehaviour
{
    [Header("Audio Settings")]
    public AudioSource audioSource;
    public AudioClip pageFlipSound;

    public AutoFlip autoFlip;
    public IngredientDisplay ingredientDisplay;
    public List<IngredientData> allIngredients = new List<IngredientData>();

    private int ingredientIndex = 0;
    private int textPageNum = 1;

    private List<int> ingredientStartPage = new List<int>();

    void Awake()
    {
        // 인스펙터에서 할당 안 했을 경우를 대비한 방어 코드
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        StartCoroutine(InitAfterLayout());
    }

    IEnumerator InitAfterLayout()
    {
        yield return null;

        BuildPageTable();

        autoFlip.OnFlipStart.AddListener(() => {
            if (ingredientDisplay != null) ingredientDisplay.Hide(); // ★ 변경
            PlayFlipSound();
        });

        autoFlip.ControledBook.OnFlip.AddListener(OnPageFlippedFinished);
        UpdateIngredientText();
    }

    void BuildPageTable()
    {
        Canvas.ForceUpdateCanvases();

        ingredientStartPage.Clear();
        int currentPage = 2;

        for (int i = 0; i < allIngredients.Count; i++)
        {
            ingredientStartPage.Add(currentPage);
            int textPages = GetTotalPagesOfIngredient(i);
            currentPage += textPages * 2;
        }

        ingredientDisplay.DisplayIngredient(allIngredients[ingredientIndex]);
        ingredientDisplay.descriptionText.ForceMeshUpdate();
    }

    public void OpenIngredientFromIndex(int index)
    {
        if (index < 0 || index >= allIngredients.Count) return;
        if (ingredientIndex == index && textPageNum == 1) return;

        bool isForward = index > ingredientIndex;

        ingredientIndex = index;
        textPageNum = 1;

        autoFlip.ControledBook.currentPage = ingredientStartPage[index];

        if (ingredientDisplay != null)
            ingredientDisplay.Hide(); // ★ 변경

        if (isForward)
            autoFlip.FlipRightPage();
        else
            autoFlip.FlipLeftPage();
    }

    public void FlipNext()
    {
        ingredientDisplay.descriptionText.ForceMeshUpdate();
        int totalInternalPages = ingredientDisplay.descriptionText.textInfo.pageCount;

        if (textPageNum < totalInternalPages)
        {
            textPageNum++;
        }
        else
        {
            if (ingredientIndex < allIngredients.Count - 1)
            {
                ingredientIndex++;
                textPageNum = 1;
            }
            else return;
        }

        autoFlip.FlipRightPage();
    }

    void OnPageFlippedFinished()
    {
        UpdateIngredientText();
    }

    void UpdateIngredientText()
    {
        int index = ingredientIndex;

        if (index >= 0 && index < allIngredients.Count)
        {
            var data = allIngredients[index];
            ingredientDisplay.DisplayIngredient(data);
            ingredientDisplay.descriptionText.ForceMeshUpdate();
            ingredientDisplay.descriptionText.pageToDisplay = textPageNum;
            ingredientDisplay.Show(); // ★ 변경
        }
        else
        {
            ingredientDisplay.Hide(); // ★ 변경
        }
    }

    public void FlipPrevious()
    {
        if (textPageNum > 1)
        {
            textPageNum--;
        }
        else
        {
            if (ingredientIndex > 0)
            {
                ingredientIndex--;
                textPageNum = GetTotalPagesOfIngredient(ingredientIndex);
            }
            else return;
        }

        autoFlip.FlipLeftPage();
    }

    private int GetTotalPagesOfIngredient(int index)
    {
        if (index < 0 || index >= allIngredients.Count) return 1;

        var data = allIngredients[index];
        ingredientDisplay.DisplayIngredient(data);

        // ★ 수정: true를 넣어 더 강력하게 업데이트하고, 
        // 만약 계산값이 0이면 최소 1페이지로 취급하도록 방어 코드 추가
        ingredientDisplay.descriptionText.ForceMeshUpdate(true);

        int pageCount = ingredientDisplay.descriptionText.textInfo.pageCount;

        // 디버깅을 위해 로그를 꼭 찍어봐! 메인 씬 콘솔창 확인 필수.
        // Debug.Log($"[{index}] {data.ingredientName} : 계산된 페이지 수 = {pageCount}");

        return (pageCount <= 0) ? 1 : pageCount;
    }

    public void JumpToCategory(string categoryName)
    {
        int targetIndex = -1;
        for (int i = 0; i < allIngredients.Count; i++)
        {
            if (allIngredients[i].category == categoryName)
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex != -1)
        {
            if (ingredientIndex == targetIndex && textPageNum == 1) return;

            bool isForward = targetIndex > ingredientIndex;

            ingredientIndex = targetIndex;
            textPageNum = 1;

            ingredientDisplay.Hide(); // ★ 추가 (JumpToCategory에도 Hide 필요)

            if (isForward)
                autoFlip.FlipRightPage();
            else
                autoFlip.FlipLeftPage();
        }
        else
        {
            Debug.LogWarning($"{categoryName} 카테고리를 찾을 수 없어!");
        }
    }

    private void PlayFlipSound()
    {
        if (audioSource != null && pageFlipSound != null)
        {
            audioSource.PlayOneShot(pageFlipSound);
        }
    }
}