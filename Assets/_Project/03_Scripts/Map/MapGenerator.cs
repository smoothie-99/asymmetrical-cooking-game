using Fusion;
using System.Collections.Generic;
using UnityEngine;

public class MapGenerator : NetworkBehaviour
{
    public static MapGenerator Instance { get; private set; }

    [Networked] public NetworkString<_256> SyncedMapData { get; set; }
    private string _lastDrawnMapData = "";

    private SeedRandom _mapRandom;
    private readonly List<NetworkObject> _spawnedStations = new();

    // 빌드(IL2CPP)와 에디터(Mono)간 완벽한 참조 동기화를 위한 커스텀 난수 생성 클래스
    private class SeedRandom
    {
        private uint state;
        public SeedRandom(int seed) { state = (uint)seed == 0 ? 1 : (uint)seed; }
        public int Next(int min, int max)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            
            uint range = (uint)(max - min);
            if (range == 0) return min; // 예외 방지

            return min + (int)(state % range);
        }
    }

    public enum TileType
    {
        Empty,              // 빈 공간 (플레이어가 걸어 다니는 곳)

        // ── 조리기구 (상호작용 없음, 환경 오브젝트) ──────────────
        General,            // 일반 조리대
        CuttingBoard,       // 도마
        Fireplace,          // 화구
        Slime,              // 슬라임 (세척 담당)
        Submission,         // 제출구
        TrashBin,           // 쓰레기통
        Pot,                // 냄비

        // ── 재료 디스펜서 (FloatingPoint, 무한 복제) ─────────────
        TomatoBox,          // 토마토
        ClamBox,            // 조개
        NoodleBox,          // 면
        MandragoraBox,      // 만드라고라
        CabbageBox,         // 양배추
        CheeseBox,          // 치즈
        BaconBox,           // 베이컨
        EggBox,             // 계란
        MushroomBox,        // 버섯
        PumpkinBox,         // 호박
        OnionBox,           // 양파
        FishBox,            // 물고기
        MeatBox,            // 고기
        DoughBox,           // 반죽
        IngredientBox,      // 나머지 재료
    }

    [Header("Seed Settings")]
    public bool useRandomSeed = true;
    [Networked] public int currentSeed { get; set; }


    [Header("Map Settings")]
    public int width = 12;
    public int height = 12;

    [Header("Environment Settings")]
    public GameObject floorPrefab;
    public float floorMargin = 4.0f; // [추가] 맵 외곽 여유 바닥 크기
    public GameObject wallPrefab;
    public GameObject ceilingPrefab; // [추가] 천장 프리팹
    public float wallHeight = 5.0f; // [추가] 벽과 기둥의 공통 높이
    public GameObject cornerPrefab; // [추가] 모서리 기둥 프리팹
    public GameObject drainPrefab; // [추가] 하수구 프리팹
    public float drainHeight = 0.5f; // [추가] 하수구 스폰 높이
    public NetworkPrefabRef hookNetworkPrefab; // 거치대 네트워크 프리팹 (SpecialEquipmentHolder 포함)
    public GameObject candlePrefab; // [추가] 촛불 프리팹
    public float headsetHeight = 3.5f;
    public float candleHeight = 3.5f;
    public float globalSpawnHeight = 0.5f; // [추가] 모든 오브젝트의 기본 생성 높이 보정값

    [Header("cooking utensils Prefabs")]
    public NetworkObject generalPrefab;
    public NetworkObject cuttingBoardPrefab;
    public NetworkObject fireplacePrefab;
    public NetworkObject slimePrefab;
    public NetworkObject submissionPrefab;
    public NetworkObject trashBinPrefab;
    public NetworkObject potPrefab;

    [Header("Submission Station Settings")]
    [Tooltip("제출 스테이션에 주입할 레시피 정답지")]
    public RecipeRequirementSO submissionRecipeRequirement;
    [Tooltip("제출 스테이션에 주입할 손님 데이터")]
    public GuestDataSO submissionGuestData;

    [Header("ingredient dispenser Prefabs")]
    public NetworkObject tomatoBoxPrefab;
    public NetworkObject clamBoxPrefab;
    public NetworkObject noodleBoxPrefab;
    public NetworkObject mandragoraBoxPrefab;
    public NetworkObject cabbageBoxPrefab;
    public NetworkObject cheeseBoxPrefab;
    public NetworkObject baconBoxPrefab;
    public NetworkObject eggBoxPrefab;
    public NetworkObject mushroomBoxPrefab;
    public NetworkObject pumpkinBoxPrefab;
    public NetworkObject onionBoxPrefab;
    public NetworkObject fishBoxPrefab;
    public NetworkObject meatBoxPrefab;
    public NetworkObject doughBoxPrefab;
    public NetworkObject ingredientBoxPrefab;

    [Header("cooking utensils Prefabs (1회 픽업, FloatingPoint)")]
    public NetworkObject[] toolPrefabs;

    [Header("special item Prefabs (1회 픽업, FloatingPoint)")]
    public NetworkObject[] specialItemPrefabs;

    private TileType[,] mapData;
    private readonly List<Vector2Int> _emptyTiles = new();
    private readonly List<Vector2Int> _tableTiles = new(); // 기획 B: 아이템을 올려둘 테이블 위치

    public bool IsMapReady => mapData != null && _emptyTiles.Count > 0;

    public override void Spawned()
    {
        Instance = this;

        GameObject container = GameObject.Find("Map_Container");
        if (container != null)
        {
            transform.SetParent(container.transform);
            transform.localPosition = Vector3.zero; // 좌표 초기화
            transform.localRotation = Quaternion.identity;
        }

        if (HasStateAuthority)
        {
            int newSeed = useRandomSeed ? Random.Range(1, 1000000) : currentSeed;
            GenerateMapAndSync(newSeed);
        }
    }

    // [핵심] 변수가 바뀌었는지 감시해서 맵을 그려!
    public override void Render()
    {
        string currentData = SyncedMapData.ToString();
        
        // 맵 데이터 문자열이 도착했고 이전과 다르다면 맵 적용!
        if (currentData.Length >= width * height && currentData != _lastDrawnMapData)
        {
            _lastDrawnMapData = currentData;
            Debug.Log($"🗺️ [MapGen] 맵 데이터 수신 완료 (길이: {currentData.Length}). 로컬에 그림 시작.");
            ApplyMapDataAndDraw(currentData);
        }
    }

    private void InjectSubmissionData(GameObject go)
    {
        ServingStation station = go.GetComponent<ServingStation>();
        if (station == null) return;
        if (submissionRecipeRequirement != null) station.SetRecipeRequirement(submissionRecipeRequirement);
        if (submissionGuestData        != null) station.SetGuestData(submissionGuestData);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (HasStateAuthority)
        {
            foreach (var station in _spawnedStations)
            {
                if (station != null && station.IsValid)
                    runner.Despawn(station);
            }
        }
        _spawnedStations.Clear();

        if (Instance == this) Instance = null;
    }

    // ── 아이템 스폰 요청 (누구든 호출 → Master Client가 처리) ──────────────

    public void RequestItemSpawn(NetworkPrefabRef prefabRef, Vector3 position, NetworkId requesterId)
    {
        if (HasStateAuthority)
            SpawnItemForPlayer(prefabRef, position, requesterId);
        else
            Rpc_RequestItemSpawn(prefabRef, position, requesterId);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestItemSpawn(NetworkPrefabRef prefabRef, Vector3 position, NetworkId requesterId)
    {
        SpawnItemForPlayer(prefabRef, position, requesterId);
    }

    private void SpawnItemForPlayer(NetworkPrefabRef prefabRef, Vector3 position, NetworkId requesterId)
    {
        NetworkObject item = Runner.Spawn(prefabRef, position, Quaternion.identity);
        Rpc_GiveItemToPlayer(item.Id, requesterId);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void Rpc_GiveItemToPlayer(NetworkId itemId, NetworkId requesterId)
    {
        NetworkObject itemObj = Runner.FindObject(itemId);
        NetworkObject playerObj = Runner.FindObject(requesterId);
        if (itemObj == null || playerObj == null) return;

        PickableItem item = itemObj.GetComponent<PickableItem>();
        CookingMasterHandsManager hands = playerObj.GetComponentInChildren<CookingMasterHandsManager>();
        if (item == null || hands == null) return;

        // 아이템 자체의 픽업 조건 체크 (예: LavaCheeseItem 냉기 주머니 필요)
        if (!item.CanInteract(hands, Interactions.InteractionType.Primary))
        {
            // 픽업 불가 → 스폰 위치에 그냥 드롭 (바닥에 떨어짐)
            return;
        }

        hands.PickupItem(item);
    }

    // 로컬 실행용 (Race Condition 원인이었던 Start() 삭제 -> MapSystem에서 직접 RequestMapGenerate 호출)


    /// <summary>
    /// MapSystem에서 호출하는 진입점입니다. Start()에서 자동 실행되지 않습니다.
    /// </summary>
    public void RequestMapGenerate()
    {
        bool generate = (Object == null || !Runner.IsRunning || HasStateAuthority);

        if (generate)
        {
            int seed = useRandomSeed ? Random.Range(1, 1000000) : currentSeed;

            if (Runner != null && Runner.IsRunning) GenerateMapAndSync(seed);
            else LocalGenerate(seed);
        }
    }

    public void LocalGenerate(int seed)
    {
        currentSeed = seed;
        useRandomSeed = false;
        Debug.Log($"[MapGenerator] 시드 {currentSeed}번으로 로컬 맵 생성");

        _mapRandom = new SeedRandom(seed);
        int safetyNet = 0; // 무한 루프 방지용
        bool isValidMap = false;

        while (!isValidMap && safetyNet < 100)
        {
            safetyNet++;
            mapData = new TileType[width, height];
            for (int x = 0; x < width; ++x)
                for (int y = 0; y < height; ++y)
                    mapData[x, y] = TileType.Empty;

            PlaceObjectsRandomly();
            if (IsMapConnected()) isValidMap = true;
        }

        if (!isValidMap) Debug.LogWarning("[MapManager] 연결된 맵 생성 실패");
        
        DrawMap();
    }

    /// <summary>
    /// 생성된 맵에서 주변 빈 공간이 넓은 타일 위치를 월드 좌표로 반환합니다.
    /// </summary>
    public Vector3 GetRandomSpawnPosition()
    {
        if (_emptyTiles.Count == 0) return transform.position + Vector3.up;

        // 상하좌우 + 대각선 8방향 빈 칸 개수로 여유 점수 계산
        int[] dx = { -1, 1, 0, 0, -1, 1, -1, 1 };
        int[] dy = { 0, 0, -1, 1, -1, -1, 1, 1 };

        int bestScore = -1;
        List<Vector2Int> candidates = new();

        foreach (var tile in _emptyTiles)
        {
            int score = 0;
            for (int i = 0; i < 8; i++)
            {
                int nx = tile.x + dx[i];
                int ny = tile.y + dy[i];
                if (nx >= 0 && nx < width && ny >= 0 && ny < height && mapData[nx, ny] == TileType.Empty)
                    score++;
            }
            if (score > bestScore)
            {
                bestScore = score;
                candidates.Clear();
                candidates.Add(tile);
            }
            else if (score == bestScore)
            {
                candidates.Add(tile);
            }
        }

        int chosenIdx = (_mapRandom != null) ? _mapRandom.Next(0, candidates.Count) : UnityEngine.Random.Range(0, candidates.Count);
        Vector2Int chosen = candidates[chosenIdx];
        // 플레이어 스폰 높이를 1.2f 정도로 상향 (0.5f 바닥 기준 넉넉히 위)
        return transform.position + new Vector3(chosen.x, 1.2f, chosen.y);
    }

    void GenerateMapAndSync(int seed)
    {
        _mapRandom = new SeedRandom(seed);

        int safetyNet = 0; // 무한 루프 방지용
        bool isValidMap = false;

        while (!isValidMap && safetyNet < 100)
        {
            safetyNet++;
            // 1. 데이터 초기화
            mapData = new TileType[width, height];
            for (int x = 0; x < width; ++x)
                for (int y = 0; y < height; ++y)
                    mapData[x, y] = TileType.Empty;

            // 2. 랜덤 배치
            PlaceObjectsRandomly();

            // 3. 모든 빈 공간 연결 확인
            if (IsMapConnected()) isValidMap = true;
        }

        if (!isValidMap) Debug.LogWarning("[MapManager] 연결된 맵 생성 실패");

        // 4. 생성된 맵을 특수 문자열로 암호화 (Encode)
        char[] arr = new char[width * height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                arr[x * height + y] = (char)('A' + (int)mapData[x, y]);
            }
        }
        SyncedMapData = new string(arr);
        Debug.Log($"🗺️ [MapGen] 방장이 맵 인코딩 및 동기화 완료! 배열 변환 ({width}x{height})");
    }

    void ApplyMapDataAndDraw(string mapStr)
    {
        // ── [추가] 클라이언트도 시드 기반 난수 생성기를 초기화하여 벽 장식 위치를 동기화합니다. ──
        if (_mapRandom == null || _mapRandom != null) // 기존 객체 교체
        {
            _mapRandom = new SeedRandom(currentSeed);
            Debug.Log($"🗺️ [MapGen] 클라이언트 맵 생성 시드 적용: {currentSeed}");
        }

        mapData = new TileType[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                mapData[x, y] = (TileType)(mapStr[x * height + y] - 'A');
            }
        }

        // 압축이 풀린 타일을 기반으로 맵 화면에 그리기 시작
        DrawMap();

        // 5. [검증용] 생성된 맵 해시값 출력 (1P와 2P가 같은지 확인용)
        int mapHash = 0;
        int entityCount = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                mapHash += (x * 100 + y) * (int)mapData[x, y];
                if (mapData[x, y] != TileType.Empty) entityCount++;
            }
        }
        Debug.Log($"🗺️ [MapGen 검증] 적용 완료된 맵 크기({width}x{height}), 스테이션 수({entityCount}) => 고유 해시값: {mapHash} (이제 클라이언트는 독자계산을 하지 않아 완벽히 일치합니다!)");
    }

    // 모든 빈 타일이 서로 연결되어 있는지 확인하는 함수 (Flood Fill / BFS)
    bool IsMapConnected()
    {
        List<Vector2Int> allEmptyTiles = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (mapData[x, y] == TileType.Empty)
                    allEmptyTiles.Add(new Vector2Int(x, y));
            }
        }

        if (allEmptyTiles.Count == 0) return false;

        // BFS로 연결된 칸 찾기
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(allEmptyTiles[0]);
        visited.Add(allEmptyTiles[0]);

        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();

            for (int i = 0; i < 4; i++)
            {
                int nx = curr.x + dx[i];
                int ny = curr.y + dy[i];

                Vector2Int neighbor = new Vector2Int(nx, ny);
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    if (mapData[nx, ny] == TileType.Empty && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        // 방문한 빈 칸의 수가 전체 빈 칸의 수와 같으면 모든 길이 연결된 거야!
        return visited.Count == allEmptyTiles.Count;
    }

    // 캐릭터가 상자들이 많은 쪽을 바라보게 하는 함수
    public Quaternion GetLookRotation(Vector2Int pos)
    {
        if (mapData == null) return Quaternion.identity;
        Vector2Int[] dirs = { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(-1, 0), new Vector2Int(1, 0) };
        List<Vector3> objectDirections = new();
        List<Vector3> emptyDirections = new();

        foreach (var d in dirs)
        {
            int nx = pos.x + d.x; int ny = pos.y + d.y;
            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
            {
                Vector3 worldDir = new Vector3(d.x, 0f, d.y);
                if (mapData[nx, ny] != TileType.Empty) objectDirections.Add(worldDir);
                else emptyDirections.Add(worldDir);
            }
        }

        Vector3 finalDir = Vector3.back;

        if (objectDirections.Count > 0)
        {
            // [수정] 시드 기반 난수 사용 (기존: UnityEngine.Random)
            int rotIdx = (_mapRandom != null) ? _mapRandom.Next(0, objectDirections.Count) : UnityEngine.Random.Range(0, objectDirections.Count);
            finalDir = objectDirections[rotIdx];
        }
        else if (emptyDirections.Count > 0)
        {
            // [수정] 시드 기반 난수 사용 (기존: UnityEngine.Random)
            int rotIdx = (_mapRandom != null) ? _mapRandom.Next(0, emptyDirections.Count) : UnityEngine.Random.Range(0, emptyDirections.Count);
            finalDir = emptyDirections[rotIdx];
        }

        return Quaternion.LookRotation(finalDir);
    }


    void PlaceObjectsRandomly()
    {
        int currentStage = 1;
        if (SystemManager.Instance != null && SystemManager.Instance.SelectedStage != 0)
        {
            currentStage = SystemManager.Instance.SelectedStage;
        }

        List<TileType> objectsToPlace = new();

        switch (currentStage)
        {
            case 1: // 1~4 스테이지 공통 설정
            case 2:
            case 3:
            case 4:
                for (int i = 0; i < 6; i++) objectsToPlace.Add(TileType.General);
                objectsToPlace.Add(TileType.MandragoraBox);
                objectsToPlace.Add(TileType.BaconBox);
                objectsToPlace.Add(TileType.TomatoBox);
                objectsToPlace.Add(TileType.CabbageBox);
                objectsToPlace.Add(TileType.CheeseBox);
                objectsToPlace.Add(TileType.ClamBox);
                break;

            case 5: // 5~8 스테이지 공통 설정
            case 6:
            case 7:
            case 8:
                for (int i = 0; i < 6; i++) objectsToPlace.Add(TileType.General);
                objectsToPlace.Add(TileType.MandragoraBox);
                objectsToPlace.Add(TileType.BaconBox);
                objectsToPlace.Add(TileType.TomatoBox);
                objectsToPlace.Add(TileType.CabbageBox);
                objectsToPlace.Add(TileType.CheeseBox);
                objectsToPlace.Add(TileType.ClamBox);
                objectsToPlace.Add(TileType.MushroomBox);
                objectsToPlace.Add(TileType.OnionBox);
                objectsToPlace.Add(TileType.FishBox);
                objectsToPlace.Add(TileType.PumpkinBox);
                break;

            case 9: // 9~12 스테이지 공통 설정
            case 10:
            case 11:
            case 12:
                for (int i = 0; i < 6; i++) objectsToPlace.Add(TileType.General);
                objectsToPlace.Add(TileType.MandragoraBox);
                objectsToPlace.Add(TileType.BaconBox);
                objectsToPlace.Add(TileType.TomatoBox);
                objectsToPlace.Add(TileType.CabbageBox);
                objectsToPlace.Add(TileType.CheeseBox);
                objectsToPlace.Add(TileType.ClamBox);
                objectsToPlace.Add(TileType.MushroomBox);
                objectsToPlace.Add(TileType.OnionBox);
                objectsToPlace.Add(TileType.FishBox);
                objectsToPlace.Add(TileType.PumpkinBox);
                objectsToPlace.Add(TileType.EggBox);
                objectsToPlace.Add(TileType.MeatBox);
                // objectsToPlace.Add(TileType.DoughBox);
                // objectsToPlace.Add(TileType.NoodleBox);
                break;

            default: // 기본값 (기본 리스트 보존)
                objectsToPlace.AddRange(new[] {
                    TileType.General, TileType.General, TileType.General, TileType.General,
                    TileType.General, TileType.General, TileType.General,
                    TileType.CuttingBoard, TileType.Fireplace, TileType.Slime,
                    TileType.Submission, TileType.TrashBin, TileType.Pot,
                    TileType.TomatoBox, TileType.ClamBox, TileType.NoodleBox, TileType.MandragoraBox,
                    TileType.CabbageBox, TileType.CheeseBox, TileType.BaconBox, TileType.EggBox,
                    TileType.MushroomBox, TileType.PumpkinBox, TileType.OnionBox, TileType.FishBox,
                    TileType.MeatBox, TileType.DoughBox, TileType.IngredientBox
                });
                break;
        }

        // 💡 [모든 스테이지 공통] 필수 조리기구 자동 추가 🛠️
        objectsToPlace.Add(TileType.CuttingBoard);
        objectsToPlace.Add(TileType.Slime);
        objectsToPlace.Add(TileType.Submission);
        objectsToPlace.Add(TileType.Fireplace);
        objectsToPlace.Add(TileType.TrashBin);
        objectsToPlace.Add(TileType.Pot);

        for (int i = 0; i < objectsToPlace.Count; ++i)
        {
            TileType temp = objectsToPlace[i];
            int randomIndex = _mapRandom.Next(i, objectsToPlace.Count);
            objectsToPlace[i] = objectsToPlace[randomIndex];
            objectsToPlace[randomIndex] = temp;
        }


        while (objectsToPlace.Count > 0)
        {
            int clusterSize = _mapRandom.Next(2, 6);
            if (clusterSize > objectsToPlace.Count) clusterSize = objectsToPlace.Count;

            // 새로운 섬의 시작점 찾기
            Vector2Int currentPos = GetIsolatedEmptyPos();

            if (currentPos.x == -1 || currentPos.y == -1)
            {
                Debug.LogWarning("[MapManager] 더 이상 배치할 수 있는 공간이 없습니다.");
                break;
            }

            // ㄱ,ㄴ,ㄷ 루프
            for (int i = 0; i < clusterSize; ++i)
            {
                if (objectsToPlace.Count == 0) break;

                TileType obj = objectsToPlace[0];
                mapData[currentPos.x, currentPos.y] = obj;
                objectsToPlace.RemoveAt(0);

                List<Vector2Int> nextNeighbors = GetNeighborSnake(currentPos.x, currentPos.y);

                if (nextNeighbors.Count > 0)
                {
                    currentPos = nextNeighbors[_mapRandom.Next(0, nextNeighbors.Count)];
                }
                else
                {
                    break;
                }
            }
        }

    }

    Vector2Int GetIsolatedEmptyPos()
    {
        for (int i = 0; i < 150; ++i)
        {
            int rx = _mapRandom.Next(0, width);
            int ry = _mapRandom.Next(0, height);

            if (mapData[rx, ry] == TileType.Empty && IsSurroundingEmpty(rx, ry))
            {
                return new Vector2Int(rx, ry);
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (mapData[x, y] == TileType.Empty)
                {
                    return new Vector2Int(x, y);
                }
            }
        }

        return new Vector2Int(-1, -1);
    }

    bool IsSurroundingEmpty(int x, int y)
    {
        for (int i = -1; i <= 1; ++i)
        {
            for (int j = -1; j <= 1; ++j)
            {
                if (i == 0 && j == 0) continue;

                int nx = x + i;
                int ny = y + j;

                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    if (mapData[nx, ny] != TileType.Empty)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    List<Vector2Int> GetNeighborSnake(int x, int y)
    {
        List<Vector2Int> list = new();
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; ++i)
        {
            int nx = x + dx[i];
            int ny = y + dy[i];

            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
            {
                if (mapData[nx, ny] == TileType.Empty && !Create2x2(nx, ny) && IsAccessible(nx, ny))
                {
                    list.Add(new Vector2Int(nx, ny));
                }
            }
        }

        return list;

    }

    // 특정 타일 주변에 (내가 곧 채울 x, y를 제외하고) 빈 공간이 있는지 확인하는 보조 함수
    bool HasEmptyNeighbor(int tx, int ty, int skipX = -1, int skipY = -1)
    {
        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; i++)
        {
            int nx = tx + dx[i];
            int ny = ty + dy[i];

            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
            {
                // 내가 곧 물건을 놓을 자리(skipX, skipY)를 제외하고 빈 공간이 있다면 ok!
                if (nx == skipX && ny == skipY) continue;
                if (mapData[nx, ny] == TileType.Empty) return true;
            }
        }
        return false;
    }

    // ㄴ, 십자가, 구석에 갇혀서 플레이어가 상호작용 못하는 상황 막는 함수
    bool IsAccessible(int x, int y)
    {
        if (!HasEmptyNeighbor(x, y)) return false;

        int[] dx = { -1, 1, 0, 0 };
        int[] dy = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; ++i)
        {
            int nx = x + dx[i];
            int ny = y + dy[i];

            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
            {
                if (mapData[nx, ny] != TileType.Empty)
                {
                    if (!HasEmptyNeighbor(nx, ny, x, y))
                    {
                        return false;
                    }
                }
            }
        }
        return true;

    }



    // 고립방지하는 함수
    bool Create2x2(int x, int y)
    {
        // 우상단
        if (Check(x + 1, y) && Check(x, y + 1) && Check(x + 1, y + 1)) return true;

        // 우하단 검사
        if (Check(x + 1, y) && Check(x, y - 1) && Check(x + 1, y - 1)) return true;

        // 좌하단 검사
        if (Check(x - 1, y) && Check(x, y - 1) && Check(x - 1, y - 1)) return true;

        // 좌상단 검사
        if (Check(x - 1, y) && Check(x, y + 1) && Check(x - 1, y + 1)) return true;

        return false;
    }

    bool Check(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            return false;
        }

        return mapData[x, y] != TileType.Empty;
    }

    void CreateFloor(float margin)
    {
        if (floorPrefab == null) return;

        float centerX = (width - 1) / 2f;
        float centerY = (height - 1) / 2f;
        float floorThickness = 0.5f;

        GameObject floor = Instantiate(floorPrefab, transform.position + new Vector3(centerX, floorThickness / 2f, centerY), Quaternion.identity, transform);

        // 여유 공간(margin)을 포함한 전체 크기 계산
        float scaleX = width + (margin * 2);
        float scaleZ = height + (margin * 2);

        // 프리팹의 기본 스케일을 고려하여 스케일 적용
        Vector3 baseScale = floorPrefab.transform.localScale;
        floor.transform.localScale = new Vector3(scaleX * baseScale.x, floorThickness * baseScale.y, scaleZ * baseScale.z);
    }

    void CreateCeiling(float margin)
    {
        if (ceilingPrefab == null) return;

        float centerX = (width - 1) / 2f;
        float centerY = (height - 1) / 2f;
        float ceilingThickness = 0.5f;

        // 천장 높이는 wallHeight로 설정하고, 아래를 바라보도록 180도 회전 시킵니다.
        Vector3 ceilingPos = transform.position + new Vector3(centerX, wallHeight, centerY);
        Quaternion ceilingRot = Quaternion.Euler(180, 0, 0); 

        GameObject ceiling = Instantiate(ceilingPrefab, ceilingPos, ceilingRot, transform);

        // 스케일 계산 (바닥과 동일)
        float scaleX = width + (margin * 2);
        float scaleZ = height + (margin * 2);

        Vector3 baseScale = ceilingPrefab.transform.localScale;
        ceiling.transform.localScale = new Vector3(scaleX * baseScale.x, ceilingThickness * baseScale.y, scaleZ * baseScale.z);

        // [중요] 레이어를 "Ceiling"으로 설정 (에디터에서 미리 생성되어 있어야 함)
        int ceilingLayer = LayerMask.NameToLayer("Ceiling");
        if (ceilingLayer != -1) ceiling.layer = ceilingLayer;
    }

    void CreateColumn(Vector3 localPos, float targetHeight)
    {
        if (cornerPrefab == null) return;
        
        // 기둥도 바닥 위(0.5f)에 배치
        GameObject col = Instantiate(cornerPrefab, transform.position + new Vector3(localPos.x, 0.5f, localPos.z), Quaternion.identity, transform);

        // 기둥의 높이를 벽 높이(targetHeight)에 맞게 조정합니다.
        // 프리팹의 기본 스케일을 기반으로 Y축만 targetHeight 상수로 확장 (기본 기둥 높이가 1이라고 가정)
        Vector3 baseScale = cornerPrefab.transform.localScale;
        col.transform.localScale = new Vector3(baseScale.x, targetHeight * baseScale.y, baseScale.z);
    }

    void CreateWall(Vector3 localPos, Vector3 scale, Vector3 rotation)
    {
        // 프리팹의 기본 회전값과 사용자 지정 회전값을 결합합니다.
        Quaternion targetRotation = Quaternion.Euler(rotation);
        
        GameObject wall = Instantiate(wallPrefab, transform.position + localPos, targetRotation, transform);

        // 프리팹 자체에 설정된 기본 스케일(사용자가 1x1x1 큐브에 맞춰둔 설정)을 고려하여 스케일을 적용합니다.
        Vector3 baseScale = wallPrefab.transform.localScale;
        wall.transform.localScale = new Vector3(scale.x * baseScale.x, scale.y * baseScale.y, scale.z * baseScale.z);

    }

    void DrawMap()
    {
        // 🟢 이전 스테이션들 데스폰 처리 (중복 생성 방지)
        if (Runner != null && Runner.IsRunning && HasStateAuthority)
        {
            foreach (var station in _spawnedStations)
            {
                if (station != null && station.IsValid)
                    Runner.Despawn(station);
            }
        }
        _spawnedStations.Clear();

        foreach (Transform child in transform)
        {
            var netObj = child.GetComponent<NetworkObject>();
            if (netObj != null && Runner != null && Runner.IsRunning && HasStateAuthority)
            {
                Runner.Despawn(netObj);
            }
            else
            {
                Destroy(child.gameObject);
            }
        }

        _emptyTiles.Clear();
        _tableTiles.Clear();

        float centerX = (width - 1) / 2f;
        float centerY = (height - 1) / 2f;

        float wallT = 5.0f; // 사용자의 요청에 따라 두께를 5.0으로 대폭 강화하여 클리핑 방지
        float actualMargin = Mathf.Max(floorMargin, wallT + 1.0f); // 바닥이 벽보다 좁지 않게 자동 보정

        // 1. Floor & Ceiling (넓은 바닥과 천장 생성)
        CreateFloor(actualMargin);
        CreateCeiling(actualMargin);

        // 2. Wall & Column
        if (wallPrefab != null)
        {
            float wallH = wallHeight; // 전역 설정값 사용

            float leftEdge = -0.5f;
            float rightEdge = width - 0.5f;
            float bottomEdge = -0.5f;
            float topEdge = height - 0.5f;

            // --- 벽 생성 ---
            // 두께(wallT)만큼 길이를 더 늘려 기둥과 완전히 교차하게 만들어 어느 각도에서도 빈틈이 없게 합니다.
            // 아래쪽 벽 (Z+가 안쪽을 보도록)
            CreateWall(new Vector3(centerX, 0.5f, bottomEdge - wallT / 2f),
                       new Vector3(width + wallT, wallH, wallT),
                       new Vector3(0, 0, 0));

            // 위쪽 벽 (180도 회전)
            CreateWall(new Vector3(centerX, 0.5f, topEdge + wallT / 2f),
                       new Vector3(width + wallT, wallH, wallT),
                       new Vector3(0, 180, 0));

            // 왼쪽 벽 (90도 회전)
            CreateWall(new Vector3(leftEdge - wallT / 2f, 0.5f, centerY),
                       new Vector3(height + wallT, wallH, wallT),
                       new Vector3(0, 90, 0));

            // 오른쪽 벽 (270도 회전)
            CreateWall(new Vector3(rightEdge + wallT / 2f, 0.5f, centerY),
                       new Vector3(height + wallT, wallH, wallT),
                       new Vector3(0, 270, 0));

            // --- 모서리 기둥 생성 (Column_01) ---
            // 기둥을 벽의 두께 중심과 일치시켜서 코너를 깔끔하게 덮도록 이동합니다.
            float colX_L = leftEdge - wallT / 2f;
            float colX_R = rightEdge + wallT / 2f;
            float colZ_B = bottomEdge - wallT / 2f;
            float colZ_T = topEdge + wallT / 2f;

            CreateColumn(new Vector3(colX_L, 0.5f, colZ_B), wallH);
            CreateColumn(new Vector3(colX_R, 0.5f, colZ_B), wallH);
            CreateColumn(new Vector3(colX_L, 0.5f, colZ_T), wallH);
            CreateColumn(new Vector3(colX_R, 0.5f, colZ_T), wallH);

            // [추가] 벽 장식(헤드셋, 촛불) 랜덤 배치
            PlaceWallDecorations();
        }

        // 3. TileType
        for (int x = 0; x < width; ++x)
        {
            for (int y = 0; y < height; ++y)
            {
                // [추가] 네 모서리 타일은 기둥과 겹치지 않도록 스폰 목록에서 제외합니다.
                bool isCorner = (x == 0 && y == 0) || (x == width - 1 && y == 0) || 
                                (x == 0 && y == height - 1) || (x == width - 1 && y == height - 1);
                
                TileType currentTile = mapData[x, y];
                
                float spawnY = globalSpawnHeight; 
                
                // 조리대 류는 피벗 위치에 따라 보정이 필요하므로 globalSpawnHeight 적용
                switch (currentTile)
                {
                    case TileType.General:
                    case TileType.CuttingBoard:
                    case TileType.Fireplace:
                    case TileType.Slime:
                    case TileType.Submission:
                    case TileType.TrashBin:
                    case TileType.Pot:
                        spawnY = globalSpawnHeight;
                        break;
                }

                Vector3 position = transform.position + new Vector3(x, spawnY, y);

                if (currentTile == TileType.Empty)
                {
                    if (!isCorner) _emptyTiles.Add(new Vector2Int(x, y));
                    continue;
                }
                else if (currentTile == TileType.General)
                {
                    if (!isCorner) _tableTiles.Add(new Vector2Int(x, y));
                }

                NetworkObject prefabToSpawn = GetNetworkPrefab(currentTile);

                if (prefabToSpawn != null)
                {
                    // 네트워크가 활성화된 경우
                    if(Runner != null && Runner.IsRunning)
                    {
                        // [수정] 방장(MasterClient) 여부가 아니라 이 객체의 상태 권한(HasStateAuthority)을 가진 플레이어가 스폰합니다.
                        if (HasStateAuthority)
                        {
                            var spawned = Runner.Spawn(prefabToSpawn, position, Quaternion.identity);
                            if (spawned != null)
                            {
                                _spawnedStations.Add(spawned);
                                if (currentTile == TileType.Submission)
                                    InjectSubmissionData(spawned.gameObject);
                            }
                        }
                    }
                    else
                    {
                        var go = Instantiate(prefabToSpawn.gameObject, position, Quaternion.identity, transform);
                        if (currentTile == TileType.Submission)
                            InjectSubmissionData(go);
                    }
                }
            }
        }

        // 4. 조리도구 (toolPrefabs) — 무작위 테이블에 배치
        if (Object == null || !Runner.IsRunning || HasStateAuthority)
        {
            SpawnPickupItems(toolPrefabs);
        }

        // 5. 특수 아이템 (specialItemPrefabs) — 무작위 테이블에 배치
        if (Object == null || !Runner.IsRunning || HasStateAuthority)
        {
            SpawnPickupItems(specialItemPrefabs);
        }

        // 6. 하수구 (drainPrefab) — 무작위 바닥에 배치
        PlaceDrains();
    }

    /// <summary>
    /// 빈 바닥 타일 중 무작위 2곳에 하수구 프리팹을 배치합니다. (모서리 및 프리팹 타일 제외)
    /// </summary>
    private void PlaceDrains()
    {
        if (drainPrefab == null || _emptyTiles.Count == 0) return;

        int drainsToPlace = 2;
        int placedCount = 0;
        int attempts = 0;

        while (placedCount < drainsToPlace && _emptyTiles.Count > 0 && attempts < 10)
        {
            attempts++;
            int idx = _mapRandom.Next(0, _emptyTiles.Count);
            Vector2Int tile = _emptyTiles[idx];

            // 사용자가 설정한 drainHeight를 사용하여 배치 (미세 조정 가능하도록)
            Vector3 pos = transform.position + new Vector3(tile.x, drainHeight, tile.y);
            
            Instantiate(drainPrefab, pos, Quaternion.identity, transform);
            
            // 중복 배치 방지 위해 리스트에서 제거
            _emptyTiles.RemoveAt(idx);
            placedCount++;
        }
    }

    /// <summary>
    /// prefab 배열의 각 항목을 '테이블(General)' 위 무작위 위치에 1개씩 스폰합니다.
    /// </summary>
    private void SpawnPickupItems(NetworkObject[] prefabs)
    {
        if (prefabs == null || prefabs.Length == 0) return;

        foreach (var prefab in prefabs)
        {
            if (prefab == null || _tableTiles.Count == 0) continue;

            // 목록 중 무작위 테이블 위치를 하나 뽑습니다.
            int idx = _mapRandom.Next(0, _tableTiles.Count);
            Vector2Int tableTile = _tableTiles[idx];
            
            // 바닥(0.5f)이 아니라 테이블 표면 살짝 위(약 1.0f ~ 1.5f)에 생성.
            Vector3 pos = transform.position + new Vector3(tableTile.x, 1.5f, tableTile.y);
            
            // 배치된 테이블은 리스트에서 제거하여 중복 배치 방지
            _tableTiles.RemoveAt(idx);

            if (Runner != null && Runner.IsRunning)
            {
                // [수정] 방장(MasterClient) 여부가 아니라 이 객체의 상태 권한(HasStateAuthority)을 가진 플레이어가 스폰합니다.
                if (HasStateAuthority)
                {
                    var spawned = Runner.Spawn(prefab, pos, Quaternion.identity);
                    if (spawned != null)
                    {
                        _spawnedStations.Add(spawned);

                        // ── ITool 즉석 리스폰을 위해 해당 테이블에 프리팹 등록 ──
                        // prefab의 PickableItem이 ITool인 경우에만 적용
                        bool isToolOrPlate = prefab.GetComponent<ITool>() != null
                                   || prefab.GetComponentInChildren<ITool>() != null
                                   || prefab.GetComponentInChildren<PlateItem>() != null;
                                Vector3 stationWorldPos = transform.position + new Vector3(tableTile.x, 0.5f, tableTile.y);
                                foreach (var station in _spawnedStations)
                                {
                                    if (station == null || !station.IsValid) continue;
                                    var gs = station.GetComponent<GeneralStation>();
                                    if (gs == null) continue;
                                    // 위치가 같은 테이블인지 XZ 거리로 판단
                                    Vector3 diff = station.transform.position - stationWorldPos;
                                    if (Mathf.Abs(diff.x) < 0.5f && Mathf.Abs(diff.z) < 0.5f)
                                    {
                                        gs.RegisterToolPrefab(prefab);
                                        break;
                                    }
                                }
                    }
                }
            }
            else
            {
                Instantiate(prefab.gameObject, pos, Quaternion.identity, transform);
            }
        }
    }

    private NetworkObject GetNetworkPrefab(TileType type)
    {
        return type switch
        {
            // 조리기구
            TileType.General        => generalPrefab,
            TileType.CuttingBoard   => cuttingBoardPrefab,
            TileType.Fireplace      => fireplacePrefab,
            TileType.Slime           => slimePrefab,
            TileType.Submission     => submissionPrefab,
            TileType.TrashBin       => trashBinPrefab,
            TileType.Pot            => potPrefab,

            // 재료 디스펜서
            TileType.TomatoBox      => tomatoBoxPrefab,
            TileType.ClamBox        => clamBoxPrefab,
            TileType.NoodleBox      => noodleBoxPrefab,
            TileType.MandragoraBox  => mandragoraBoxPrefab,
            TileType.CabbageBox     => cabbageBoxPrefab,
            TileType.CheeseBox      => cheeseBoxPrefab,
            TileType.BaconBox       => baconBoxPrefab,
            TileType.EggBox         => eggBoxPrefab,
            TileType.MushroomBox    => mushroomBoxPrefab,
            TileType.PumpkinBox     => pumpkinBoxPrefab,
            TileType.OnionBox       => onionBoxPrefab,
            TileType.FishBox        => fishBoxPrefab,
            TileType.MeatBox        => meatBoxPrefab,
            TileType.DoughBox       => doughBoxPrefab,
            TileType.IngredientBox  => ingredientBoxPrefab,

            _ => null
        };
    }

    // --- 벽 장식 배치 로직 ---
    private void PlaceWallDecorations()
    {
        if (!hookNetworkPrefab.IsValid && candlePrefab == null) return;
        if (_mapRandom == null) return; // 비방장은 _mapRandom이 초기화되지 않음

        // 4개 벽면의 가용 타일 수집 (모서리 제외)
        List<Vector2Int>[] sides = new List<Vector2Int>[4];
        for (int i = 0; i < 4; i++) sides[i] = new List<Vector2Int>();

        for (int x = 1; x < width - 1; x++)
        {
            sides[0].Add(new Vector2Int(x, 0)); // Bottom
            sides[1].Add(new Vector2Int(x, height - 1)); // Top
        }
        for (int y = 1; y < height - 1; y++)
        {
            sides[2].Add(new Vector2Int(0, y)); // Left
            sides[3].Add(new Vector2Int(width - 1, y)); // Right
        }

        int[] totalItemsPerSide = new int[4]; // 해당 면에 설치된 총 아이템 수 (헤드셋 + 촛불)
        int maxItemsPerSide = 2; // 면당 최대 2개로 제한 (균등 배분용)

        // --- 1. 거치대 배치 (무조건 1개, NetworkObject — SpecialEquipmentHolder가 헤드셋 자동 스폰) ---
        if (hookNetworkPrefab.IsValid && HasStateAuthority)
        {
            int sideIdx = _mapRandom.Next(0, 4);
            if (sides[sideIdx].Count > 0)
            {
                int tileIdx = _mapRandom.Next(0, sides[sideIdx].Count);
                Vector2Int pos = sides[sideIdx][tileIdx];

                SpawnNetworkOnWall(hookNetworkPrefab, pos, sideIdx, headsetHeight);

                sides[sideIdx].RemoveAt(tileIdx);
                totalItemsPerSide[sideIdx]++;
            }
        }

        // --- 2. 촛불 배치 (면당 2개씩 균등 고정 배치) ---
        if (candlePrefab != null)
        {
            for (int sideIdx = 0; sideIdx < 4; sideIdx++)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (sides[sideIdx].Count == 0) break;
                    int tileIdx = _mapRandom.Next(0, sides[sideIdx].Count);
                    SpawnOnWall(candlePrefab, sides[sideIdx][tileIdx], sideIdx, candleHeight);
                    sides[sideIdx].RemoveAt(tileIdx);
                }
            }
        }
    }

    private GameObject SpawnOnWall(GameObject prefab, Vector2Int tilePos, int sideIdx, float heightOffset)
    {
        Vector3 spawnPos = new Vector3(tilePos.x, heightOffset, tilePos.y);
        Vector3 rotation = Vector3.zero;
        float offset = 2.3f;

        switch (sideIdx)
        {
            case 0: spawnPos.z -= offset; rotation = new Vector3(0, 0, 0); break;
            case 1: spawnPos.z += offset; rotation = new Vector3(0, 180, 0); break;
            case 2: spawnPos.x -= offset; rotation = new Vector3(0, 90, 0); break;
            case 3: spawnPos.x += offset; rotation = new Vector3(0, 270, 0); break;
        }

        return Instantiate(prefab, spawnPos, Quaternion.Euler(rotation), transform);
    }

    // 네트워크 오브젝트 전용 스폰 로직 (방장 전용)
    private void SpawnNetworkOnWall(NetworkPrefabRef prefabRef, Vector2Int tilePos, int sideIdx, float height)
    {
        Vector3 spawnPos = new Vector3(tilePos.x, height, tilePos.y);
        Vector3 rotation = Vector3.zero;
        float offset = 2.3f;

        switch (sideIdx)
        {
            case 0: spawnPos.z -= offset; rotation = new Vector3(0, 0, 0); break;
            case 1: spawnPos.z += offset; rotation = new Vector3(0, 180, 0); break;
            case 2: spawnPos.x -= offset; rotation = new Vector3(0, 90, 0); break;
            case 3: spawnPos.x += offset; rotation = new Vector3(0, 270, 0); break;
        }

        Runner.Spawn(prefabRef, spawnPos, Quaternion.Euler(rotation));
    }
}
