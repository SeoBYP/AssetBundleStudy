#!/bin/bash
# Addressables 측정 (AD1~AD6). aa 폴더를 단계별로 갈아 끼운다.
cd "$(dirname "$0")/.."
P=Builds/Player/AssetBundleStudy.exe; R=Measure/run.sh; S=Measure/swap_aa.sh
$S ad3_after
$R $P AD1 player ""
$R $P AD3 after ""
for v in plain unloadunused; do $R $P AD4 $v ""; done
for v in obj_instantiate instasync_release instasync_destroy instasync_releaseinstance; do $R $P AD6 $v ""; done
$S ad3_before; $R $P AD3 before ""
for m in together separate label; do $S ad2_$m; for r in 1 2 3; do $R $P AD2 $m $r; done; done
# AD1: Addressables 콘텐츠 없이 Player만 빌드한 경우
rm -rf Builds/Player/AssetBundleStudy_Data/StreamingAssets/aa; $R $P AD1 player_no_aa ""
$S ad3_after
