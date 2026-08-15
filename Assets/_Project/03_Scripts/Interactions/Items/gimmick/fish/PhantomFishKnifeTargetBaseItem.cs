using System.Linq;
using UnityEngine;
using Interactions;

/// <summary>
/// 환영 물고기 계열의 칼 상호작용 공통 베이스.
/// - Knife guideline 생성
/// - 도마 위에서만 칼 사용 허용
/// - generic cut 대신 Chop() 직접 호출
/// </summary>
public abstract class PhantomFishKnifeTargetBaseItem : PickableItem, ICuttable
{
    [Header("Knife Guideline")]
    [SerializeField] private CutGuidelineConfig[] knifeGuidelineConfigs = { new CutGuidelineConfig() };

    private CutGuidelineVisual[] _guidelineVisuals;

    protected abstract string DefaultItemName { get; }
    protected abstract string DefaultIngredientId { get; }
    protected virtual string KnifeLabel => "손질하기";

    protected abstract bool CanKnifeProcess { get; }

    protected virtual void Start()
    {
        itemName = DefaultItemName;
        metadata["ingredientID"] = DefaultIngredientId;
        EnsureKnifeGuidelines();
    }

    public override void Spawned()
    {
        EnsureKnifeGuidelines();
    }

    public override bool CanChop => !isHeld && CanKnifeProcess;

    protected void EnsureKnifeGuidelines()
    {
        if (knifeGuidelineConfigs == null || knifeGuidelineConfigs.Length == 0)
        {
            _guidelineVisuals = System.Array.Empty<CutGuidelineVisual>();
            return;
        }

        bool valid = _guidelineVisuals != null && _guidelineVisuals.Length == knifeGuidelineConfigs.Length;
        if (valid)
        {
            for (int i = 0; i < _guidelineVisuals.Length; i++)
            {
                if (_guidelineVisuals[i] == null)
                {
                    valid = false;
                    break;
                }
            }
        }

        if (valid)
            return;

        ClearExistingGuidelineObjects();
        _guidelineVisuals = new CutGuidelineVisual[knifeGuidelineConfigs.Length];

        for (int i = 0; i < knifeGuidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = knifeGuidelineConfigs[i].localPosition;
            go.transform.localEulerAngles = knifeGuidelineConfigs[i].localEulerAngles;

            CutGuidelineVisual vis = go.AddComponent<CutGuidelineVisual>();
            vis.toolType = "Knife";
            vis.cutName = string.IsNullOrEmpty(knifeGuidelineConfigs[i].cutName) ? "절단" : knifeGuidelineConfigs[i].cutName;

            // 공용 generic cut 경로를 막기 위해 실제 결과 프리팹은 비워둔다.
            vis.resultPrefabs = System.Array.Empty<Fusion.NetworkObject>();
            vis.resultCounts = System.Array.Empty<int>();

            _guidelineVisuals[i] = vis;
        }
    }

    private void ClearExistingGuidelineObjects()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("CutGuideline_"))
                continue;

            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType)
    {
        if (!CanChop)
            return System.Array.Empty<CutGuidelineVisual>();

        EnsureKnifeGuidelines();

        return _guidelineVisuals != null
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();
    }

    public override bool CanInteract(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem && IsOnStation)
        {
            if (!CanChop || player == null)
                return false;

            PickableItem held = player.GetActiveHandItem();
            return held is ITool tool && tool.ToolType == "Knife" && GetGuidelineVisuals(tool.ToolType).Length > 0;
        }

        return base.CanInteract(player, type);
    }

    public override void Interact(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem && IsOnStation)
        {
            if (!CanChop || player == null)
                return;

            PickableItem held = player.GetActiveHandItem();
            if (held is not ITool tool || tool.ToolType != "Knife")
                return;

            if (SelectedGuidelineIndex < 0)
                return;

            Chop(null);
            return;
        }

        base.Interact(player, type);
    }

    public override string GetInteractionLabel(CookingMasterHandsManager player, InteractionType type)
    {
        if (type == InteractionType.UseItem && IsOnStation)
        {
            if (!CanChop || player == null)
                return "";

            PickableItem held = player.GetActiveHandItem();
            if (held is ITool tool && tool.ToolType == "Knife" && GetGuidelineVisuals(tool.ToolType).Length > 0)
                return SelectedGuidelineIndex >= 0 ? $"[L-Click] {KnifeLabel}" : "[점선에 조준하세요]";

            return "";
        }

        return base.GetInteractionLabel(player, type);
    }
}
