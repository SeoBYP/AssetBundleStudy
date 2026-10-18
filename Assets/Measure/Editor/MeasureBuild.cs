using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.AnalyzeRules;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.AddressableAssets;

// batchmode -executeMethod 진입점. 결과는 Measure/results/<이름>.txt 에 "키<TAB>값" 줄로 남긴다.
public static class MeasureBuild
{
    const string Results = "Measure/results";
    const string PlayerExe = "Builds/Player/AssetBundleStudy.exe";

    public static string Arg(string name, string def = null)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, "-" + name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static void Log(string file, string key, object value)
    {
        Directory.CreateDirectory(Results);
        File.AppendAllText($"{Results}/{file}.txt", $"{key}\t{value}\n");
        Debug.Log($"[Measure] {file} {key} = {value}");
    }

    static string KB(long bytes) => $"{bytes / 1024.0:F1} KB";

    // ---------------- AssetBundle ----------------

    public static void BuildBundles()
    {
        var modes = new (string name, BuildAssetBundleOptions opt)[]
        {
            ("lzma", BuildAssetBundleOptions.None),
            ("lz4", BuildAssetBundleOptions.ChunkBasedCompression),
            ("none", BuildAssetBundleOptions.UncompressedAssetBundle),
        };
        foreach (var (name, opt) in modes)
        {
            Bundles("compress_" + name, opt,
                B("payload", MeasureSetup.Gen + "/Payload.png"),
                B("cube", MeasureSetup.CubePrefab),
                B("scene", "Assets/Scenes/InDoor.unity"));
        }
        Bundles("dup_lz4", BuildAssetBundleOptions.ChunkBasedCompression,
            B("cubea", MeasureSetup.CubePrefab), B("cubeb", MeasureSetup.Gen + "/CubeB.prefab"));
        Bundles("shared_lz4", BuildAssetBundleOptions.ChunkBasedCompression,
            B("cubea", MeasureSetup.CubePrefab), B("cubeb", MeasureSetup.Gen + "/CubeB.prefab"), B("mat", MeasureSetup.LitMat));
    }

    static AssetBundleBuild B(string name, string asset) => new AssetBundleBuild { assetBundleName = name, assetNames = new[] { asset } };

