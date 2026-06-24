using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using CesiumForUnity;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine.InputSystem.XR;
using System.Collections.Generic;

public class CesiumExtractorSceneBuilder
{
    [MenuItem("Unity Cesium Extractor/Build Scene")]
    public static void BuildScene()
    {
        Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        newScene.name = "UnityCesiumExtractor";
        
        GameObject arSessionGO = new GameObject("AR Session");
        arSessionGO.AddComponent<ARSession>();
        arSessionGO.AddComponent<UnityEngine.XR.ARFoundation.ARInputManager>();

        GameObject xrOriginPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Samples/XR Interaction Toolkit/3.0.4/AR Starter Assets/Prefabs/XR Origin (AR Rig).prefab");
        
        GameObject xrOriginGO;
        XROrigin xrOrigin;
        Camera cam;
        ARPlaneManager planeManager;
        ARRaycastManager raycastManager;
        ARAnchorManager anchorManager;
        
        if (xrOriginPrefab != null)
        {
            xrOriginGO = (GameObject)PrefabUtility.InstantiatePrefab(xrOriginPrefab);
            xrOriginGO.name = "XR Origin";
            xrOrigin = xrOriginGO.GetComponent<XROrigin>();
            cam = xrOriginGO.GetComponentInChildren<Camera>();
            
            if (cam != null)
            {
                cam.farClipPlane = 20000f;
            }
            
            planeManager = xrOriginGO.GetComponent<ARPlaneManager>();
            if (planeManager == null)
                planeManager = xrOriginGO.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            
            raycastManager = xrOriginGO.GetComponent<ARRaycastManager>();
            if (raycastManager == null)
                raycastManager = xrOriginGO.AddComponent<ARRaycastManager>();
                
            anchorManager = xrOriginGO.GetComponent<ARAnchorManager>();
            if (anchorManager == null)
                anchorManager = xrOriginGO.AddComponent<ARAnchorManager>();
            
            if (xrOriginGO.GetComponent<ARPlaneVisualizer>() == null)
                xrOriginGO.AddComponent<ARPlaneVisualizer>();
                
            Debug.Log("[CesiumExtractorSceneBuilder] Using XR Origin prefab with proper TrackedPoseDriver configuration");
        }
        else
        {
            Debug.LogWarning("[CesiumExtractorSceneBuilder] XR Origin prefab not found! Creating from scratch.");
            Debug.LogWarning("[CesiumExtractorSceneBuilder] Install XR Interaction Toolkit samples for proper simulator support.");
            
            xrOriginGO = new GameObject("XR Origin");
            xrOrigin = xrOriginGO.AddComponent<XROrigin>();
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            GameObject cameraOffsetGO = new GameObject("Camera Offset");
            cameraOffsetGO.transform.SetParent(xrOriginGO.transform, false);
            xrOrigin.CameraFloorOffsetObject = cameraOffsetGO;

            GameObject mainCameraGO = new GameObject("Main Camera");
            mainCameraGO.tag = "MainCamera";
            mainCameraGO.transform.SetParent(cameraOffsetGO.transform, false);
            cam = mainCameraGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20000f;

            xrOrigin.Camera = cam;

            mainCameraGO.AddComponent<ARCameraManager>();
            mainCameraGO.AddComponent<ARCameraBackground>();
            mainCameraGO.AddComponent<TrackedPoseDriver>();

            planeManager = xrOriginGO.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            
            xrOriginGO.AddComponent<ARPlaneVisualizer>();
            raycastManager = xrOriginGO.AddComponent<ARRaycastManager>();
            anchorManager = xrOriginGO.AddComponent<ARAnchorManager>();
        }

        GameObject geoContainer = new GameObject("GeospatialContainer");
        geoContainer.transform.position = new Vector3(0, -1000, 0);
        geoContainer.transform.localScale = Vector3.one;
        geoContainer.AddComponent<ARContentDebugger>();

        GameObject georeferenceGO = new GameObject("CesiumGeoreference");
        georeferenceGO.transform.SetParent(geoContainer.transform, false);
        CesiumGeoreference georeference = georeferenceGO.AddComponent<CesiumGeoreference>();

        GameObject tilesetGO = new GameObject("Google3DTileset");
        tilesetGO.transform.SetParent(georeferenceGO.transform, false);
        Cesium3DTileset tileset = tilesetGO.AddComponent<Cesium3DTileset>();
        tileset.ionAssetID = 2275207;
        tileset.maximumScreenSpaceError = 16.0f;
        tileset.maximumCachedBytes = 536870912;
        tileset.preloadAncestors = true;
        tileset.preloadSiblings = true;
        tileset.loadingDescendantLimit = 10;
        tileset.enableFrustumCulling = false;
        tileset.showCreditsOnScreen = true;
        tilesetGO.SetActive(false);

        GameObject systemsGO = new GameObject("Systems");
        CesiumTabletopController controller = systemsGO.AddComponent<CesiumTabletopController>();
        controller.georeference = georeference;
        controller.tileset = tileset;
        controller.contentContainer = geoContainer.transform;
        controller.anchorManager = anchorManager;
        controller.xrOrigin = xrOrigin;
        controller.placementHeightOffset = 0.5f;

        GameObject canvasGO = new GameObject("UI Canvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        CityPlacementController placementController = canvasGO.AddComponent<CityPlacementController>();
        placementController.raycastManager = raycastManager;
        placementController.planeManager = planeManager;
        placementController.controller = controller;
        placementController.autoPlaceOnDetection = true;

        CitySelectorUI citySelectorUI = canvasGO.AddComponent<CitySelectorUI>();
        citySelectorUI.placementController = placementController;
        citySelectorUI.tabletopController = controller;
        controller.citySelectorUI = citySelectorUI;

        GameObject statusTextGO = new GameObject("StatusText");
        statusTextGO.transform.SetParent(canvasGO.transform, false);
        RectTransform statusRect = statusTextGO.AddComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0, 1);
        statusRect.anchorMax = new Vector2(1, 1);
        statusRect.pivot = new Vector2(0.5f, 1);
        statusRect.anchoredPosition = new Vector2(0, -50);
        statusRect.sizeDelta = new Vector2(0, 60);
        
        TextMeshProUGUI statusText = statusTextGO.AddComponent<TextMeshProUGUI>();
        statusText.text = "Scan floor, then tap to place city";
        statusText.fontSize = 28;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = Color.white;
        citySelectorUI.statusText = statusText;

        GameObject buttonContainerGO = new GameObject("CityButtonContainer");
        buttonContainerGO.transform.SetParent(canvasGO.transform, false);
        RectTransform containerRect = buttonContainerGO.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0, 0);
        containerRect.anchorMax = new Vector2(1, 0);
        containerRect.pivot = new Vector2(0.5f, 0);
        containerRect.anchoredPosition = new Vector2(0, 20);
        containerRect.sizeDelta = new Vector2(0, 120);
        
