using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 국그릇 아이템.
///
/// PotStation에서 [E]키로 국물을 퍼담고, ServingStation에 제출하는 들고 다닐 수 있는 그릇.
/// 내용물은 PotStation에서 FillFromPot()으로 한 번에 채워진다. (적층 방식 아님)
/// </summary>
public class SoupBowlItem : PickableItem, IServable
{
    private const int MaxIngredients = 12;

    [Serializable]
    private struct NetworkSoupIngredient : INetworkStruct
    {
        public NetworkString<_64> IngredientID;
        public NetworkString<_64> ItemClassName;
        public NetworkString<_32> CutMethod;
        public int   CookState;
        public float CookingTime;
        public NetworkBool Used;
    }

    [Header("Soup Bowl Settings")]
    [SerializeField] private string dishType = "Soup";
    public string DishType => dishType;

    [Networked, OnChangedRender(nameof(OnNetworkIngredientsChanged))]
    private int IngredientCountNetworked { get; set; }
    [Networked] private int SoupCookStateNetworked   { get; set; }
    [Networked, Capacity(MaxIngredients)]
    private NetworkArray<NetworkSoupIngredient> IngredientSlots => default;

    [Header("Debug View")]
    [SerializeField] private List<PlatedIngredient> _ingredients = new List<PlatedIngredient>();
    private CookState _localCookState = CookState.Raw;

    public IReadOnlyList<PlatedIngredient> PlatedIngredients => _ingredients;
    public bool IsEmpty => Count <= 0;
    public int  Count   => IsNetworkReady ? IngredientCountNetworked : _ingredients.Count;

    // ── 액체 비주얼 ───────────────────────────────────────────────

    [Header("Liquid Visual")]
    [SerializeField] private Transform liquidTransform;
    [SerializeField] private float liquidBottomY   = 0.05f;
    [SerializeField] private float liquidMaxScaleY = 0.08f;
    [SerializeField] private Color liquidColorRaw    = new Color(0.8f, 0.7f, 0.3f, 0.6f);
    [SerializeField] private Color liquidColorCooked = new Color(0.6f, 0.4f, 0.1f, 0.6f);
    [SerializeField] private Color liquidColorBurned = new Color(0.2f, 0.1f, 0.0f, 0.6f);

    [Header("Floating Ingredients")]
    [SerializeField] private Transform floatingParent;
    [SerializeField] private float bowlRadius     = 0.06f;
    [SerializeField] private float floatAmplitude = 0.005f;
    [SerializeField] private float floatSpeed     = 1.2f;
    [Tooltip("0 = 바닥, 1 = 표면")]
    [SerializeField] [Range(0f, 1f)] private float floatHeightRatio = 0.5f;
    [Tooltip("재료별 비주얼 설정 — PotStation과 동일한 PotIngredientVisualConfig 사용")]
    [SerializeField] private List<PotIngredientVisualConfig> ingredientVisualConfigs = new List<PotIngredientVisualConfig>();

    private Renderer _liquidRenderer;
    private MaterialPropertyBlock _mpb;
    private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

    private readonly List<GameObject> _floatingVisuals = new List<GameObject>();
    private readonly List<float>      _floatBaseY      = new List<float>();
    private readonly List<float>      _floatPhase      = new List<float>();
    private int _lastVisualHash = -1;

    // ── Fusion 생명주기 ────────────────────────────────────────────

    protected override void Awake()
    {
        // Liquid를 먼저 비활성화 — base.Awake()에서 AddComponent<Outline>() 시
        // Outline.Awake()가 GetComponentsInChildren<Renderer>()를 호출하는데,
        // 비활성 오브젝트는 포함되지 않으므로 Liquid renderer가 outline 캐시에서 제외된다.
        if (liquidTransform != null)
            liquidTransform.gameObject.SetActive(false);

        base.Awake(); // rb, cols, outline 초기화

        _mpb = new MaterialPropertyBlock();
    }

    private void Start()
    {
        itemName = "국그릇";
        if (liquidTransform != null)
        {
            liquidTransform.gameObject.SetActive(true); // Outline 캐시 완료 후 재활성화
            _liquidRenderer = liquidTransform.GetComponent<Renderer>();
        }
    }

