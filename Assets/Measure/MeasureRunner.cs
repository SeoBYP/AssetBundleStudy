using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// Player에서 "-exp <ID> -variant <V> -out <파일> -bundles <폴더>" 로 실행한다.
// 결과는 "키<TAB>값" 줄로 -out 파일에 덧붙이고 종료한다.
public class MeasureRunner : MonoBehaviour
{
    static readonly Stopwatch sinceBoot = Stopwatch.StartNew();
    StreamWriter w;
    string exp, variant, rep, outDir, bundles;
    const double MB = 1024.0 * 1024.0;

    static string Arg(string name, string def = null)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, "-" + name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Arg("exp") == null) return;
        var go = new GameObject("MeasureRunner");
        DontDestroyOnLoad(go); // AD6: 씬을 다시 로드해도 측정을 이어 간다
        go.AddComponent<MeasureRunner>();
    }

    void Log(string key, object value)
    {
        if (w == null) return;
        w.WriteLine($"{exp}{(variant == "" ? "" : "/" + variant)}{(rep == "" ? "" : "#" + rep)}.{key}\t{value}");
        w.Flush();
        Debug.Log($"[M] {key} = {value}");
    }

    IEnumerator Start()
    {
        exp = Arg("exp");
        variant = Arg("variant", "");
        rep = Arg("rep", "");
        bundles = Arg("bundles", "");
        var outFile = Arg("out");
        outDir = Path.GetDirectoryName(outFile);
        w = new StreamWriter(outFile, true);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type != LogType.Log) Log("log_" + type, msg.Split('\n')[0]);
        };
        // AB9·AD6: -grd off 로 GPU Resident Drawer를 끄고 비교한다
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp != null && Arg("grd") == "off") urp.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
        if (urp != null) Log("gpu_resident_drawer", urp.gpuResidentDrawerMode);
        SetupCamera();

        yield return new WaitForEndOfFrame();
        // AB2: 프로세스 시작 → 첫 프레임 끝
        Log("first_frame_ms_since_process_start", (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds.ToString("F0"));
        Log("first_frame_realtimeSinceStartup_ms", (Time.realtimeSinceStartup * 1000).ToString("F0"));

        var routine = exp switch
        {
            "AB1" => AB1(), "AB3" => AB3(), "AB4" => AB4(), "AB5" => AB5(), "AB6" => AB6(), "AB7" => AB7(),
            "AB8" => AB8(), "AB9" => AB9(), "AD1" => AD1(), "AD2" => AD2(), "AD3" => AD3(), "AD4" => AD4(),
            "AD6" => AD6(), "AD7" => AD7(), _ => Empty(),
        };
        float deadline = Time.realtimeSinceStartup + 120;
        var co = StartCoroutine(Guard(routine));
        while (!finished && Time.realtimeSinceStartup < deadline) yield return null;
        if (!finished) Log("timeout", "120s");
        Log("done", "1");
        w.Close();
        w = null;
        Application.Quit();
    }

    bool finished;

    IEnumerator Guard(IEnumerator r)
    {
        while (true)
        {
            object cur;
            try
            {
                if (!r.MoveNext()) break;
                cur = r.Current;
            }
            catch (Exception e)
            {
                Log("exception", e.GetType().Name + ": " + e.Message);
                break;
            }
            yield return cur;
        }
        finished = true;
    }

    static IEnumerator Empty() { yield break; }

    // ---------------- 공용 도구 ----------------

    struct Mem { public long native, gc, gfx, reserved; }

    static Mem Snap() => new Mem
    {
        native = Profiler.GetTotalAllocatedMemoryLong(),
        gc = GC.GetTotalMemory(false),
        gfx = Profiler.GetAllocatedMemoryForGraphicsDriver(),
        reserved = Profiler.GetTotalReservedMemoryLong(),
    };

    static string Delta(Mem a, Mem b) =>
        $"native {(b.native - a.native) / MB:+0.00;-0.00} MB, gc {(b.gc - a.gc) / MB:+0.00;-0.00} MB, gfx {(b.gfx - a.gfx) / MB:+0.00;-0.00} MB, reserved {(b.reserved - a.reserved) / MB:+0.00;-0.00} MB";

    static void FullGC()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static string Ms(Stopwatch sw) => sw.Elapsed.TotalMilliseconds.ToString("F2") + " ms";

    static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }

    // 이름으로 메모리에 살아 있는 객체 수를 센다. 참조를 들고 있지 않도록 개수와 ID만 돌려준다.
    static string Alive<T>(string name) where T : Object
    {
        var ids = Resources.FindObjectsOfTypeAll<T>().Where(o => o.name == name).Select(o => o.GetEntityId()).ToList();
        return $"{ids.Count} [{string.Join(",", ids)}]";
    }

    static string LoadedBundles()
    {
        var names = AssetBundle.GetAllLoadedAssetBundles().Select(b => b.name).OrderBy(n => n).ToList();
        return $"{names.Count} [{string.Join(", ", names)}]";
    }

    static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) cam = new GameObject("Cam") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 1.5f, -6);
        cam.transform.LookAt(Vector3.zero);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
    }

    // 화면을 저장하고, 각 물체 중심 픽셀 색을 기록한다.
    IEnumerator Shot(string tag, params GameObject[] objs)
    {
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(outDir, $"{exp}{(variant == "" ? "" : "_" + variant)}{(rep == "" ? "" : "_" + rep)}_{tag}.png"), tex.EncodeToPNG());
        for (int i = 0; i < objs.Length; i++)
        {
            if (objs[i] == null) { Log($"{tag}.pixel[{i}]", "object destroyed"); continue; }
            var p = Camera.main.WorldToScreenPoint(objs[i].transform.position);
            Color32 c = tex.GetPixel((int)p.x, (int)p.y);
            Log($"{tag}.pixel[{i}]", $"rgb({c.r},{c.g},{c.b})");
        }
        Destroy(tex);
    }

    static string MatInfo(GameObject go)
    {
        if (go == null) return "GameObject destroyed";
        var m = go.GetComponent<Renderer>().sharedMaterial;
        return m == null ? "sharedMaterial = null (missing)" : $"{m.name} id={m.GetEntityId()} shader={(m.shader == null ? "null" : m.shader.name)}";
    }

    string B(string dir, string name) => Path.Combine(bundles, dir, name);

    // ---------------- AssetBundle ----------------

    IEnumerator AB1()
    {
        var t = Resources.Load<TextAsset>("AB1_Unreferenced");
        Log("resources_load", t != null ? $"loaded: \"{t.text}\"" : "null");
        yield break;
    }

    // variant = lzma | lz4 | none
    IEnumerator AB3()
    {
        var dir = "compress_" + variant;
        foreach (var (name, asset) in new[] { ("payload", "Payload"), ("cube", "Cube"), ("scene", null) })
        {
            var path = B(dir, name);
            Log($"{name}.file_size", $"{new FileInfo(path).Length / 1024.0:F1} KB");
            FullGC();
            yield return null;
            var m0 = Snap();
            var sw = Stopwatch.StartNew();
            var bundle = AssetBundle.LoadFromFile(path);
            sw.Stop();
            var m1 = Snap();
            Log($"{name}.open_ms", Ms(sw));
            Log($"{name}.mem_after_open", Delta(m0, m1));
            Log($"{name}.bundle_runtime_size", $"{Profiler.GetRuntimeMemorySizeLong(bundle) / MB:F2} MB");
            if (asset != null)
            {
                sw.Restart();
                var o = bundle.LoadAsset(asset);
                sw.Stop();
                Log($"{name}.loadasset_ms", Ms(sw));
                Log($"{name}.mem_after_loadasset", Delta(m0, Snap()));
                o = null;
            }
            bundle.Unload(true);
            yield return null;
        }
    }

    // variant = lzma | lz4 | none
    IEnumerator AB4()
    {
        var path = B("compress_" + variant, "payload");
        Log("file_size", $"{new FileInfo(path).Length / MB:F2} MB");
        FullGC();
        yield return null;
        var m0 = Snap();
        var bytes = File.ReadAllBytes(path);
        var m1 = Snap();
        Log("1_after_ReadAllBytes", Delta(m0, m1));
        var sw = Stopwatch.StartNew();
        var bundle = AssetBundle.LoadFromMemory(bytes);
        sw.Stop();
        var m2 = Snap();
        Log("2_after_LoadFromMemory", Delta(m0, m2) + $" ({Ms(sw)})");
        Log("2_bundle_runtime_size", $"{Profiler.GetRuntimeMemorySizeLong(bundle) / MB:F2} MB");
        bytes = null;
        FullGC();
        yield return null;
        var m3 = Snap();
        Log("3_after_bytes_null_GC", Delta(m0, m3));
        var tex = bundle.LoadAsset<Texture2D>("Payload");
        yield return null;
        Log("4_after_LoadAsset", Delta(m0, Snap()));
        tex = null;
        bundle.Unload(true);
        FullGC();
        yield return null;
        Log("5_after_Unload_true", Delta(m0, Snap()));

        // 비교: 같은 파일을 LoadFromFile
        FullGC();
        yield return null;
        var f0 = Snap();
        var fb = AssetBundle.LoadFromFile(path);
        Log("cmp_LoadFromFile_after_open", Delta(f0, Snap()));
        fb.Unload(true);
    }

    // variant = nover | ver_first | ver_second, -comp lzma|lz4
    IEnumerator AB5()
    {
        var comp = Arg("comp", "lzma");
        var url = $"http://127.0.0.1:8765/Bundles/compress_{comp}/payload";
        Log("comp", comp);
        if (variant == "ver_first") Log("ClearCache", Caching.ClearCache());
        for (int i = 1; i <= 2; i++)
        {
            FullGC();
            yield return null;
            var m0 = Snap();
            var sw = Stopwatch.StartNew();
            var req = variant == "nover"
                ? UnityWebRequestAssetBundle.GetAssetBundle(url)
                : UnityWebRequestAssetBundle.GetAssetBundle(url, 1, 0);
            yield return req.SendWebRequest();
            var bundle = DownloadHandlerAssetBundle.GetContent(req);
            sw.Stop();
            var m1 = Snap();
            Log($"load{i}.ms", Ms(sw));
            Log($"load{i}.result", $"{req.result} downloadedBytes={req.downloadedBytes}");
            Log($"load{i}.mem_after_open", Delta(m0, m1));
            Log($"load{i}.bundle_runtime_size", $"{Profiler.GetRuntimeMemorySizeLong(bundle) / MB:F2} MB");
            var tex = bundle.LoadAsset<Texture2D>("Payload");
            Log($"load{i}.loadasset", tex != null ? "ok" : "null");
            tex = null;
            bundle.Unload(true);
            req.Dispose();
            yield return null;
        }
        var cache = Caching.currentCacheForWriting;
        var files = Directory.Exists(cache.path) ? Directory.GetFiles(cache.path, "*", SearchOption.AllDirectories) : new string[0];
        Log("cache_files", $"{files.Length} files, {files.Sum(f => new FileInfo(f).Length) / MB:F2} MB");
    }

    // 의존 번들(mat) 없이 Instantiate → 나중에 mat 번들을 열면 고쳐지는가
    IEnumerator AB6()
    {
        var cube = AssetBundle.LoadFromFile(B("shared_lz4", "cubea"));
        var prefab = cube.LoadAsset<GameObject>("Cube");
        var go1 = Instantiate(prefab, new Vector3(-1.5f, 0, 0), Quaternion.identity);
        yield return Frames(2);
        Log("1_instance_before_dep", MatInfo(go1));
        Log("1_prefab_before_dep", MatInfo(prefab));
        yield return Shot("1_before", go1);

        var mat = AssetBundle.LoadFromFile(B("shared_lz4", "mat"));
        yield return Frames(2);
        Log("2_instance_after_dep", MatInfo(go1));
        Log("2_prefab_after_dep", MatInfo(prefab));
        var go2 = Instantiate(prefab, new Vector3(1.5f, 0, 0), Quaternion.identity);
        Log("2_new_instance_after_dep", MatInfo(go2));
        var again = cube.LoadAsset<GameObject>("Cube");
        Log("2_loadasset_again_same_object", ReferenceEquals(again, prefab));
        yield return Shot("2_after", go1, go2);

        // 번들을 다시 열어 프리팹을 새로 읽으면?
        Destroy(go1); Destroy(go2);
        cube.Unload(true);
        cube = AssetBundle.LoadFromFile(B("shared_lz4", "cubea"));
        var go3 = Instantiate(cube.LoadAsset<GameObject>("Cube"));
        yield return Frames(2);
        Log("3_reload_cube_bundle_instance", MatInfo(go3));
        yield return Shot("3_reloaded", go3);
    }

    // variant = dup | shared
    IEnumerator AB7()
    {
        FullGC();
        yield return null;
        var m0 = Snap();
        var dir = variant + "_lz4";
        AssetBundle mat = variant == "shared" ? AssetBundle.LoadFromFile(B(dir, "mat")) : null;
        var a = AssetBundle.LoadFromFile(B(dir, "cubea"));
        var b = AssetBundle.LoadFromFile(B(dir, "cubeb"));
        foreach (var (n, bundle) in new[] { ("cubea", a), ("cubeb", b) })
        {
            Log($"{n}.file_size", $"{new FileInfo(B(dir, n)).Length / 1024.0:F1} KB");
            Log($"{n}.GetAllAssetNames", string.Join(", ", bundle.GetAllAssetNames()));
        }
        var ga = Instantiate(a.LoadAsset<GameObject>("Cube"), new Vector3(-1.5f, 0, 0), Quaternion.identity);
        var gb = Instantiate(b.LoadAsset<GameObject>("CubeB"), new Vector3(1.5f, 0, 0), Quaternion.identity);
        yield return Frames(2);
        Log("material_Lit_alive", Alive<Material>("Lit"));
        Log("shader_URP_Lit_alive", Alive<Shader>("Universal Render Pipeline/Lit"));
        Log("cubeA_material", MatInfo(ga));
        Log("cubeB_material", MatInfo(gb));
        Log("same_material_object", ga.GetComponent<Renderer>().sharedMaterial == gb.GetComponent<Renderer>().sharedMaterial);
        Log("mem_after_load_all", Delta(m0, Snap()));
        // 번들 안의 객체 목록 (암묵적으로 포함된 에셋 포함)
        foreach (var (n, bundle) in new[] { ("cubea", a), ("cubeb", b) })
            Log($"{n}.LoadAllAssets", string.Join(", ", bundle.LoadAllAssets().Select(o => $"{o.GetType().Name}:{o.name}")));
        yield return Shot("both", ga, gb);
    }

    // Unload(false) 뒤 재로드 → 옛 객체와 새 객체 공존?
    IEnumerator AB8()
    {
        FullGC();
        yield return null;
        var m0 = Snap();
        var mat = AssetBundle.LoadFromFile(B("shared_lz4", "mat"));
        var cube = AssetBundle.LoadFromFile(B("shared_lz4", "cubea"));
        var go1 = Instantiate(cube.LoadAsset<GameObject>("Cube"), new Vector3(-1.5f, 0, 0), Quaternion.identity);
        yield return Frames(2);
        Log("1_loaded.material_Lit_alive", Alive<Material>("Lit"));
        Log("1_loaded.go1", MatInfo(go1));
        var m1 = Snap();

        mat.Unload(false);
        cube.Unload(false);
        yield return Frames(2);
        Log("2_after_Unload_false.loaded_bundles", LoadedBundles());
        Log("2_after_Unload_false.material_Lit_alive", Alive<Material>("Lit"));
        Log("2_after_Unload_false.go1", MatInfo(go1));

        mat = AssetBundle.LoadFromFile(B("shared_lz4", "mat"));
        cube = AssetBundle.LoadFromFile(B("shared_lz4", "cubea"));
        var go2 = Instantiate(cube.LoadAsset<GameObject>("Cube"), new Vector3(1.5f, 0, 0), Quaternion.identity);
        yield return Frames(2);
        Log("3_reloaded.material_Lit_alive", Alive<Material>("Lit"));
        Log("3_reloaded.shader_URP_Lit_alive", Alive<Shader>("Universal Render Pipeline/Lit"));
        Log("3_reloaded.go1", MatInfo(go1));
        Log("3_reloaded.go2", MatInfo(go2));
        Log("3_reloaded.mem_vs_single_load", $"first load {Delta(m0, m1)} / now {Delta(m0, Snap())}");
        yield return Shot("3_reloaded", go1, go2);

        Destroy(go1);
        yield return null;
        var op = Resources.UnloadUnusedAssets();
        yield return op;
        Log("4_after_destroy_go1_UnloadUnusedAssets.material_Lit_alive", Alive<Material>("Lit"));
        Log("4_after_destroy_go1_UnloadUnusedAssets.go2", MatInfo(go2));
    }

    // 사용 중 Unload(true) + UnloadUnusedAssets 비용
    IEnumerator AB9()
    {
        var mat = AssetBundle.LoadFromFile(B("shared_lz4", "mat"));
        var cube = AssetBundle.LoadFromFile(B("shared_lz4", "cubea"));
        var go = Instantiate(cube.LoadAsset<GameObject>("Cube"));
        yield return Frames(2);
        Log("1_before.go", MatInfo(go));
        yield return Shot("1_before", go);

        mat.Unload(true);
        yield return Frames(2);
        Log("2_after_mat_Unload_true.go", MatInfo(go));
        Log("2_after_mat_Unload_true.material_Lit_alive", Alive<Material>("Lit"));
        yield return Shot("2_after_mat_unload_true", go);

        cube.Unload(true);
        yield return Frames(2);
        Log("3_after_cube_Unload_true.go_alive", go != null);
        Log("3_after_cube_Unload_true.mesh", go != null ? go.GetComponent<MeshFilter>().sharedMesh?.name ?? "null" : "-");
        yield return Shot("3_after_cube_unload_true", go);
        Destroy(go);

        // UnloadUnusedAssets 비용: 씬 GameObject 수에 따라
        var root = new GameObject("Pool");
        root.SetActive(false);
        var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        template.transform.SetParent(root.transform);
        int made = 1;
        foreach (var n in new[] { 1, 1000, 10000, 50000 })
        {
            for (; made < n; made++) Instantiate(template, root.transform);
            yield return Frames(3);
            for (int rep = 0; rep < 3; rep++)
            {
                var swf = Stopwatch.StartNew();
                yield return null;
                var frame = swf.Elapsed.TotalMilliseconds;
                var sw = Stopwatch.StartNew();
                var op = Resources.UnloadUnusedAssets();
                while (!op.isDone) yield return null;
                Log($"4_UnloadUnusedAssets.objects_{n}.rep{rep}", $"{sw.Elapsed.TotalMilliseconds:F2} ms (empty frame {frame:F2} ms)");
            }
        }
    }

    // ---------------- Addressables ----------------

    IEnumerator Init()
    {
        var sw = Stopwatch.StartNew();
        var h = Addressables.InitializeAsync(false);
        yield return h;
        Log("init", $"{h.Status} {Ms(sw)}");
        Addressables.Release(h);
    }

    IEnumerator AD1()
    {
        yield return Init();
        foreach (var key in new[] { "AD1/Included", "AD1/Excluded" })
        {
            var h = Addressables.LoadAssetAsync<Texture2D>(key);
            yield return h;
            Log(key, h.Status == AsyncOperationStatus.Succeeded ? $"Succeeded ({h.Result.name})" : $"{h.Status}: {h.OperationException?.GetType().Name} {h.OperationException?.Message.Split('\n')[0]}");
            if (h.IsValid()) Addressables.Release(h);
        }
    }

    IEnumerator AD2()
    {
        yield return Init();
        FullGC();
        yield return null;
        var m0 = Snap();
        var sw = Stopwatch.StartNew();
        var h = Addressables.LoadAssetAsync<Texture2D>("AD2/Tex03");
        yield return h;
        Log("load_one", $"{h.Status} {Ms(sw)}");
        Log("mem_after_load_one", Delta(m0, Snap()));
        Log("loaded_bundles", LoadedBundles());
        Log("textures_AD2_alive", Enumerable.Range(0, 20).Count(i => Alive<Texture2D>($"AD2_Tex{i:00}")[0] != '0'));
        Addressables.Release(h);
        var aa = Path.Combine(Application.streamingAssetsPath, "aa");
        foreach (var f in Directory.GetFiles(aa, "catalog*")) Log("catalog_file", $"{Path.GetFileName(f)} {new FileInfo(f).Length / 1024.0:F1} KB");
    }

    // variant = before | after (Analyze Fix 전후 aa)
    IEnumerator AD3()
    {
        yield return Init();
        FullGC();
        yield return null;
        var m0 = Snap();
        var ha = Addressables.InstantiateAsync("AD3/CubeA", new Vector3(-1.5f, 0, 0), Quaternion.identity);
        var hb = Addressables.InstantiateAsync("AD3/CubeB", new Vector3(1.5f, 0, 0), Quaternion.identity);
        yield return ha;
        yield return hb;
        yield return Frames(2);
        Log("material_AD3_Mat_alive", Alive<Material>("AD3_Mat"));
        Log("same_material_object", ha.Result.GetComponent<Renderer>().sharedMaterial == hb.Result.GetComponent<Renderer>().sharedMaterial);
        Log("loaded_bundles", LoadedBundles());
        Log("mem", Delta(m0, Snap()));
    }

    // variant = plain (AD4) | unloadunused (AD5)
    IEnumerator AD4()
    {
        yield return Init();
        FullGC();
        yield return null;
        var m0 = Snap();
        var hA = Addressables.LoadAssetAsync<Texture2D>("AD4/A");
        var hB = Addressables.LoadAssetAsync<Texture2D>("AD4/B");
        yield return hA;
        yield return hB;
        yield return Frames(2);
        Log("1_loaded_A_B.A_alive", Alive<Texture2D>("AD4_A"));
        Log("1_loaded_A_B.mem", Delta(m0, Snap()));
        Log("1_loaded_A_B.bundles", LoadedBundles());

        Addressables.Release(hA);
        hA = default;
        yield return Frames(3);
        FullGC();
        Log("2_release_A.A_alive", Alive<Texture2D>("AD4_A"));
        Log("2_release_A.B_alive", Alive<Texture2D>("AD4_B"));
        Log("2_release_A.mem", Delta(m0, Snap()));
        Log("2_release_A.bundles", LoadedBundles());

        if (variant == "unloadunused")
        {
            var sw = Stopwatch.StartNew();
            var op = Resources.UnloadUnusedAssets();
            yield return op;
            Log("3_UnloadUnusedAssets.ms", Ms(sw));
            Log("3_UnloadUnusedAssets.A_alive", Alive<Texture2D>("AD4_A"));
            Log("3_UnloadUnusedAssets.B_alive", Alive<Texture2D>("AD4_B"));
            Log("3_UnloadUnusedAssets.mem", Delta(m0, Snap()));
            Log("3_UnloadUnusedAssets.bundles", LoadedBundles());

            sw.Restart();
            var hA2 = Addressables.LoadAssetAsync<Texture2D>("AD4/A");
            yield return hA2;
            Log("4_reload_A", $"{hA2.Status} {Ms(sw)} alive={Alive<Texture2D>("AD4_A")}");
            Addressables.Release(hA2);
            yield return Frames(2);
        }

        Addressables.Release(hB);
        hB = default;
        yield return Frames(3);
        FullGC();
        Log("5_release_B.A_alive", Alive<Texture2D>("AD4_A"));
        Log("5_release_B.B_alive", Alive<Texture2D>("AD4_B"));
        Log("5_release_B.mem", Delta(m0, Snap()));
        Log("5_release_B.bundles", LoadedBundles());
    }

    // variant = obj_instantiate | instasync_release | instasync_destroy | instasync_releaseinstance
    IEnumerator AD6()
    {
        yield return Init();
        GameObject go;
        AsyncOperationHandle<GameObject> h;
        if (variant == "obj_instantiate")
        {
            h = Addressables.LoadAssetAsync<GameObject>("AD6/Cube");
            yield return h;
            go = Instantiate(h.Result);
        }
        else
        {
            h = Addressables.InstantiateAsync("AD6/Cube");
            yield return h;
            go = h.Result;
        }
        yield return Frames(2);
        Log("1_before.instance", MatInfo(go));
        Log("1_before.bundles", LoadedBundles());
        yield return Shot("1_before", go);

        switch (variant)
        {
            case "obj_instantiate":
            case "instasync_release": Addressables.Release(h); break;
            case "instasync_destroy": Destroy(go); break;
            case "instasync_releaseinstance": Log("ReleaseInstance_returned", Addressables.ReleaseInstance(go)); break;
        }
        yield return Frames(3);
        Log("2_after.instance_alive", go != null);
        Log("2_after.instance", MatInfo(go));
        Log("2_after.handle_valid", h.IsValid());
        Log("2_after.bundles", LoadedBundles());
        Log("2_after.material_Lit_alive", Alive<Material>("Lit"));
        yield return Shot("2_after", go);

        // 한 시점만 보지 않도록: 2초 뒤, UnloadUnusedAssets 뒤, 씬을 다시 로드한 뒤
        yield return new WaitForSecondsRealtime(2);
        Log("3_after_2s.bundles", LoadedBundles());
        Log("3_after_2s.handle_valid", h.IsValid());
        yield return Resources.UnloadUnusedAssets();
        Log("4_after_UnloadUnusedAssets.bundles", LoadedBundles());
        Log("4_after_UnloadUnusedAssets.material_Lit_alive", Alive<Material>("Lit"));
        Log("4_after_UnloadUnusedAssets.instance", MatInfo(go));
        yield return SceneManager.LoadSceneAsync(0, LoadSceneMode.Single);
        yield return Frames(3);
        Log("5_after_scene_reload.instance_alive", go != null);
        Log("5_after_scene_reload.bundles", LoadedBundles());
        Log("5_after_scene_reload.handle_valid", h.IsValid());
        Log("5_after_scene_reload.material_Lit_alive", Alive<Material>("Lit"));
    }

    static long CacheBytes()
    {
        var path = Caching.currentCacheForWriting.path;
        return Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
    }

    // variant = run1 (캐시 비우고 시작) | run2 (콘텐츠 업데이트 뒤)
    IEnumerator AD7()
    {
        if (variant == "run1")
        {
            Log("ClearCache", Caching.ClearCache());
            var catDir = Path.Combine(Application.persistentDataPath, "com.unity.addressables");
            if (Directory.Exists(catDir)) Directory.Delete(catDir, true);
        }
        yield return Init();
        var check = Addressables.CheckForCatalogUpdates(false);
        yield return check;
        Log("CheckForCatalogUpdates", $"{check.Status} [{string.Join(",", check.Result ?? new List<string>())}]");
        if (check.Status == AsyncOperationStatus.Succeeded && check.Result.Count > 0)
        {
            var up = Addressables.UpdateCatalogs(check.Result, false);
            yield return up;
            Log("UpdateCatalogs", up.Status);
            Addressables.Release(up);
        }
        Addressables.Release(check);
        foreach (var key in new object[] { "AD7/Item0", "AD7/Item1", "AD7" })
        {
            var size = Addressables.GetDownloadSizeAsync(key);
            yield return size;
            Log($"GetDownloadSizeAsync({key})", $"{size.Result} B");
            Addressables.Release(size);
        }
        // 카탈로그 값과 독립된 확인: 다운로드 전후 디스크 캐시 크기
        long cacheBefore = CacheBytes();
        var dl = Addressables.DownloadDependenciesAsync("AD7");
        yield return dl;
        Log("cache_bytes_added_by_download", $"{CacheBytes() - cacheBefore} B (before {cacheBefore} B)");
        Log("DownloadDependenciesAsync", $"{dl.Status} total={dl.GetDownloadStatus().TotalBytes} B");
        Addressables.Release(dl);
        var after = Addressables.GetDownloadSizeAsync("AD7");
        yield return after;
        Log("GetDownloadSizeAsync_after_download", $"{after.Result} B");
        Addressables.Release(after);
    }
}
