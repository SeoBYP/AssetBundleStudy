#!/bin/bash
# 측정에 필요한 모든 빌드. AB2 빌드는 별도(build_ab2.sh).
set -e
cd "$(dirname "$0")/.."
U=Measure/unity.sh
$U setup -quit -executeMethod MeasureSetup.Setup
$U bundles -quit -executeMethod MeasureBuild.BuildBundles
for p in ad2_together ad2_separate ad2_label ad3_before ad3_after; do
  $U addr_$p -quit -executeMethod MeasureBuild.AddrBuild -phase $p
done
$U player -quit -executeMethod MeasureBuild.BuildPlayer
