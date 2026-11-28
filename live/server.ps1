$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $env:LABDROP) {
  if (Test-Path 'C:\LabDrop') { $env:LABDROP = 'C:\LabDrop' }
  else { $env:LABDROP = Join-Path $repoRoot 'labdrop' }
}
Add-Type -Path (Join-Path $PSScriptRoot 'Trend.cs')
$htmlPath = Join-Path $PSScriptRoot 'index.html'
$pathsFile = Join-Path $PSScriptRoot 'paths.json'
$script:locations = [ordered]@{
  specview = Join-Path $env:LABDROP (Join-Path 'SpecViewShareddata' 'Copy Logs')
  cole = Join-Path $env:LABDROP 'ColePalmerSharedData'
  gc = Join-Path $env:LABDROP (Join-Path 'TotalChromSharedData' 'CopyLogs')
  calibration = Join-Path $env:LABDROP 'livedashboardvariables'
  injectionTable = Join-Path (Join-Path $env:LABDROP 'InjectionTables') 'injection-table.csv'
}
$knownLab = @{
  specview = 'C:\SV32\4871-913861-3zTubeRx&StirredRx'
  cole = 'C:\Users\SUNY\Desktop\ColePalmer_gasflowmeter\ColePalmerData'
  gc = 'C:\Users\Mahajan\Desktop\TotalChrom_Files\Data'
}
foreach ($key in @($knownLab.Keys)) {
  if (Test-Path -LiteralPath $knownLab[$key]) { $script:locations[$key] = $knownLab[$key] }
}
if (Test-Path $pathsFile) {
  $saved = Get-Content -Raw -Path $pathsFile | ConvertFrom-Json
  foreach ($key in @($script:locations.Keys)) {
    $value = [string]$saved.$key
    if (-not [string]::IsNullOrWhiteSpace($value)) { $script:locations[$key] = $value }
  }
}
$devNotesFile = Join-Path $PSScriptRoot 'dev-notes.json'
$script:devNotesMax = 20000
function Read-JsonStrings($text) {
  # ConvertFrom-Json turns ISO timestamps into DateTime and rewrites them.
  $doc = [System.Text.Json.JsonDocument]::Parse([string]$text)
  try {
    $map = @{}
    foreach ($prop in $doc.RootElement.EnumerateObject()) {
      if ($prop.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
        $map[$prop.Name] = $prop.Value.GetString()
      } elseif ($prop.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Null) {
        $map[$prop.Name] = $null
      } else {
        $map[$prop.Name] = $prop.Value.GetRawText()
      }
    }
    return $map
  } finally { $doc.Dispose() }
}
function Read-DevNotes {
  if (-not (Test-Path -LiteralPath $devNotesFile)) {
    return [pscustomobject]@{ content = ''; updatedAt = '' }
  }
  try {
    $map = Read-JsonStrings ([IO.File]::ReadAllText($devNotesFile))
  } catch {
    return [pscustomobject]@{ content = ''; updatedAt = '' }
  }
  $content = ''
  if ($null -ne $map['content']) { $content = [string]$map['content'] }
  if ($content.Length -gt $script:devNotesMax) { $content = $content.Substring(0, $script:devNotesMax) }
  $updatedAt = ''
  if ($null -ne $map['updatedAt']) { $updatedAt = [string]$map['updatedAt'] }
  return [pscustomobject]@{ content = $content; updatedAt = $updatedAt }
}
function Write-DevNotes([string]$content) {
  if ($null -eq $content) { $content = '' }
  if ($content.Length -gt $script:devNotesMax) { $content = $content.Substring(0, $script:devNotesMax) }
  $updatedAt = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
  $json = ([pscustomobject]@{ content = $content; updatedAt = $updatedAt } | ConvertTo-Json -Compress) + "`n"
  $tmp = '{0}.{1}.{2}.tmp' -f $devNotesFile, $PID, [DateTime]::UtcNow.Ticks
  $utf8 = New-Object System.Text.UTF8Encoding $false
  [IO.File]::WriteAllText($tmp, $json, $utf8)
  if (Test-Path -LiteralPath $devNotesFile) {
    try { [IO.File]::Move($tmp, $devNotesFile, $true) }
    catch {
      $bak = "$devNotesFile.bak"
      [IO.File]::Replace($tmp, $devNotesFile, $bak, $true)
      Remove-Item -LiteralPath $bak -Force -ErrorAction SilentlyContinue
    }
  } else {
    [IO.File]::Move($tmp, $devNotesFile)
  }
  return [pscustomobject]@{ content = $content; updatedAt = $updatedAt }
}
function Save-Locations {
  $obj = [pscustomobject]@{
    specview = $script:locations.specview
    cole = $script:locations.cole
    gc = $script:locations.gc
    calibration = $script:locations.calibration
    injectionTable = $script:locations.injectionTable
  }
  [IO.File]::WriteAllText($pathsFile, ($obj | ConvertTo-Json -Compress))
  $script:varDir = $script:locations.calibration
  if (-not (Test-Path -LiteralPath $script:varDir)) { New-Item -ItemType Directory -Path $script:varDir | Out-Null }
  [Trend]::UseLocations($script:locations.specview, $script:locations.cole, $script:locations.gc)
  if ($script:pub) {
    $script:pub.Specview = [string]$script:locations.specview
    $script:pub.Cole = [string]$script:locations.cole
    $script:pub.Gc = [string]$script:locations.gc
  }
}
$script:trendGate = New-Object object
$script:pub = [hashtable]::Synchronized(@{
  Enabled = $false
  Kick = $false
  Disconnect = $false
  VarsJson = ''
  Url = 'https://minipilot.jayhasty.com/api/ingest'
  Token = ''
  LastError = ''
  LastAt = ''
  Specview = ''
  Cole = ''
  Gc = ''
  Gate = $script:trendGate
})
function Import-PublishConfig {
  $candidates = @(Join-Path $PSScriptRoot 'publish.json')
  if ($env:USERPROFILE) { $candidates += (Join-Path $env:USERPROFILE '.minipilot-publish.json') }
  foreach ($file in $candidates) {
    if (-not (Test-Path -LiteralPath $file)) { continue }
    try {
      $doc = Get-Content -Raw -LiteralPath $file | ConvertFrom-Json
      if ($doc.url) { $script:pub.Url = [string]$doc.url }
      if ($doc.token) { $script:pub.Token = [string]$doc.token }
      break
    } catch {
      $script:pub.LastError = "Could not read $file"
    }
  }
  if ($env:MINIPILOT_PUBLISH_URL) { $script:pub.Url = [string]$env:MINIPILOT_PUBLISH_URL }
  if ($env:MINIPILOT_PUBLISH_TOKEN) { $script:pub.Token = [string]$env:MINIPILOT_PUBLISH_TOKEN }
}
function Invoke-Trend([scriptblock]$Action) {
  [System.Threading.Monitor]::Enter($script:trendGate)
  try { return & $Action }
  finally { [System.Threading.Monitor]::Exit($script:trendGate) }
}
function Publish-Status {
  return [pscustomobject]@{
    enabled = [bool]$script:pub.Enabled
    configured = -not [string]::IsNullOrWhiteSpace([string]$script:pub.Token)
    lastError = [string]$script:pub.LastError
    lastAt = [string]$script:pub.LastAt
    url = [string]$script:pub.Url
  }
}
Import-PublishConfig
Save-Locations
if (-not [string]::IsNullOrWhiteSpace([string]$script:pub.Token)) {
  $script:pub.Enabled = $true
  $script:pub.Kick = $true
} else {
  $script:pub.LastError = "Copy live/publish.json onto this computer, next to server.ps1."
}

