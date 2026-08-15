using UnityEditor;
using UnityEngine;

public class CookingMasterSetupBuilder : EditorWindow
{
    [MenuItem("Tools/Create Chef Player")]
    public static void CreatePlayer()
    {
        // 1. Create Player Base
        GameObject player = new GameObject("Player 2");
        player.transform.position = new Vector3(7.5f, 1f, 7.5f); // 방 한가운데 근처 배치
        player.tag = "Player";

        // 2. Add Character Controller
        CharacterController cc = player.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.center = new Vector3(0, 1f, 0);

        // 3. Add Movement Script
        player.AddComponent<CookingMasterMovement>();

        // 4. Create Camera
        GameObject camObj = new GameObject("FirstPersonCamera");
        camObj.transform.SetParent(player.transform);
        camObj.transform.localPosition = new Vector3(0, 1.6f, 0); // 눈높이

        Camera cam = camObj.AddComponent<Camera>();
        camObj.AddComponent<AudioListener>();

        // 5. Add Look Script
        CookingMasterCamera fpCam = camObj.AddComponent<CookingMasterCamera>();
        fpCam.playerBody = player.transform;

        // 6. Create Interaction Origin (Hands)
        GameObject handsObj = new GameObject("Hands_InteractionPoint");
        handsObj.transform.SetParent(camObj.transform);
        handsObj.transform.localPosition = new Vector3(0.0f, -0.3f, 0.7f);

        GameObject leftHand = new GameObject("LeftHandPoint");
        leftHand.transform.SetParent(handsObj.transform);
        leftHand.transform.localPosition = new Vector3(-0.4f, 0, 0);

        GameObject rightHand = new GameObject("RightHandPoint");
        rightHand.transform.SetParent(handsObj.transform);
        rightHand.transform.localPosition = new Vector3(0.4f, 0, 0);

        // 7. Add Hands Manager
        CookingMasterHandsManager handsManager = player.AddComponent<CookingMasterHandsManager>();
        handsManager.mainCamera = cam;
        handsManager.leftHandPoint = leftHand.transform;
        handsManager.rightHandPoint = rightHand.transform;

        // "Interactable" 이라는 레이어가 있다면 수동으로 할당해야 하지만 임시로 Default Layer 포함 설정
        handsManager.interactableLayer = LayerMask.GetMask("Default", "Interactable");

        // 기존 Main Camera 삭제 (오디오 리스너 충돌 방지)
        GameObject mainCam = GameObject.Find("Main Camera");
        if (mainCam != null && mainCam != camObj)
        {
            DestroyImmediate(mainCam);
        }

        Selection.activeGameObject = player;
        Debug.Log("First Person Player created successfully!");
    }
}