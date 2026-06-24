using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

/// <summary>
/// Debug script to visualize AR content placement and verify it's not following the camera.
/// Add this to the GeospatialContainer to see debug info.
/// </summary>
public class ARContentDebugger : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool showDebugInfo = true;
    public bool drawGizmos = true;
    
    private Vector3 initialPosition;
    private bool hasInitialPosition = false;
    private Camera arCamera;
    private XROrigin xrOrigin;
    
    void Start()
    {
        arCamera = Camera.main;
        xrOrigin = FindObjectOfType<XROrigin>();
    }
    
    void Update()
    {
        if (!gameObject.activeInHierarchy) return;
        
        // Don't track while at hidden position (y < -100)
        if (transform.position.y < -100)
        {
            hasInitialPosition = false;
            return;
        }
        
        // Capture initial position when first placed (not at hidden position)
        if (!hasInitialPosition)
        {
            initialPosition = transform.position;
            hasInitialPosition = true;
            Debug.Log($"[ARDebug] Content placed at: {initialPosition}");
        }
        
        // Check if position has drifted from placed position
        float drift = Vector3.Distance(transform.position, initialPosition);
        if (drift > 0.001f)
        {
            Debug.LogWarning($"[ARDebug] Content has drifted {drift:F4}m from placed position!");
            Debug.LogWarning($"[ARDebug] Placed: {initialPosition}, Current: {transform.position}");
        }
    }
    
    // OnGUI has been moved to CitySelectorUI.UpdateDebugDisplay() to use TextMeshProUGUI
    // This keeps debug info in the UI Canvas so it doesn't appear in screenshots
    // The old OnGUI code is kept commented for reference:
    /*
    void OnGUI()
    {
        if (!showDebugInfo || !gameObject.activeInHierarchy) return;
        
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 14;
        style.normal.textColor = Color.yellow;
        
        float y = 120;
        float lineHeight = 20;
        
        bool isHidden = transform.position.y < -100;
        
        GUI.Label(new Rect(10, y, 500, lineHeight), $"=== AR Content Debug ===", style);
        y += lineHeight;
        
        // ... rest of debug display moved to CitySelectorUI
    }
    */
    
    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        
        // Draw content position marker
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.15f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.5f);
        
        // Draw initial position if we have it
        if (hasInitialPosition)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(initialPosition, 0.1f);
        }
        
        // Draw line from camera to content
        if (Camera.main != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(Camera.main.transform.position, transform.position);
        }
    }
}
