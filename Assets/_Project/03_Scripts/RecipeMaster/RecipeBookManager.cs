using System.Collections;

using System.Collections.Generic;

using TMPro;

using UnityEngine;



public class RecipeBookManager : MonoBehaviour

{

    public AutoFlip autoFlip;

    public RecipeDisplay recipeDisplay;

    public List<RecipeData> allRecipes = new List<RecipeData>();

    [Header("Audio Settings")]
    public AudioSource audioSource;
    public AudioClip pageFlipSound;

    private int lastFoundIndex = -1;

    private List<int> recipeStartPages = new List<int>();



    private int currentRecipeIndex = 0;

    private int textPageNum = 1;


    void Awake()
    {
        // ↓ 추가
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    void Start()

    {

        StartCoroutine(InitAfterLayout());

    }



    // 오브젝트가 활성화될 때마다 다시 계산하도록 추가 (안전장치)

    void OnEnable()

    {

        if (allRecipes.Count > 0)

        {

            StopAllCoroutines();

            StartCoroutine(InitAfterLayout());

        }

    }



    IEnumerator InitAfterLayout()
    {
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        BuildPageTable();

        // RemoveAllListeners 대신 특정 함수만 제거 후 재등록
        autoFlip.OnFlipStart.RemoveListener(OnFlipStartHandler);
        autoFlip.OnFlipStart.AddListener(OnFlipStartHandler);

        autoFlip.ControledBook.OnFlip.RemoveListener(OnPageFlippedFinished);
        autoFlip.ControledBook.OnFlip.AddListener(OnPageFlippedFinished);

        UpdateRecipeText();
    }

    void OnFlipStartHandler()
    {
        if (recipeDisplay != null) recipeDisplay.Hide();
        PlayFlipSound();
    }

    void OnPageFlippedFinished()
    {
        UpdateRecipeText();
    }



    void BuildPageTable()

    {

        recipeStartPages.Clear();

        int currentPage = 2;



        for (int i = 0; i < allRecipes.Count; i++)

        {

            recipeStartPages.Add(currentPage);

            int pagesNeeded = GetTotalPagesOfRecipe(i);

            currentPage += pagesNeeded * 2;

        }



        if (allRecipes.Count > 0)

        {

            recipeDisplay.recipeData = allRecipes[currentRecipeIndex];

            recipeDisplay.DisplayRecipe();

            recipeDisplay.descriptionText.ForceMeshUpdate(true);

        }

    }



    // ★ 핵심 수정 부분: 텍스트 업데이트 강제 및 페이지 계산 안정화

    private int GetTotalPagesOfRecipe(int index)

    {

        if (index < 0 || index >= allRecipes.Count) return 1;



        recipeDisplay.recipeData = allRecipes[index];

        recipeDisplay.DisplayRecipe();



        // true를 인자로 넣어 텍스트 메쉬와 레이아웃을 즉시 다시 계산하게 함

        recipeDisplay.descriptionText.ForceMeshUpdate(true);



        int pageCount = recipeDisplay.descriptionText.textInfo.pageCount;



        // 계산 결과가 0 이하라면 최소 1페이지로 반환

        return (pageCount <= 0) ? 1 : pageCount;

    }



    // --- 이하 기존 로직 동일 (Flip, Search 등) ---



    public void JumpToRecipe(int targetIndex)

    {

        if (targetIndex < 0 || targetIndex >= allRecipes.Count) return;

        if (currentRecipeIndex == targetIndex && textPageNum == 1) return;



        bool isForward = targetIndex > currentRecipeIndex;

        currentRecipeIndex = targetIndex;

        textPageNum = 1;



        autoFlip.ControledBook.currentPage = recipeStartPages[targetIndex];

        if (recipeDisplay != null) recipeDisplay.Hide();



        if (isForward) autoFlip.FlipRightPage();

        else autoFlip.FlipLeftPage();

    }



    public void SearchNext(string keyword)

    {

        string cleanKeyword = keyword.Trim().Replace(" ", "").ToLower();

        int startIndex = lastFoundIndex + 1;

        if (startIndex >= allRecipes.Count) startIndex = 0;



        int found = -1;

        for (int i = 0; i < allRecipes.Count; i++)

        {

            int checkIndex = (startIndex + i) % allRecipes.Count;

            bool matchName = allRecipes[checkIndex].recipeName.Replace(" ", "").ToLower().Contains(cleanKeyword);

            bool matchDesc = allRecipes[checkIndex].description.Replace(" ", "").ToLower().Contains(cleanKeyword);



            if (matchName || matchDesc)

            {

                found = checkIndex;

                break;

            }

        }



        if (found != -1)

        {

            lastFoundIndex = found;

            JumpToRecipe(found);

        }

        else

        {

            Debug.Log($"'{keyword}'가 포함된 레시피가 더 이상 없어요.");

            lastFoundIndex = -1;

        }

    }



    public void FlipNext()

    {

        recipeDisplay.descriptionText.ForceMeshUpdate(true);

        int totalInternalPages = recipeDisplay.descriptionText.textInfo.pageCount;



        if (textPageNum < totalInternalPages) textPageNum++;

        else

        {

            if (currentRecipeIndex < allRecipes.Count - 1)

            {

                currentRecipeIndex++;

                textPageNum = 1;

            }

            else return;

        }



        recipeDisplay.Hide();

        autoFlip.FlipRightPage();

    }



    public void FlipPrevious()

    {

        if (textPageNum > 1) textPageNum--;

        else

        {

            if (currentRecipeIndex > 0)

            {

                currentRecipeIndex--;

                textPageNum = GetTotalPagesOfRecipe(currentRecipeIndex);

            }

            else return;

        }



        recipeDisplay.Hide();

        autoFlip.FlipLeftPage();

    }

    void UpdateRecipeText()
    {
        if (currentRecipeIndex >= 0 && currentRecipeIndex < allRecipes.Count)
        {
            recipeDisplay.recipeData = allRecipes[currentRecipeIndex];
            recipeDisplay.DisplayRecipe();

            // pageToDisplay를 먼저 설정한 뒤 ForceMeshUpdate
            recipeDisplay.descriptionText.pageToDisplay = textPageNum;
            recipeDisplay.descriptionText.ForceMeshUpdate(true);

            recipeDisplay.Show();
        }
        else
        {
            recipeDisplay.Hide();
        }
    }

    private void PlayFlipSound()
    {
        if (audioSource != null && pageFlipSound != null)
            audioSource.PlayOneShot(pageFlipSound);
    }

}