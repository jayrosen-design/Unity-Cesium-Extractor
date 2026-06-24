/*
 * MaterialTextureEnhancer.cs
 *
 * Editor utility to enhance texture visibility by removing lighting effects.
 * Sets smoothness and metallic to 0, and optionally switches to unlit shader.
 * Use: Tools > Material Texture Enhancer > Enhance Textures on Selected GameObject
 */

using UnityEngine;
using UnityEditor;

public class MaterialTextureEnhancer : EditorWindow
{
    private bool useUnlitShader = true;
    private bool preserveOriginalShader = false;
    private float emissionIntensity = 1.0f;

    [MenuItem("Tools/Material Texture Enhancer/Enhance Textures on Selected GameObject")]
    public static void ShowWindow()
    {
        GetWindow<MaterialTextureEnhancer>("Material Texture Enhancer");
    }

    [MenuItem("Tools/Material Texture Enhancer/Quick Fix: Make Bright & Flat (Unlit)")]
    public static void QuickFixMaterials()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("No GameObject Selected", "Please select a GameObject in the Hierarchy.", "OK");
            return;
        }

        EnhanceMaterials(selected, true, false, 1.0f); // Switch to unlit, add emission
    }

    void OnGUI()
    {
        GUILayout.Label("Material Texture Enhancer", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "This tool makes textures appear bright, flat, and non-reflective - similar to how they look in Blender.\n\n" +
            "It will:\n" +
            "• Switch to Unlit shader (no lighting effects)\n" +
            "• Set Smoothness to 0 (no shine/reflection)\n" +
            "• Set Metallic to 0 (no metallic reflection)\n" +
            "• Add emission to brighten textures",
            MessageType.Info);
        
        GUILayout.Space(10);

        useUnlitShader = EditorGUILayout.Toggle("Use Unlit Shader (Recommended)", useUnlitShader);
        
        if (!useUnlitShader)
        {
            preserveOriginalShader = EditorGUILayout.Toggle("Preserve Original Shader", preserveOriginalShader);
            EditorGUILayout.HelpBox("If using original shader, materials will still respond to lighting but with reduced smoothness/metallic.", MessageType.Warning);
        }
        else
        {
            GUILayout.Space(5);
            emissionIntensity = EditorGUILayout.Slider("Emission Intensity", emissionIntensity, 0.5f, 2.0f);
            EditorGUILayout.HelpBox("Emission makes textures self-illuminated and bright, unaffected by scene lighting.", MessageType.Info);
        }

        GUILayout.Space(10);

        if (GUILayout.Button("Enhance Materials on Selected GameObject", GUILayout.Height(30)))
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("No GameObject Selected", "Please select a GameObject in the Hierarchy.", "OK");
                return;
            }

            EnhanceMaterials(selected, useUnlitShader, preserveOriginalShader, emissionIntensity);
        }

        GUILayout.Space(10);
        EditorGUILayout.HelpBox("Tip: Use 'Quick Fix' menu option for fastest results (switches to Unlit shader with emission).", MessageType.Info);
    }

    static void EnhanceMaterials(GameObject target, bool switchToUnlit, bool preserveShader, float emissionIntensity = 1.0f)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        int count = 0;
        int total = 0;
        System.Collections.Generic.HashSet<Material> processedMaterials = new System.Collections.Generic.HashSet<Material>();

        EditorUtility.DisplayProgressBar("Enhancing Materials", "Processing...", 0f);

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

                        if (EnhanceMaterial(mat, switchToUnlit, preserveShader, emissionIntensity))
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

        string message = $"Enhanced {count} out of {total} unique materials on {target.name}.\n\n";
        if (switchToUnlit)
        {
            message += "Materials switched to Unlit shader with emission. Textures should now appear bright, flat, and non-reflective.";
        }
        else
        {
            message += "Smoothness and Metallic set to 0. Textures should now appear more vibrant.";
        }

        EditorUtility.DisplayDialog("Enhancement Complete", message, "OK");
    }

    static bool EnhanceMaterial(Material mat, bool switchToUnlit, bool preserveShader, float emissionIntensity = 1.0f)
    {
        if (mat == null) return false;

        Shader originalShader = mat.shader;
        if (originalShader == null) return false;

        bool updated = false;
        string shaderName = originalShader.name;

        // Remove smoothness (makes textures more visible, less reflective)
        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", 0f);
            updated = true;
        }
        else if (mat.HasProperty("_Glossiness"))
        {
            mat.SetFloat("_Glossiness", 0f);
            updated = true;
        }
        else if (mat.HasProperty("_SmoothnessTextureChannel"))
        {
            // Some shaders use smoothness in alpha channel
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            updated = true;
        }

        // Remove metallic (makes textures less reflective)
        if (mat.HasProperty("_Metallic"))
        {
            mat.SetFloat("_Metallic", 0f);
            updated = true;
        }
        else if (mat.HasProperty("_MetallicGlossMap"))
        {
            mat.SetTexture("_MetallicGlossMap", null);
            updated = true;
        }

        // Optionally switch to unlit shader for pure texture display
        if (switchToUnlit)
        {
            // Try URP Unlit shader first, then Built-in Unlit
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                unlitShader = Shader.Find("Unlit/Texture");
            }
            if (unlitShader == null)
            {
                unlitShader = Shader.Find("Unlit/Color");
            }

            if (unlitShader != null)
            {
                // Preserve the main texture
                Texture mainTex = null;
                Color mainColor = Color.white;
                
                if (mat.HasProperty("_MainTex"))
                {
                    mainTex = mat.GetTexture("_MainTex");
                }
                else if (mat.HasProperty("_BaseMap"))
                {
                    mainTex = mat.GetTexture("_BaseMap");
                }
                
                if (mat.HasProperty("_Color"))
                {
                    mainColor = mat.GetColor("_Color");
                }
                else if (mat.HasProperty("_BaseColor"))
                {
                    mainColor = mat.GetColor("_BaseColor");
                }

                mat.shader = unlitShader;

                // Restore main texture
                if (mainTex != null)
                {
                    if (mat.HasProperty("_MainTex"))
                    {
                        mat.SetTexture("_MainTex", mainTex);
                    }
                    else if (mat.HasProperty("_BaseMap"))
                    {
                        mat.SetTexture("_BaseMap", mainTex);
                    }
                }

                // Set color
                if (mat.HasProperty("_Color"))
                {
                    mat.SetColor("_Color", mainColor);
                }
                else if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", mainColor);
                }

                // Add emission for brightness (if shader supports it)
                if (mat.HasProperty("_EmissionColor"))
                {
                    Color emissionColor = mainColor * emissionIntensity;
                    mat.SetColor("_EmissionColor", emissionColor);
                    mat.EnableKeyword("_EMISSION");
                }
                else if (mat.HasProperty("_Emission"))
                {
                    Color emissionColor = mainColor * emissionIntensity;
                    mat.SetColor("_Emission", emissionColor);
                    mat.EnableKeyword("_EMISSION");
                }

                updated = true;
                Debug.Log($"Switched material '{mat.name}' from '{shaderName}' to Unlit shader with emission");
            }
            else
            {
                Debug.LogWarning($"Could not find Unlit shader. Material '{mat.name}' will keep original shader.");
            }
        }
        else
        {
            // Even if not switching shader, try to add emission for brightness
            if (mat.HasProperty("_EmissionColor"))
            {
                Color mainColor = Color.white;
                if (mat.HasProperty("_Color"))
                {
                    mainColor = mat.GetColor("_Color");
                }
                else if (mat.HasProperty("_BaseColor"))
                {
                    mainColor = mat.GetColor("_BaseColor");
                }
                
                Color emissionColor = mainColor * emissionIntensity;
                mat.SetColor("_EmissionColor", emissionColor);
                mat.EnableKeyword("_EMISSION");
                updated = true;
            }
        }

        if (updated)
        {
            EditorUtility.SetDirty(mat);
            if (!switchToUnlit)
            {
                Debug.Log($"Enhanced material '{mat.name}' (shader: {shaderName}) - removed smoothness and metallic");
            }
        }

        return updated;
    }
}

