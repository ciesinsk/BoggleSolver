param(
    [string]$CorpusId = "deu_news_2024_1M",
    [int]$TopN = 100000,
    [string]$OutFile = "de_top_${TopN}.txt",
    [int]$MinLen = 2,
    [switch]$Uppercase = $true,
    [switch]$KeepHyphens = $false,
    [switch]$AttributionHeader = $true
)

$ErrorActionPreference = "Stop"

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }
function Say($m){ Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $m" }

function Normalize-Word([string]$w, [bool]$keepHyphens) {
    $pattern = $keepHyphens ? '[^A-Za-zÄÖÜäöüß-]' : '[^A-Za-zÄÖÜäöüß]'
    $w = $w -replace $pattern, ''
    if ($Uppercase) { $w = $w.ToUpperInvariant() }
    return $w
}
function Have-Cmd($n){ [bool](Get-Command $n -ErrorAction SilentlyContinue) }

# Temp & URLs
$temp = Join-Path $env:TEMP ("lcc_" + [Guid]::NewGuid())
$null = New-Item -ItemType Directory -Force -Path $temp
$download = Join-Path $temp "$CorpusId.tar.gz"
$extractDir = Join-Path $temp $CorpusId
$baseUrl = "https://downloads.wortschatz-leipzig.de/corpora"
$url = "$baseUrl/$CorpusId.tar.gz"

Say "Arbeitsverzeichnis: $temp"
Say "Korpus-ID: $CorpusId"
Say "Ziel: Top $TopN Wörter → $OutFile"
Say "Download: $url"

# Download mit Fallbacks
$downloaded = $false
try {
    Invoke-WebRequest -Uri $url -OutFile $download -UseBasicParsing `
      -Headers @{ "User-Agent"="Mozilla/5.0 PowerShell"; "Accept"="*/*"} -MaximumRedirection 5
    $downloaded = $true
    Say "Download via Invoke-WebRequest erfolgreich."
} catch { Say "Invoke-WebRequest fehlgeschlagen: $($_.Exception.Message)" }

if (-not $downloaded -and (Have-Cmd "curl.exe")) {
    Say "Fallback: curl.exe …"
    & curl.exe -L "$url" -o "$download"
    if (Test-Path $download) { $downloaded = $true; Say "Download via curl.exe erfolgreich." }
}
if (-not $downloaded -and (Get-Command Start-BitsTransfer -ErrorAction SilentlyContinue)) {
    Say "Fallback: BITS …"
    Start-BitsTransfer -Source $url -Destination $download
    $downloaded = $true; Say "Download via BITS erfolgreich."
}
if (-not $downloaded) { throw "Download fehlgeschlagen. URL/Proxy/TLS prüfen: $url" }

# Entpacken
$null = New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
$extracted = $false
if (Have-Cmd "tar") {
    Say "Entpacke mit tar …"
    try { tar -xzf "$download" -C "$temp"; $extracted = $true; Say "Entpacken mit tar erfolgreich." }
    catch { Say "tar fehlgeschlagen: $($_.Exception.Message)" }
}
if (-not $extracted -and (Have-Cmd "7z")) {
    Say "Fallback: 7z …"
    & 7z x "$download" -o"$temp" -y | Out-Null
    $tarPath = Join-Path $temp "$CorpusId.tar"
    if (-not (Test-Path $tarPath)) {
        $cand = Get-ChildItem $temp -Filter "*.tar" | Select-Object -First 1
        if ($cand) { $tarPath = $cand.FullName }
    }
    if (-not (Test-Path $tarPath)) { throw "Nach dem .gz keine .tar gefunden." }
    & 7z x "$tarPath" -o"$temp" -y | Out-Null
    $extracted = $true; Say "Entpacken mit 7z erfolgreich."
}
if (-not $extracted) { throw "Entpacken nicht möglich. Bitte 'tar' oder 7-Zip (7z) bereitstellen." }

# --- NEU: words-Datei robust finden ----------------------------------------
# Manche Archive legen die Dateien NICHT in $CorpusId\ sondern direkt ab,
# und der Dateiname kann variieren (words.txt, deu_news_2024_1M-words.txt, wordlist.txt, etc.)
$searchRoot = $temp
$wordCandidates = @(
    Get-ChildItem -Path $searchRoot -Recurse -File -Filter "words.txt";
    Get-ChildItem -Path $searchRoot -Recurse -File -Filter "*-words.txt";
    Get-ChildItem -Path $searchRoot -Recurse -File -Filter "wordlist*.txt";
    Get-ChildItem -Path $searchRoot -Recurse -File -Filter "*words*.txt"
) | Where-Object { $_ } | Select-Object -Unique

if (-not $wordCandidates -or $wordCandidates.Count -eq 0) {
    Say "Keine passende words-Datei gefunden. Verfügbare Dateien im Archiv:"
    Get-ChildItem -Path $searchRoot -Recurse -File | Select-Object FullName, Length | Format-Table -AutoSize | Out-String | Write-Host
    throw "words.txt nicht gefunden. (Struktur des Archivs abweichend.)"
}

# Bevorzugung: exakt 'words.txt' im Ordner $CorpusId, sonst die größte *words*.txt
$prefer = $wordCandidates | Where-Object { $_.Name -ieq "words.txt" -and $_.DirectoryName -like "*$CorpusId*" } | Select-Object -First 1
if (-not $prefer) {
    $prefer = $wordCandidates | Sort-Object Length -Descending | Select-Object -First 1
}
$wordsFile = $prefer.FullName
Say "Gefundene words-Datei: $wordsFile (Größe: $((Get-Item $wordsFile).Length) Bytes)"

# Einlesen & TopN bilden
Say "Lese und filtere words …"
$unique = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::InvariantCultureIgnoreCase)
$result = New-Object 'System.Collections.Generic.List[string]'

$lines = Get-Content -LiteralPath $wordsFile -Encoding UTF8
$processed = 0
foreach ($line in $lines) {
    $processed++
    # Häufig: TSV mit 'Rang<TAB>Wort<TAB>Freq...'; fallback: wenn nur 1 Spalte, nimm diese
    $parts = $line -split "`t"
    $rawWord = if ($parts.Count -ge 2) { $parts[1].Trim() } else { $line.Trim() }
    if (-not $rawWord) { continue }

    $w = Normalize-Word $rawWord $KeepHyphens
    if ($w.Length -lt $MinLen) { continue }

    if ($unique.Add($w)) {
        $result.Add($w) | Out-Null
        if ($result.Count -ge $TopN) { break }
    }
}

Say "Verarbeitet: $processed Zeilen. Ergebnis (eindeutig): $($result.Count)."

# Ausgabe
$header = @()
if ($AttributionHeader) {
$header = @(
  "# Quelle: Leipzig Corpora Collection (Wortschatz Leipzig) – words aus $CorpusId",
  "# Download: $url",
  "# Erzeugt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ssK')",
  "# Parameter: TopN=$TopN MinLen=$MinLen Uppercase=$($Uppercase.IsPresent) KeepHyphens=$($KeepHyphens.IsPresent)",
  "# Lizenzhinweise der Quelle beachten."
)}
$encoding = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllLines($OutFile, ($header + $result), $encoding)

Say "Fertig: $OutFile"
Say "Temp-Verzeichnis: $temp"