$publishRs = [runspacefactory]::CreateRunspace()
$publishRs.Open()
$publisher = [powershell]::Create()
$publisher.Runspace = $publishRs
[void]$publisher.AddScript(@'
param($sync, $trendPath)
$ErrorActionPreference = "Stop"
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}
if (-not ("Trend" -as [type])) { Add-Type -Path $trendPath }
function As-Text($v) {
  if ($null -eq $v) { return "" }
  return [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0}", $v)
}
function Points-FromVars([string]$varsJson) {
  $h2m = ""; $h2b = ""; $com = ""; $cob = ""; $co2m = ""; $co2b = ""; $ch4m = ""; $ch4b = ""
  $feedCo = ""; $feedCo2 = ""; $feedCh4 = ""
  if (-not [string]::IsNullOrWhiteSpace($varsJson)) {
    $doc = $varsJson | ConvertFrom-Json
    $cal = $doc.calibration
    $feed = $doc.feed
    if ($cal -and $cal.h2) { $h2m = As-Text $cal.h2.slope; $h2b = As-Text $cal.h2.intercept }
    if ($cal -and $cal.co) { $com = As-Text $cal.co.slope; $cob = As-Text $cal.co.intercept }
    if ($cal -and $cal.co2) { $co2m = As-Text $cal.co2.slope; $co2b = As-Text $cal.co2.intercept }
    if ($cal -and $cal.ch4) { $ch4m = As-Text $cal.ch4.slope; $ch4b = As-Text $cal.ch4.intercept }
    if ($feed) { $feedCo = As-Text $feed.co; $feedCo2 = As-Text $feed.co2; $feedCh4 = As-Text $feed.ch4 }
  }
  return [Trend]::BuildPoints($h2m, $h2b, $com, $cob, $co2m, $co2b, $ch4m, $ch4b, $feedCo, $feedCo2, $feedCh4)
}
function Post-Snapshot([string]$payload) {
  $req = [System.Net.HttpWebRequest]::Create([string]$sync.Url)
  $req.Method = "POST"
  $req.ContentType = "application/json; charset=utf-8"
  $req.Headers["Authorization"] = "Bearer " + [string]$sync.Token
  $req.Timeout = 20000
  $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
  $req.ContentLength = $bytes.Length
  $stream = $req.GetRequestStream()
  try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Close() }
  $resp = $req.GetResponse()
  try {
    $code = [int]$resp.StatusCode
    if ($code -lt 200 -or $code -ge 300) { throw "The website returned $code" }
  } finally { $resp.Close() }
  $sync.LastError = ""
  $sync.LastAt = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
}
$next = [datetime]::MinValue
while ($true) {
  try {
    Start-Sleep -Milliseconds 400
    $disconnect = [bool]$sync.Disconnect
    $kick = [bool]$sync.Kick
    $enabled = [bool]$sync.Enabled
    if (-not $disconnect -and -not $kick -and -not $enabled) { continue }
    if (-not $disconnect -and -not $kick -and [datetime]::UtcNow -lt $next) { continue }
    $sync.Kick = $false
    if ([string]::IsNullOrWhiteSpace([string]$sync.Token)) {
      $sync.LastError = "Copy live/publish.json onto this computer, next to server.ps1."
      $sync.Enabled = $false
      $sync.Disconnect = $false
      continue
    }
    if ($disconnect) {
      $sync.Disconnect = $false
      Post-Snapshot '{"connected":false}'
      continue
    }
    if (-not $enabled) { continue }
    $next = [datetime]::UtcNow.AddSeconds(3)
    [System.Threading.Monitor]::Enter($sync.Gate)
    try {
      [Trend]::UseLocations([string]$sync.Specview, [string]$sync.Cole, [string]$sync.Gc)
      $trend = [Trend]::BuildJson()
      $points = Points-FromVars ([string]$sync.VarsJson)
    } finally {
      [System.Threading.Monitor]::Exit($sync.Gate)
    }
    $vars = [string]$sync.VarsJson
    if ([string]::IsNullOrWhiteSpace($vars)) { $vars = "null" }
    Post-Snapshot ('{"connected":true,"vars":' + $vars + ',"trend":' + $trend + ',"points":' + $points + '}')
  } catch {
    $sync.LastError = $_.Exception.Message
  }
}
'@).AddArgument($script:pub).AddArgument((Join-Path $PSScriptRoot 'Trend.cs'))
$script:publishHandle = $publisher.BeginInvoke()

