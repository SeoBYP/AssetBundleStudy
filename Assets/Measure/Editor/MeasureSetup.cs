using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

// 측정용 에셋과 Addressables 그룹을 만든다. 여러 번 실행해도 결과가 같다.
public static class MeasureSetup
{
    public const string Gen = "Assets/Measure/Generated";
    // Cube.prefab이 참조하는 머티리얼은 URP 패키지의 Lit.mat (GUID 31321ba1...)
    public const string LitMat = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat";
    public const string CubePrefab = "Assets/Prefab/Cube.prefab";

    [MenuItem("Measure/Setup")]
    public static void Setup()
    {
        Directory.CreateDirectory(Gen);
        Directory.CreateDirectory("Assets/Resources");
        Directory.CreateDirectory("Assets/Measure/Unused");

        // AB1: Resources 안/밖에 아무도 참조하지 않는 에셋
        WriteIfMissing("Assets/Resources/AB1_Unreferenced.txt", "AB1 unreferenced asset inside Resources");
        WriteIfMissing("Assets/Measure/Unused/AB1_NotInResources.txt", "AB1 unreferenced asset outside Resources");

        // AB3~AB5: 2048x2048 RGBA32 무압축 텍스처 (메모리 16MB)
        Texture(Gen + "/Payload.png", 2048, 1);
        Copy(CubePrefab, Gen + "/CubeB.prefab");

        // Addressables용 에셋
        Texture(Gen + "/AD1_In.png", 256, 11);
        Texture(Gen + "/AD1_Out.png", 256, 12);
        for (int i = 0; i < 20; i++) Texture($"{Gen}/AD2_Tex{i:00}.png", 512, 100 + i);
        Copy(LitMat, Gen + "/AD3_Mat.mat");
        Copy(CubePrefab, Gen + "/AD3_CubeA.prefab");
        Copy(CubePrefab, Gen + "/AD3_CubeB.prefab");
        AssetDatabase.Refresh();
        SetPrefabMaterial(Gen + "/AD3_CubeA.prefab", Gen + "/AD3_Mat.mat");
        SetPrefabMaterial(Gen + "/AD3_CubeB.prefab", Gen + "/AD3_Mat.mat");
        Texture(Gen + "/AD4_A.png", 2048, 41);
        Texture(Gen + "/AD4_B.png", 64, 42);
        Copy(CubePrefab, Gen + "/AD6_Cube.prefab");
        Texture(Gen + "/AD7_Item0.png", 256, 70);
        Texture(Gen + "/AD7_Item1.png", 256, 71);
        AssetDatabase.Refresh();

        SetupAddressables();
        AssetDatabase.SaveAssets();
        Debug.Log("[Measure] Setup done");
    }

