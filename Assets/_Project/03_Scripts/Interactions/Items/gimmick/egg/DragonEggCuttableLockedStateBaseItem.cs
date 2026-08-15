using System.Linq;
using UnityEngine;
using Fusion;
using Interactions;

/// <summary>
/// 초록/노란색 드래곤 알 공통 베이스.
/// - 가이드라인 표시
/// - 칼로 자르면 GuidelineConfig의 결과물 프리팹으로 치환
/// </summary>
public abstract class DragonEggCuttableLockedStateBaseItem : DragonEggLockedStateBaseItem, ICuttable
{
    [Header("Cutting Settings")]
    [SerializeField] private CutGuidelineConfig[] _guidelineConfigs = { new CutGuidelineConfig() };

    private CutGuidelineVisual[] _guidelineVisuals;

    public override bool CanChop => !isHeld && TryGetPrimaryResultPrefab(out _);

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
        EnsureGuidelineVisuals();

        return _guidelineVisuals != null
            ? _guidelineVisuals.Where(g => g != null && g.toolType == toolType).ToArray()
            : System.Array.Empty<CutGuidelineVisual>();
    }

    public override void Chop(CuttingStation board)
    {
        if (!CanChop)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
            {
                RPC_RequestChop();
                return;
            }

            ExecuteChop();
            return;
        }

        ExecuteChop();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestChop()
    {
        if (!CanChop)
            return;

        ExecuteChop();
    }

    private void ExecuteChop()
    {
        if (!TryGetSelectedResultPrefab(out NetworkObject resultPrefab))
        {
            Debug.LogError($"[DragonEggCuttableLockedStateBaseItem] {name} selected guideline has no result prefab.");
            return;
        }

        SpawnReplacementAndDespawnSelf(resultPrefab, true);
    }

    private bool TryGetSelectedResultPrefab(out NetworkObject resultPrefab)
    {
        resultPrefab = null;

        // SelectedGuidelineIndex가 유효한지 확인
        if (SelectedGuidelineIndex < 0 || SelectedGuidelineIndex >= _guidelineConfigs.Length)
        {
            // Fallback: 첫 번째 유효한 결과물 사용
            return TryGetPrimaryResultPrefab(out resultPrefab);
        }

        CutGuidelineConfig selectedConfig = _guidelineConfigs[SelectedGuidelineIndex];
        if (selectedConfig.resultPrefabs == null || selectedConfig.resultPrefabs.Length == 0)
        {
            return false;
        }

        // 첫 번째 결과 프리팹 사용
        for (int j = 0; j < selectedConfig.resultPrefabs.Length; j++)
        {
            if (selectedConfig.resultPrefabs[j] != null)
            {
                resultPrefab = selectedConfig.resultPrefabs[j];
                return true;
            }
        }

        return false;
    }

    private bool TryGetPrimaryResultPrefab(out NetworkObject resultPrefab)
    {
        resultPrefab = null;

        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0)
            return false;

        for (int i = 0; i < _guidelineConfigs.Length; i++)
        {
            CutGuidelineConfig config = _guidelineConfigs[i];
            if (config.resultPrefabs == null || config.resultPrefabs.Length == 0)
                continue;

            for (int j = 0; j < config.resultPrefabs.Length; j++)
            {
                if (config.resultPrefabs[j] != null)
                {
                    resultPrefab = config.resultPrefabs[j];
                    return true;
                }
            }
        }

        return false;
    }

    private void EnsureGuidelineVisuals()
    {
        if (_guidelineConfigs == null || _guidelineConfigs.Length == 0)
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

        if (validCache)
            return;

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
            vis.cutName = _guidelineConfigs[i].cutName;
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
            if (!child.name.StartsWith("CutGuideline_"))
                continue;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(child.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
    }
}