function Send-Bytes($ctx, $bytes, $type, $status) {
  $ctx.Response.StatusCode = $status
  $ctx.Response.ContentType = $type
  $ctx.Response.ContentLength64 = $bytes.Length
  $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
}
function Send-Json($ctx, $obj, $status) {
  $json = $obj | ConvertTo-Json -Depth 8 -Compress
  Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($json)) 'application/json; charset=utf-8' $status
}
function Read-Body($req) {
  $enc = $req.ContentEncoding
  if (-not $enc) { $enc = [Text.Encoding]::UTF8 }
  $reader = New-Object IO.StreamReader($req.InputStream, $enc)
  try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Safe-Name($name) {
  if ([string]::IsNullOrWhiteSpace($name)) { return 'run-variables' }
  $clean = [regex]::Replace($name, '[^\w .\-]+', '').Trim().Trim('.')
  if ([string]::IsNullOrWhiteSpace($clean)) { return 'run-variables' }
  return $clean
}
function Pick-Path($save, $dir, $suggested) {
  $rs = [runspacefactory]::CreateRunspace()
  $rs.ApartmentState = 'STA'
  $rs.Open()
  $ps = [powershell]::Create()
  $ps.Runspace = $rs
  [void]$ps.AddScript({
    param($save, $dir, $suggested)
    Add-Type -AssemblyName System.Windows.Forms
    if ($save) {
      $d = New-Object System.Windows.Forms.SaveFileDialog
      $d.Title = 'Save run variables'
      $d.Filter = 'Run variables (*.json)|*.json'
      $d.InitialDirectory = $dir
      $d.FileName = $suggested
      if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $d.FileName }
    } else {
      $d = New-Object System.Windows.Forms.OpenFileDialog
      $d.Title = 'Load run variables'
      $d.Filter = 'Run variables (*.json)|*.json'
      $d.InitialDirectory = $dir
      if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $d.FileName }
    }
    return ''
  }).AddArgument([bool]$save).AddArgument($dir).AddArgument($suggested)
  try {
    $out = $ps.Invoke()
    if ($ps.HadErrors) { throw $ps.Streams.Error[0].Exception.Message }
    if ($out -and $out.Count -gt 0) { return [string]$out[0] }
    return ''
  } finally {
    $ps.Dispose()
    $rs.Dispose()
  }
}

