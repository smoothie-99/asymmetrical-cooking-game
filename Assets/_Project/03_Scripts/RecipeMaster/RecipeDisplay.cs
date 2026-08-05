using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RecipeDisplay : MonoBehaviour
{
    // 기존 UI
    public TextMeshProUGUI nameText;
    public Image iconImage;
    public TextMeshProUGUI descriptionText;


    [Header("재료 표시 영역")]
    public Transform mainIngredientContainer; // Description3 역할
    public Transform subIngredientContainer;  // Description4 역할
    public GameObject ingredientPrefab;

    [HideInInspector]
    public RecipeData recipeData;

    private CanvasGroup canvasGroup;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void Hide()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
    }

    public void Show()
    {
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
    }
    public void DisplayRecipe()
    {
        if (recipeData == null) return;

        nameText.text = recipeData.recipeName;
        iconImage.sprite = recipeData.recipeIcon;
        descriptionText.text = recipeData.description;

        // Destroy 대신 DestroyImmediate로 즉시 제거
        ClearContainer(mainIngredientContainer);
        ClearContainer(subIngredientContainer);

        ShowIngredients(recipeData.mainIngredients, mainIngredientContainer);
        ShowIngredients(recipeData.subIngredients, subIngredientContainer);
    }

    private void ClearContainer(Transform container)
    {
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(container.GetChild(i).gameObject);
        }
    }

    // [중요] 컨테이너를 인자로 받도록 수정
    private void ShowIngredients(List<IngredientData> ingredients, Transform container)
    {
        if (ingredients == null) return;

        foreach (var ingredient in ingredients)
        {
            // 인자로 받은 container를 부모로 설정
            GameObject obj = Instantiate(ingredientPrefab, container);

            var textComp = obj.transform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
            if (textComp != null) textComp.text = ingredient.ingredientName;

            var imgComp = obj.transform.Find("IconImage")?.GetComponent<Image>();
            if (imgComp != null) imgComp.sprite = ingredient.icon;
        }
    }
}