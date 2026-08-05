using System;
using UnityEngine;

/// <summary>
/// ICuttable 아이템의 가이드라인 설정 데이터. 코드로 생성하는 아이템에서 공용으로 사용합니다.
/// </summary>
[Serializable]
public struct CutGuidelineConfig
{
    public Vector3 localPosition;
    public Vector3 localEulerAngles;
    [Tooltip("이 가이드라인으로 자를 때 기록할 조리 방법 이름 (예: 포썰기, 깍뚝썰기)")]
    public string cutName;
    public Fusion.NetworkObject[] resultPrefabs;
    public int[] resultCounts;
}

/// <summary>
/// 아이템 프리팹의 자식 오브젝트에 붙이는 절단 가이드라인 컴포넌트.
/// 이 오브젝트의 위치 = 절단선 중심, transform.right = 절단선 방향.
/// CuttingStation이 도구 감지 시 활성화/비활성화합니다.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class CutGuidelineVisual : MonoBehaviour
{
    [Header("Guide Data")]
    public string toolType;
    [Tooltip("이 가이드라인으로 자를 때 기록할 조리 방법 이름 (예: 포썰기, 깍뚝썰기)")]
    public string cutName = "";
    public Fusion.NetworkObject[] resultPrefabs;
    public int[] resultCounts;

    [Header("Visual")]
    [SerializeField] private float lineHalfLength = 0.25f;
    [SerializeField] private Color normalColor   = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField] private Color highlightColor = Color.yellow;

    private LineRenderer _line;

    private void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.positionCount  = 2;
        _line.startWidth     = 0.015f;
        _line.endWidth       = 0.015f;
        _line.useWorldSpace  = true;
        _line.startColor     = normalColor;
        _line.endColor       = normalColor;
        _line.material       = new Material(Shader.Find("Sprites/Default"));
        _line.enabled        = false;
    }

    private void LateUpdate()
    {
        if (!_line.enabled) return;

        Vector3 center = transform.position;
        Vector3 right  = transform.right;
        _line.SetPosition(0, center - right * lineHalfLength);
        _line.SetPosition(1, center + right * lineHalfLength);
    }

    public void SetVisible(bool visible)
    {
        _line.enabled = visible;
        if (!visible) SetHighlight(false);
    }

    public void SetHighlight(bool on)
    {
        Color c = on ? highlightColor : normalColor;
        _line.startColor = c;
        _line.endColor   = c;
    }

    /// <summary>스크린 중심과 이 선분 사이의 최단 거리 (픽셀).</summary>
    public float ScreenDistanceToCenter(Camera cam, Vector2 screenCenter)
    {
        Vector3 s0 = cam.WorldToScreenPoint(transform.position - transform.right * lineHalfLength);
        Vector3 s1 = cam.WorldToScreenPoint(transform.position + transform.right * lineHalfLength);
        if (s0.z < 0 && s1.z < 0) return float.MaxValue;

        Vector2 a = new Vector2(s0.x, s0.y);
        Vector2 b = new Vector2(s1.x, s1.y);
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 0f
            ? Mathf.Clamp01(Vector2.Dot(screenCenter - a, ab) / ab.sqrMagnitude)
            : 0f;
        return Vector2.Distance(screenCenter, a + t * ab);
    }

    /// <summary>이 가이드라인의 결과물 총 개수.</summary>
    public int TotalResultCount()
    {
        int total = 0;
        if (resultCounts != null)
            foreach (int c in resultCounts) total += c;
        return total;
    }
}
