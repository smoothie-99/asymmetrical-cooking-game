using System.Linq;
using UnityEngine;
using Interactions;

/// <summary>
/// 슬라이스 통통버섯.
/// - 도망가지 않음
/// - 한 번 더 썰면 큐브 통통버섯으로 분리됨
/// </summary>
public class SlicedBoppyMushroomItem : PickableItem, ICuttable
{
    [Header("Cutting")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new CutGuidelineConfig() };
    private CutGuidelineVisual[] _guidelineVisuals;

    private void Start()
    {
        itemName = "슬라이스 통통버섯";
        metadata["ingredientID"] = "통통 버섯";
        CreateGuidelineVisuals();
    }

    public override void Spawned()
    {
        CreateGuidelineVisuals();
    }

    public override bool CanChop => !isHeld && HasValidCutResults();

    private bool HasValidCutResults()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0)
            return false;

        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            if (_guidelineConfigs[i].resultPrefabs != null &&
                _guidelineConfigs[i].resultPrefabs.Length > 0)
                return true;
        }

        return false;
    }

    private void CreateGuidelineVisuals()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0)
            return;

        if (_guidelineVisuals != null && _guidelineVisuals.Length == _guidelineConfigs.Length)
            return;

        _guidelineVisuals = new CutGuidelineVisual[_guidelineConfigs.Length];

        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _guidelineConfigs[i].localPosition;
            go.transform.localEulerAngles = _guidelineConfigs[i].localEulerAngles;

            CutGuidelineVisual vis = go.AddComponent<CutGuidelineVisual>();
            vis.toolType = "Knife";
            vis.cutName = _guidelineConfigs[i].cutName;
            vis.resultPrefabs = _guidelineConfigs[i].resultPrefabs;
            vis.resultCounts = _guidelineConfigs[i].resultCounts;

            _guidelineVisuals[i] = vis;
        }
    }


    public override void Chop(CuttingStation board)
    {
        if (!CanChop)
            return;

        const string toolType = "Knife";

        if (IsNetworkReady)
            Rpc_Cut(0, toolType);
        else
            LocalCut(0, toolType);
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType)
    {
        if (!CanChop)
            return System.Array.Empty<CutGuidelineVisual>();

        return _guidelineVisuals != null
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();
    }

    public override void TransferStateTo(PickableItem target)
    {
        if (target is CubedBoppyMushroomItem cubed)
        {
            metadata["ingredientID"] = "통통 버섯";
            cubed.metadata["ingredientID"] = "통통 버섯";
        }
    }
}
