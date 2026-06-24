using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Handles city placement on AR planes, pinch-to-zoom, and drag-to-pan interaction.
/// For "Godzilla-scale" floor overlay experience.
/// </summary>
public class CityPlacementController : MonoBehaviour
{
    [Header("AR Components")]
    public ARRaycastManager raycastManager;
    public ARPlaneManager planeManager;
    
    [Header("Controller")]
    public CesiumTabletopController controller;
    
    [Header("City Selection")]
    public CityProfile currentCity;
    
    [Header("Auto-Placement Settings")]
    [Tooltip("Automatically place the city when a plane is detected")]
    public bool autoPlaceOnDetection = true;
    
    [Tooltip("Minimum plane area (m²) required for auto-placement")]
    public float minPlaneArea = 0.5f;
    
    [Header("Manual Placement")]
    [Tooltip("If auto-place is off, allow tap to place")]
    public bool allowTapToPlace = true;
    
    [Tooltip("Allow repositioning after placement")]
    public bool allowRepositioning = false;
    
    [Header("Drag-to-Pan Settings")]
    [Tooltip("Enable dragging to pan around the city")]
    public bool enableDragToPan = true;
    
    [Tooltip("Minimum drag distance to start panning (pixels)")]
    public float dragThreshold = 10f;

    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private float initialPinchDistance;
    private bool isProcessingPlacement = false;
    private bool hasAutoPlaced = false;
    
    // Drag tracking
    private bool isDragging = false;
    private Vector2 dragStartPosition;
    private Vector2 lastDragPosition;
    private float lastTapTime = 0f;
    private Vector2 lastTapPosition = Vector2.zero;
    private const float doubleTapMaxDelay = 0.3f;
    private const float doubleTapMaxDistance = 40f; // pixels

