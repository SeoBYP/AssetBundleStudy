#!/bin/bash
# Player 실행 도우미. 사용법: Measure/run.sh <exe> <exp> <variant> [rep] [추가 인자...]
cd "$(dirname "$0")/.."
exe=$1 exp=$2 variant=$3 rep=$4; shift 4
args=(-exp "$exp" -out "$(pwd -W)/Measure/results/$exp.txt" -bundles "$(pwd -W)/Builds/Bundles"
      -screen-fullscreen 0 -screen-width 960 -screen-height 540
      -logFile "Measure/logs/player_${exp}${variant:+_$variant}${rep:+_$rep}.log")
[ -n "$variant" ] && args+=(-variant "$variant")
[ -n "$rep" ] && args+=(-rep "$rep")
"$exe" "${args[@]}" "$@" > /dev/null 2>&1
echo "[$exp${variant:+/$variant}${rep:+#$rep}] exit=$?"
