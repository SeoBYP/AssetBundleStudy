#!/bin/bash
# Player의 StreamingAssets/aa 를 Builds/aa/<phase> 로 교체한다.
cd "$(dirname "$0")/.."
dst=Builds/Player/AssetBundleStudy_Data/StreamingAssets/aa
rm -rf "$dst"; mkdir -p "$(dirname "$dst")"; cp -r "Builds/aa/$1" "$dst"; echo "aa <- $1"