    public override void Spawned()
    {
        base.Spawned();
        _mpb ??= new MaterialPropertyBlock();
        if (liquidTransform != null && _liquidRenderer == null)
            _liquidRenderer = liquidTransform.GetComponent<Renderer>();
        if (HasStateAuthority) ExecuteClear();
        SyncFromNetworkState();
    }

    public override void Render()
    {
        base.Render();
        AnimateFloating();
    }

    // ── IServable ──────────────────────────────────────────────────

    public void ClearDish()
    {
        if (IsNetworkReady)
        {
            if (!HasStateAuthority) { Rpc_RequestClear(); return; }
            ExecuteClear();
        }
        else
        {
            _ingredients.Clear();
            _localCookState = CookState.Raw;
            RefreshVisuals();
        }
    }

    // ── PotStation에서 호출 ────────────────────────────────────────

    /// <summary>
    /// PotStation이 국물을 퍼낼 때 호출. 냄비 내용물 전체를 한 번에 복사한다.
    /// </summary>
    public void FillFromPot(IReadOnlyList<PlatedIngredient> ingredients, CookState cookState)
    {
        if (IsNetworkReady)
        {
            if (!HasStateAuthority) { Rpc_RequestFill(BuildFillData(ingredients, cookState)); return; }
            CommitFill(ingredients, cookState);
        }
        else
        {
            _ingredients.Clear();
            foreach (var ingr in ingredients)
                _ingredients.Add(ingr);
            _localCookState = cookState;
            RefreshVisuals();
        }
    }

    // ── 비주얼 ────────────────────────────────────────────────────

    private void RefreshVisuals()
    {
        UpdateLiquidLevel();
        UpdateLiquidColor();
    }

    // IngredientCountNetworked가 바뀔 때만 호출 (Render 버퍼 lag 방지)
    private void OnNetworkIngredientsChanged()
    {
        SyncFromNetworkState();
        // StateAuthority는 CommitFill에서 이미 visual을 생성했으므로 덮어쓰지 않음.
        // Proxy만 여기서 visual을 생성한다.
        if (!HasStateAuthority)
            RefreshFloatingIngredients();
    }

    private void UpdateLiquidLevel()
    {
        if (liquidTransform == null) return;
        // slot 데이터가 render buffer 기준으로 아직 stale할 수 있으므로
        // _ingredients.Count 대신 IngredientCountNetworked를 직접 사용
        int count = IsNetworkReady ? IngredientCountNetworked : _ingredients.Count;
        bool visible = count > 0;
        if (_liquidRenderer != null) _liquidRenderer.enabled = visible;
        if (!visible) return;

    }



    private void UpdateLiquidColor()
    {
        if (_liquidRenderer == null) return;
        Color target = _localCookState switch
        {
            CookState.Cooked => liquidColorCooked,
            CookState.Burned => liquidColorBurned,
            _                => liquidColorRaw,
        };
        _mpb.SetColor(ColorProp, target);
        _liquidRenderer.SetPropertyBlock(_mpb);
    }

    private float GetFloatY()
    {
        // Liquid transform의 실제 위치 기준 (코드에서 위치를 수정하지 않으므로 Inspector 값 대신 직접 읽음)
        float surfaceInRoot = liquidTransform != null
            ? liquidTransform.localPosition.y + liquidTransform.localScale.y
            : liquidBottomY + liquidMaxScaleY * 2f;
        float parentOffsetY = floatingParent != null ? floatingParent.localPosition.y : 0f;
        return surfaceInRoot - parentOffsetY;
    }

