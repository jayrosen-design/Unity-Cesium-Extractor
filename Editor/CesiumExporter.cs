using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CesiumForUnity;

/// <summary>
/// Editor-only utility to export the currently visible Cesium tiles to OBJ format with textures.
/// </summary>
public static class CesiumExporter
{
    [MenuItem("Unity Cesium Extractor/Export Visible Cesium (OBJ)")]
    public static void ExportActiveTileset()
    {
        var tileset = GameObject.FindObjectOfType<Cesium3DTileset>();
        var georeference = GameObject.FindObjectOfType<CesiumGeoreference>();
        if (tileset == null || georeference == null)
        {
            Debug.LogError("[CesiumExporter] Tileset or Georeference not found in scene.");
            return;
        }

        ExportVisibleCesium(tileset, georeference);
    }

    public static void ExportVisibleCesium(Cesium3DTileset tileset, CesiumGeoreference georeference)
    {
        if (tileset == null || georeference == null)
        {
            Debug.LogError("[CesiumExporter] Tileset or Georeference null.");
            return;
        }

        // Collect meshes under the tileset hierarchy (including inactive for Cesium LOD tiles)
        var meshFilters = tileset.GetComponentsInChildren<MeshFilter>(true);
        
        Debug.Log($"[CesiumExporter] Found {meshFilters.Length} mesh filters under tileset");

        if (meshFilters.Length == 0)
        {
            Debug.LogWarning("[CesiumExporter] No meshes found to export.");
            return;
        }
        
        // Diagnostic counters
        int skippedNull = 0;
        int skippedEmpty = 0;
        int skippedNoRenderer = 0;
        int skippedDisabled = 0;
        int skippedOutOfBounds = 0;
        
        // Get clipping bounds from CesiumTabletopController if available
        float clippingSize = 500f; // default
        var controller = GameObject.FindObjectOfType<CesiumTabletopController>();
        if (controller != null)
        {
            clippingSize = controller.clippingBoxSizeMeters;
            Debug.Log($"[CesiumExporter] Using clipping size from controller: {clippingSize}m");
        }
        
        // Create world-space bounds around the georeference origin
        // The georeference transform position is where content is anchored
        Bounds clippingBounds = new Bounds(
            georeference.transform.position,
            Vector3.one * clippingSize * 2f // full box size (size is diameter, not radius)
        );
        Debug.Log($"[CesiumExporter] Clipping bounds: center={clippingBounds.center}, size={clippingBounds.size}");

        // Build filename with GPS
        double lon = georeference.longitude;
        double lat = georeference.latitude;
        double h = georeference.height;
        string ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string baseName = $"Cesium_{lat:F4}_{lon:F4}_{h:F0}m_{ts}";
        
        // Ensure export path
        string dir = $"Assets/Resources/ExportedCesium/{baseName}";
        if (!Directory.Exists(dir)) 
        {
            Directory.CreateDirectory(dir);
        }
        
        string textureDir = $"{dir}/textures";
        if (!Directory.Exists(textureDir))
        {
            Directory.CreateDirectory(textureDir);
        }

        string objPath = $"{dir}/{baseName}.obj";
        string mtlPath = $"{dir}/{baseName}.mtl";

        // Export OBJ + MTL
        int meshCount = 0;
        int triCount = 0;
        int textureCount = 0;
        
        StringBuilder objSb = new StringBuilder();
        StringBuilder mtlSb = new StringBuilder();
        Dictionary<string, string> exportedMaterials = new Dictionary<string, string>();
        
        objSb.AppendLine("# Cesium Export");
        objSb.AppendLine($"# Location: {lat:F6}, {lon:F6}, {h:F1}m");
        objSb.AppendLine($"# Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        objSb.AppendLine($"mtllib {baseName}.mtl");
        objSb.AppendLine();

        mtlSb.AppendLine("# Cesium Materials");
        mtlSb.AppendLine($"# Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        mtlSb.AppendLine();

        int vertexOffset = 0;
        int uvOffset = 0;
        int normalOffset = 0;

        foreach (var mf in meshFilters)
        {
            // Skip null mesh filters or meshes
            if (mf == null || mf.sharedMesh == null)
            {
                skippedNull++;
                continue;
            }

            var mesh = mf.sharedMesh;
            
            // Skip empty meshes (no geometry)
            if (mesh.vertexCount == 0 || mesh.triangles.Length == 0)
            {
                skippedEmpty++;
                continue;
            }
            
            var renderer = mf.GetComponent<Renderer>();
            if (renderer == null)
            {
                skippedNoRenderer++;
                continue;
            }
            
            // Check if renderer is enabled (Cesium disables renderers for non-visible/non-clipped tiles)
            if (!renderer.enabled)
            {
                skippedDisabled++;
                continue;
            }
            
            // Check if mesh bounds intersect with clipping area
            Bounds meshBounds = renderer.bounds;
            if (!clippingBounds.Intersects(meshBounds))
            {
                skippedOutOfBounds++;
                continue;
            }

            meshCount++;
            triCount += mesh.triangles.Length / 3;

            // Object name
            string objName = $"mesh_{meshCount}_{mf.gameObject.name}".Replace(" ", "_").Replace("/", "_");
            objSb.AppendLine($"o {objName}");

            // Get world transform
            Matrix4x4 localToWorld = mf.transform.localToWorldMatrix;

            // Vertices
            Vector3[] vertices = mesh.vertices;
            foreach (var v in vertices)
            {
                Vector3 worldV = localToWorld.MultiplyPoint3x4(v);
                // OBJ uses right-handed coords, Unity is left-handed - flip X
                objSb.AppendLine($"v {-worldV.x:F6} {worldV.y:F6} {worldV.z:F6}");
            }

            // UVs
            Vector2[] uvs = mesh.uv;
            if (uvs != null && uvs.Length > 0)
            {
                foreach (var uv in uvs)
                {
                    objSb.AppendLine($"vt {uv.x:F6} {uv.y:F6}");
                }
            }

            // Normals
            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length > 0)
            {
                foreach (var n in normals)
                {
                    Vector3 worldN = localToWorld.MultiplyVector(n).normalized;
                    objSb.AppendLine($"vn {-worldN.x:F6} {worldN.y:F6} {worldN.z:F6}");
                }
            }

            // Materials and faces
            Material[] mats = renderer.sharedMaterials;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                Material mat = (mats != null && subMesh < mats.Length) ? mats[subMesh] : null;
                string matKey = mat != null ? mat.GetInstanceID().ToString() : "default";
                string matName = $"mat_{matKey}";

                // Export material if not already done
                if (mat != null && !exportedMaterials.ContainsKey(matKey))
                {
                    ExportMaterial(mtlSb, mat, matName, textureDir, ref textureCount);
                    exportedMaterials[matKey] = matName;
                }

                objSb.AppendLine($"usemtl {matName}");

                int[] tris = mesh.GetTriangles(subMesh);
                bool hasUVs = uvs != null && uvs.Length > 0;
                bool hasNormals = normals != null && normals.Length > 0;

                for (int i = 0; i < tris.Length; i += 3)
                {
                    // OBJ indices are 1-based, and we flip winding order for coordinate flip
                    int v1 = tris[i] + 1 + vertexOffset;
                    int v2 = tris[i + 2] + 1 + vertexOffset; // Swapped for winding
                    int v3 = tris[i + 1] + 1 + vertexOffset;

                    if (hasUVs && hasNormals)
                    {
                        int uv1 = tris[i] + 1 + uvOffset;
                        int uv2 = tris[i + 2] + 1 + uvOffset;
                        int uv3 = tris[i + 1] + 1 + uvOffset;
                        int n1 = tris[i] + 1 + normalOffset;
                        int n2 = tris[i + 2] + 1 + normalOffset;
                        int n3 = tris[i + 1] + 1 + normalOffset;
                        objSb.AppendLine($"f {v1}/{uv1}/{n1} {v2}/{uv2}/{n2} {v3}/{uv3}/{n3}");
                    }
                    else if (hasUVs)
                    {
                        int uv1 = tris[i] + 1 + uvOffset;
                        int uv2 = tris[i + 2] + 1 + uvOffset;
                        int uv3 = tris[i + 1] + 1 + uvOffset;
                        objSb.AppendLine($"f {v1}/{uv1} {v2}/{uv2} {v3}/{uv3}");
                    }
                    else if (hasNormals)
                    {
                        int n1 = tris[i] + 1 + normalOffset;
                        int n2 = tris[i + 2] + 1 + normalOffset;
                        int n3 = tris[i + 1] + 1 + normalOffset;
                        objSb.AppendLine($"f {v1}//{n1} {v2}//{n2} {v3}//{n3}");
                    }
                    else
                    {
                        objSb.AppendLine($"f {v1} {v2} {v3}");
                    }
                }
            }

            vertexOffset += vertices.Length;
            uvOffset += (uvs != null ? uvs.Length : 0);
            normalOffset += (normals != null ? normals.Length : 0);

            objSb.AppendLine();
        }

        if (meshCount == 0)
        {
            Debug.LogWarning("[CesiumExporter] No meshes to export within clipping bounds.");
            Debug.LogWarning($"[CesiumExporter] Skipped: {skippedNull} null, {skippedEmpty} empty, {skippedNoRenderer} no renderer, {skippedDisabled} disabled, {skippedOutOfBounds} out of bounds");
            return;
        }

        // Write files
        File.WriteAllText(objPath, objSb.ToString());
        File.WriteAllText(mtlPath, mtlSb.ToString());
        
        AssetDatabase.Refresh();

        Debug.Log($"[CesiumExporter] === EXPORT SUMMARY ===");
        Debug.Log($"[CesiumExporter] Clipping bounds: center={clippingBounds.center}, size={clippingBounds.size}");
        Debug.Log($"[CesiumExporter] Total mesh filters found: {meshFilters.Length}");
        Debug.Log($"[CesiumExporter] Exported: {meshCount} meshes, ~{triCount} tris, {textureCount} textures");
        Debug.Log($"[CesiumExporter] Skipped: {skippedNull} null, {skippedEmpty} empty, {skippedNoRenderer} no renderer, {skippedDisabled} disabled, {skippedOutOfBounds} out of bounds");
        Debug.Log($"[CesiumExporter] Saved to: {dir}");
    }

