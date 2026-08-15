using System.Linq;
using UnityEngine;
using Interactions;

/// <summary>
/// 회복 양파 절단 공통 베이스.
/// - CutGuidelineConfig 기반 가이드라인 생성
/// - 절단 가능 여부 계산
/// </summary>
public abstract class OnionCuttableBaseItem : OnionBaseItem, ICuttable
{
    [Header("Cutting")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new CutGuidelineConfig() };

    private CutGuidelineVisual[] _guidelineVisuals;

    protected virtual bool SupportsCutting => true;

    public override bool CanChop
    {
        get
        {
            if (!SupportsCutting) return false;
            if (!base.CanChop) return false;
            if (CurrentCookState == CookState.Burned) return false;
            return HasValidCutResults();
        }
    }

    protected override void Start()
    {
        base.Start();
        EnsureGuidelineVisuals();
    }

    public override void Spawned()
    {
        base.Spawned();
        EnsureGuidelineVisuals();
    }

    public CutGuidelineVisual[] GetGuidelineVisuals(string toolType)
    {
        if (!SupportsCutting) return System.Array.Empty<CutGuidelineVisual>();
        if (CurrentCookState == CookState.Burned) return System.Array.Empty<CutGuidelineVisual>();

        EnsureGuidelineVisuals();

        return _guidelineVisuals != null
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();
    }

    private bool HasValidCutResults()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0)
            return false;

        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            if (_guidelineConfigs[i].resultPrefabs != null &&
                _guidelineConfigs[i].resultPrefabs.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureGuidelineVisuals()
    {
        if (!SupportsCutting || !HasValidCutResults())
        {
            ClearGuidelineObjects();
            _guidelineVisuals = System.Array.Empty<CutGuidelineVisual>();
            return;
        }

        bool validCache = _guidelineVisuals != null && _guidelineVisuals.Length == _guidelineConfigs.Length;
        if (validCache)
        {
            for (int i = 0; i < _guidelineVisuals.Length; i++)
            {
                if (_guidelineVisuals[i] == null)
                {
                    validCache = false;
                    break;
                }
            }
        }

        if (validCache) return;

        ClearGuidelineObjects();
        _guidelineVisuals = new CutGuidelineVisual[_guidelineConfigs.Length];

        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            GameObject go = new GameObject($"CutGuideline_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = _guidelineConfigs[i].localPosition;
            go.transform.localEulerAngles = _guidelineConfigs[i].localEulerAngles;

            CutGuidelineVisual vis = go.AddComponent<CutGuidelineVisual>();
            vis.toolType = "Knife";
            vis.cutName = string.IsNullOrEmpty(_guidelineConfigs[i].cutName) ? "절단" : _guidelineConfigs[i].cutName;
            vis.resultPrefabs = _guidelineConfigs[i].resultPrefabs;
            vis.resultCounts = _guidelineConfigs[i].resultCounts;

            _guidelineVisuals[i] = vis;
        }
    }

    private void ClearGuidelineObjects()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("CutGuideline_")) continue;

            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
    }
}