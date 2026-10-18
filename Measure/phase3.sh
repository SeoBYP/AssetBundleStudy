#!/bin/bash
# 3차 (검수 반영): 스플래시 끔, AB2 순서 섞기, AB5 3회, AB9·AD6 GRD 비교, AD3 깨끗한 시작, AD7 독립 근거
# http 서버가 8765에서 Builds/를 서비스하고 있어야 한다.
cd "$(dirname "$0")/.."
U=Measure/unity.sh; R=Measure/run.sh; P=Builds/Player/AssetBundleStudy.exe
for f in AB2 AB5 AB6 AB9 AD3 AD6 AD7; do rm -f Measure/results/$f.txt Measure/results/${f}_*.png; done
rm -f Measure/results/AD7_http.log

$U addr_ad3_before -quit -executeMethod MeasureBuild.AddrBuild -phase ad3_before
$U addr_ad3_after -quit -executeMethod MeasureBuild.AddrBuild -phase ad3_after
$U player3 -quit -executeMethod MeasureBuild.BuildPlayer

Measure/swap_aa.sh ad3_after
$R $P AD3 after ""
for g in on off; do
  $R $P AB6 grd_$g "" -grd $g
  $R $P AB9 grd_$g "" -grd $g
  for v in obj_instantiate instasync_release instasync_destroy instasync_releaseinstance; do $R $P AD6 ${v} grd_$g -grd $g; done
done
Measure/swap_aa.sh ad3_before; $R $P AD3 before ""
for c in lzma lz4; do for r in 1 2 3; do for v in nover ver_first ver_second; do $R $P AB5 $v $c-$r -comp $c; done; done; done

# AD7
http_mark() { echo "=== $1 (line $(wc -l < Measure/logs/http.log)) ===" >> Measure/results/AD7_http.log; }
$U addr_ad7_v1 -quit -executeMethod MeasureBuild.AddrBuild -phase ad7_v1
Measure/swap_aa.sh ad7_v1
s=$(wc -l < Measure/logs/http.log); $R $P AD7 run1 ""
echo "=== run1 ===" >> Measure/results/AD7_http.log; tail -n +$((s+1)) Measure/logs/http.log >> Measure/results/AD7_http.log
$U addr_ad7_v2 -quit -executeMethod MeasureBuild.AddrBuild -phase ad7_v2
s=$(wc -l < Measure/logs/http.log); $R $P AD7 run2 ""
echo "=== run2 ===" >> Measure/results/AD7_http.log; tail -n +$((s+1)) Measure/logs/http.log >> Measure/results/AD7_http.log
Measure/swap_aa.sh ad3_after

# AB2: 세 Player를 먼저 빌드하고, 각자 한 번 예열한 뒤 8라운드 동안 순서를 섞어 실행
for n in 0 1000 10000; do $U ab2_$n -quit -executeMethod MeasureBuild.BuildAB2 -count $n; done
for n in 0 1000 10000; do $R Builds/AB2_$n/AssetBundleStudy.exe AB2 n$n warmup; done
for round in 1 2 3 4 5 6 7 8; do
  for n in $(printf "0\n1000\n10000\n" | shuf); do $R Builds/AB2_$n/AssetBundleStudy.exe AB2 n$n r$round; done
done
echo PHASE3_DONE
