#!/bin/bash
# 2차: 머티리얼 경로 수정 뒤 AB6~AB9 재측정, AD1 에디터, AD7, AB2
cd "$(dirname "$0")/.."
U=Measure/unity.sh; R=Measure/run.sh; P=Builds/Player/AssetBundleStudy.exe
rm -f Measure/results/bundles.txt Measure/results/AB6.txt Measure/results/AB7.txt Measure/results/AB8.txt Measure/results/AB9.txt
$U bundles -quit -executeMethod MeasureBuild.BuildBundles
$R $P AB6 "" ""; for v in dup shared; do $R $P AB7 $v ""; done; $R $P AB8 "" ""; $R $P AB9 "" ""

# AD1 에디터 Play Mode: Use Asset Database / Use Existing Build
for m in fast packed; do
  $U playmode_$m -quit -executeMethod MeasureSetup.SetPlayModeFromArgs -mode $m
  MEASURE_PLAYMODE=$m $U test_$m -runTests -testPlatform PlayMode -testResults "$(pwd -W)/Measure/logs/test_$m.xml"
done
$U playmode_fast_restore -quit -executeMethod MeasureSetup.SetPlayModeFromArgs -mode fast

# AD7: 원격 그룹 + 원격 카탈로그 → v1 실행 → 항목 하나 수정 후 콘텐츠 업데이트 → v2 실행
$U addr_ad7_v1 -quit -executeMethod MeasureBuild.AddrBuild -phase ad7_v1
Measure/swap_aa.sh ad7_v1
echo "=== run1 ===" >> Measure/logs/http_marks.log; wc -l < Measure/logs/http.log >> Measure/logs/http_marks.log
$R $P AD7 run1 ""
$U addr_ad7_v2 -quit -executeMethod MeasureBuild.AddrBuild -phase ad7_v2
echo "=== run2 ===" >> Measure/logs/http_marks.log; wc -l < Measure/logs/http.log >> Measure/logs/http_marks.log
$R $P AD7 run2 ""
echo "=== end ===" >> Measure/logs/http_marks.log; wc -l < Measure/logs/http.log >> Measure/logs/http_marks.log
Measure/swap_aa.sh ad3_after

# AB2: Resources 에셋 수별 Player
for n in 0 1000 10000; do
  $U ab2_$n -quit -executeMethod MeasureBuild.BuildAB2 -count $n
  for r in 1 2 3 4 5 6; do $R Builds/AB2_$n/AssetBundleStudy.exe AB2 n$n $r; done
done
echo PHASE2_DONE
