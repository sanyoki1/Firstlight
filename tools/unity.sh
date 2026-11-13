#!/usr/bin/env bash
# Usage: tools/unity.sh <method> <logname>   (runs Unity in batch mode on this project)
UNITY="/c/Program Files/Unity/Hub/Editor/6000.4.2f1/Editor/Unity.exe"
ROOT="$(cd "$(dirname "$0")/.." && pwd -W)"
mkdir -p "$ROOT/runs"
"$UNITY" -batchmode -quit -projectPath "$ROOT" -executeMethod "$1" -logFile "$ROOT/runs/$2.log"
echo "exit code $?"
