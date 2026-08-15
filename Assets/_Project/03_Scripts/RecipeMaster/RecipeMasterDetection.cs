using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class RecipeMasterDetection : MonoBehaviour
{
    [Header("Settings")]
    public TextMeshProUGUI infoText;
    public Camera recipeMasterCamera;

    private Dictionary<string, string> nameMap = new Dictionary<string, string>()
    {
        { "tomato", "구름 토마토" },
        { "bacon", "기름 베이컨" },
        { "cabbage", "비틀비틀 양배추" },
        { "jem_shell", "수면보석조개" },
        { "clam", "수면보석조개" },
        { "egg", "드래곤 알" },
        { "onion", "회복 양파" },
        { "mimic", "미믹" },
        { "fish", "환영 물고기" },
        { "mandrake", "만드라고라" },
        { "cheese", "용암 치즈" },
        { "meatbox", "미믹 고기" },
        { "meat", "고기" },
        { "pumkin", "투명 호박" },
        { "pumpkin", "투명 호박" },
        { "mushroom", "통통 버섯" },
        { "pasta", "파스타 면" },
        { "headphone", "귀마개" },
        { "cube", "슬라임" },
        { "knife", "칼" },
        { "skew", "꼬치" },
        { "plate", "접시" },
        { "liquid", "그릇" },
        { "cuttingstation", "도마" },
        { "servingstation", "제출대" },
        { "potbox", "냄비" },
        { "firestation", "화구" },
        { "cookingmaster", "쿠킹마스터" },
    };

    void Start()
    {
        Debug.Log("Start 실행됨!");
        if (infoText != null) infoText.gameObject.SetActive(false);
    }

    void Awake()
    {
        Debug.Log("Awake 실행됨!");
        
        if (infoText == null)
        {
            GameObject textObj = GameObject.Find("ObjectInfoText");
            if (textObj != null) infoText = textObj.GetComponent<TextMeshProUGUI>();
        }
    }

    void Update()
    {
        if (recipeMasterCamera == null)
        {
            GameObject camObj = GameObject.Find("TopCamera");
            if (camObj != null)
            {
                recipeMasterCamera = camObj.GetComponent<Camera>();
            }
            else
            {
                RecipeMasterController rmController = FindAnyObjectByType<RecipeMasterController>();
                if (rmController != null)
                {
                    recipeMasterCamera = rmController.GetComponentInChildren<Camera>(true);
                }
            }
        }

        if (recipeMasterCamera == null || infoText == null || !recipeMasterCamera.gameObject.activeInHierarchy) return;

        Ray ray = recipeMasterCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit))
        {
            string objName = hit.collider.gameObject.name.ToLower();
            string displayName = null;

            foreach (var entry in nameMap)
            {
                if (objName.Contains(entry.Key))
                {
                    displayName = entry.Value;
                    break;
                }
            }

            if (displayName != null) ShowText(displayName, hit.collider.bounds.center);
            else HideText();
        }
        else
        {
            HideText();
        }
    }

    void ShowText(string name, Vector3 worldPosition)
    {
        infoText.gameObject.SetActive(true);
        infoText.text = name;
        Vector3 screenPos = recipeMasterCamera.WorldToScreenPoint(worldPosition);
        Vector2 offset = new Vector2(0f, 30f);
        infoText.rectTransform.position = screenPos + (Vector3)offset;
    }

    void HideText()
    {
        if (infoText != null) infoText.gameObject.SetActive(false);
    }
}