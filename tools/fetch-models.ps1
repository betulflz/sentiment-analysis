<#
.SYNOPSIS
    SentimentAnalysis'in ihtiyaç duyduğu model dosyalarını indirir.

.DESCRIPTION
    Doğrudan indirilebilen üç dosyayı çeker:
      - ggml-large-v3-turbo-q5_0.bin   (Whisper STT, ~547 MB)
      - silero_vad.onnx                (konuşma tespiti, ~2 MB)
      - sentencepiece.bpe.model        (XLM-R tokenizer, ~5 MB)

    Kalan iki ONNX modeli (prozodi + metin duygu) PyTorch checkpoint'lerinden export
    edilmelidir; onlar için tools/export_onnx_models.py kullanılır.

    Var olan ve boyutu makul olan dosyalar tekrar indirilmez (idempotent).

.PARAMETER ModelDir
    Hedef klasör. Varsayılan: %APPDATA%\SentimentAnalysis\models (uygulamanın aradığı yer).

.PARAMETER Force
    Var olan dosyaları da yeniden indirir.

.EXAMPLE
    pwsh tools/fetch-models.ps1
#>
[CmdletBinding()]
param(
    [string]$ModelDir = (Join-Path $env:APPDATA 'SentimentAnalysis\models'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# İndirilecek dosyalar: ad, URL, beklenen asgari boyut (bayt) — kesik/hatalı indirmeyi yakalar.
$files = @(
    @{
        Name    = 'ggml-large-v3-turbo-q5_0.bin'
        Url     = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin'
        MinSize = 500MB
        What    = 'Whisper STT modeli (kuantize large-v3-turbo)'
    },
    @{
        Name    = 'silero_vad.onnx'
        Url     = 'https://raw.githubusercontent.com/snakers4/silero-vad/master/src/silero_vad/data/silero_vad.onnx'
        MinSize = 1MB
        What    = 'Silero VAD (konuşma var mı)'
    },
    @{
        Name    = 'sentencepiece.bpe.model'
        Url     = 'https://huggingface.co/FacebookAI/xlm-roberta-base/resolve/main/sentencepiece.bpe.model'
        MinSize = 4MB
        What    = 'XLM-R SentencePiece tokenizer'
    }
)

function Get-CurlPath {
    $c = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return $null
}

New-Item -ItemType Directory -Path $ModelDir -Force | Out-Null
Write-Host "Model klasoru: $ModelDir" -ForegroundColor Cyan
Write-Host ''

$curl = Get-CurlPath
$failed = @()

foreach ($f in $files) {
    $dest = Join-Path $ModelDir $f.Name

    if ((Test-Path $dest) -and -not $Force) {
        $size = (Get-Item $dest).Length
        if ($size -ge $f.MinSize) {
            Write-Host ("[ATLA] {0} - zaten var ({1:N1} MB)" -f $f.Name, ($size / 1MB)) -ForegroundColor DarkGray
            continue
        }
        Write-Host ("[TEKRAR] {0} - mevcut dosya cok kucuk ({1:N1} MB), yeniden indiriliyor" -f $f.Name, ($size / 1MB)) -ForegroundColor Yellow
        Remove-Item $dest -Force
    }

    Write-Host ("[INDIR] {0} - {1}" -f $f.Name, $f.What) -ForegroundColor Green
    $tmp = "$dest.partial"
    if (Test-Path $tmp) { Remove-Item $tmp -Force }

    try {
        if ($curl) {
            # curl.exe ilerleme cubugu gosterir ve buyuk dosyalarda Invoke-WebRequest'ten cok daha hizlidir.
            & $curl -L --fail --progress-bar -o $tmp $f.Url
            if ($LASTEXITCODE -ne 0) { throw "curl cikis kodu $LASTEXITCODE" }
        }
        else {
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $f.Url -OutFile $tmp -UseBasicParsing
        }

        $size = (Get-Item $tmp).Length
        if ($size -lt $f.MinSize) {
            throw ("indirilen dosya beklenenden kucuk: {0:N1} MB (asgari {1:N1} MB)" -f ($size / 1MB), ($f.MinSize / 1MB))
        }

        Move-Item $tmp $dest -Force
        Write-Host ("        tamam - {0:N1} MB" -f ($size / 1MB)) -ForegroundColor DarkGreen
    }
    catch {
        if (Test-Path $tmp) { Remove-Item $tmp -Force }
        Write-Host ("[HATA]  {0}: {1}" -f $f.Name, $_.Exception.Message) -ForegroundColor Red
        $failed += $f.Name
    }
}

Write-Host ''
Write-Host '--- Model klasoru durumu ---' -ForegroundColor Cyan
$expected = @(
    @{ Name = 'ggml-large-v3-turbo-q5_0.bin'; Optional = $false },
    @{ Name = 'silero_vad.onnx'; Optional = $false },
    @{ Name = 'sentencepiece.bpe.model'; Optional = $false },
    @{ Name = 'prosody-msp-dim.onnx'; Optional = $false },
    @{ Name = 'text-emotion-xlmr.onnx'; Optional = $false },
    @{ Name = 'prosody-turkish-ser.onnx'; Optional = $true }   # Turkce'ye ozel ikinci prozodi kanali
)
foreach ($e in $expected) {
    $p = Join-Path $ModelDir $e.Name
    if (Test-Path $p) {
        Write-Host ("  [VAR]  {0,-32} {1,8:N1} MB" -f $e.Name, ((Get-Item $p).Length / 1MB))
    }
    elseif ($e.Optional) {
        Write-Host ("  [ops]  {0,-32} opsiyonel (Turkce SER)" -f $e.Name) -ForegroundColor DarkGray
    }
    else {
        Write-Host ("  [YOK]  {0}" -f $e.Name) -ForegroundColor Yellow
    }
}

Write-Host ''
if ($failed.Count -gt 0) {
    Write-Host ("Basarisiz: {0}" -f ($failed -join ', ')) -ForegroundColor Red
    exit 1
}
Write-Host 'Dogrudan indirilebilen dosyalar hazir.' -ForegroundColor Green
Write-Host 'Kalan iki ONNX modeli icin: python tools/export_onnx_models.py' -ForegroundColor Cyan