    private void RefreshFloatingIngredients()
    {
        if (floatingParent == null) return;

        // 내용물이 바뀌었을 때만 재빌드
        int hash = ComputeIngredientHash();
        if (hash == _lastVisualHash) return;
        _lastVisualHash = hash;

        // 기존 비주얼 전체 제거
        foreach (var v in _floatingVisuals) if (v != null) Destroy(v);
        _floatingVisuals.Clear();
        _floatBaseY.Clear();
        _floatPhase.Clear();

        if (_ingredients.Count == 0) return;

        float floatY = GetFloatY();

        for (int i = 0; i < _ingredients.Count; i++)
        {
            PotIngredientVisualConfig cfg = GetConfigByClassName(_ingredients[i].itemClassName);

            GameObject prefab = cfg?.prefab;
            int   copies = (cfg != null && cfg.amount > 0)              ? cfg.amount              : 1;
            float radius = (cfg != null && cfg.distributionRadius > 0f) ? cfg.distributionRadius  : bowlRadius;
            float size   = (cfg != null && cfg.size > 0f)               ? cfg.size                : 1f;

            for (int k = 0; k < copies; k++)
            {
                if (prefab == null)
                {
                    _floatingVisuals.Add(null);
                    _floatBaseY.Add(floatY);
                    _floatPhase.Add(0f);
                    continue;
                }

                float angleStep   = copies > 1 ? Mathf.PI * 2f / copies : 0f;
                float groupOffset = i * (Mathf.PI * 2f / Mathf.Max(1, MaxIngredients));
                float angle = k * angleStep + groupOffset;
                float r     = copies == 1 ? 0f : radius;

                var rng = new System.Random(i * 100 + k);

                GameObject visual = Instantiate(prefab, floatingParent);
                visual.transform.localPosition = new Vector3(Mathf.Cos(angle) * r, floatY, Mathf.Sin(angle) * r);
                visual.transform.localRotation = Quaternion.Euler(
                    (float)(rng.NextDouble() * 30f - 15f),
                    (float)(rng.NextDouble() * 360f),
                    (float)(rng.NextDouble() * 30f - 15f));
                visual.transform.localScale = Vector3.one * size;
                foreach (var rb  in visual.GetComponentsInChildren<Rigidbody>())  rb.isKinematic = true;
                foreach (var col in visual.GetComponentsInChildren<Collider>())   col.enabled = false;
                foreach (var ol  in visual.GetComponentsInChildren<Outline>())    Destroy(ol);

                _floatingVisuals.Add(visual);
                _floatBaseY.Add(floatY);
                _floatPhase.Add(i * 1.7f + k * 0.9f);
            }
        }
    }

    private void AnimateFloating()
    {
        for (int i = 0; i < _floatingVisuals.Count; i++)
        {
            if (_floatingVisuals[i] == null) continue;
            Vector3 local = _floatingVisuals[i].transform.localPosition;
            local.y = _floatBaseY[i] + Mathf.Sin(Time.time * floatSpeed + _floatPhase[i]) * floatAmplitude;
            _floatingVisuals[i].transform.localPosition = local;
        }
    }

    private int ComputeIngredientHash()
    {
        int h = _ingredients.Count;
        foreach (var ingr in _ingredients)
            h = h * 31 + ingr.ingredientID.GetHashCode();
        return h;
    }

    private PotIngredientVisualConfig GetConfigByClassName(string itemClassName)
    {
        if (ingredientVisualConfigs == null || string.IsNullOrEmpty(itemClassName)) return null;
        foreach (var cfg in ingredientVisualConfigs)
        {
            if (cfg?.prefab == null) continue;
            PickableItem item = cfg.prefab.GetComponent<PickableItem>();
            if (item != null && item.GetType().Name == itemClassName) return cfg;
        }
        return null;
    }

    // ── 내부 커밋 ─────────────────────────────────────────────────

    private void CommitFill(IReadOnlyList<PlatedIngredient> ingredients, CookState cookState)
    {
        ExecuteClear();
        int count = Mathf.Min(ingredients.Count, MaxIngredients);
        Debug.Log($"[Bowl.CommitFill] input.Count={ingredients.Count}, count={count}");
        for (int i = 0; i < count; i++)
        {
            IngredientSlots.Set(i, new NetworkSoupIngredient
            {
                IngredientID  = Truncate(ingredients[i].ingredientID),
                ItemClassName = Truncate(ingredients[i].itemClassName),
                CutMethod     = ingredients[i].cutMethod ?? "",
                CookState     = (int)ingredients[i].cookState,
                CookingTime   = ingredients[i].cookTime,
                Used          = true
            });
        }
        IngredientCountNetworked = count;
        SoupCookStateNetworked   = (int)cookState;
        // PlateItem.SyncListFromNetworkState 대신 직접 리스트 관리 (조리 기록 손실 방지용 로컬 캐시)
        _ingredients.Clear();
        foreach (var ingr in ingredients)
            _ingredients.Add(ingr);
        _localCookState = cookState;

        Debug.Log($"[Bowl.CommitFill] after Local Mapping: _ingredients={_ingredients.Count}, liquidEnabled={_liquidRenderer?.enabled}, liquidActive={liquidTransform?.gameObject.activeInHierarchy}");
        RefreshVisuals();
        RefreshFloatingIngredients();
        int nonNull = 0; foreach (var v in _floatingVisuals) if (v != null) nonNull++;
        Debug.Log($"[Bowl.CommitFill] visuals={_floatingVisuals.Count}(nonNull={nonNull}), floatY={GetFloatY():F3}, lastHash={_lastVisualHash}");
    }

