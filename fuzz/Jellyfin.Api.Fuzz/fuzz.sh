#!/bin/sh

set -e

dotnet build -c Release ../../MulletaFlix.Api/MulletaFlix.Api.csproj --output bin
sharpfuzz bin/MulletaFlix.Api.dll
cp bin/MulletaFlix.Api.dll .

dotnet build
mkdir -p Findings
AFL_SKIP_BIN_CHECK=1 afl-fuzz -i "Testcases/$1" -o "Findings/$1" -t 5000 ./bin/Debug/net10.0/MulletaFlix.Api.Fuzz "$1"

