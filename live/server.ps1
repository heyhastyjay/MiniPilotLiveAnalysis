$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'Trend.cs')
$htmlPath = Join-Path $PSScriptRoot 'index.html'
$varDir = 'C:\LabDrop\livedashboardvariables'
if (-not (Test-Path $varDir)) { New-Item -ItemType Directory -Path $varDir | Out-Null }

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
    } elseif ($path -eq '/api/trend') {
      $json = [Trend]::BuildJson()
      Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($json)) 'application/json; charset=utf-8' 200
    } elseif ($path -eq '/api/vars/list') {
      $files = @(Get-ChildItem -Path $varDir -Filter '*.json' -File | Sort-Object LastWriteTime -Descending)
      $parts = foreach ($f in $files) {
        ConvertTo-Json -InputObject ([pscustomobject]@{ name = $f.BaseName; file = $f.FullName }) -Depth 4 -Compress
      }
      $json = '[' + ($parts -join ',') + ']'
      Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($json)) 'application/json; charset=utf-8' 200
    } elseif ($path -eq '/api/vars/load') {
      $name = Safe-Name $ctx.Request.QueryString['name']
      $file = Join-Path $varDir ($name + '.json')
      if (-not (Test-Path $file)) { Send-Json $ctx @{ error = 'File not found' } 404 }
      else {
        $text = [IO.File]::ReadAllText($file)
        Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($text)) 'application/json; charset=utf-8' 200
      }
    } elseif ($path -eq '/api/vars/save' -and $ctx.Request.HttpMethod -eq 'POST') {
      $raw = Read-Body $ctx.Request
      $doc = $raw | ConvertFrom-Json
      $name = Safe-Name $doc.name
      $file = Join-Path $varDir ($name + '.json')
      [IO.File]::WriteAllText($file, $raw)
      Send-Json $ctx @{ file = $file } 200
    } elseif ($path -eq '/api/vars/save-as' -and $ctx.Request.HttpMethod -eq 'POST') {
      $raw = Read-Body $ctx.Request
      $doc = $raw | ConvertFrom-Json
      $suggested = (Safe-Name $doc.name) + '.json'
      $file = Pick-Path $true $varDir $suggested
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
      $csv = [Trend]::BuildTable(
        $(if ($cal -and $cal.h2) { As-Text $cal.h2.slope } else { '' }),
        $(if ($cal -and $cal.h2) { As-Text $cal.h2.intercept } else { '' }),
        $(if ($cal -and $cal.co) { As-Text $cal.co.slope } else { '' }),
        $(if ($cal -and $cal.co) { As-Text $cal.co.intercept } else { '' }),
        $(if ($cal -and $cal.co2) { As-Text $cal.co2.slope } else { '' }),
        $(if ($cal -and $cal.co2) { As-Text $cal.co2.intercept } else { '' }),
        $(if ($cal -and $cal.ch4) { As-Text $cal.ch4.slope } else { '' }),
        $(if ($cal -and $cal.ch4) { As-Text $cal.ch4.intercept } else { '' })
      )
      $ctx.Response.Headers['Content-Disposition'] = 'attachment; filename="injection-table.csv"'
      Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($csv)) 'text/csv; charset=utf-8' 200
    } elseif ($path -eq '/api/vars/load-from' -and $ctx.Request.HttpMethod -eq 'POST') {
      $file = Pick-Path $false $varDir ''
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