    static void SetupAddressables()
    {
        var s = AddressableAssetSettingsDefaultObject.GetSettings(true);
        s.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
        s.profileSettings.SetValue(s.activeProfileId, AddressableAssetSettings.kRemoteBuildPath, "Builds/ServerData/[BuildTarget]");
        s.profileSettings.SetValue(s.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, "http://127.0.0.1:8765/ServerData/[BuildTarget]");
        SetPlayMode("fast");

        Group(s, "AD1_Included", BundledAssetGroupSchema.BundlePackingMode.PackTogether, (Gen + "/AD1_In.png", "AD1/Included", null));
        var excluded = Group(s, "AD1_Excluded", BundledAssetGroupSchema.BundlePackingMode.PackTogether, (Gen + "/AD1_Out.png", "AD1/Excluded", null));
        excluded.GetSchema<BundledAssetGroupSchema>().IncludeInBuild = false;

        Group(s, "AD2", BundledAssetGroupSchema.BundlePackingMode.PackTogether,
            Enumerable.Range(0, 20).Select(i => ($"{Gen}/AD2_Tex{i:00}.png", $"AD2/Tex{i:00}", "L" + (i % 4))).ToArray());
        Group(s, "AD3_A", BundledAssetGroupSchema.BundlePackingMode.PackTogether, (Gen + "/AD3_CubeA.prefab", "AD3/CubeA", null));
        Group(s, "AD3_B", BundledAssetGroupSchema.BundlePackingMode.PackTogether, (Gen + "/AD3_CubeB.prefab", "AD3/CubeB", null));
        Group(s, "AD4", BundledAssetGroupSchema.BundlePackingMode.PackTogether, (Gen + "/AD4_A.png", "AD4/A", null), (Gen + "/AD4_B.png", "AD4/B", null));
        Group(s, "AD6", BundledAssetGroupSchema.BundlePackingMode.PackTogether, (Gen + "/AD6_Cube.prefab", "AD6/Cube", null));

        var remote = Group(s, "AD7_Remote", BundledAssetGroupSchema.BundlePackingMode.PackSeparately,
            (Gen + "/AD7_Item0.png", "AD7/Item0", "AD7"), (Gen + "/AD7_Item1.png", "AD7/Item1", "AD7"));
        var schema = remote.GetSchema<BundledAssetGroupSchema>();
        schema.BuildPath.SetVariableByName(s, AddressableAssetSettings.kRemoteBuildPath);
        schema.LoadPath.SetVariableByName(s, AddressableAssetSettings.kRemoteLoadPath);
        schema.IncludeInBuild = false; // AD7 단계에서만 켠다
        EditorUtility.SetDirty(s);
    }

    static AddressableAssetGroup Group(AddressableAssetSettings s, string name, BundledAssetGroupSchema.BundlePackingMode mode,
        params (string path, string address, string label)[] entries)
    {
        var g = s.FindGroup(name) ?? s.CreateGroup(name, false, false, true, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        g.GetSchema<BundledAssetGroupSchema>().BundleMode = mode;
        foreach (var (path, address, label) in entries)
        {
            var e = s.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), g);
            e.address = address;
            if (label != null) { s.AddLabel(label); e.SetLabel(label, true, true); }
        }
        EditorUtility.SetDirty(g);
        return g;
    }

    // "fast" = Use Asset Database, "packed" = Use Existing Build
    public static void SetPlayMode(string mode)
    {
        var s = AddressableAssetSettingsDefaultObject.GetSettings(true);
        var type = mode == "fast" ? typeof(BuildScriptFastMode) : typeof(BuildScriptPackedPlayMode);
        s.ActivePlayModeDataBuilderIndex = s.DataBuilders.FindIndex(b => b.GetType() == type);
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Measure] PlayMode = {s.ActivePlayModeDataBuilder.Name}");
    }

    public static void SetPlayModeFromArgs() => SetPlayMode(MeasureBuild.Arg("mode", "fast"));

    static void WriteIfMissing(string path, string text)
    {
        if (!File.Exists(path)) File.WriteAllText(path, text);
    }

    static void Copy(string from, string to)
    {
        if (!File.Exists(to)) AssetDatabase.CopyAsset(from, to);
    }

    static void SetPrefabMaterial(string prefab, string mat)
    {
        var root = PrefabUtility.LoadPrefabContents(prefab);
        root.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(mat);
        PrefabUtility.SaveAsPrefabAsset(root, prefab);
        PrefabUtility.UnloadPrefabContents(root);
    }

    // Perlin 노이즈 텍스처. 압축 비교가 의미 있도록 완전 랜덤이 아닌 구조가 있는 패턴을 쓴다.
    public static void Texture(string path, int size, int seed, bool overwrite = false)
    {
        if (File.Exists(path) && !overwrite) return;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        float ox = seed * 37.1f, oy = seed * 11.7f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float n = Mathf.PerlinNoise(ox + x * 0.01f, oy + y * 0.01f);
            float m = Mathf.PerlinNoise(ox + x * 0.07f, oy + y * 0.07f);
            px[y * size + x] = new Color32((byte)(n * 255), (byte)(m * 255), (byte)((n + m) * 127), 255);
        }
        tex.SetPixels32(px);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.mipmapEnabled = false;
        imp.isReadable = false;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.SaveAndReimport();
    }
}
