using UnityEditor;
using UnityEngine;

public class PhysicsFixerTool : EditorWindow
{
    [MenuItem("Tools/Fix Props Physics (Convex Colliders)")]
    public static void FixMeshColliders()
    {
        // 씬 내의 모든 PickableItem 찾기
        PickableItem[] items = UnityEngine.Object.FindObjectsByType<PickableItem>(UnityEngine.FindObjectsSortMode.None);
        int count = 0;

        foreach (var item in items)
        {
            // O(n^2)일지라도 에디터 스크립트라 씬 내 오브젝트 대상 무관
            MeshCollider[] mcs = item.GetComponentsInChildren<MeshCollider>(true);
            foreach (MeshCollider mc in mcs)
            {
                if (!mc.convex)
                {
                    // Rigidbody(Dynamic)와 충돌(Collision)하기 위해선 Convex 여야만 합니다
                    mc.convex = true;
                    count++;
                }
            }

            // 추가적으로 Rigidbody가 실수로 Use Gravity가 꺼져있거나 IsKinematic이 켜져있다면 기본값 강제 할당
            Rigidbody rb = item.GetComponent<Rigidbody>();
            if (rb != null && !item.isHeld) 
            {
                rb.useGravity = true;
                rb.isKinematic = false;
            }
        }
        
        Debug.Log($"[Physics Fix] {count}개의 Mesh Collider 상태를 Convex로 강제 변환 완료!");
    }
}
