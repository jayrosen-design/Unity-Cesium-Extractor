using UnityEngine;

[CreateAssetMenu(fileName = "NewCityProfile", menuName = "Unity Cesium Extractor/City Profile")]
public class CityProfile : ScriptableObject
{
    [Header("Metadata")]
    public string cityName;
    public string description;
    public Sprite thumbnail;

    [Header("Geospatial Coordinates")]
    public double latitude;
    public double longitude;

    [Tooltip("The default height of the camera/view relative to the ground.")]
    public double defaultHeight = 100.0;

    [Tooltip("The initial scale of the world when placed.")]
    public float initialScale = 0.005f;
}