        GridLayoutGroup layout = buttonContainerGO.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(100, 45);
        layout.spacing = new Vector2(10, 8);
        layout.padding = new RectOffset(10, 10, 5, 5);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 12;
        layout.childAlignment = TextAnchor.MiddleCenter;
        
        citySelectorUI.buttonContainer = containerRect;
        
        GameObject dpadContainer = new GameObject("DPadContainer");
        dpadContainer.transform.SetParent(canvasGO.transform, false);
        RectTransform dpadRect = dpadContainer.AddComponent<RectTransform>();
        dpadRect.anchorMin = new Vector2(0, 0.5f);
        dpadRect.anchorMax = new Vector2(0, 0.5f);
        dpadRect.pivot = new Vector2(0, 0.5f);
        dpadRect.anchoredPosition = new Vector2(20, 0);
        dpadRect.sizeDelta = new Vector2(150, 150);
        
        citySelectorUI.moveUpButton = CreateDPadButton(dpadContainer.transform, "▲", new Vector2(50, 100));
        citySelectorUI.moveDownButton = CreateDPadButton(dpadContainer.transform, "▼", new Vector2(50, 0));
        citySelectorUI.moveLeftButton = CreateDPadButton(dpadContainer.transform, "◄", new Vector2(0, 50));
        citySelectorUI.moveRightButton = CreateDPadButton(dpadContainer.transform, "►", new Vector2(100, 50));
        
        GameObject zoomContainer = new GameObject("ZoomContainer");
        zoomContainer.transform.SetParent(canvasGO.transform, false);
        RectTransform zoomRect = zoomContainer.AddComponent<RectTransform>();
        zoomRect.anchorMin = new Vector2(1, 0.5f);
        zoomRect.anchorMax = new Vector2(1, 0.5f);
        zoomRect.pivot = new Vector2(1, 0.5f);
        zoomRect.anchoredPosition = new Vector2(-20, 0);
        zoomRect.sizeDelta = new Vector2(80, 200);
        
        citySelectorUI.zoomInButton = CreateZoomButton(zoomContainer.transform, "+", new Vector2(0, 130));
        citySelectorUI.heightText = CreateHeightText(zoomContainer.transform, new Vector2(0, 75));
        citySelectorUI.zoomOutButton = CreateZoomButton(zoomContainer.transform, "−", new Vector2(0, 20));

        GameObject resetButtonGO = new GameObject("ResetButton");
        resetButtonGO.transform.SetParent(canvasGO.transform, false);
        RectTransform resetRect = resetButtonGO.AddComponent<RectTransform>();
        resetRect.anchorMin = new Vector2(1, 1);
        resetRect.anchorMax = new Vector2(1, 1);
        resetRect.pivot = new Vector2(1, 1);
        resetRect.anchoredPosition = new Vector2(-20, -50);
        resetRect.sizeDelta = new Vector2(100, 50);
        
