using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine.Splines;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

/// <summary>
/// Manages placement and georeferencing of 3D city for "Godzilla-scale" AR experience.
/// Content is placed as a floor overlay - user walks around like a giant over the city.
/// Supports drag-to-pan to move around the city view.
/// </summary>
public class CesiumTabletopController : MonoBehaviour
{
    [Header("Cesium Components")]
    public CesiumGeoreference georeference;
    public Cesium3DTileset tileset;
    public Transform contentContainer;

    [Header("AR Components")]
    public ARAnchorManager anchorManager;
    public XROrigin xrOrigin;
    private Camera arCamera;

    [Header("View Settings")]
    [Tooltip("Georeference height - set to 0 for ground-level tile loading, higher values shift the center point up")]
    public float viewHeightMeters = 0f; // Ground level for AR - tiles centered at street level
    
    [Tooltip("Scale of the miniature city (0.01 = 1:100 for easier debugging)")]
    public float cityScale = 0.01f; // Increased from 0.002 for better visibility
    
    public float minScale = 0.0005f;
    public float maxScale = 0.01f;
    
    [Header("Pan Settings")]
    [Tooltip("How fast dragging moves the city view (degrees per screen unit)")]
    public float panSensitivity = 0.0001f;
    
    [Header("Placement Settings")]
    [Tooltip("Height offset above AR plane to ensure tiles are visible (in Unity units)")]
    public float placementHeightOffset = 0.1f;

    [Header("Clipping Settings")]
    [Tooltip("Base size of the clipping box (meters) around the view center")]
    public float clippingBoxSizeMeters = 500f;
    [Tooltip("Scale clipping size by current height so zooming out loads a larger area")]
    public bool scaleClippingWithHeight = true;
    [Tooltip("When true, only tiles inside the box are shown; outside tiles culled")]
    public bool invertClippingSelection = true;

    [Header("Plane Visibility")]
    [Tooltip("Plane manager to auto-hide generated AR planes")]
    public ARPlaneManager planeManager;
    [Tooltip("Create an invisible collider to allow screen taps to hit the tileset")]
    public bool addHitCollider = true;

    [Header("Events")]
    public CitySelectorUI citySelectorUI;

    private bool isCityPlaced = false;
    private bool isGeoreferenceReady = false;
    private CityProfile currentCity;
    private CityProfile pendingCity;
    
    // Current view position (can be panned from original city center)
    private double currentLongitude;
    private double currentLatitude;
    private double currentHeight;
    
    // Store the placed position in world space
    private Vector3 placedWorldPosition;
    
    // Hide position (far away until placed)
    private readonly Vector3 hiddenPosition = new Vector3(0, -1000, 0);

    // Clipping helpers
    private CesiumPolygonRasterOverlay clippingOverlay;
    private CesiumCartographicPolygon clippingPolygon;
    private BoxCollider hitCollider;
    private float baseCityScale;

    public bool IsCityPlaced => isCityPlaced;
    public double CurrentLongitude => currentLongitude;
    public double CurrentLatitude => currentLatitude;
    public Camera ArCamera => arCamera;

    void Awake()
    {
        if (xrOrigin == null)
            xrOrigin = FindObjectOfType<XROrigin>();

        arCamera = Camera.main;
        AdjustCameraClipPlanes();

        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();

        // DON'T hide the container - we need georeference to initialize
        // Instead, move it far away and make tileset invisible
        if (contentContainer != null)
        {
            contentContainer.position = hiddenPosition;
            contentContainer.gameObject.SetActive(true); // Keep active for initialization!
        }
        
        // Keep georeference active but hide the tileset
        if (tileset != null)
        {
            tileset.createPhysicsMeshes = true; // enable colliders for raycast focus
            tileset.gameObject.SetActive(false);
        }

        baseCityScale = cityScale;

        // CRITICAL: Remove any Cesium camera-tracking components
        RemoveCesiumCameraTracking();

        if (planeManager != null)
        {
            planeManager.planesChanged += OnPlanesChanged;
        }

        // Create a large invisible collider on the georeference to catch taps/clicks
        if (addHitCollider && georeference != null && georeference.GetComponent<Collider>() == null)
        {
            hitCollider = georeference.gameObject.AddComponent<BoxCollider>();
            hitCollider.isTrigger = true;
            hitCollider.center = Vector3.zero;
            hitCollider.size = Vector3.one * (clippingBoxSizeMeters * 10f);
        }
    }