    private void ExecuteClear()
    {
        IngredientCountNetworked = 0;
        SoupCookStateNetworked   = (int)CookState.Raw;
        for (int i = 0; i < MaxIngredients; i++)
            IngredientSlots.Set(i, default);
        _ingredients.Clear();
        _localCookState = CookState.Raw;
        foreach (var v in _floatingVisuals) if (v != null) Destroy(v);
        _floatingVisuals.Clear();
        _floatBaseY.Clear();
        _floatPhase.Clear();
        _lastVisualHash = -1;
        RefreshVisuals();
    }

    private void SyncFromNetworkState()
    {
        if (!IsNetworkReady) return;

        // 호스트(서버)는 이미 CommitFill에서 완벽한 데이터를 로컬 리스트에 가지고 있으므로 
        // 네트워크 상태로부터 복구(동기화)를 스킵하여 cookingSeq 손실을 방지합니다.
        if (HasStateAuthority) return;

        _ingredients.Clear();
        int safe = Mathf.Clamp(IngredientCountNetworked, 0, MaxIngredients);
        for (int i = 0; i < safe; i++)
        {
            var slot = IngredientSlots[i];
            if (!slot.Used) continue;

            _ingredients.Add(new PlatedIngredient(
                slot.IngredientID.ToString(),
                (CookState)slot.CookState,
                null, // [주의] 네트워크에서는 조리 기록이 전송되지 않음
                slot.CutMethod.ToString(),
                itemClassName: slot.ItemClassName.ToString(),
                cookTime: slot.CookingTime));
        }
        _localCookState = (CookState)SoupCookStateNetworked;
        RefreshVisuals();
    }

    // ── RPC ───────────────────────────────────────────────────────

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestClear() => ExecuteClear();

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestFill(NetworkFillData data)
    {
        ExecuteClear();
        for (int i = 0; i < data.Count; i++)
        {
            IngredientSlots.Set(i, new NetworkSoupIngredient
            {
                IngredientID  = data.GetId(i),
                ItemClassName = data.GetClassName(i),
                CutMethod     = data.GetCutMethod(i),
                CookState     = data.GetCookState(i),
                CookingTime   = data.GetCookingTime(i),
                Used          = true
            });
        }
        IngredientCountNetworked = data.Count;
        SoupCookStateNetworked   = data.SoupCookState;
        SyncFromNetworkState();
        RefreshFloatingIngredients();
    }

    // ── 유틸 ──────────────────────────────────────────────────────

    private NetworkString<_64> Truncate(string s)
    {
        s ??= string.Empty;
        if (s.Length > 64) s = s.Substring(0, 64);
        return s;
    }

    private NetworkFillData BuildFillData(IReadOnlyList<PlatedIngredient> ingredients, CookState cookState)
    {
        var data = new NetworkFillData { SoupCookState = (int)cookState };
        int count = Mathf.Min(ingredients.Count, MaxIngredients);
        data.Count = count;
        for (int i = 0; i < count; i++)
        {
            data.Set(i, 
                Truncate(ingredients[i].ingredientID), 
                Truncate(ingredients[i].itemClassName), 
                ingredients[i].cutMethod ?? "", 
                (int)ingredients[i].cookState, 
                ingredients[i].cookTime);
        }
        return data;
    }

    // ── RPC 전달용 구조체 ─────────────────────────────────────────

    private struct NetworkFillData : INetworkStruct
    {
        public int SoupCookState;
        public int Count;