function Find-Gh {
  $cmd = Get-Command gh -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  foreach ($p in @('/usr/bin/gh', "$env:ProgramFiles\GitHub CLI\gh.exe")) {
    if (Test-Path -LiteralPath $p) { return $p }
  }
  return $null
}
function New-SupportIssue($email, $message) {
  $gh = Find-Gh
  if (-not $gh) { throw 'GitHub CLI is not available on this computer, so the request could not be filed.' }
  $title = '[MiniPilotLiveAnalysis-SUPPORT] Request'
  $bodyText = "Reply to: $email`r`n`r`n$message"
  $payload = @{ title = $title; body = $bodyText } | ConvertTo-Json -Compress
  $tmp = New-TemporaryFile
  try {
    [IO.File]::WriteAllText($tmp.FullName, $payload)
    $raw = & $gh api --method POST repos/heyhastyjay/MiniPilotLiveAnalysis/issues --input $tmp.FullName 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($raw | Out-String).Trim() }
    $issue = ($raw | Out-String) | ConvertFrom-Json
    return [pscustomobject]@{
      number = $issue.number
      url = $issue.html_url
      subject = $title
    }
  } finally {
    Remove-Item -LiteralPath $tmp.FullName -Force -ErrorAction SilentlyContinue
  }
}

$listener = New-Object System.Net.HttpListener
$prefix = 'http://127.0.0.1:8765/'
$listener.Prefixes.Add($prefix)
$listener.Start()
Write-Host "trend server $prefix"