    void RemoveCesiumCameraTracking()
    {
        // Don't remove CesiumOriginShift - we NEED it for proper tile streaming!
        // But we should remove CesiumCameraController which would take over camera movement
        foreach (var cam in FindObjectsOfType<Camera>(true))
        {
            var controller = cam.GetComponent<CesiumCameraController>();
            if (controller != null)
            {
                Debug.Log($"[UnityCesiumExtractor] Removing CesiumCameraController from {cam.name}");
                DestroyImmediate(controller);
            }
        }
    }
    
    /// <summary>
    /// For AR tabletop viewing, we DON'T want origin shifting because that would
    /// move the content in world space. Instead, we set a high-altitude view
    /// which loads tiles for a wider area and enables viewing from any angle.
    /// </summary>
    void SetupForTabletopViewing()
    {
        // Set view height high enough to load tiles for a good overview
        // This makes tiles load for the whole area, not just directly below
        currentHeight = 500; // 500m for a good overview, tiles load for wider area
        
        try
        {
            georeference.SetOriginLongitudeLatitudeHeight(
                currentLongitude,
                currentLatitude,
                currentHeight
            );
            Debug.Log($"[UnityCesiumExtractor] Set tabletop view height to {currentHeight}m for better tile coverage");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityCesiumExtractor] Could not set tabletop view: {ex.Message}");
        }
        
        // Add a DEBUG CUBE to verify 3D rendering works correctly
        // If the cube rotates with camera movement but Cesium doesn't, the issue is Cesium-specific
        CreateDebugCube();

