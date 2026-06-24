using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// UI controller for selecting and switching between Florida cities.
/// </summary>
public class CitySelectorUI : MonoBehaviour
{
    [Header("References")]
    public CityPlacementController placementController;
    public CesiumTabletopController tabletopController;
    
    [Header("City Profiles")]
    [Tooltip("Assign all city profiles here. Miami should be first for default.")]
    public List<CityProfile> cities = new List<CityProfile>();
    
    [Header("Default City")]
    [Tooltip("Name of the default city to load (case-insensitive)")]
    public string defaultCityName = "Miami";
    
    [Header("UI Elements")]
    public Transform buttonContainer;
    public GameObject cityButtonPrefab;
    public TextMeshProUGUI statusText;
    public Button resetButton;
    public Button centerButton;
    
    [Header("Movement Controls")]
    public Button moveUpButton;
    public Button moveDownButton;
    public Button moveLeftButton;
    public Button moveRightButton;
    
    [Header("Zoom Controls")]
    public Button zoomInButton;
    public Button zoomOutButton;
    public TextMeshProUGUI heightText;
    
    [Tooltip("Height change per zoom button click in meters")]
    public float zoomStepMeters = 100f;
    
    [Header("Export (Editor Only)")]
    public Button exportButton;
    
    [Header("Screenshot")]
    public Button screenshotButton;
    
    [Header("Info Display")]
    public TextMeshProUGUI infoText;
    public ARContentDebugger contentDebugger;
    
    // Data tracking
    private long sessionTotalBytes = 0;
    private int sessionTotalMeshes = 0;
    private int sessionTotalTextures = 0;
    private float lastStatsUpdate = 0f;
    private const float STATS_UPDATE_INTERVAL = 0.5f;
    
    private int currentCityIndex = 0;
    private bool cityPlaced = false;
    private List<Button> cityButtons = new List<Button>();

    void Start()
    {
        Debug.Log($"[CitySelectorUI] Start - Cities count: {cities.Count}");
        
        // If no cities assigned, try to load from Resources
        if (cities.Count == 0)
        {
            LoadCitiesFromResources();
            Debug.Log($"[CitySelectorUI] After loading from Resources: {cities.Count} cities");
        }
        
        // If still no cities, log error
        if (cities.Count == 0)
        {
            Debug.LogError("[CitySelectorUI] NO CITIES FOUND! Assign cities in inspector or place in Resources/FloridaCities folder.");
        }
        
        // Generate UI buttons for each city
        Debug.Log($"[CitySelectorUI] Generating buttons for {cities.Count} cities");
        GenerateCityButtons();
        Debug.Log($"[CitySelectorUI] Generated {cityButtons.Count} buttons");
        
        // Setup reset button
        if (resetButton != null)
        {
            resetButton.onClick.AddListener(ResetPlacement);
        }
        
        // Setup center button (returns view to city center after panning)
        if (centerButton != null)
        {
            centerButton.onClick.AddListener(ReturnToCenter);
        }
        
        // Setup D-Pad movement buttons
        if (moveUpButton != null) moveUpButton.onClick.AddListener(() => MoveView(Vector2.up));
        if (moveDownButton != null) moveDownButton.onClick.AddListener(() => MoveView(Vector2.down));
        if (moveLeftButton != null) moveLeftButton.onClick.AddListener(() => MoveView(Vector2.left));
        if (moveRightButton != null) moveRightButton.onClick.AddListener(() => MoveView(Vector2.right));
        
        // Setup zoom buttons
        if (zoomInButton != null) zoomInButton.onClick.AddListener(ZoomIn);
        if (zoomOutButton != null) zoomOutButton.onClick.AddListener(ZoomOut);
        
        // Setup export button (editor only)
        if (exportButton != null) exportButton.onClick.AddListener(ExportCesium);
        
        // Setup screenshot button
        if (screenshotButton != null) screenshotButton.onClick.AddListener(TakeScreenshot);
        
        // Find ARContentDebugger if not assigned
        if (contentDebugger == null)
        {
            contentDebugger = FindObjectOfType<ARContentDebugger>();
        }
        
        // Find and set the default city (Miami) after a short delay
        // to allow CesiumGeoreference to initialize
        StartCoroutine(SelectDefaultCityDelayed());
    }
    
    void Update()
    {
        // Periodically update combined info display
        if (Time.time - lastStatsUpdate > STATS_UPDATE_INTERVAL)
        {
            lastStatsUpdate = Time.time;
            UpdateInfoDisplay();
        }
    }

