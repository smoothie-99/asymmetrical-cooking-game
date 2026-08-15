using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class KitchenMockupBuilder : EditorWindow
{
    [MenuItem("Tools/Setup Demo Scene as Kitchen")]
    public static void SetupDemoScene()
    {
        string demoScenePath = "Assets/LowPolyDungeonsLite/Scenes/LowPolyDungeonsLite_Demo.unity";
        string targetScenePath = "Assets/Scenes/KitchenMockup_Main.unity";

        // 1. 유효성 검사
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(demoScenePath) == null)
        {
            Debug.LogError($"에셋 경로를 찾을 수 없습니다: {demoScenePath}. 임포트 상태를 확인해주세요.");
            return;
        }

        // 2. Scenes 폴더가 없다면 생성
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
        {
            AssetDatabase.CreateFolder("Assets", "Scenes");
        }

        // 3. 씬 복사 시도
        if (AssetDatabase.CopyAsset(demoScenePath, targetScenePath))
        {
            Debug.Log($"씬 복사 성공! 원본 보존을 위해 {targetScenePath} 에 복사본을 생성했습니다.");
            
            // 4. 생성한 복사본 씬 열기
            EditorSceneManager.OpenScene(targetScenePath);
            Debug.Log("새로운 주방 씬(KitchenMockup_Main)을 열었습니다. 이제 이곳에 플레이어를 배치해보세요!");
        }
        else
        {
            // 이미 존재할 경우 바로 엽니다
            Debug.LogWarning($"씬이 이미 존재합니다 ({targetScenePath}). 바로 엽니다.");
            EditorSceneManager.OpenScene(targetScenePath);
        }
    }
}
