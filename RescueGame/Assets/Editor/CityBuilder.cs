using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/Rescue Heli/Build City: sets up the City object (a CityGenerator that builds the brick
/// city of concept 3 when the level loads - nothing is stored, so the repo stays small), its
/// materials, the golden-hour sky and the asphalt ground, and shows a preview in the editor.
/// </summary>
public static class CityBuilder
{
    const string TexDir = "Assets/Art/City/Textures/";
    const string MatDir = "Assets/Art/City/Materials/";
    const string MeshDir = "Assets/Art/City/Meshes/";

    [MenuItem("Tools/Rescue Heli/Build City")]
    public static void Build()
    {
        // Old stored city meshes are no longer used (the hospital's is).
        if (AssetDatabase.IsValidFolder(MeshDir.TrimEnd('/')))
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshDir.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!System.IO.Path.GetFileName(path).StartsWith("Hospital")) AssetDatabase.DeleteAsset(path);
            }

        var gen = EnsureGenerator();
        var env = GameObject.Find("Environment").transform;
        // The cliff is a placeholder for a later mission; it doesn't belong in the city.
        foreach (var name in new[] { "Cliff", "Ledge", "Survivor_Placeholder" }) { var t = env.Find(name); if (t) t.gameObject.SetActive(false); }
        var ground = env.Find("Ground");
        if (ground)
        {
            var m = Mat("Street", new Color(0.62f, 0.58f, 0.54f), "Asphalt");
            m.SetTextureScale("_BaseMap", new Vector2(60f, 60f));
            ground.GetComponent<Renderer>().sharedMaterial = m;
        }
        Sky();
        gen.Generate(); // editor preview (not saved; the game builds it on load)
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
    }

    /// <summary>The scene's City generator, created (with materials) if missing.</summary>
    public static CityGenerator EnsureGenerator()
    {
        var go = GameObject.Find("City");
        if (go && !go.GetComponent<CityGenerator>()) { Object.DestroyImmediate(go); go = null; }
        if (!go) go = new GameObject("City");
        var gen = go.GetComponent<CityGenerator>() ?? go.AddComponent<CityGenerator>();
        gen.brickMats = new[] { Mat("Brick_Red", Color.white, "Brick_Red"), Mat("Brick_Brown", Color.white, "Brick_Brown"), Mat("Brick_Tan", Color.white, "Brick_Tan") };
        gen.sharedMats = new[]
        {
            Mat("TrimLight", new Color(0.86f, 0.8f, 0.68f), "Concrete"),
            Mat("TrimDark", new Color(0.52f, 0.46f, 0.42f), "Concrete"),
            Mat("WindowGlass", new Color(0.24f, 0.29f, 0.34f)),
            Mat("WindowFrame", new Color(0.9f, 0.86f, 0.76f)),
            Mat("RoofTar", Color.white, "RoofTar"),
            Mat("WindowLit", new Color(1f, 0.78f, 0.45f), null, 0.9f),
            Mat("RoofMetal", new Color(0.55f, 0.56f, 0.58f)),
            Mat("TankWood", new Color(0.46f, 0.36f, 0.26f)),
            Mat("Accent", new Color(0.3f, 0.42f, 0.36f)),
        };
        gen.roadPaint = Mat("RoadPaint", new Color(0.92f, 0.86f, 0.66f));
        gen.pavement = Mat("Pavement", new Color(0.8f, 0.76f, 0.68f), "Concrete");
        EditorUtility.SetDirty(gen);
        return gen;
    }

    /// <summary>Editor: a brick building with a stored mesh (used for the hospital).</summary>
    public static GameObject MakeStoredBuilding(Transform parent, string name, Vector3 basePos, Quaternion rot, float w, float d, float h, string brick, int seed, int shopFace)
    {
        var go = CityGenerator.MakeBuildingWith(EnsureGenerator(), parent, name, basePos, rot, w, d, h, brick, new System.Random(seed), true, shopFace);
        System.IO.Directory.CreateDirectory(MeshDir);
        var mf = go.GetComponent<MeshFilter>();
        string path = MeshDir + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mf.sharedMesh, path);
        return go;
    }

    static Material Mat(string name, Color color, string tex = null, float emission = 0f)
    {
        System.IO.Directory.CreateDirectory(MatDir);
        string path = MatDir + "M_" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Interior/Mat_White.mat"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetTexture("_BaseMap", tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + tex + ".png") : null);
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color * emission);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Sky()
    {
        // Warm golden-hour haze instead of a clear blue sky (concept 3).
        string path = MatDir + "M_GoldenSky.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("RescueGame/Golden Sky");
        if (m == null || m.shader != shader) { AssetDatabase.DeleteAsset(path); m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        RenderSettings.skybox = m;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            cam.clearFlags = CameraClearFlags.Skybox;
            EditorUtility.SetDirty(cam);
        }
        RenderSettings.fogColor = new Color(0.94f, 0.72f, 0.5f);
        RenderSettings.fogStartDistance = 40f;
        RenderSettings.fogEndDistance = 330f;
    }
}