    IEnumerator SelectDefaultCityDelayed()
    {
        // Wait for systems to initialize
        yield return new WaitForSeconds(0.5f);
        
        int defaultIndex = FindCityByName(defaultCityName);
        if (defaultIndex < 0 && cities.Count > 0)
        {
            defaultIndex = 0; // Fallback to first city
        }
        
        if (defaultIndex >= 0)
        {
            SelectCity(defaultIndex);
            UpdateStatusText($"Loading {cities[defaultIndex].cityName}... Point device at floor.");
        }
        else
        {
            UpdateStatusText("No cities configured. Add city profiles.");
        }
    }

    void LoadCitiesFromResources()
    {
        // Try loading from Resources/FloridaCities (the correct location)
        CityProfile[] loadedCities = Resources.LoadAll<CityProfile>("FloridaCities");
        if (loadedCities != null && loadedCities.Length > 0)
        {
            cities.AddRange(loadedCities);
            Debug.Log($"[CitySelectorUI] Loaded {loadedCities.Length} cities from Resources/FloridaCities");
        }
        else
        {
            Debug.LogWarning("[CitySelectorUI] No cities found in Resources/FloridaCities!");
            Debug.LogWarning("[CitySelectorUI] Run 'Unity Cesium Extractor > Generate City Profiles' to create them.");
        }
    }