    static void Bundles(string dir, BuildAssetBundleOptions opt, params AssetBundleBuild[] builds)
    {
        var path = "Builds/Bundles/" + dir;
        Directory.CreateDirectory(path);
        var manifest = BuildPipeline.BuildAssetBundles(path, builds, opt | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        foreach (var b in builds)
        {
            Log("bundles", $"{dir}/{b.assetBundleName}.size", KB(new FileInfo($"{path}/{b.assetBundleName}").Length));
            Log("bundles", $"{dir}/{b.assetBundleName}.deps", string.Join(",", manifest.GetAllDependencies(b.assetBundleName)));
        }
    }

    // ---------------- Player ----------------

    public static void BuildPlayer()
    {
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 960;
        PlayerSettings.defaultScreenHeight = 540;
        PlayerSettings.runInBackground = true;
        PlayerSettings.resizableWindow = false;
        var report = Player(PlayerExe);

        // AB1: Resources 안/밖의 미참조 에셋이 빌드에 들어갔는가
        var packed = report.packedAssets.SelectMany(p => p.contents.Select(c => (file: p.shortPath, c.sourceAssetPath, c.packedSize))).ToList();
        foreach (var name in new[] { "AB1_Unreferenced", "AB1_NotInResources" })
        {
            var hit = packed.Where(p => p.sourceAssetPath.Contains(name)).ToList();
            Log("AB1", name, hit.Count == 0 ? "not in build" : string.Join(", ", hit.Select(h => $"{h.file} ({h.sourceAssetPath}, {h.packedSize} B)")));
        }
    }

    static BuildReport Player(string exe)
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            target = BuildTarget.StandaloneWindows64,
            locationPathName = exe,
            options = BuildOptions.Development,
        });
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Player build failed: " + report.summary.result);
        return report;
    }

    // AB2: Resources 에셋 수만 바꾼 Player 빌드. -count N
    public static void BuildAB2()
    {
        int n = int.Parse(Arg("count", "0"));
        const string dir = "Assets/AB2Gen/Resources";
        if (Directory.Exists("Assets/AB2Gen")) AssetDatabase.DeleteAsset("Assets/AB2Gen");
        Directory.CreateDirectory(dir);
        for (int i = 0; i < n; i++) File.WriteAllText($"{dir}/item_{i:00000}.txt", $"resources item {i}");
        AssetDatabase.Refresh();

        var report = Player($"Builds/AB2_{n}/AssetBundleStudy.exe");
        var res = report.packedAssets.Where(p => p.shortPath.EndsWith("resources.assets")).ToList();
        Log("AB2", $"n{n}.resources_assets_objects", res.Sum(p => p.contents.Length));
        Log("AB2", $"n{n}.resources_assets_size", KB(new FileInfo($"Builds/AB2_{n}/AssetBundleStudy_Data/resources.assets").Length));
        Log("AB2", $"n{n}.build_seconds", report.summary.totalTime.TotalSeconds.ToString("F1"));

        AssetDatabase.DeleteAsset("Assets/AB2Gen");
    }

    // ---------------- Addressables ----------------

    static AddressableAssetSettings S => AddressableAssetSettingsDefaultObject.Settings;

    // -phase ad2_together | ad2_separate | ad2_label | ad3_before | ad3_after | ad7_v1 | ad7_v2
    public static void AddrBuild()
    {
        var phase = Arg("phase");
        var s = S;
        var ad2 = s.FindGroup("AD2").GetSchema<BundledAssetGroupSchema>();
        var remoteGroup = s.FindGroup("AD7_Remote");
        ad2.BundleMode = phase switch
        {
            "ad2_separate" => BundledAssetGroupSchema.BundlePackingMode.PackSeparately,
            "ad2_label" => BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel,
            _ => BundledAssetGroupSchema.BundlePackingMode.PackTogether,
        };
        bool ad7 = phase.StartsWith("ad7");
        remoteGroup.GetSchema<BundledAssetGroupSchema>().IncludeInBuild = ad7;
        s.BuildRemoteCatalog = ad7;
        if (ad7)
        {
            s.RemoteCatalogBuildPath.SetVariableByName(s, AddressableAssetSettings.kRemoteBuildPath);
            s.RemoteCatalogLoadPath.SetVariableByName(s, AddressableAssetSettings.kRemoteLoadPath);
        }

        if (phase == "ad3_before" || phase == "ad3_after")
        {
            var rule = new CheckBundleDupeDependencies();
            var results = rule.RefreshAnalysis(s);
            Log("AD3", $"{phase}.analyze_result_count", results.Count);
            foreach (var r in results) Log("AD3", $"{phase}.analyze", r.resultName);
            if (phase == "ad3_after")
            {
                rule.FixIssues(s);
                var iso = s.groups.Where(g => g != null && g.Name.Contains("Duplicate")).ToList();
                foreach (var g in iso)
                    Log("AD3", "fix.created_group", $"{g.Name}: {string.Join(", ", g.entries.Select(e => e.AssetPath))}");
                Log("AD3", "after_fix.analyze_result_count", new CheckBundleDupeDependencies().RefreshAnalysis(s).Count);
            }
        }
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();

        AddressablesPlayerBuildResult result;
        if (phase == "ad7_v2")
        {
            // 원격 항목 하나만 바꾸고 "Update a Previous Build"
            MeasureSetup.Texture(MeasureSetup.Gen + "/AD7_Item1.png", 256, 999, overwrite: true);
            AssetDatabase.Refresh();
            result = ContentUpdateScript.BuildContentUpdate(s, ContentUpdateScript.GetContentStateDataPath(false, s));
        }
        else
        {
            if (phase == "ad7_v1" && Directory.Exists("Builds/ServerData")) Directory.Delete("Builds/ServerData", true);
            AddressableAssetSettings.CleanPlayerContent();
            AddressableAssetSettings.BuildPlayerContent(out result);
        }
        if (!string.IsNullOrEmpty(result.Error)) throw new Exception(result.Error);

        var file = phase.Substring(0, 3).ToUpper();
        Log(file, $"{phase}.build_seconds", result.Duration.ToString("F1"));
        foreach (var grp in result.AssetBundleBuildResults.GroupBy(b => b.SourceAssetGroup.Name).OrderBy(g => g.Key))
        {
            if (!grp.Key.StartsWith(file) && !grp.Key.Contains("Duplicate")) continue;
            Log(file, $"{phase}.group.{grp.Key}.bundle_count", grp.Count());
            foreach (var b in grp) Log(file, $"{phase}.bundle", $"{Path.GetFileName(b.FilePath)} {KB(new FileInfo(b.FilePath).Length)}");
        }
        foreach (var cat in Directory.GetFiles(Addressables.BuildPath, "catalog*", SearchOption.TopDirectoryOnly))
            Log(file, $"{phase}.catalog", $"{Path.GetFileName(cat)} {KB(new FileInfo(cat).Length)}");
        if (ad7)
            foreach (var f in Directory.GetFiles("Builds/ServerData", "*", SearchOption.AllDirectories).OrderBy(f => f))
                Log("AD7", $"{phase}.serverdata", $"{Path.GetFileName(f)} {KB(new FileInfo(f).Length)} {File.GetLastWriteTime(f):HH:mm:ss}");

        // 이 단계의 aa 폴더를 보관해 두고, Player의 StreamingAssets/aa 를 갈아 끼워 실행한다.
        if (phase != "ad7_v2") CopyDir(Addressables.BuildPath, "Builds/aa/" + phase);
    }

    static void CopyDir(string from, string to)
    {
        if (Directory.Exists(to)) Directory.Delete(to, true);
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(f, dst);
        }
    }
}
