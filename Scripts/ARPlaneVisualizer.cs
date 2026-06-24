using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Creates visual representations for detected AR planes.
/// Attach this to the same GameObject as ARPlaneManager (XR Origin).
/// This is optional - AR Foundation has built-in plane visualization.
/// </summary>
[RequireComponent(typeof(ARPlaneManager))]
public class ARPlaneVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    public Color planeColor = new Color(0.0f, 0.8f, 0.5f, 0.3f);
    public Color edgeColor = new Color(0.0f, 1.0f, 0.6f, 0.8f);
    
    [Header("Material (Assign in Inspector for builds!)")]
    [Tooltip("IMPORTANT: Assign a material in inspector for device builds. Shader.Find() doesn't work at runtime.")]
    public Material planeMaterial;
    public Material lineMaterial;

    private ARPlaneManager planeManager;

    void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
        
        // Only create prefab if materials are assigned
        // Without assigned materials, we can't create the prefab at runtime on device
        if (planeManager != null && planeManager.planePrefab == null)
        {
            GameObject prefab = TryCreatePlanePrefab();
            if (prefab != null)
            {
                planeManager.planePrefab = prefab;
            }
            else
            {
                Debug.LogWarning("[ARPlaneVisualizer] Could not create plane prefab. Assign materials in inspector or use AR Foundation's default plane prefab.");
            }
        }
    }

    GameObject TryCreatePlanePrefab()
    {
        // Try to get or create materials
        Material meshMat = planeMaterial;
        Material lineMat = lineMaterial;
        
        // If no material assigned, try to create one (only works in Editor, not on device)
        if (meshMat == null)
        {
            meshMat = TryCreateDefaultMaterial();
            if (meshMat == null)
            {
                Debug.LogWarning("[ARPlaneVisualizer] No plane material and couldn't create default. Planes won't be visualized.");
                return null;
            }
        }
        
        if (lineMat == null)
        {
            lineMat = TryCreateLineMaterial();
            // Line material is optional, continue without it
        }
        
        // Create the prefab
        GameObject prefab = new GameObject("AR Plane Prefab");
        prefab.SetActive(false);
        
        // Add required AR components
        prefab.AddComponent<ARPlane>();
        prefab.AddComponent<ARPlaneMeshVisualizer>();
        
        // Add mesh components
        prefab.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = prefab.AddComponent<MeshRenderer>();
        meshRenderer.material = meshMat;
        
        // Add line renderer for edges (optional)
        if (lineMat != null)
        {
            LineRenderer lineRenderer = prefab.AddComponent<LineRenderer>();
            lineRenderer.material = lineMat;
            lineRenderer.startColor = edgeColor;
            lineRenderer.endColor = edgeColor;
            lineRenderer.startWidth = 0.02f;
            lineRenderer.endWidth = 0.02f;
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            
            prefab.AddComponent<PlaneEdgeVisualizer>();
        }
        
        return prefab;
    }

    Material TryCreateDefaultMaterial()
    {
        // Try various shaders that might be included in the build
        string[] shadersToTry = new string[]
        {
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Lit",
            "Unlit/Color",
            "Standard",
            "Mobile/Diffuse",
            "Sprites/Default"
        };
        
        Shader shader = null;
        foreach (string shaderName in shadersToTry)
        {
            shader = Shader.Find(shaderName);
            if (shader != null)
            {
                Debug.Log($"[ARPlaneVisualizer] Using shader: {shaderName}");
                break;
            }
        }
        
        if (shader == null)
        {
            Debug.LogError("[ARPlaneVisualizer] No shader found! Assign a material in the inspector.");
            return null;
        }
        
        Material mat = new Material(shader);
        mat.name = "AR Plane Material (Runtime)";
        mat.color = planeColor;
        
        // Try to make it transparent
        try
        {
            mat.SetFloat("_Surface", 1);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }
        catch (System.Exception)
        {
            // Shader doesn't support these properties, that's ok
        }
        
        return mat;
    }
    
    Material TryCreateLineMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }
        
        if (shader == null)
        {
            return null;
        }
        
        Material mat = new Material(shader);
        mat.color = edgeColor;
        return mat;
    }
}

/// <summary>
/// Updates the line renderer to show plane boundaries.
/// </summary>
public class PlaneEdgeVisualizer : MonoBehaviour
{
    private ARPlane arPlane;
    private LineRenderer lineRenderer;

    void Awake()
    {
        arPlane = GetComponent<ARPlane>();
        lineRenderer = GetComponent<LineRenderer>();
    }

    void Update()
    {
        if (arPlane == null || lineRenderer == null) return;
        
        var boundary = arPlane.boundary;
        if (boundary.Length > 0)
        {
            lineRenderer.positionCount = boundary.Length;
            for (int i = 0; i < boundary.Length; i++)
            {
                lineRenderer.SetPosition(i, new Vector3(boundary[i].x, 0, boundary[i].y));
            }
        }
    }
}
