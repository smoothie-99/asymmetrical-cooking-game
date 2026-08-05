#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// DicedClamMeatItem 프리팹의 큐브 군집 배치 도우미.
/// DicedClamMeatItem 오브젝트를 선택한 뒤
/// 메뉴 Tools > Setup Diced Clam Meat 를 실행하면
/// 첫 번째 자식 메시를 기준으로 큐브 군집을 자동 생성합니다.
/// </summary>
public static class DicedClamMeatSetupEditor
{
    // 배치할 큐브 위치 (로컬 좌표, 단위: m)
    // 2×2 하단층 + 1×2 상단층 → 자연스러운 덩어리 느낌
    private static readonly Vector3[] Positions = new Vector3[]
    {
        // 하단층 (y = 0)
        new Vector3(-0.04f,  0.00f, -0.04f),
        new Vector3( 0.04f,  0.00f, -0.04f),
        new Vector3(-0.04f,  0.00f,  0.04f),
        new Vector3( 0.04f,  0.00f,  0.04f),
        // 상단층 (y = 0.04) — 약간 어긋나게
        new Vector3( 0.00f,  0.04f, -0.04f),
        new Vector3( 0.00f,  0.04f,  0.04f),
    };

    // 각 큐브에 줄 약간의 랜덤 회전 범위(도)
    private const float RotationJitter = 12f;
    // 각 큐브의 스케일
    private static readonly Vector3 CubeScale = new Vector3(0.06f, 0.04f, 0.06f);

    [MenuItem("Tools/Setup Diced Clam Meat")]
    private static void Setup()
    {
        GameObject root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("오류", "DicedClamMeatItem 오브젝트를 먼저 선택하세요.", "확인");
            return;
        }

        if (root.GetComponent<DicedClamMeatItem>() == null)
        {
            EditorUtility.DisplayDialog("오류", "선택한 오브젝트에 DicedClamMeatItem 컴포넌트가 없습니다.", "확인");
            return;
        }

        // 기존 모델(첫 번째 자식) 찾기
        Transform source = null;
        foreach (Transform child in root.transform)
        {
            if (child.GetComponent<MeshRenderer>() != null ||
                child.GetComponentInChildren<MeshRenderer>() != null)
            {
                source = child;
                break;
            }
        }

        if (source == null)
        {
            EditorUtility.DisplayDialog("오류", "메시가 있는 자식 오브젝트를 찾을 수 없습니다.\n먼저 모델 오브젝트를 자식으로 넣어주세요.", "확인");
            return;
        }

        Undo.SetCurrentGroupName("Setup Diced Clam Meat");
        int group = Undo.GetCurrentGroup();

        // 기존 큐브 사본이 있으면 제거 (재실행 대비)
        for (int i = root.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = root.transform.GetChild(i);
            if (child != source && child.name.StartsWith("DiceChunk_"))
                Undo.DestroyObjectImmediate(child.gameObject);
        }

        // source를 [0] 위치로 이동
        Undo.RecordObject(source, "Move Source Chunk");
        source.name            = "DiceChunk_0";
        source.localPosition   = Positions[0];
        source.localEulerAngles = RandomJitter();
        source.localScale      = CubeScale;

        // 나머지 복사
        for (int i = 1; i < Positions.Length; i++)
        {
            GameObject copy = Object.Instantiate(source.gameObject, root.transform);
            Undo.RegisterCreatedObjectUndo(copy, "Create Chunk");
            copy.name            = $"DiceChunk_{i}";
            copy.transform.localPosition   = Positions[i];
            copy.transform.localEulerAngles = RandomJitter();
            copy.transform.localScale      = CubeScale;
        }

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(root);
        Debug.Log($"[DicedClamMeat] 큐브 {Positions.Length}개 배치 완료.");
    }

    [MenuItem("Tools/Setup Diced Clam Meat", validate = true)]
    private static bool Validate()
        => Selection.activeGameObject != null &&
           Selection.activeGameObject.GetComponent<DicedClamMeatItem>() != null;

    private static Vector3 RandomJitter()
        => new Vector3(
            Random.Range(-RotationJitter, RotationJitter),
            Random.Range(0f, 360f),
            Random.Range(-RotationJitter, RotationJitter));
}
#endif