while ($listener.IsListening) {
  $ctx = $listener.GetContext()
  try {
    $ctx.Response.Headers['Cache-Control'] = 'no-store'
    $path = $ctx.Request.Url.AbsolutePath
    if ($path -eq '/igit.png') {
      $file = Join-Path $PSScriptRoot 'igit.png'
      $bytes = [IO.File]::ReadAllBytes($file)
      Send-Bytes $ctx $bytes 'image/png' 200
    } elseif ($path -eq '/specview-stirred.png') {
      $file = Join-Path $PSScriptRoot 'specview-stirred.png'
      $bytes = [IO.File]::ReadAllBytes($file)
      Send-Bytes $ctx $bytes 'image/png' 200
    } elseif ($path -eq '/api/publish' -and $ctx.Request.HttpMethod -eq 'GET') {
      Send-Json $ctx (Publish-Status) 200
    } elseif ($path -eq '/api/publish' -and $ctx.Request.HttpMethod -eq 'POST') {
      $doc = (Read-Body $ctx.Request) | ConvertFrom-Json
      $turnOn = $false
      if ($null -ne $doc.enabled) { $turnOn = [bool]$doc.enabled }
      if ($doc.vars) { $script:pub.VarsJson = ($doc.vars | ConvertTo-Json -Depth 8 -Compress) }
      if ($turnOn -and [string]::IsNullOrWhiteSpace([string]$script:pub.Token)) {
        $script:pub.Enabled = $false
        Send-Json $ctx @{ error = 'Copy live/publish.json onto this computer, next to server.ps1.' } 400
      } else {
        if ($script:pub.Enabled -and -not $turnOn) { $script:pub.Disconnect = $true }
        $script:pub.Enabled = $turnOn
        if ($turnOn) { $script:pub.Kick = $true }
        Send-Json $ctx (Publish-Status) 200
      }
    } elseif ($path -eq '/api/trend') {
      $json = Invoke-Trend { [Trend]::BuildJson() }
      Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($json)) 'application/json; charset=utf-8' 200
    } elseif ($path -eq '/api/vars/list') {
      if (-not (Test-Path -LiteralPath $script:varDir -PathType Container)) {
        Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes('[]')) 'application/json; charset=utf-8' 200
      } else {
      $files = @(Get-ChildItem -LiteralPath $script:varDir -Filter '*.json' -File | Sort-Object LastWriteTime -Descending)
      $parts = foreach ($f in $files) {
        ConvertTo-Json -InputObject ([pscustomobject]@{ name = $f.BaseName; file = $f.FullName }) -Depth 4 -Compress
      }
      $json = '[' + ($parts -join ',') + ']'
      Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($json)) 'application/json; charset=utf-8' 200
      }
    } elseif ($path -eq '/api/vars/load') {
      $name = Safe-Name $ctx.Request.QueryString['name']
      $file = Join-Path $script:varDir ($name + '.json')
      if (-not (Test-Path $file)) { Send-Json $ctx @{ error = 'File not found' } 404 }
      else {
        $text = [IO.File]::ReadAllText($file)
        Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($text)) 'application/json; charset=utf-8' 200
      }
    } elseif ($path -eq '/api/vars/save' -and $ctx.Request.HttpMethod -eq 'POST') {
      $raw = Read-Body $ctx.Request
      $doc = $raw | ConvertFrom-Json
      $name = Safe-Name $doc.name
      $file = Join-Path $script:varDir ($name + '.json')
      [IO.File]::WriteAllText($file, $raw)
      Send-Json $ctx @{ file = $file } 200
    } elseif ($path -eq '/api/vars/save-as' -and $ctx.Request.HttpMethod -eq 'POST') {
      $raw = Read-Body $ctx.Request
      $doc = $raw | ConvertFrom-Json
      $suggested = (Safe-Name $doc.name) + '.json'
      $file = Pick-Path $true $script:varDir $suggested
      if ([string]::IsNullOrWhiteSpace($file)) { Send-Json $ctx @{ file = '' } 200 }
      else {
        if (-not $file.EndsWith('.json')) { $file = $file + '.json' }
        $folder = Split-Path $file -Parent
        if (-not (Test-Path $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }
        [IO.File]::WriteAllText($file, $raw)
        Send-Json $ctx @{ file = $file } 200
      }
    } elseif ($path -eq '/api/table' -and $ctx.Request.HttpMethod -eq 'POST') {
      $raw = Read-Body $ctx.Request
      $doc = $raw | ConvertFrom-Json
      function As-Text($v) {
        if ($null -eq $v) { return '' }
        return [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $v)
      }
      $cal = $doc.calibration
      $feed = $doc.feed
      $csv = Invoke-Trend { [Trend]::BuildTable(
        $(if ($cal -and $cal.h2) { As-Text $cal.h2.slope } else { '' }),
        $(if ($cal -and $cal.h2) { As-Text $cal.h2.intercept } else { '' }),
        $(if ($cal -and $cal.co) { As-Text $cal.co.slope } else { '' }),
        $(if ($cal -and $cal.co) { As-Text $cal.co.intercept } else { '' }),
        $(if ($cal -and $cal.co2) { As-Text $cal.co2.slope } else { '' }),
        $(if ($cal -and $cal.co2) { As-Text $cal.co2.intercept } else { '' }),
        $(if ($cal -and $cal.ch4) { As-Text $cal.ch4.slope } else { '' }),
        $(if ($cal -and $cal.ch4) { As-Text $cal.ch4.intercept } else { '' }),
        $(if ($feed) { As-Text $feed.co } else { '' }),
        $(if ($feed) { As-Text $feed.co2 } else { '' }),
        $(if ($feed) { As-Text $feed.ch4 } else { '' })
      ) }
      $tableFile = $script:locations.injectionTable
      $tableDir = Split-Path -Parent $tableFile
      if ($tableDir -and -not (Test-Path -LiteralPath $tableDir)) { New-Item -ItemType Directory -Path $tableDir | Out-Null }
      [IO.File]::WriteAllText($tableFile, $csv)
      $ctx.Response.Headers['Content-Disposition'] = 'attachment; filename="injection-table.csv"'
      Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($csv)) 'text/csv; charset=utf-8' 200
    } elseif ($path -eq '/api/points' -and $ctx.Request.HttpMethod -eq 'POST') {
      $raw = Read-Body $ctx.Request
      $script:pub.VarsJson = $raw
      $doc = $raw | ConvertFrom-Json
      function As-Text($v) {
        if ($null -eq $v) { return '' }
        return [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $v)
      }
      $cal = $doc.calibration
      $feed = $doc.feed
      $json = Invoke-Trend { [Trend]::BuildPoints(
        $(if ($cal -and $cal.h2) { As-Text $cal.h2.slope } else { '' }),
        $(if ($cal -and $cal.h2) { As-Text $cal.h2.intercept } else { '' }),
        $(if ($cal -and $cal.co) { As-Text $cal.co.slope } else { '' }),
        $(if ($cal -and $cal.co) { As-Text $cal.co.intercept } else { '' }),
        $(if ($cal -and $cal.co2) { As-Text $cal.co2.slope } else { '' }),
        $(if ($cal -and $cal.co2) { As-Text $cal.co2.intercept } else { '' }),
        $(if ($cal -and $cal.ch4) { As-Text $cal.ch4.slope } else { '' }),
        $(if ($cal -and $cal.ch4) { As-Text $cal.ch4.intercept } else { '' }),
        $(if ($feed) { As-Text $feed.co } else { '' }),
        $(if ($feed) { As-Text $feed.co2 } else { '' }),
        $(if ($feed) { As-Text $feed.ch4 } else { '' })
      ) }
      Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($json)) 'application/json; charset=utf-8' 200
    } elseif ($path -eq '/api/dev-notes' -and $ctx.Request.HttpMethod -eq 'GET') {
      Send-Json $ctx @{ ok = $true; note = (Read-DevNotes) } 200
    } elseif ($path -eq '/api/dev-notes' -and $ctx.Request.HttpMethod -eq 'PUT') {
      $map = Read-JsonStrings (Read-Body $ctx.Request)
      if (-not $map.ContainsKey('content')) { Send-Json $ctx @{ ok = $false; error = 'content_required' } 400 }
      else {
        $content = ''
        if ($null -ne $map['content']) { $content = [string]$map['content'] }
        if ($content.Length -gt $script:devNotesMax) { Send-Json $ctx @{ ok = $false; error = 'content_too_long' } 400 }
        else { Send-Json $ctx @{ ok = $true; note = (Write-DevNotes $content) } 200 }
      }
    } elseif ($path -eq '/api/paths' -and $ctx.Request.HttpMethod -eq 'GET') {
      Send-Json $ctx ([pscustomobject]$script:locations) 200
    } elseif ($path -eq '/api/paths' -and $ctx.Request.HttpMethod -eq 'POST') {
      $doc = (Read-Body $ctx.Request) | ConvertFrom-Json
      $key = [string]$doc.key
      $next = ([string]$doc.path).Trim()
      $allowed = @('specview', 'cole', 'gc', 'calibration', 'injectionTable')
      if ($allowed -notcontains $key) { Send-Json $ctx @{ error = 'Unknown location' } 400 }
      elseif ([string]::IsNullOrWhiteSpace($next)) { Send-Json $ctx @{ error = 'Enter a location' } 400 }
      else {
        $script:locations[$key] = $next
        Save-Locations
        Send-Json $ctx ([pscustomobject]$script:locations) 200
      }
    } elseif ($path -eq '/api/support' -and $ctx.Request.HttpMethod -eq 'POST') {
      $doc = (Read-Body $ctx.Request) | ConvertFrom-Json
      $email = ([string]$doc.email).Trim()
      $message = ([string]$doc.message).Trim()
      if ($email -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { Send-Json $ctx @{ error = 'Enter an email address so we can write back' } 400 }
      elseif ([string]::IsNullOrWhiteSpace($message)) { Send-Json $ctx @{ error = 'Enter the issues, comments, or ideas' } 400 }
      elseif ($message.Length -gt 8000) { Send-Json $ctx @{ error = 'The note is too long' } 400 }
      else {
        $issue = New-SupportIssue $email $message
        $mailBody = "Reply to: $email`r`n`r`nGitHub: $($issue.url)`r`n`r`n$message"
        Send-Json $ctx @{
          number = $issue.number
          url = $issue.url
          subject = $issue.subject
          to = 'jay@jayhasty.com'
          mailBody = $mailBody
        } 200
      }
    } elseif ($path -eq '/api/vars/load-from' -and $ctx.Request.HttpMethod -eq 'POST') {
      $file = Pick-Path $false $script:varDir ''
      if ([string]::IsNullOrWhiteSpace($file)) { Send-Json $ctx @{ file = '' } 200 }
      else {
        $text = [IO.File]::ReadAllText($file)
        $data = $text | ConvertFrom-Json
        Send-Json $ctx @{ file = $file; data = $data } 200
      }
    } else {
      $bytes = [Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText($htmlPath))
      Send-Bytes $ctx $bytes 'text/html; charset=utf-8' 200
    }
  } catch {
    try {
      $msg = [Text.Encoding]::UTF8.GetBytes(($_ | Out-String))
      Send-Bytes $ctx $msg 'text/plain; charset=utf-8' 500
    } catch {}
    Write-Host $_
  } finally {
    try { $ctx.Response.Close() } catch {}
  }
}
