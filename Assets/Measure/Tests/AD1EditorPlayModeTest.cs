using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

// AD1: 에디터 Play Mode에서 같은 키를 로드한다. Play Mode Script는 실행 전에 MeasureSetup.SetPlayMode로 바꾼다.
public class AD1EditorPlayModeTest
{
    [UnityTest]
    public IEnumerator LoadInEditorPlayMode()
    {
        LogAssert.ignoreFailingMessages = true;
        var mode = System.Environment.GetEnvironmentVariable("MEASURE_PLAYMODE") ?? "unknown";
        var init = Addressables.InitializeAsync(false);
        yield return init;
        var lines = $"AD1/editor_{mode}.init\t{init.Status}\n";
        foreach (var key in new[] { "AD1/Included", "AD1/Excluded" })
        {
            var h = Addressables.LoadAssetAsync<Texture2D>(key);
            yield return h;
            lines += $"AD1/editor_{mode}.{key}\t" + (h.Status == AsyncOperationStatus.Succeeded
                ? $"Succeeded ({h.Result.name})"
                : $"{h.Status}: {h.OperationException?.GetType().Name} {h.OperationException?.Message.Split('\n')[0]}") + "\n";
            if (h.IsValid()) Addressables.Release(h);
        }
        Directory.CreateDirectory("Measure/results");
        File.AppendAllText("Measure/results/AD1.txt", lines);
    }
}