        public NetworkString<_64> Id0,  Id1,  Id2,  Id3,  Id4,  Id5;
        public NetworkString<_64> Id6,  Id7,  Id8,  Id9,  Id10, Id11;
        public NetworkString<_64> Cls0,  Cls1,  Cls2,  Cls3,  Cls4,  Cls5;
        public NetworkString<_64> Cls6,  Cls7,  Cls8,  Cls9,  Cls10, Cls11;
        public NetworkString<_32> CM0,  CM1,  CM2,  CM3,  CM4,  CM5;
        public NetworkString<_32> CM6,  CM7,  CM8,  CM9,  CM10, CM11;
        public int CS0, CS1, CS2, CS3, CS4,  CS5;
        public int CS6, CS7, CS8, CS9, CS10, CS11;
        public float CT0, CT1, CT2, CT3, CT4,  CT5;
        public float CT6, CT7, CT8, CT9, CT10, CT11;

        public NetworkString<_64> GetId(int i) => i switch
        {
            0 => Id0,  1 => Id1,  2 => Id2,  3 => Id3,  4 => Id4,  5 => Id5,
            6 => Id6,  7 => Id7,  8 => Id8,  9 => Id9,  10 => Id10, _ => Id11
        };
        public NetworkString<_64> GetClassName(int i) => i switch
        {
            0 => Cls0,  1 => Cls1,  2 => Cls2,  3 => Cls3,  4 => Cls4,  5 => Cls5,
            6 => Cls6,  7 => Cls7,  8 => Cls8,  9 => Cls9,  10 => Cls10, _ => Cls11
        };
        public NetworkString<_32> GetCutMethod(int i) => i switch
        {
            0 => CM0,  1 => CM1,  2 => CM2,  3 => CM3,  4 => CM4,  5 => CM5,
            6 => CM6,  7 => CM7,  8 => CM8,  9 => CM9,  10 => CM10, _ => CM11
        };
        public int GetCookState(int i) => i switch
        {
            0 => CS0,  1 => CS1,  2 => CS2,  3 => CS3,  4 => CS4,  5 => CS5,
            6 => CS6,  7 => CS7,  8 => CS8,  9 => CS9,  10 => CS10, _ => CS11
        };
        public float GetCookingTime(int i) => i switch
        {
            0 => CT0,  1 => CT1,  2 => CT2,  3 => CT3,  4 => CT4,  5 => CT5,
            6 => CT6,  7 => CT7,  8 => CT8,  9 => CT9,  10 => CT10, _ => CT11
        };
        public void Set(int i, NetworkString<_64> id, NetworkString<_64> cls, string cut, int cs, float ct = 0f)
        {
            switch (i)
            {
                case 0:  Id0  = id; Cls0  = cls; CM0 = cut; CS0  = cs; CT0  = ct; break;
                case 1:  Id1  = id; Cls1  = cls; CM1 = cut; CS1  = cs; CT1  = ct; break;
                case 2:  Id2  = id; Cls2  = cls; CM2 = cut; CS2  = cs; CT2  = ct; break;
                case 3:  Id3  = id; Cls3  = cls; CM3 = cut; CS3  = cs; CT3  = ct; break;
                case 4:  Id4  = id; Cls4  = cls; CM4 = cut; CS4  = cs; CT4  = ct; break;
                case 5:  Id5  = id; Cls5  = cls; CM5 = cut; CS5  = cs; CT5  = ct; break;
                case 6:  Id6  = id; Cls6  = cls; CM6 = cut; CS6  = cs; CT6  = ct; break;
                case 7:  Id7  = id; Cls7  = cls; CM7 = cut; CS7  = cs; CT7  = ct; break;
                case 8:  Id8  = id; Cls8  = cls; CM8 = cut; CS8  = cs; CT8  = ct; break;
                case 9:  Id9  = id; Cls9  = cls; CM9 = cut; CS9  = cs; CT9  = ct; break;
                case 10: Id10 = id; Cls10 = cls; CM10 = cut; CS10 = cs; CT10 = ct; break;
                default: Id11 = id; Cls11 = cls; CM11 = cut; CS11 = cs; CT11 = ct; break;
            }
        }
    }
}
