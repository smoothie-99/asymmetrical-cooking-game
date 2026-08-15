using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 요리사 로컬 HUD. Canvas는 씬 루트에 생성되어 Camera 계층과 독립적으로 동작합니다.
/// </summary>
public class CookingMasterHUD : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset _font;
    private TMP_Text _labelE, _labelF, _labelL;
    private TMP_Text _secondaryE, _secondaryF;
    private TMP_Text _leftHand, _rightHand;

    private GameObject _canvasRoot;
    private bool _isInitialized = false;

    void Awake()
    {
        // [수정] Awake에서 즉시 생성하지 않고, 실제 필요할 때만 지연 생성하도록 변경합니다.
    }

    void OnDestroy()
    {
        if (_canvasRoot != null) Destroy(_canvasRoot);
    }

    public void BuildHUD()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        _canvasRoot = new GameObject("HUD_Canvas");

        Canvas canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _canvasRoot.AddComponent<GraphicRaycaster>();

        // ── 십자선 (화면 정중앙) ─────────────────────────────────
        GameObject crosshairGO = MakeRectGO(_canvasRoot, "Crosshair",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
        Image img = crosshairGO.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.8f);

        // ── 상호작용 레이블 (십자선 오른쪽) ──────────────────────
        GameObject labelRoot = new GameObject("Labels");
        labelRoot.transform.SetParent(_canvasRoot.transform, false);
        RectTransform labelRT = labelRoot.AddComponent<RectTransform>();
        labelRT.anchorMin = labelRT.anchorMax = new Vector2(0.5f, 0.5f);
        labelRT.pivot = new Vector2(0f, 0.5f);
        labelRT.anchoredPosition = new Vector2(20, 0);
        labelRT.sizeDelta = new Vector2(500, 200);

        _labelE     = MakeLabel(labelRoot, "LabelE",     Color.white,   80);
        _labelF     = MakeLabel(labelRoot, "LabelF",     Color.yellow,  28);
        _labelL     = MakeLabel(labelRoot, "LabelL",     Color.cyan,     0);
        _secondaryE = MakeLabel(labelRoot, "SecondaryE", Color.white,  -40);
        _secondaryF = MakeLabel(labelRoot, "SecondaryF", Color.yellow, -80);

        // ── 손 인벤토리 (화면 하단 중앙) ─────────────────────────
        _leftHand  = MakeHandLabel(_canvasRoot, "LeftHand",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-180, 50));
        _rightHand = MakeHandLabel(_canvasRoot, "RightHand",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(180, 50));
    }

    private GameObject MakeRectGO(GameObject parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return go;
    }

    private TMP_Text MakeLabel(GameObject parent, string name, Color color, float offsetY)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(0, offsetY);
        rt.sizeDelta = new Vector2(500, 36);

        TMP_Text tmp = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) tmp.font = _font;
        tmp.fontSize = 22;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.text = "";
        go.SetActive(false);
        return tmp;
    }

    private TMP_Text MakeHandLabel(GameObject parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(320, 48);

        TMP_Text tmp = go.AddComponent<TextMeshProUGUI>();
        if (_font != null) tmp.font = _font;
        tmp.fontSize = 22;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = "";
        return tmp;
    }

    public void UpdateHUD(
        string labelE, string labelF, string labelL,
        string secondaryE, string secondaryF,
        string leftItem, string rightItem,
        int activeHandIndex)
    {
        SetLabel(_labelE,     labelE);
        SetLabel(_labelF,     labelF);
        SetLabel(_labelL,     labelL);
        SetLabel(_secondaryE, secondaryE);
        SetLabel(_secondaryF, secondaryF);

        if (_leftHand != null)
        {
            _leftHand.text  = $"[1] {leftItem}";
            _leftHand.color = activeHandIndex == 0 ? Color.cyan : new Color(1f, 1f, 1f, 0.6f);
        }
        if (_rightHand != null)
        {
            _rightHand.text  = $"[2] {rightItem}";
            _rightHand.color = activeHandIndex == 1 ? Color.cyan : new Color(1f, 1f, 1f, 0.6f);
        }
    }

    public void SetVisibility(bool visible)
    {
        if (_canvasRoot != null)
        {
            _canvasRoot.SetActive(visible);
        }
    }

    private void SetLabel(TMP_Text label, string text)
    {
        if (label == null) return;
        bool show = !string.IsNullOrEmpty(text);
        label.gameObject.SetActive(show);
        if (show) label.text = text;
    }
}
