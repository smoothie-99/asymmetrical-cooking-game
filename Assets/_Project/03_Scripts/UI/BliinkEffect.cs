using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BliinkEffect : MonoBehaviour
{
    private TextMeshProUGUI targetText;
    public float blinkSpeed = 2.0f;

    void Start()
    {
        targetText = GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        // 시간을 기준으로 0에서 1 사이를 부드럽게 왔다갔다 해
        float alpha = Mathf.PingPong(Time.time * blinkSpeed, 1.0f);

        // 글자 색상의 알파값만 변경
        Color color = targetText.color;
        color.a = alpha;
        targetText.color = color;
    }
}
