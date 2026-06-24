/*
 * BatchMaterialUpdater.cs
 *
 * Editor utility to batch update materials on a selected GameObject to render both faces.
 * Use: Tools > Batch Update Materials > Update Materials on Selected GameObject
 */

using UnityEngine;
using UnityEditor;

public class BatchMaterialUpdater
{
    [MenuItem("Tools/Batch Update Materials/Update Materials on Selected GameObject")]
    public static void UpdateGameObjectMaterials()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("No GameObject Selected", "Please select a GameObject in the Hierarchy.", "OK");
            return;
        }

        Renderer[] renderers = selected.GetComponentsInChildren<Renderer>(true);
        int count = 0;
        int total = 0;
        System.Collections.Generic.HashSet<Material> processedMaterials = new System.Collections.Generic.HashSet<Material>();

        EditorUtility.DisplayProgressBar("Updating Materials", "Processing...", 0f);

        foreach (Renderer renderer in renderers)
        {
            if (renderer.sharedMaterials != null)
            {
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat != null && !processedMaterials.Contains(mat))
                    {
                        processedMaterials.Add(mat);
                        total++;
                        
                        if (SetMaterialDoubleSided(mat))
                        {
                            count++;
                        }
                    }
                }
            }
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Update Complete", 
            $"Updated {count} out of {total} unique materials on {selected.name} and its children.\n\n" +
            $"Materials are now set to render both faces (double-sided).", "OK");
    }

    static bool SetMaterialDoubleSided(Material mat)
    {
        if (mat == null) return false;

        Shader shader = mat.shader;
        if (shader == null) return false;

        bool updated = false;
        string shaderName = shader.name;

        // For URP Lit shader - Render Face "Both" = Cull Off = 0
        // 0 = Off (Both/Double-sided)
        // 1 = Front
        // 2 = Back
        if (mat.HasProperty("_CullMode"))
        {
            float currentValue = mat.GetFloat("_CullMode");
            if (currentValue != 0)
            {
                mat.SetFloat("_CullMode", 0); // 0 = Off (Both/Double-sided)
                updated = true;
            }
        }
        // Alternative property name for some shaders
        else if (mat.HasProperty("_Cull"))
        {
            float currentValue = mat.GetFloat("_Cull");
            if (currentValue != 0)
            {
                mat.SetFloat("_Cull", 0); // 0 = Off (Both/Double-sided)
                updated = true;
            }
        }
        // Some shaders use RenderFace property with different values
        // 0 = Front, 1 = Back, 2 = Both
        else if (mat.HasProperty("_RenderFace"))
        {
            float currentValue = mat.GetFloat("_RenderFace");
            if (currentValue != 2)
            {
                mat.SetFloat("_RenderFace", 2); // 2 = Both
                updated = true;
            }
        }
        // For Standard shader (legacy)
        else if (shaderName.Contains("Standard"))
        {
            if (mat.HasProperty("_Cull"))
            {
                mat.SetFloat("_Cull", 0);
                updated = true;
            }
        }

        if (updated)
        {
            EditorUtility.SetDirty(mat);
            Debug.Log($"Updated material '{mat.name}' to render both faces (shader: {shaderName})");
        }
        else
        {
            Debug.LogWarning($"Could not update material '{mat.name}' - shader '{shaderName}' may not support Render Face setting, or it's already set correctly.");
        }

        return updated;
    }
}