    int FindCityByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        
        string searchName = name.ToLower().Trim();
        for (int i = 0; i < cities.Count; i++)
        {
            if (cities[i] != null && cities[i].cityName.ToLower().Trim() == searchName)
            {
                return i;
            }
        }
        return -1;
    }

    void GenerateCityButtons()
    {
        if (buttonContainer == null) return;
        
        cityButtons.Clear();
        
        for (int i = 0; i < cities.Count; i++)
        {
            int index = i;
            CityProfile city = cities[i];
            
            if (city == null) continue;
            
            GameObject buttonGO;
            
            if (cityButtonPrefab != null)
            {
                buttonGO = Instantiate(cityButtonPrefab, buttonContainer);
            }
            else
            {
                buttonGO = CreateDefaultButton(city.cityName);
                buttonGO.transform.SetParent(buttonContainer, false);
            }
            
            // Set button text
            TextMeshProUGUI buttonText = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = city.cityName;
            }
            else
            {
                Text legacyText = buttonGO.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = city.cityName;
                }
            }
            
            // Add click listener
            Button button = buttonGO.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnCityButtonClicked(index));
                cityButtons.Add(button);
            }
        }
    }

    GameObject CreateDefaultButton(string cityName)
    {
        GameObject buttonGO = new GameObject(cityName + "_Button");
        
        RectTransform rect = buttonGO.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(100, 45); // Matches GridLayoutGroup cell size
        
        // White button with shadow
        Image image = buttonGO.AddComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0.9f); // White
        
        // Add shadow
        UnityEngine.UI.Shadow shadow = buttonGO.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.3f);
        shadow.effectDistance = new Vector2(2, -2);
        
        Button button = buttonGO.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.9f); // White
        colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = new Color(0.4f, 0.8f, 0.4f, 1f); // Green for selected
        button.colors = colors;
        
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;
        
        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = cityName;
        tmp.fontSize = 18;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.black; // Black text on white button
        
        return buttonGO;
    }

    void OnCityButtonClicked(int index)
    {
        if (index < 0 || index >= cities.Count) return;
        
        CityProfile selectedCity = cities[index];
        
        // Update current selection
        currentCityIndex = index;
        
        // Highlight selected button
        HighlightButton(index);
        
        if (cityPlaced && tabletopController != null)
        {
            // Switch to the new city
            tabletopController.SwitchCity(selectedCity);
            UpdateStatusText($"Switching to {selectedCity.cityName}...");
        }
        else
        {
            // Set as current city for when placement happens
        if (placementController != null)
        {
            placementController.SetCurrentCity(selectedCity);
        }
            UpdateStatusText($"{selectedCity.cityName} selected. Point at floor to place.");
        }
    }

    void HighlightButton(int selectedIndex)
    {
        for (int i = 0; i < cityButtons.Count; i++)
        {
            if (cityButtons[i] == null) continue;
            
            Image img = cityButtons[i].GetComponent<Image>();
            if (img != null)
            {
                img.color = (i == selectedIndex) 
                    ? new Color(0.4f, 0.85f, 0.4f, 1f)  // Light green for selected
                    : new Color(1f, 1f, 1f, 0.9f); // White for unselected
            }
        }
    }

    public void SelectCity(int index)
    {
        if (index < 0 || index >= cities.Count) return;
        
        currentCityIndex = index;
        CityProfile city = cities[index];
        
        // Set on input manager (this will queue initialization on controller)
        if (placementController != null)
        {
            placementController.SetCurrentCity(city);
        }
        
        HighlightButton(index);
        
        Debug.Log($"[CitySelectorUI] Selected city: {city.cityName}");
    }

    public void OnCityPlaced()
    {
        cityPlaced = true;
        if (cities.Count > 0 && currentCityIndex < cities.Count)
        {
            UpdateStatusText($"Viewing {cities[currentCityIndex].cityName}");
        }
        UpdateHeightDisplay();
    }
    
    public void ReturnToCenter()
    {
        if (placementController != null)
        {
            placementController.ReturnToCenter();
        }
        
        if (cities.Count > 0 && currentCityIndex < cities.Count)
        {
            UpdateStatusText($"Returned to {cities[currentCityIndex].cityName} center.");
        }
        
        UpdateHeightDisplay();
    }
    
    void MoveView(Vector2 direction)
    {
        if (tabletopController != null)
        {
            tabletopController.MoveView(direction);
        }
    }
    
    void ZoomIn()
    {
        if (tabletopController != null)
        {
            tabletopController.ChangeHeight(-zoomStepMeters);
            UpdateHeightDisplay();
        }
    }
    
    void ZoomOut()
    {
        if (tabletopController != null)
        {
            tabletopController.ChangeHeight(zoomStepMeters);
            UpdateHeightDisplay();
        }
    }
    
    void UpdateHeightDisplay()
    {
        if (heightText != null && tabletopController != null)
        {
            double height = tabletopController.GetCurrentHeight();
            if (height >= 1000)
            {
                heightText.text = $"{height/1000:F1} km";
            }
            else
            {
                heightText.text = $"{height:F0} m";
            }
        }
    }

    public void ResetPlacement()
    {
        cityPlaced = false;
        
        if (tabletopController != null)
        {
            tabletopController.ResetCity();
        }
        
        if (placementController != null)
        {
            placementController.ResetPlacement();
        }
        
        // Re-select current city after a brief delay
        StartCoroutine(ReselectCityAfterReset());
    }

    IEnumerator ReselectCityAfterReset()
    {
        yield return new WaitForSeconds(0.3f);
        
        if (currentCityIndex >= 0 && currentCityIndex < cities.Count)
        {
            SelectCity(currentCityIndex);
        }
        
        UpdateStatusText("Reset. Point at floor to place city again.");
    }

    void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"[CitySelectorUI] {message}");
    }
    
    /// <summary>
    /// Combined info display showing AR status and data statistics.
    /// </summary>
    void UpdateInfoDisplay()
    {
        if (infoText == null) return;
        
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        
        // AR Status
        string statusText = "WAITING";
        if (contentDebugger != null && contentDebugger.showDebugInfo)
        {
            bool isHidden = contentDebugger.transform.position.y < -100;
            statusText = isHidden ? "WAITING" : "PLACED";
        }
        
        // Data Statistics
        var tileset = FindObjectOfType<CesiumForUnity.Cesium3DTileset>();
        if (tileset != null)
        {
            var meshFilters = tileset.GetComponentsInChildren<MeshFilter>(true);
            
            int enabledMeshes = 0;
            long totalTriangles = 0;
            long estimatedMeshBytes = 0;
            long estimatedTextureBytes = 0;
            int textureCount = 0;
            HashSet<int> countedTextures = new HashSet<int>();
            
            foreach (var mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                
                var mesh = mf.sharedMesh;
                var renderer = mf.GetComponent<Renderer>();
                
                if (renderer != null && renderer.enabled)
                {
                    enabledMeshes++;
                    totalTriangles += mesh.triangles.Length / 3;
                    estimatedMeshBytes += mesh.vertexCount * 48;
                    estimatedMeshBytes += mesh.triangles.Length * 4;
                    
                    if (renderer.sharedMaterials != null)
                    {
                        foreach (var mat in renderer.sharedMaterials)
                        {
                            if (mat == null) continue;
                            string[] texProps = mat.GetTexturePropertyNames();
                            foreach (var propName in texProps)
                            {
                                var tex = mat.GetTexture(propName);
                                if (tex != null && !countedTextures.Contains(tex.GetInstanceID()))
                                {
                                    countedTextures.Add(tex.GetInstanceID());
                                    textureCount++;
                                    estimatedTextureBytes += tex.width * tex.height * 4;
                                }
                            }
                        }
                    }
                }
            }
            
            // Update session totals
            if (enabledMeshes > sessionTotalMeshes) sessionTotalMeshes = enabledMeshes;
            if (textureCount > sessionTotalTextures) sessionTotalTextures = textureCount;
            long currentTotal = estimatedMeshBytes + estimatedTextureBytes;
            if (currentTotal > sessionTotalBytes) sessionTotalBytes = currentTotal;
            
            string currentSize = FormatBytes(currentTotal);
            string sessionSize = FormatBytes(sessionTotalBytes);
            
            sb.Append($"[{statusText}] {enabledMeshes} meshes, {totalTriangles/1000}K tris, {textureCount} tex | ");
            sb.Append($"Size: {currentSize} | Session: {sessionSize}");
        }
        else
        {
            sb.Append($"[{statusText}] No tileset loaded");
        }
        
        infoText.text = sb.ToString();
    }
    
    string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{bytes / (1024f * 1024f * 1024f):F1} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024f * 1024f):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024f:F1} KB";
        return $"{bytes} B";
    }
    
    /// <summary>
    /// Take a screenshot of the AR view without UI.
    /// </summary>
    void TakeScreenshot()
    {
        StartCoroutine(CaptureScreenshot());
    }
    
    IEnumerator CaptureScreenshot()
    {
        // Hide the UI Canvas
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();
        
        bool wasCanvasEnabled = canvas != null && canvas.enabled;
        if (canvas != null) canvas.enabled = false;
        
        // Wait for end of frame so UI is hidden
        yield return new WaitForEndOfFrame();
        
        // Create screenshot filename with timestamp
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string screenshotDir = Application.persistentDataPath + "/Screenshots";
        
        // Ensure directory exists
        if (!System.IO.Directory.Exists(screenshotDir))
        {
            System.IO.Directory.CreateDirectory(screenshotDir);
        }
        
        string filename = $"{screenshotDir}/ARScreenshot_{timestamp}.png";
        
        // Capture screenshot
        ScreenCapture.CaptureScreenshot(filename);
        
        // Wait a frame for screenshot to be saved
        yield return null;
        
        // Restore UI Canvas
        if (canvas != null) canvas.enabled = wasCanvasEnabled;
        
        Debug.Log($"[Screenshot] Saved to: {filename}");
        UpdateStatusText($"Screenshot saved!");
        
        // Also save to Assets folder in editor for easy access
#if UNITY_EDITOR
        string editorDir = "Assets/Screenshots";
        if (!System.IO.Directory.Exists(editorDir))
        {
            System.IO.Directory.CreateDirectory(editorDir);
        }
        string editorFilename = $"{editorDir}/ARScreenshot_{timestamp}.png";
        // ScreenCapture doesn't support custom paths directly, so we copy after
        StartCoroutine(CopyScreenshotToEditor(filename, editorFilename));
#endif
    }
    
