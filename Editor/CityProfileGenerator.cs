using UnityEngine;
using UnityEditor;
using System.IO;

public class CityProfileGenerator
{
    [MenuItem("Unity Cesium Extractor/Generate City Profiles")]
    public static void GenerateCities()
    {
        // IMPORTANT: Must be in Resources folder for Resources.LoadAll to work at runtime!
        string path = "Assets/Resources/FloridaCities";
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        // Scale 0.002 = 1:500 scale, good for tabletop viewing
        // Height 152m = 500 feet above ground for bird's eye view
        CreateCity(path, "Miami", 25.7744, -80.1918, "Downtown / Brickell", 152.0, 0.002f);
        CreateCity(path, "Miami Beach", 25.799845913958936, -80.12439801186251, "South Beach", 152.0, 0.002f);
        CreateCity(path, "Orlando", 28.4177, -81.5812, "Walt Disney Magic Kingdom - Cinderella Castle", 152.0, 0.002f);
        CreateCity(path, "Orlando Universal", 28.472248708466555, -81.4677195371011, "Universal Studios", 152.0, 0.002f);
        CreateCity(path, "Tampa", 27.9475, -82.4584, "Riverwalk / Downtown", 152.0, 0.002f);
        CreateCity(path, "Tampa Busch Gardens", 28.03571255844349, -82.41887260927999, "Busch Gardens", 152.0, 0.002f);
        CreateCity(path, "Jacksonville", 30.3256, -81.6558, "St. Johns River Bridge", 152.0, 0.002f);
        CreateCity(path, "Tallahassee", 30.4383, -84.2807, "State Capitol", 152.0, 0.002f);
        CreateCity(path, "Key West", 24.5465, -81.7975, "Southernmost Point", 152.0, 0.002f);
        CreateCity(path, "Fort Lauderdale", 26.1224, -80.1373, "Las Olas Blvd", 152.0, 0.002f);
        CreateCity(path, "St. Petersburg", 27.7731, -82.6398, "The Pier", 152.0, 0.002f);
        CreateCity(path, "Daytona Beach", 29.1895, -81.0664, "The Speedway", 152.0, 0.002f);
        CreateCity(path, "Sarasota", 27.3316, -82.5460, "Marina Jack", 152.0, 0.002f);
        CreateCity(path, "St Augustine", 29.89780375394142, -81.31149769678042, "Fort Castillo de San Marcos", 152.0, 0.002f);
        CreateCity(path, "Cape Canaveral", 28.586131153301324, -80.65085874875322, "NASA Kennedy Space Center", 152.0, 0.002f);
        CreateCity(path, "Dry Tortugas", 24.628361505468238, -82.87343365305166, "Dry Tortugas National Park", 152.0, 0.002f);
        CreateCity(path, "Gainesville", 29.648666340615094, -82.34320510184041, "University of Florida", 152.0, 0.002f);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[UnityCesiumExtractor] City profiles generated/updated with 500ft view height.");
    }

    private static void CreateCity(string path, string name, double lat, double lon, string desc, double height, float scale)
    {
        string assetPath = $"{path}/{name}.asset";
        CityProfile profile = AssetDatabase.LoadAssetAtPath<CityProfile>(assetPath);

        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<CityProfile>();
            AssetDatabase.CreateAsset(profile, assetPath);
        }

        profile.cityName = name;
        profile.latitude = lat;
        profile.longitude = lon;
        profile.description = desc;
        profile.defaultHeight = height;
        profile.initialScale = scale;

        EditorUtility.SetDirty(profile);
    }
}