        Image resetBg = resetButtonGO.AddComponent<Image>();
        resetBg.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);
        
        Button resetButton = resetButtonGO.AddComponent<Button>();
        citySelectorUI.resetButton = resetButton;
        
        GameObject resetTextGO = new GameObject("Text");
        resetTextGO.transform.SetParent(resetButtonGO.transform, false);
        RectTransform resetTextRect = resetTextGO.AddComponent<RectTransform>();
        resetTextRect.anchorMin = Vector2.zero;
        resetTextRect.anchorMax = Vector2.one;
        resetTextRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI resetText = resetTextGO.AddComponent<TextMeshProUGUI>();
        resetText.text = "Reset";
        resetText.fontSize = 24;
        resetText.alignment = TextAlignmentOptions.Center;
        resetText.color = Color.white;
        
        GameObject centerButtonGO = new GameObject("CenterButton");
        centerButtonGO.transform.SetParent(canvasGO.transform, false);
        RectTransform centerRect = centerButtonGO.AddComponent<RectTransform>();
        centerRect.anchorMin = new Vector2(1, 1);
        centerRect.anchorMax = new Vector2(1, 1);
        centerRect.pivot = new Vector2(1, 1);
        centerRect.anchoredPosition = new Vector2(-20, -110);
        centerRect.sizeDelta = new Vector2(100, 50);
        
        Image centerBg = centerButtonGO.AddComponent<Image>();
        centerBg.color = new Color(0.2f, 0.5f, 0.8f, 0.9f);
        
        Button centerButton = centerButtonGO.AddComponent<Button>();
        citySelectorUI.centerButton = centerButton;
        
        GameObject centerTextGO = new GameObject("Text");
        centerTextGO.transform.SetParent(centerButtonGO.transform, false);
        RectTransform centerTextRect = centerTextGO.AddComponent<RectTransform>();
        centerTextRect.anchorMin = Vector2.zero;
        centerTextRect.anchorMax = Vector2.one;
        centerTextRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI centerText = centerTextGO.AddComponent<TextMeshProUGUI>();
        centerText.text = "Center";
        centerText.fontSize = 24;
        centerText.alignment = TextAlignmentOptions.Center;
        centerText.color = Color.white;
        
        GameObject exportButtonGO = new GameObject("ExportButton");
        exportButtonGO.transform.SetParent(canvasGO.transform, false);
        RectTransform exportRect = exportButtonGO.AddComponent<RectTransform>();
        exportRect.anchorMin = new Vector2(1, 1);
        exportRect.anchorMax = new Vector2(1, 1);
        exportRect.pivot = new Vector2(1, 1);
        exportRect.anchoredPosition = new Vector2(-20, -170);
        exportRect.sizeDelta = new Vector2(100, 50);
        
        Image exportBg = exportButtonGO.AddComponent<Image>();
        exportBg.color = new Color(0.2f, 0.7f, 0.3f, 0.9f);
        
        Button exportButton = exportButtonGO.AddComponent<Button>();
        citySelectorUI.exportButton = exportButton;
        
        GameObject exportTextGO = new GameObject("Text");
        exportTextGO.transform.SetParent(exportButtonGO.transform, false);
        RectTransform exportTextRect = exportTextGO.AddComponent<RectTransform>();
        exportTextRect.anchorMin = Vector2.zero;
        exportTextRect.anchorMax = Vector2.one;
        exportTextRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI exportText = exportTextGO.AddComponent<TextMeshProUGUI>();
        exportText.text = "Export";
        exportText.fontSize = 22;
        exportText.alignment = TextAlignmentOptions.Center;
        exportText.color = Color.white;
        
        GameObject screenshotButtonGO = new GameObject("ScreenshotButton");
        screenshotButtonGO.transform.SetParent(canvasGO.transform, false);
        RectTransform screenshotRect = screenshotButtonGO.AddComponent<RectTransform>();
        screenshotRect.anchorMin = new Vector2(1, 1);
        screenshotRect.anchorMax = new Vector2(1, 1);
        screenshotRect.pivot = new Vector2(1, 1);
        screenshotRect.anchoredPosition = new Vector2(-20, -230);
        screenshotRect.sizeDelta = new Vector2(100, 50);
        
        Image screenshotBg = screenshotButtonGO.AddComponent<Image>();
        screenshotBg.color = new Color(0.3f, 0.5f, 0.8f, 0.9f);
        
        Button screenshotButton = screenshotButtonGO.AddComponent<Button>();
        citySelectorUI.screenshotButton = screenshotButton;
        
        GameObject screenshotTextGO = new GameObject("Text");
        screenshotTextGO.transform.SetParent(screenshotButtonGO.transform, false);
        RectTransform screenshotTextRect = screenshotTextGO.AddComponent<RectTransform>();
        screenshotTextRect.anchorMin = Vector2.zero;
        screenshotTextRect.anchorMax = Vector2.one;
        screenshotTextRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI screenshotText = screenshotTextGO.AddComponent<TextMeshProUGUI>();
        screenshotText.text = "Photo";
        screenshotText.fontSize = 22;
        screenshotText.alignment = TextAlignmentOptions.Center;
        screenshotText.color = Color.white;
        
        GameObject infoTextGO = new GameObject("InfoText");
        infoTextGO.transform.SetParent(canvasGO.transform, false);
        RectTransform infoTextRect = infoTextGO.AddComponent<RectTransform>();
        infoTextRect.anchorMin = new Vector2(0, 1);
        infoTextRect.anchorMax = new Vector2(0, 1);
        infoTextRect.pivot = new Vector2(0, 1);
        infoTextRect.anchoredPosition = new Vector2(10, -120);
        infoTextRect.sizeDelta = new Vector2(400, 60);
        
        TextMeshProUGUI infoText = infoTextGO.AddComponent<TextMeshProUGUI>();
        infoText.text = "";
        infoText.fontSize = 18;
        infoText.alignment = TextAlignmentOptions.TopLeft;
        infoText.color = Color.white;
        infoText.enableWordWrapping = true;
        infoText.richText = true;
        
        citySelectorUI.infoText = infoText;

        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            GameObject eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        string[] guids = AssetDatabase.FindAssets("t:CityProfile", new[] { "Assets/Resources/FloridaCities" });
        List<CityProfile> loadedCities = new List<CityProfile>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            CityProfile city = AssetDatabase.LoadAssetAtPath<CityProfile>(path);
            if (city != null)
            {
                loadedCities.Add(city);
                Debug.Log($"[CesiumExtractorSceneBuilder] Loaded city: {city.cityName}");
            }
        }
        
        loadedCities.Sort((a, b) => a.cityName.CompareTo(b.cityName));
        
        citySelectorUI.cities = loadedCities;
        Debug.Log($"[CesiumExtractorSceneBuilder] Assigned {loadedCities.Count} cities to CitySelectorUI");
        
        citySelectorUI.defaultCityName = "Gainesville";

        string scenePath = "Assets/UnityCesiumExtractor/Scenes/UnityCesiumExtractor.unity";
        EditorSceneManager.SaveScene(newScene, scenePath);
        
        Debug.Log($"[CesiumExtractorSceneBuilder] Scene built and saved at {scenePath}");
    }
    
    static Button CreateDPadButton(Transform parent, string text, Vector2 position)
    {
        GameObject buttonGO = new GameObject($"DPad_{text}");
        buttonGO.transform.SetParent(parent, false);
        
        RectTransform rect = buttonGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0, 0);
        rect.pivot = new Vector2(0, 0);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(50, 50);
        
        Image bg = buttonGO.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.85f);
        
        Button button = buttonGO.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.85f);
        colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        button.colors = colors;
        
        UnityEngine.UI.Shadow shadow = buttonGO.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.3f);
        shadow.effectDistance = new Vector2(2, -2);
        
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 28;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.black;
        
        return button;
    }
    
    static Button CreateZoomButton(Transform parent, string text, Vector2 position)
    {
        GameObject buttonGO = new GameObject($"Zoom_{text}");
        buttonGO.transform.SetParent(parent, false);
        
        RectTransform rect = buttonGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0, 0);
        rect.pivot = new Vector2(0, 0);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(60, 50);
        
        Image bg = buttonGO.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.85f);
        
        Button button = buttonGO.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.85f);
        colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        button.colors = colors;
        
        UnityEngine.UI.Shadow shadow = buttonGO.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.3f);
        shadow.effectDistance = new Vector2(2, -2);
        
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 36;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.black;
        tmp.fontStyle = FontStyles.Bold;
        
        return button;
    }
    
    static TextMeshProUGUI CreateHeightText(Transform parent, Vector2 position)
    {
        GameObject textGO = new GameObject("HeightText");
        textGO.transform.SetParent(parent, false);
        
        RectTransform rect = textGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0, 0);
        rect.pivot = new Vector2(0, 0);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(80, 40);
        
        Image bg = textGO.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.5f);
        
        GameObject textChild = new GameObject("Text");
        textChild.transform.SetParent(textGO.transform, false);
        RectTransform textRect = textChild.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        
        TextMeshProUGUI tmp = textChild.AddComponent<TextMeshProUGUI>();
        tmp.text = "152 m";
        tmp.fontSize = 16;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        
        return tmp;
    }
}
