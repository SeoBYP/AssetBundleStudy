#!/bin/bash
# AssetBundle 측정 (AB1, AB3~AB9). http 서버가 8765에서 Builds/를 서비스하고 있어야 한다(AB5).
cd "$(dirname "$0")/.."
P=Builds/Player/AssetBundleStudy.exe; R=Measure/run.sh
$R $P AB1 "" ""
for m in lzma lz4 none; do for r in 1 2 3 4 5; do $R $P AB3 $m $r; done; done
for m in lzma lz4 none; do $R $P AB4 $m ""; done
for c in lzma lz4; do for v in nover ver_first ver_second; do $R $P AB5 $v $c -comp $c; done; done
$R $P AB6 "" ""
for v in dup shared; do $R $P AB7 $v ""; done
$R $P AB8 "" ""
$R $P AB9 "" ""
