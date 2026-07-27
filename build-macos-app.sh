#!/usr/bin/env bash
# macOS .app paketi üretir. Mikrofon izni bundle kimliğine bağlandığı için canlı
# mikrofon yalnızca bu paketle çalışır (terminalden `dotnet run` iznini terminale atfeder).
set -euo pipefail

PROJECT="src/SentimentAnalysis.Desktop.Avalonia/SentimentAnalysis.Desktop.Avalonia.csproj"
PLIST="src/SentimentAnalysis.Desktop.Avalonia/Info.plist"
APP="dist/Sesli Duygu Analizi.app"
RID="${RID:-osx-arm64}"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained false -o "$APP/Contents/MacOS"

cp "$PLIST" "$APP/Contents/Info.plist"

# Ad-hoc imza: TCC'nin izni kalıcı olarak hatırlaması için bundle imzalı olmalı.
codesign --force --deep --sign - "$APP"

echo "Hazır: $APP"
echo "Çalıştırmak için: open '$APP'"