    static string ExportMaterial(StringBuilder sb, Material mat, string matName, string textureDir, ref int textureCount)
    {
        sb.AppendLine($"newmtl {matName}");
        
        // Basic material properties
        Color color = Color.white;
        if (mat.HasProperty("_Color"))
        {
            color = mat.color;
        }
        else if (mat.HasProperty("_BaseColor"))
        {
            color = mat.GetColor("_BaseColor");
        }
        
        sb.AppendLine($"Ka 0.2 0.2 0.2"); // Ambient
        sb.AppendLine($"Kd {color.r:F4} {color.g:F4} {color.b:F4}"); // Diffuse
        sb.AppendLine($"Ks 0.1 0.1 0.1"); // Specular
        sb.AppendLine($"Ns 10.0"); // Specular exponent
        sb.AppendLine($"d {color.a:F4}"); // Dissolve (transparency)
        sb.AppendLine($"illum 2"); // Illumination model
        
        string savedTexPath = null;
        
        // Dynamically discover ALL texture properties on this material
        string[] texProps = mat.GetTexturePropertyNames();
        Debug.Log($"[CesiumExporter] Material '{mat.name}' ({matName}) has {texProps.Length} texture properties: [{string.Join(", ", texProps)}]");
        
        // Track if we found a diffuse/base texture for map_Kd
        bool foundDiffuse = false;
        
        foreach (var propName in texProps)
        {
            Texture tex = mat.GetTexture(propName);
            if (tex == null)
            {
                Debug.Log($"[CesiumExporter]   Property '{propName}' = null");
                continue;
            }
            
            Debug.Log($"[CesiumExporter]   Property '{propName}' = {tex.name} ({tex.width}x{tex.height}, type: {tex.GetType().Name})");
            
            // Create a clean property name for the filename
            string cleanPropName = propName.Replace("_", "").Replace("/", "_");
            string texSuffix = cleanPropName;
            
            // Determine the MTL map type based on property name
            string mtlMapType = null;
            bool isDiffuse = propName.Contains("MainTex") || propName.Contains("BaseMap") || 
                            propName.Contains("BaseColor") || propName.Contains("Albedo") ||
                            propName.Contains("Diffuse") || propName.Contains("baseColor") ||
                            propName.Contains("overlay") || propName.Contains("Overlay");
            bool isNormal = propName.Contains("Normal") || propName.Contains("Bump");
            bool isSpecular = propName.Contains("Specular") || propName.Contains("Metallic");
            
            if (isDiffuse && !foundDiffuse)
            {
                mtlMapType = "map_Kd";
                texSuffix = "diffuse";
                foundDiffuse = true;
            }
            else if (isNormal)
            {
                mtlMapType = "map_Bump";
                texSuffix = "normal";
            }
            else if (isSpecular)
            {
                mtlMapType = "map_Ks";
                texSuffix = "specular";
            }
            else if (!foundDiffuse)
            {
                // If we haven't found a diffuse yet, use this as diffuse
                mtlMapType = "map_Kd";
                texSuffix = "diffuse";
                foundDiffuse = true;
            }
            
            // Save the texture
            string texPath = null;
            if (tex is Texture2D tex2D)
            {
                texPath = SaveTexture(tex2D, textureDir, matName, texSuffix, ref textureCount);
            }
            else
            {
                // Handle non-Texture2D (RenderTexture, runtime-generated, etc.)
                texPath = SaveTextureFromGPU(tex, textureDir, matName, texSuffix, ref textureCount);
            }
            
            if (texPath != null)
            {
                savedTexPath = texPath;
                if (mtlMapType != null)
                {
                    sb.AppendLine($"{mtlMapType} textures/{Path.GetFileName(texPath)}");
                }
            }
        }
        
        if (!foundDiffuse)
        {
            Debug.LogWarning($"[CesiumExporter] Material '{mat.name}' has no diffuse texture - may use vertex colors");
        }
        
        sb.AppendLine();
        return savedTexPath;
    }

