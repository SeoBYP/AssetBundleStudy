#!/bin/bash
# 사용법: Measure/unity.sh <로그이름> <unity 인자...>
cd "$(dirname "$0")/.."
name=$1; shift
"/c/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe" -batchmode -projectPath "$(pwd -W)" -logFile "Measure/logs/$name.log" "$@"
code=$?
grep -E "error CS|Exception|\[Measure\]" "Measure/logs/$name.log" | grep -v "^\s*at " | head -40
echo "[$name] exit=$code"
