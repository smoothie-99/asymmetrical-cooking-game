using UnityEngine;
using Fusion;

/// <summary>
/// 2P (Recipe Master) 전용 컨트롤러
/// 카메라 위치와 회전은 인스펙터에서 고정 설정합니다.
/// </summary>
public class RecipeMasterController : NetworkBehaviour
{
    [Header("Components")]
    [SerializeField] private Camera recipeCamera;

    [Header("Top View Camera Settings")]
    public float cameraHeight = 15f;
    public Vector2 cameraOffset = Vector2.zero; // x축, z축 시점 위치 세부 조정 가능

    void Start()
    {
        // 샌드박스용: 런너가 없으면 TopCamera 자동 비활성화
        bool isSandbox = (Runner == null || !Runner.IsRunning);
        if (isSandbox)
        {
            if (recipeCamera != null) recipeCamera.gameObject.SetActive(false);
            else
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) cam.gameObject.SetActive(false);
            }
        }
    }

    public override void Spawned()
    {
        // 최신 카메라 컴포넌트 참조 확보
        if (recipeCamera == null) recipeCamera = GetComponentInChildren<Camera>();

        // 내 클라이언트(내가 조종하는 2P)인 경우에만 카메라 활성화
        if (Object.HasStateAuthority)
        {
            if (recipeCamera != null)
            {
                recipeCamera.gameObject.SetActive(true);
                recipeCamera.enabled = true;

                // [추가] 1. 카메라를 플레이어 오브젝트의 종속 관계에서 해제
                recipeCamera.transform.SetParent(null);

                // [추가] 2. 맵의 중앙 계산 및 오프셋 적용
                float centerX = 3.5f; // 기본값
                float centerY = 3.5f;
                MapGenerator mapGen = FindObjectOfType<MapGenerator>();
                Vector3 basePosition = Vector3.zero;

                if (mapGen != null)
                {
                    centerX = (mapGen.width - 1) / 2f;
                    centerY = (mapGen.height - 1) / 2f;
                    basePosition = mapGen.transform.position; // 맵의 원점 오프셋 반영
                }

                // [추가] 3. 유니티 Inspector에서 설정한 값을 반영하여 카메라 위치 고정
                recipeCamera.transform.position = basePosition + new Vector3(centerX + cameraOffset.x, cameraHeight, centerY + cameraOffset.y);
                recipeCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 수직 하향 (Top-Down)

                // 오디오 리스너 관리: 중복 리스너 비활성화
                AudioListener myListener = recipeCamera.GetComponent<AudioListener>();
                if (myListener != null)
                {
                    myListener.enabled = true;
                    AudioListener[] allListeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var listener in allListeners)
                    {
                        if (listener != myListener)
                        {
                            listener.enabled = false;
                            Debug.Log($"🔇 [RecipeMaster] 중복 오디오 리스너 정리: {listener.gameObject.name}");
                        }
                    }
                }
            }

            // 2P는 마우스 커서가 자유로운 상태여야 함
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Debug.Log("✅ [RecipeMaster] 카메라 고정 위치 적용 완료.");
        }
        else
        {
            // 타인 화면에서는 2P 카메라 비활성화
            if (recipeCamera != null)
            {
                recipeCamera.gameObject.SetActive(false);
                recipeCamera.enabled = false;
            }
            Debug.Log("🚫 [RecipeMaster] 타인 캐릭터이므로 카메라를 비활성화합니다.");
        }
    }
}