        // Initialize clipping
        UpdateClippingBox();
    }
    
    void CreateDebugCube()
    {
        // Create a visible cube at the content origin for debugging
        GameObject debugCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        debugCube.name = "DEBUG_ViewTest_Cube";
        debugCube.transform.SetParent(contentContainer, false);
        debugCube.transform.localPosition = Vector3.up * 0.5f; // Slightly above origin
        debugCube.transform.localScale = Vector3.one * 0.3f; // 30cm cube (before city scale)
        
        // Make it bright red so it's easy to see
        var renderer = debugCube.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            renderer.material.color = Color.red;
        }
        
        Debug.Log("[UnityCesiumExtractor] Created DEBUG CUBE - if this cube rotates with camera but Cesium doesn't, the issue is Cesium-specific");
    }

    // Fixed rotation to prevent any camera-facing behavior
    private Quaternion fixedRotation = Quaternion.identity;
    private bool rotationLocked = false;
    
    void Start()
    {
        // Check Cesium Ion setup
        Debug.Log($"[UnityCesiumExtractor] Georeference: {(georeference != null ? "OK" : "NULL")}");
        Debug.Log($"[UnityCesiumExtractor] Tileset: {(tileset != null ? "OK" : "NULL")}");
        Debug.Log($"[UnityCesiumExtractor] Content Container: {(contentContainer != null ? "OK" : "NULL")}");
        
        if (tileset != null)
        {
            Debug.Log($"[UnityCesiumExtractor] Tileset Ion Asset ID: {tileset.ionAssetID}");
        }
        
        StartCoroutine(WaitForGeoreferenceInitialization());
    }
    
    private int frameCounter = 0;
    
    void LateUpdate()
    {
        // Continuously check and remove any Cesium camera-following components
        CheckAndRemoveCesiumCameraTracking();
        
        // Ensure content stays at fixed position and rotation when placed
        if (isCityPlaced && contentContainer != null)
        {
            // Lock rotation to prevent any billboard/facing behavior
            if (rotationLocked)
            {
                contentContainer.rotation = fixedRotation;
            }
            
            // Also lock the georeference rotation
            if (georeference != null)
            {
                georeference.transform.localRotation = Quaternion.identity;
            }
            
            // Ensure still at scene root
            if (contentContainer.parent != null)
            {
                Debug.LogWarning("[UnityCesiumExtractor] Content was re-parented! Fixing...");
                contentContainer.SetParent(null, true);
            }
            
            // Periodic debug logging (every 5 seconds)
            frameCounter++;
            if (frameCounter % 300 == 0)
            {
                Debug.Log($"[UnityCesiumExtractor] Content pos: {contentContainer.position}, rot: {contentContainer.rotation.eulerAngles}");
                if (georeference != null)
                {
                    Debug.Log($"[UnityCesiumExtractor] Georef pos: {georeference.transform.position}, rot: {georeference.transform.rotation.eulerAngles}");
                }
            }
        }
    }
    
    void CheckAndRemoveCesiumCameraTracking()
    {
        // Only remove CesiumCameraController - we WANT CesiumOriginShift for tile streaming
        // and CesiumGlobeAnchor gets auto-added with origin shift
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            var controller = mainCam.GetComponent<CesiumCameraController>();
            if (controller != null)
            {
                Debug.LogWarning("[UnityCesiumExtractor] Found CesiumCameraController on camera - removing!");
                Destroy(controller);
            }
        }
    }

    System.Collections.IEnumerator WaitForGeoreferenceInitialization()
    {
        // Wait for Cesium to initialize (it needs a few frames even when active)
        yield return new WaitForSeconds(1.5f);
        
        // Ensure content is at scene root but hidden position
        if (contentContainer != null && contentContainer.parent != null)
        {
            contentContainer.SetParent(null, true);
        }
        
        isGeoreferenceReady = true;
        Debug.Log("[UnityCesiumExtractor] CesiumGeoreference ready");
        
        // Process any pending city
        if (pendingCity != null)
        {
            SetCityCoordinates(pendingCity);
            pendingCity = null;
        }
    }

    public void InitializeCity(CityProfile city)
    {
        if (city == null) return;
        currentCity = city;
        
        // Store city coordinates as current view position
        currentLongitude = city.longitude;
        currentLatitude = city.latitude;
        currentHeight = city.defaultHeight > 0 ? city.defaultHeight : viewHeightMeters;
        
        if (!isGeoreferenceReady)
        {
            pendingCity = city;
            Debug.Log($"[UnityCesiumExtractor] Queuing {city.cityName} until georeference ready");
            return;
        }
        
        SetCityCoordinates(city);
    }

    void SetCityCoordinates(CityProfile city)
    {
        if (georeference == null) return;
        
        // Don't check isGeoreferenceReady here - if we got here, we should try
        try
        {
            currentLongitude = city.longitude;
            currentLatitude = city.latitude;
            currentHeight = city.defaultHeight > 0 ? city.defaultHeight : viewHeightMeters;
            
            ApplyGeoreferenceOriginAndAlign();
            Debug.Log($"[UnityCesiumExtractor] Set coordinates: {city.cityName} at {currentLatitude}, {currentLongitude}");

            // Refresh clipping whenever origin changes
            UpdateClippingBox();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UnityCesiumExtractor] Coordinate error: {ex.Message}");
        }
    }

    /// <summary>
    /// Pan the city view by adjusting longitude/latitude.
    /// Called when user drags on screen.
    /// </summary>
    public void PanView(Vector2 screenDelta)
    {
        if (!isCityPlaced || georeference == null) return;
        
        // Convert screen delta to geographic delta in meters, then to degrees
        // Negative X: drag right shows content to the right
        double metersPerPixel = clippingBoxSizeMeters * 0.001; // tune sensitivity
        double offsetEastMeters = -screenDelta.x * metersPerPixel;
        double offsetNorthMeters = screenDelta.y * metersPerPixel;

        double latKmPerDegree = 111.0;
        double lonKmPerDegree = Math.Max(1e-6, 111.0 * Math.Cos(currentLatitude * Math.PI / 180.0));

        double lonDelta = (offsetEastMeters / 1000.0) / lonKmPerDegree;
        double latDelta = (offsetNorthMeters / 1000.0) / latKmPerDegree;
        
        currentLongitude += lonDelta;
        currentLatitude += latDelta;
        
        currentLatitude = Math.Clamp(currentLatitude, -85.0, 85.0);
        if (currentLongitude > 180) currentLongitude -= 360;
        if (currentLongitude < -180) currentLongitude += 360;
        
        ApplyGeoreferenceOriginAndAlign();

        UpdateClippingBox();
        RecenterContent();
    }

    public async Task PlaceCityAtPosition(Vector3 worldPosition, Quaternion rotation)
    {
        if (currentCity == null)
        {
            Debug.LogError("[UnityCesiumExtractor] No city set");
            return;
        }

        // Wait for georeference to be ready
        float waitTime = 0f;
        while (!isGeoreferenceReady && waitTime < 10f)
        {
            await Task.Delay(100);
            waitTime += 0.1f;
        }
        
        if (!isGeoreferenceReady)
        {
            Debug.LogWarning("[UnityCesiumExtractor] Georeference still initializing, proceeding anyway...");
            isGeoreferenceReady = true; // Force it
        }

        try
        {
            // Add height offset to ensure tiles appear ABOVE the ground plane
            Vector3 placementPos = worldPosition + Vector3.up * placementHeightOffset;
            placedWorldPosition = placementPos;
            
            // Keep content at SCENE ROOT - fixed world position
            contentContainer.SetParent(null, false);
            
            // Move from hidden position to actual placement position
            contentContainer.position = placementPos;
            Debug.Log($"[UnityCesiumExtractor] Placing at {placementPos} (offset: {placementHeightOffset})");
            
            // Use a FIXED rotation - north facing forward, no camera tracking
            // This ensures the city stays oriented consistently regardless of camera position
            fixedRotation = Quaternion.identity; // North = forward
            contentContainer.rotation = fixedRotation;
            rotationLocked = true;
            Debug.Log($"[UnityCesiumExtractor] Rotation locked at {fixedRotation.eulerAngles}");
            
            // Apply scale
        cityScale = baseCityScale;
        contentContainer.localScale = Vector3.one * baseCityScale;
            Debug.Log($"[UnityCesiumExtractor] Content scale: {contentContainer.localScale}");
            
            // Set city coordinates
            SetCityCoordinates(currentCity);
            
            // Make sure container is active and visible
            contentContainer.gameObject.SetActive(true);
            Debug.Log($"[UnityCesiumExtractor] Container active: {contentContainer.gameObject.activeInHierarchy}");
            
            // Enable tileset (this actually shows the tiles)
            if (tileset != null)
            {
                tileset.gameObject.SetActive(true);
                Debug.Log($"[UnityCesiumExtractor] Tileset active: {tileset.gameObject.activeInHierarchy}");
                Debug.Log($"[UnityCesiumExtractor] Tileset ionAssetID: {tileset.ionAssetID}");
            }
            else
            {
                Debug.LogError("[UnityCesiumExtractor] TILESET IS NULL!");
            }
            
            Debug.Log($"[UnityCesiumExtractor] Placed {currentCity.cityName} at world position {worldPosition}");
            Debug.Log($"[UnityCesiumExtractor] Georeference position: {georeference?.transform.position}");
            
            // Setup for tabletop viewing - high altitude for better tile coverage
            SetupForTabletopViewing();
            
            // Wait for tiles to load
            await Task.Delay(2000);
            
            // Adjust height based on terrain
            await AdjustHeightAboveTerrain();
            UpdateClippingBox();
            
            isCityPlaced = true;
            citySelectorUI?.OnCityPlaced();
            
            Debug.Log($"[UnityCesiumExtractor] {currentCity.cityName} placement complete");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UnityCesiumExtractor] Placement failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    async Task AdjustHeightAboveTerrain()
    {
        if (tileset == null || currentCity == null) return;
        
        try
        {
            var positions = new double3[] { new double3(currentLongitude, currentLatitude, 0) };
            var result = await tileset.SampleHeightMostDetailed(positions);
            
            if (result.sampleSuccess != null && result.sampleSuccess.Length > 0 && result.sampleSuccess[0])
            {
                double terrainHeight = result.longitudeLatitudeHeightPositions[0].z;
                currentHeight = terrainHeight + viewHeightMeters;
                
                georeference.SetOriginLongitudeLatitudeHeight(
                    currentLongitude,
                    currentLatitude,
                    currentHeight
                );
                
                Debug.Log($"[UnityCesiumExtractor] View height: {viewHeightMeters}m above terrain (terrain={terrainHeight}m)");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityCesiumExtractor] Height sampling failed: {ex.Message}");
        }
    }

    public async void SwitchCity(CityProfile newCity)
    {
        if (newCity == null) return;
        
        currentCity = newCity;
        currentLongitude = newCity.longitude;
        currentLatitude = newCity.latitude;
        currentHeight = viewHeightMeters;
        
        SetCityCoordinates(newCity);
        SetupForTabletopViewing();
        
        await Task.Delay(500);
        await AdjustHeightAboveTerrain();
        UpdateClippingBox();
        RecenterContent();
        
        citySelectorUI?.OnCityPlaced();
        Debug.Log($"[UnityCesiumExtractor] Switched to {newCity.cityName}");
    }
    
    /// <summary>
    /// Return to the original city center position
    /// </summary>
    public void ReturnToCenter()
    {
        if (currentCity == null) return;
        
        currentLongitude = currentCity.longitude;
        currentLatitude = currentCity.latitude;
        ApplyGeoreferenceOrigin();
        RecenterContent();
        Debug.Log($"[UnityCesiumExtractor] Returned to {currentCity.cityName} center");
    }
    
    /// <summary>
    /// Move the view in a cardinal direction by a fixed amount.
    /// Amount is scaled by current height - higher = larger steps.
    /// </summary>
    public void MoveView(Vector2 direction)
    {
        if (!isCityPlaced || georeference == null) return;
        
        // Move by a fraction of the clipping box size (in meters converted to degrees)
        double stepMeters = clippingBoxSizeMeters * 0.75; // move most of the box per click to force new tiles
        double latKmPerDegree = 111.0;
        double lonKmPerDegree = Math.Max(1e-6, 111.0 * Math.Cos(currentLatitude * Math.PI / 180.0));

        double lonDelta = (direction.x * stepMeters / 1000.0) / lonKmPerDegree;
        double latDelta = (direction.y * stepMeters / 1000.0) / latKmPerDegree;

        currentLongitude += lonDelta;
        currentLatitude += latDelta;
        
        // Clamp latitude
        currentLatitude = Math.Clamp(currentLatitude, -85.0, 85.0);
        
        // Wrap longitude
        if (currentLongitude > 180) currentLongitude -= 360;
        if (currentLongitude < -180) currentLongitude += 360;
        
        ApplyGeoreferenceOriginAndAlign();

        UpdateClippingBox();
        RecenterContent();
        HidePlaneVisuals();
    }
    
    /// <summary>
    /// Change the view height (altitude) by the specified amount in meters.
    /// Positive = zoom out (higher altitude), Negative = zoom in (lower altitude)
    /// </summary>
    public void ChangeHeight(double deltaMeters)
    {
        if (!isCityPlaced || georeference == null) return;

        // If zooming in from high altitude, try to focus on what the camera is looking at
        if (deltaMeters < 0 && currentHeight > 500.0)
        {
            if (TryFocusToCameraForward(out double lon, out double lat, out double h))
            {
                currentLongitude = lon;
                currentLatitude = lat;
                // Keep current height; just recenter. Zoom step will apply below.
                ApplyGeoreferenceOriginAndAlign();
                UpdateClippingBox();
                RecenterContent();
                HidePlaneVisuals();
            }
        }

        // Scale the step based on current altitude for faster navigation
        double sign = Math.Sign(deltaMeters);
        double magnitude = Math.Abs(deltaMeters);
        double tieredStep = magnitude;
        if (sign > 0)
        {
            // Zoom out: use larger jumps at higher altitudes
            if (currentHeight >= 100000.0) tieredStep = Math.Max(tieredStep, 100000.0); // 100 km
            else if (currentHeight >= 10000.0) tieredStep = Math.Max(tieredStep, 10000.0); // 10 km
            else if (currentHeight >= 1000.0) tieredStep = Math.Max(tieredStep, 1000.0); // 1 km
        }
        else if (sign < 0)
        {
            // Zoom in: still use tiered steps to move down quicker from very high altitudes
            if (currentHeight > 100000.0) tieredStep = Math.Max(tieredStep, 100000.0);
            else if (currentHeight > 10000.0) tieredStep = Math.Max(tieredStep, 10000.0);
            else if (currentHeight > 1000.0) tieredStep = Math.Max(tieredStep, 1000.0);
        }

        deltaMeters = tieredStep * sign;

        currentHeight += deltaMeters;
        
        // Clamp to reasonable range (10m to 500km)
        currentHeight = Math.Clamp(currentHeight, 10.0, 500000.0);
        
        ApplyGeoreferenceOriginAndAlign();
        Debug.Log($"[UnityCesiumExtractor] View height: {currentHeight}m");

        UpdateClippingBox();
        RecenterContent();
        HidePlaneVisuals();
        AdjustCameraClipPlanes();
    }
    
    /// <summary>
    /// Get current view height for UI display
    /// </summary>
    public double GetCurrentHeight() => currentHeight;

    public void ResetCity()
    {
        isCityPlaced = false;
        
        if (contentContainer != null)
        {
            contentContainer.SetParent(null, false);
            contentContainer.position = hiddenPosition; // Move back to hidden position
            contentContainer.localScale = Vector3.one;
        }
        
        if (tileset != null)
            tileset.gameObject.SetActive(false);
    }

    public void RescaleWorld(float factor)
    {
        if (!isCityPlaced || contentContainer == null) return;
        
        float newScale = Mathf.Clamp(contentContainer.localScale.x * factor, baseCityScale * 0.25f, baseCityScale * 4f);
        contentContainer.localScale = Vector3.one * newScale;
        cityScale = newScale;
    }

    /// <summary>
    /// Ensure clipping overlay and polygon exist, then update to a fixed-size box around origin.
    /// </summary>
    void UpdateClippingBox()
    {
        if (tileset == null || georeference == null || clippingBoxSizeMeters <= 0)
            return;

        EnsureClippingComponents();

        if (clippingPolygon == null)
            return;

        float size = clippingBoxSizeMeters;
        if (scaleClippingWithHeight)
        {
            // Grow the clip area as we zoom out, shrink as we zoom in. Clamp to avoid extremes.
            double baseH = Math.Max(10.0, currentCity != null && currentCity.defaultHeight > 0 ? currentCity.defaultHeight : viewHeightMeters);
            float factor = (float)(currentHeight / baseH);
            factor = Mathf.Clamp(factor, 1f, 5000f); // never shrink below base footprint
            size = clippingBoxSizeMeters * factor;
        }
        float half = size * 0.5f;
        var splineContainer = clippingPolygon.GetComponent<SplineContainer>();
        if (splineContainer == null)
            return;

        // Clear existing splines
        var splines = splineContainer.Splines;
        for (int i = splines.Count - 1; i >= 0; i--)
        {
            splineContainer.RemoveSpline(splines[i]);
        }

        Spline spline = new Spline();
        BezierKnot[] knots = new BezierKnot[]
        {
            new BezierKnot(new float3(-half, 0f, -half)),
            new BezierKnot(new float3(half, 0f, -half)),
            new BezierKnot(new float3(half, 0f, half)),
            new BezierKnot(new float3(-half, 0f, half)),
        };
        spline.Knots = knots;
        spline.Closed = true;
        spline.SetTangentMode(TangentMode.Linear);
        splineContainer.AddSpline(spline);

        // Make sure the overlay references this polygon
        if (clippingOverlay != null)
        {
            clippingOverlay.polygons = new List<CesiumCartographicPolygon> { clippingPolygon };
            clippingOverlay.invertSelection = invertClippingSelection;
            clippingOverlay.excludeSelectedTiles = true; // Cull tiles outside selection
            clippingOverlay.Refresh();
        }

        // Keep the polygon centered at the current origin
        if (clippingPolygon != null)
        {
            clippingPolygon.transform.localPosition = Vector3.zero;
            var anchor = clippingPolygon.GetComponent<CesiumGlobeAnchor>();
            if (anchor != null)
            {
                anchor.longitudeLatitudeHeight = new double3(currentLongitude, currentLatitude, currentHeight);
                anchor.rotationEastUpNorth = Quaternion.identity;
            }
        }

        // Resize hit collider to cover the new clip area
        if (hitCollider != null)
        {
            hitCollider.center = Vector3.zero;
            hitCollider.size = new Vector3(size * 2f, 2000f, size * 2f); // generous vertical size
        }
    }

    void EnsureClippingComponents()
    {
        // Add overlay to tileset if missing
        if (clippingOverlay == null && tileset != null)
        {
            clippingOverlay = tileset.GetComponent<CesiumPolygonRasterOverlay>();
            if (clippingOverlay == null)
            {
                clippingOverlay = tileset.gameObject.AddComponent<CesiumPolygonRasterOverlay>();
            }
            // Use clipping material key so the overlay only clips (no red/black tint)
            clippingOverlay.materialKey = "Clipping";
            clippingOverlay.invertSelection = invertClippingSelection;
            clippingOverlay.excludeSelectedTiles = true;
            clippingOverlay.showCreditsOnScreen = false;
        }

        // Add polygon child under georeference to define the clipping footprint
        if (clippingPolygon == null && georeference != null)
        {
            GameObject polyGO = new GameObject("ClippingPolygon");
            polyGO.transform.SetParent(georeference.transform, false);
            clippingPolygon = polyGO.AddComponent<CesiumCartographicPolygon>();
            // Anchor polygon to georeference so it follows origin shifts
            if (polyGO.GetComponent<CesiumGlobeAnchor>() == null)
            {
                polyGO.AddComponent<CesiumGlobeAnchor>();
            }
            clippingPolygon.transform.localPosition = Vector3.zero;
        }
    }

    /// <summary>
    /// Keep content anchored where it was placed; counteracts any drift after origin changes.
    /// </summary>
    void RecenterContent()
    {
        if (contentContainer == null || !isCityPlaced) return;
        contentContainer.SetParent(null, false);
        contentContainer.position = placedWorldPosition;
        if (rotationLocked) contentContainer.rotation = fixedRotation;
        HidePlaneVisuals();
    }

    /// <summary>
    /// Apply georeference origin and lock its world pose to the placed position.
    /// This prevents visual shifts when changing origin.
    /// </summary>
    void ApplyGeoreferenceOrigin()
    {
        if (georeference == null) return;
        georeference.SetOriginLongitudeLatitudeHeight(
            currentLongitude,
            currentLatitude,
            currentHeight
        );
    }

    /// <summary>
    /// Apply georeference origin and also align the georeference transform to the placed world position.
    /// Use this when changing origin to avoid initial drift on first navigation input.
    /// </summary>
    void ApplyGeoreferenceOriginAndAlign()
    {
        if (georeference == null) return;
        georeference.SetOriginLongitudeLatitudeHeight(
            currentLongitude,
            currentLatitude,
            currentHeight
        );
        if (isCityPlaced && contentContainer != null)
        {
            georeference.transform.position = placedWorldPosition;
            georeference.transform.rotation = Quaternion.identity;
        }
        HidePlaneVisuals();
    }

    /// <summary>
    /// Raycast from camera forward to tiles; if hit, convert to lon/lat/height for recentering.
    /// </summary>
    bool TryFocusToCameraForward(out double lon, out double lat, out double height)
    {
        lon = lat = 0;
        height = 0;

        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null || georeference == null) return false;

        Ray ray = new Ray(arCamera.transform.position, arCamera.transform.forward);
        RaycastHit hit;
        const float maxDist = 1_000_000f; // 1000 km

        if (Physics.Raycast(ray, out hit, maxDist))
        {
            // Skip AR planes
            if (hit.collider.GetComponent<ARPlane>() != null)
            {
                return false;
            }

            Vector3 worldHit = hit.point;
            // Convert world hit to georeference local, then to LLH
            Vector3 local = georeference.transform.InverseTransformPoint(worldHit);
            double3 localD = new double3(local.x, local.y, local.z);
            double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(localD);
            double3 llh = georeference.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef);

            lon = llh.x;
            lat = llh.y;
            height = llh.z;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Focus by screen point: raycast from camera through screen point to tiles and recenter.
    /// </summary>
    public bool FocusAtScreenPoint(Vector2 screenPoint)
    {
        double lon = 0, lat = 0, height = 0;

        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null || georeference == null) return false;

        Ray ray = arCamera.ScreenPointToRay(screenPoint);
        RaycastHit hit;
        const float maxDist = 1_000_000f; // 1000 km

        if (Physics.Raycast(ray, out hit, maxDist))
        {
            if (hit.collider.GetComponent<ARPlane>() != null)
                return false;

            Vector3 worldHit = hit.point;
            Vector3 local = georeference.transform.InverseTransformPoint(worldHit);
            double3 localD = new double3(local.x, local.y, local.z);
            double3 ecef = georeference.TransformUnityPositionToEarthCenteredEarthFixed(localD);
            double3 llh = georeference.ellipsoid.CenteredFixedToLongitudeLatitudeHeight(ecef);

            currentLongitude = llh.x;
            currentLatitude = llh.y;
            currentHeight = Math.Max(10.0, llh.z + viewHeightMeters);

            ApplyGeoreferenceOriginAndAlign();
            UpdateClippingBox();
            RecenterContent();
            HidePlaneVisuals();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Disable/hide AR plane visuals so they don't occlude Cesium content.
    /// </summary>
    void HidePlaneVisuals()
    {
        var planes = FindObjectsOfType<ARPlane>();
        foreach (var p in planes)
        {
            var mr = p.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
            var lr = p.GetComponent<LineRenderer>();
            if (lr != null) lr.enabled = false;
            var pmv = p.GetComponent<ARPlaneMeshVisualizer>();
            if (pmv != null) pmv.enabled = false;
            p.gameObject.SetActive(false);
        }

        // Also hide ARPlaneManager's renderer prefab if any
        if (planeManager != null && planeManager.planePrefab != null)
        {
            var rend = planeManager.planePrefab.GetComponent<MeshRenderer>();
            if (rend != null) rend.enabled = false;
            var lr = planeManager.planePrefab.GetComponent<LineRenderer>();
            if (lr != null) lr.enabled = false;
            var pmv = planeManager.planePrefab.GetComponent<ARPlaneMeshVisualizer>();
            if (pmv != null) pmv.enabled = false;
        }
    }

    void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Hide newly added/updated planes immediately
        foreach (var p in args.added)
        {
            if (p != null) p.gameObject.SetActive(false);
        }
        foreach (var p in args.updated)
        {
            if (p != null) p.gameObject.SetActive(false);
        }
    }

    void AdjustCameraClipPlanes()
    {
        if (arCamera == null) return;
        arCamera.nearClipPlane = 0.05f;
        arCamera.farClipPlane = 1_000_000f; // 1000 km far clip to support high zoom-out
    }

    void OnDrawGizmos()
    {
        if (isCityPlaced && contentContainer != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(contentContainer.position, 0.15f);
            Gizmos.DrawLine(contentContainer.position, contentContainer.position + Vector3.up * 0.5f);
        }
    }

    void OnDestroy()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }
}
