using UnityEngine;
using Fusion;

/// <summary>
/// [Map_System] Architecture_guide의 Container Pattern에 따라
/// MetaState를 수신하고 [Map_Container] 하위에 맵을 생성/제거합니다.
/// SystemManager.InitializeSystems()에서 mapSystemPrefab으로 인스턴스화됩니다.
/// </summary>
public class MapSystem : MonoBehaviour
{
    [Header("Map Prefabs")]
    [SerializeField] private GameObject testMapPrefab;    // 테스트용 대형 프리팹
    [SerializeField] private MapGenerator mapGeneratorPrefab;

    [Header("Container")]
    [SerializeField] private Transform mapContainer; // [Map_Container] 오브젝트 참조

    private GameObject _currentMap;

    private void OnEnable()
    {
        SystemManager.SubscribeWhenReady(HandleMetaStateChanged);
    }

    private void OnDisable()
    {
        SystemManager.Unsubscribe(HandleMetaStateChanged);
    }

    private void HandleMetaStateChanged(MetaState state)
    {
        switch (state)
        {
            case MetaState.OrderDialogue:
                SpawnMap();
                break;

            case MetaState.Lobby:
            case MetaState.Result:
            case MetaState.ReadyConfirmation:
            case MetaState.StageSelection: // [추가] 라운드 선택 메뉴로 진입할 때도 맵을 제거합니다.
                ClearMap();
                break;
        }
    }

    private void SpawnMap()
    {
        ClearMap();

        Transform parent = mapContainer != null ? mapContainer : transform;
        var runner = NetworkLauncher.Instance?.Runner;

        if (SystemManager.Instance != null && SystemManager.Instance.useTestMap)
        {
            if (testMapPrefab == null)
            {
                Debug.LogWarning("⚠️ [MapSystem] testMapPrefab이 설정되지 않았습니다.");
                return;
            }

            if (runner != null && runner.IsRunning && testMapPrefab.GetComponent<NetworkObject>() != null)
            {
                // 방장(MasterClient) 여부가 아니라 CookingMaster 역할을 선택했는지 1순위로 확인합니다.
                int roleInt = (LobbyPlayer.Local != null) ? LobbyPlayer.Local.SelectedRole : 0;
                
                // LobbyPlayer.Local이 없거나 아직 역할이 안골라졌으면 Fallback 확인
                if (roleInt == 0)
                {
                    roleInt = (int)NetworkLauncher.SelectedJob;
                }

                bool isCookingMaster = (roleInt == 2);
                if (isCookingMaster)
                {
                    // CookingMaster만 Spawn → Fusion이 모든 클라이언트에 자동 복제 (StateAuthority 획득)
                    NetworkObject spawned = runner.Spawn(testMapPrefab, Vector3.zero, Quaternion.identity);
                    if (spawned != null)
                    {
                        spawned.transform.SetParent(parent);
                        _currentMap = spawned.gameObject;
                    }
                }
            }
            else
            {
                // NetworkObject 없거나 네트워크 미연결 시 일반 Instantiate
                _currentMap = Instantiate(testMapPrefab, Vector3.zero, Quaternion.identity, parent);
            }
            Debug.Log("🗺️ [MapSystem] 테스트 맵 생성 완료");
        }
        else
        {
            if (mapGeneratorPrefab == null)
            {
                Debug.LogWarning("⚠️ [MapSystem] mapGeneratorPrefab이 설정되지 않았습니다.");
                return;
            }

            if (runner != null && runner.IsRunning)
            {
                // 방장(MasterClient) 여부가 아니라 CookingMaster 역할을 선택했는지 1순위로 확인합니다.
                int roleInt = (LobbyPlayer.Local != null) ? LobbyPlayer.Local.SelectedRole : 0;

                // LobbyPlayer.Local이 없거나 아직 역할이 안골라졌으면 Fallback 확인
                if (roleInt == 0)
                {
                    roleInt = (int)NetworkLauncher.SelectedJob;
                }

                bool isCookingMaster = (roleInt == 2);
                if (isCookingMaster)
                {
                    MapGenerator generator = runner.Spawn(mapGeneratorPrefab, Vector3.zero, Quaternion.identity).GetComponent<MapGenerator>();
                    if (generator != null)
                    {
                        //generator.Object.transform.SetParent(parent);
                        _currentMap = generator.gameObject;

                    }
                }
            }
            else
            {
                // 로컬 샌드박스 실행 시: Race Condition을 피하기 위해 명시적으로 Request 호출
                MapGenerator generator = Instantiate(mapGeneratorPrefab, parent);
                generator.RequestMapGenerate();
                _currentMap = generator.gameObject;
            }

            Debug.Log("🗺️ [MapSystem] 프로시저럴 맵 생성 완료");
        }
    }

    private void ClearMap()
    {
        var runner = NetworkLauncher.Instance?.Runner;

        // 🟢 [추가] 필드에 굴러다니는 모든 PickableItem 들을 수집하여 데스폰 처리
        // 각 클라이언트가 자신이 권한(StateAuthority)을 가진 오브젝트만 데스폰하게 하여 권한 충돌 방지 및 모든 아이템 완전 제거 보장
        PickableItem[] itemsInWorld = FindObjectsByType<PickableItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var item in itemsInWorld)
        {
            if (item == null) continue;
            
            if (item.Object != null && item.Object.IsValid)
            {
                if (runner != null && runner.IsRunning && item.Object.HasStateAuthority)
                {
                    runner.Despawn(item.Object);
                }
            }
            else if (runner == null || !runner.IsRunning)
            {
                // 네트워크가 오프라인이거나 시작 전 샌드박스일 때
                Destroy(item.gameObject);
            }
        }

        if (_currentMap == null) return;

        NetworkObject netObj = _currentMap.GetComponent<NetworkObject>();
        if (runner != null && runner.IsRunning && netObj != null)
            runner.Despawn(netObj);
        else
            Destroy(_currentMap);

        _currentMap = null;
        Debug.Log("🗺️ [MapSystem] 맵 제거 완료");
    }
}