    void OnEnable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged += OnPlanesChanged;
        }
    }

    void OnDisable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (autoPlaceOnDetection && !hasAutoPlaced && currentCity != null && controller != null)
        {
            foreach (var plane in args.added)
            {
                if (IsSuitablePlane(plane))
                {
                    AutoPlaceOnPlane(plane);
                    return;
                }
            }
            
            foreach (var plane in args.updated)
            {
                if (IsSuitablePlane(plane))
                {
                    AutoPlaceOnPlane(plane);
                    return;
                }
            }
        }
    }

    bool IsSuitablePlane(ARPlane plane)
    {
        if (plane.alignment != PlaneAlignment.HorizontalUp &&
            plane.alignment != PlaneAlignment.HorizontalDown)
        {
            return false;
        }

        float area = plane.size.x * plane.size.y;
        return area >= minPlaneArea;
    }

    async void AutoPlaceOnPlane(ARPlane plane)
    {
        if (isProcessingPlacement || hasAutoPlaced) return;
        if (currentCity == null || controller == null) return;

        isProcessingPlacement = true;
        hasAutoPlaced = true;

        Debug.Log($"[CityPlacement] Auto-placing {currentCity.cityName} on detected plane");

        try
        {
            controller.InitializeCity(currentCity);
            
            Vector3 placePosition = plane.transform.position;
            Quaternion placeRotation = plane.transform.rotation;

            await controller.PlaceCityAtPosition(placePosition, placeRotation);
            HidePlaneVisuals();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CityPlacement] Auto-placement failed: {ex.Message}");
            hasAutoPlaced = false;
        }
        finally
        {
            isProcessingPlacement = false;
        }
    }

    void Update()
    {
        if (controller == null) return;

        // Handle touch input
        if (Input.touchCount == 2 && controller.IsCityPlaced)
        {
            // Two fingers = pinch to zoom
            HandlePinchToScale();
            isDragging = false;
            return;
        }

        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);
            
            // Check if touching UI
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(touch.fingerId))
            {
                isDragging = false;
                return;
            }
            
            if (controller.IsCityPlaced && enableDragToPan)
            {
                // One finger on placed city = drag to pan
                HandleDragToPan(touch);
                HandleDoubleTap(touch);
            }
            else if (allowTapToPlace && !autoPlaceOnDetection)
            {
                // One finger tap = place city
                HandleTapToPlace();
            }
        }
        else
        {
            isDragging = false;
        }
        
        // Mouse input for editor testing
        #if UNITY_EDITOR
        HandleMouseInput();
        #endif
    }
    
    void HandleMouseInput()
    {
        if (controller == null || !controller.IsCityPlaced) return;
        
        // Check if over UI
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            isDragging = false;
            return;
        }
        
        // Mouse scroll for zoom (using Input System compatible method)
        Vector2 scrollDelta = UnityEngine.InputSystem.Mouse.current?.scroll.ReadValue() ?? Vector2.zero;
        if (Mathf.Abs(scrollDelta.y) > 0.01f)
        {
            float zoomFactor = 1f + scrollDelta.y * 0.001f;
            controller.RescaleWorld(zoomFactor);
        }
        
        // Mouse drag for pan
        if (enableDragToPan)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            
            if (mouse.leftButton.wasPressedThisFrame)
            {
                isDragging = true;
                dragStartPosition = mouse.position.ReadValue();
                lastDragPosition = dragStartPosition;
            }
            else if (mouse.leftButton.isPressed && isDragging)
            {
                Vector2 currentPos = mouse.position.ReadValue();
                Vector2 delta = currentPos - lastDragPosition;
                
                if (delta.magnitude > 0.1f)
                {
                    controller.PanView(delta);
                    lastDragPosition = currentPos;
                }
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                isDragging = false;
            }
        }

        // Double-click to focus when not dragging
        var mouseForFocus = UnityEngine.InputSystem.Mouse.current;
        if (mouseForFocus != null && !isDragging && mouseForFocus.leftButton.wasPressedThisFrame)
        {
            float t = Time.unscaledTime;
            Vector2 pos = mouseForFocus.position.ReadValue();
            if (t - lastTapTime <= doubleTapMaxDelay &&
                (pos - lastTapPosition).sqrMagnitude <= doubleTapMaxDistance * doubleTapMaxDistance)
            {
                controller.FocusAtScreenPoint(pos);
            }
            lastTapTime = t;
            lastTapPosition = pos;
        }
    }

    void HandlePinchToScale()
    {
        UnityEngine.Touch t1 = Input.GetTouch(0);
        UnityEngine.Touch t2 = Input.GetTouch(1);

        if (t1.phase == UnityEngine.TouchPhase.Began || t2.phase == UnityEngine.TouchPhase.Began)
        {
            initialPinchDistance = Vector2.Distance(t1.position, t2.position);
        }
        else if (t1.phase == UnityEngine.TouchPhase.Moved || t2.phase == UnityEngine.TouchPhase.Moved)
        {
            float currentDistance = Vector2.Distance(t1.position, t2.position);
            if (Mathf.Approximately(initialPinchDistance, 0)) return;

            float factor = currentDistance / initialPinchDistance;
            controller.RescaleWorld(factor);
            initialPinchDistance = currentDistance;
        }
    }
    
    void HandleDragToPan(UnityEngine.Touch touch)
    {
        switch (touch.phase)
        {
            case UnityEngine.TouchPhase.Began:
                dragStartPosition = touch.position;
                lastDragPosition = touch.position;
                isDragging = false; // Will become true after threshold
                break;
                
            case UnityEngine.TouchPhase.Moved:
                Vector2 currentPos = touch.position;
                
                // Check if we've exceeded drag threshold
                if (!isDragging)
                {
                    if (Vector2.Distance(currentPos, dragStartPosition) > dragThreshold)
                    {
                        isDragging = true;
                        lastDragPosition = currentPos;
                    }
                }
                
                if (isDragging)
                {
                    Vector2 delta = currentPos - lastDragPosition;
                    controller.PanView(delta);
                    lastDragPosition = currentPos;
                }
                break;
                
            case UnityEngine.TouchPhase.Ended:
            case UnityEngine.TouchPhase.Canceled:
                isDragging = false;
                break;
        }
    }

    void HandleDoubleTap(UnityEngine.Touch touch)
    {
        if (touch.phase != UnityEngine.TouchPhase.Began) return;
        if (controller == null || !controller.IsCityPlaced) return;
        if (isDragging) return;

        float t = Time.unscaledTime;
        Vector2 pos = touch.position;
        if (t - lastTapTime <= doubleTapMaxDelay &&
            (pos - lastTapPosition).sqrMagnitude <= doubleTapMaxDistance * doubleTapMaxDistance)
        {
            controller.FocusAtScreenPoint(pos);
        }
        lastTapTime = t;
        lastTapPosition = pos;
    }

    void HandleTapToPlace()
    {
        UnityEngine.Touch touch = Input.GetTouch(0);

        if (touch.phase != UnityEngine.TouchPhase.Began) return;
        if (isProcessingPlacement) return;
        if (controller.IsCityPlaced && !allowRepositioning) return;

        if (raycastManager.Raycast(touch.position, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;

            if (currentCity != null)
            {
                _ = ManualPlaceCity(hitPose);
            }
        }
    }

    async System.Threading.Tasks.Task ManualPlaceCity(Pose hitPose)
    {
        isProcessingPlacement = true;

        try
        {
            controller.InitializeCity(currentCity);
            await controller.PlaceCityAtPosition(hitPose.position, hitPose.rotation);
            HidePlaneVisuals();
        }
        finally
        {
            isProcessingPlacement = false;
        }
    }

    void HidePlaneVisuals()
    {
        if (planeManager != null)
        {
            foreach (var plane in planeManager.trackables)
            {
                plane.gameObject.SetActive(false);
            }
        }
    }

    public void SetCurrentCity(CityProfile city)
    {
        currentCity = city;
        if (city == null) return;

        Debug.Log($"[CityPlacement] City set to: {city.cityName}");

        if (controller != null && controller.IsCityPlaced)
        {
            controller.SwitchCity(city);
        }
    }

    public void ResetPlacement()
    {
        hasAutoPlaced = false;
        isProcessingPlacement = false;
        isDragging = false;

        if (planeManager != null)
        {
            foreach (var plane in planeManager.trackables)
            {
                plane.gameObject.SetActive(true);
            }
        }
    }
    
    /// <summary>
    /// Return the view to the city center
    /// </summary>
    public void ReturnToCenter()
    {
        if (controller != null)
        {
            controller.ReturnToCenter();
        }
    }
}
