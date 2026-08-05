using UnityEditor;
using UnityEngine;

public class OutlineMaterialGenerator : EditorWindow
{
    [MenuItem("Tools/Generate Outline Materials")]
    public static void GenerateMaterials()
    {
        string maskPath = "Assets/Resources/Materials/OutlineMask.mat";
        string fillPath = "Assets/Resources/Materials/OutlineFill.mat";

        Shader maskShader = Shader.Find("Custom/OutlineMask");
        Shader fillShader = Shader.Find("Custom/OutlineFill");

        if (maskShader != null)
        {
            Material maskMat = new Material(maskShader);
            AssetDatabase.CreateAsset(maskMat, maskPath);
            Debug.Log("Created OutlineMask material.");
        }
        else Debug.LogError("Could not find Custom/OutlineMask shader!");

        if (fillShader != null)
        {
            Material fillMat = new Material(fillShader);
            AssetDatabase.CreateAsset(fillMat, fillPath);
            Debug.Log("Created OutlineFill material.");
        }
        else Debug.LogError("Could not find Custom/OutlineFill shader!");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
