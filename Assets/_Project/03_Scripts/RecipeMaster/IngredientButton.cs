using UnityEngine; // using은 항상 맨 위에!

public class IngredientButton : MonoBehaviour
{
    public IngredientBookManager bookManager;
    public int ingredientIndex;

    public void OnClickIndex()
    {
        if (bookManager != null)
        {
            bookManager.OpenIngredientFromIndex(ingredientIndex);
        }
        else
        {
            Debug.LogError("BookManager가 연결되지 않았어! 인스펙터 확인해봐~");
        }
    }
}