    static string SaveTexture(Texture2D tex, string dir, string matName, string texSuffix, ref int textureCount)
    {
        if (tex == null) return null;
        
        try
        {
            string texName = $"{matName}_{texSuffix}.png";
            string fullPath = $"{dir}/{texName}";
            
            // Check if texture is readable
            Texture2D readableTex = tex;
            
            if (!tex.isReadable)
            {
                // Create a temporary RenderTexture and copy the texture
                readableTex = MakeTextureReadable(tex);
            }
            
            if (readableTex != null)
            {
                byte[] pngData = readableTex.EncodeToPNG();
                if (pngData != null && pngData.Length > 0)
                {
                    File.WriteAllBytes(fullPath, pngData);
                    textureCount++;
                    
                    // Clean up temporary texture
                    if (readableTex != tex)
                    {
                        UnityEngine.Object.DestroyImmediate(readableTex);
                    }
                    
                    return fullPath;
                }
                
                // Clean up temporary texture
                if (readableTex != tex)
                {
                    UnityEngine.Object.DestroyImmediate(readableTex);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CesiumExporter] Could not save texture {tex.name}: {e.Message}");
        }
        
        return null;
    }
    
    static string SaveTextureFromGPU(Texture tex, string dir, string matName, string texSuffix, ref int textureCount)
    {
        if (tex == null) return null;
        
        try
        {
            string texName = $"{matName}_{texSuffix}.png";
            string fullPath = $"{dir}/{texName}";
            
            // Create a temporary RenderTexture
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            
            // Copy texture to RenderTexture
            Graphics.Blit(tex, rt);
            
            // Read pixels from RenderTexture
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            
            Texture2D readableTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            readableTex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readableTex.Apply();
            
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            
            // Encode and save
            byte[] pngData = readableTex.EncodeToPNG();
            if (pngData != null && pngData.Length > 0)
            {
                File.WriteAllBytes(fullPath, pngData);
                textureCount++;
                UnityEngine.Object.DestroyImmediate(readableTex);
                return fullPath;
            }
            
            UnityEngine.Object.DestroyImmediate(readableTex);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CesiumExporter] Could not save GPU texture {tex.name}: {e.Message}");
        }
        
        return null;
    }
    
    static Texture2D MakeTextureReadable(Texture2D source)
    {
        // Create a temporary RenderTexture
        RenderTexture rt = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB
        );
        
        // Copy the source texture to the RenderTexture
        Graphics.Blit(source, rt);
        
        // Store the active RenderTexture
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        
        // Create a new readable Texture2D
        Texture2D readableTex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        
        // Read the pixels from the RenderTexture
        readableTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readableTex.Apply();
        
        // Restore the active RenderTexture
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        
        return readableTex;
    }
}