#if UNITY_EDITOR
    IEnumerator CopyScreenshotToEditor(string sourcePath, string destPath)
    {
        // Wait for file to be written
        yield return new WaitForSeconds(0.5f);
        
        if (System.IO.File.Exists(sourcePath))
        {
            try
            {
                System.IO.File.Copy(sourcePath, destPath, true);
                UnityEditor.AssetDatabase.Refresh();
                Debug.Log($"[Screenshot] Also saved to: {destPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Screenshot] Could not copy to editor: {e.Message}");
            }
        }
    }
#endif
    
    /// <summary>
    /// Export currently visible Cesium tiles to OBJ (Editor only).
    /// </summary>
    void ExportCesium()
    {
#if UNITY_EDITOR
        var tileset = FindObjectOfType<CesiumForUnity.Cesium3DTileset>();
        var georeference = FindObjectOfType<CesiumForUnity.CesiumGeoreference>();
        if (tileset == null || georeference == null)
        {
            UpdateStatusText("Export failed: Cesium not found");
            return;
        }
        UpdateStatusText("Exporting Cesium to OBJ...");
        
        // Use reflection to call editor-only CesiumExporter from Assembly-CSharp-Editor
        System.Type exporterType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == "Assembly-CSharp-Editor")
            {
                exporterType = asm.GetType("CesiumExporter");
                break;
            }
        }
        
        if (exporterType != null)
        {
            var method = exporterType.GetMethod("ExportVisibleCesium", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method != null)
            {
                method.Invoke(null, new object[] { tileset, georeference });
                UpdateStatusText("Export complete! Check Assets/Resources/ExportedCesium");
                return;
            }
        }
        UpdateStatusText("Export failed: CesiumExporter not found in editor assembly");
#else
        UpdateStatusText("Export only available in Unity Editor");
#endif
    }
}
