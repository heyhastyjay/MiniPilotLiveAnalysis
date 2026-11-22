$ErrorActionPreference = 'Stop'
function Rgb([int]$r, [int]$g, [int]$b) { return $r + ($g * 256) + ($b * 65536) }

# 1-minute process series. Fault codes blanked. Exact duplicate traces dropped.
$keep = @(
  @{ h = 'Tags500-.Tag 15'; n = 'Tag 15'; ax = 1 },
  @{ h = 'Tags500-.Tag 14'; n = 'Tag 14'; ax = 1 },
  @{ h = 'Tags500-.Tag 16'; n = 'Tag 16'; ax = 2 },
  @{ h = 'Loop1.Process Value'; n = 'Loop1 PV'; ax = 1 },
  @{ h = 'Loop1.Working SetPoint'; n = 'Loop1 SP'; ax = 1 },
  @{ h = 'Loop3.Process Value'; n = 'Loop3 PV'; ax = 1 },
  @{ h = 'Loop1.Output'; n = 'Loop1 Out'; ax = 2 },
  @{ h = 'Loop6.Process Value'; n = 'Loop6 PV'; ax = 2 },
  @{ h = 'Loop3.Working SetPoint'; n = 'Loop3 SP'; ax = 1 },
  @{ h = 'Loop2.Process Value'; n = 'Loop2 PV'; ax = 1 },
  @{ h = 'AIs-.Module5 Inp1'; n = 'AI5 In1'; ax = 1 },
  @{ h = 'AIs-.Module5 Inp2'; n = 'AI5 In2'; ax = 1 },
  @{ h = 'AIs-.Module5 Inp4'; n = 'AI5 In4'; ax = 2 },
  @{ h = 'AIs-.Module5 Inp3'; n = 'AI5 In3'; ax = 1 },
  @{ h = 'Loop4.Working SetPoint'; n = 'Loop4 SP'; ax = 1 },
  @{ h = 'Tags500-.Tag 11'; n = 'Tag 11'; ax = 1 },
  @{ h = 'Loop4.Output'; n = 'Loop4 Out'; ax = 2 }
)
$lines = [IO.File]::ReadAllLines('G:\11-19\26_322.csv')
$header = $lines[0].Trim('"').Split('","')
$idx = @{}
for ($c = 0; $c -lt $header.Length; $c++) { $idx[$header[$c]] = $c }
$rows = New-Object System.Collections.Generic.List[object]
for ($i = 1; $i -lt $lines.Length; $i += 6) {
  $p = $lines[$i].Split(',')
  $t = [datetime]::ParseExact($p[0], 'yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
  $vals = New-Object object[] $keep.Count
  for ($k = 0; $k -lt $keep.Count; $k++) {
    $raw = $p[$idx[$keep[$k].h]]
    $v = 0.0
    if ([double]::TryParse($raw, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$v) -and $v -lt 9000 -and $v -gt -90) {
      $vals[$k] = $v
    } else {
      $vals[$k] = $null
    }
  }
  $rows.Add([pscustomobject]@{ T = $t.ToOADate(); V = $vals }) | Out-Null
}
Write-Host ("chart rows {0} series {1}" -f $rows.Count, $keep.Count)

$ppt = New-Object -ComObject PowerPoint.Application
$ppt.Visible = -1
$path = 'C:\Users\dmahajan\Downloads\MethanolMiniPilotRun1-093026-draftreport.pptx'
$pres = $ppt.Presentations.Open($path, $false, $false, $true)
$W = $pres.PageSetup.SlideWidth
$H = $pres.PageSetup.SlideHeight

function Set-RunFont($range, [int]$size, [int]$color, [bool]$bold) {
  $range.Font.Name = 'Lato'
  $range.Font.Size = $size
  $range.Font.Bold = $(if ($bold) { -1 } else { 0 })
  $range.Font.Color.RGB = $color
}

# Renumber existing page marks from / 9 to / 11
foreach ($slide in $pres.Slides) {
  foreach ($sh in @($slide.Shapes)) {
    if ($sh.HasTextFrame -ne -1) { continue }
    $tr = $sh.TextFrame.TextRange
    if ($tr.Text -match '^\s*\d+\s*/\s*9\s*$') {
      $tr.Text = ($tr.Text -replace '9\s*$', '11')
      Set-RunFont $tr 9 (Rgb 110 124 144) $false
    }
  }
}

function Add-Chrome($slide, [string]$page) {
  $bar = $slide.Shapes.AddShape(1, 0, 0, $W, 3.2)
  $bar.Fill.ForeColor.RGB = (Rgb 123 255 206)
  $bar.Line.Visible = 0
  $rule = $slide.Shapes.AddShape(1, 36, 508, $W - 72, 0.6)
  $rule.Fill.ForeColor.RGB = (Rgb 36 48 68)
  $rule.Line.Visible = 0
  $draft = $slide.Shapes.AddTextbox(1, 28, 512, 80, 18)
  $draft.TextFrame.TextRange.Text = 'Draft'
  Set-RunFont $draft.TextFrame.TextRange 9 (Rgb 110 124 144) $false
  $mid = $slide.Shapes.AddTextbox(1, 120, 512, $W - 280, 18)
  $mid.TextFrame.TextRange.Text = 'Jay Hasty   ·   Devinder Mahajan   ·   INTERNAL ONLY'
  $mid.TextFrame.TextRange.ParagraphFormat.Alignment = 2
  Set-RunFont $mid.TextFrame.TextRange 9 (Rgb 110 124 144) $false
  $pg = $slide.Shapes.AddTextbox(1, $W - 150, 512, 120, 18)
  $pg.TextFrame.TextRange.Text = $page
  $pg.TextFrame.TextRange.ParagraphFormat.Alignment = 3
  Set-RunFont $pg.TextFrame.TextRange 9 (Rgb 110 124 144) $false
}

# ----- slide: full run -----
$s1 = $pres.Slides.Add($pres.Slides.Count + 1, 12)
$s1.FollowMasterBackground = 0
$s1.Background.Fill.ForeColor.RGB = (Rgb 10 16 24)
$title = $s1.Shapes.AddTextbox(1, 36, 12, 880, 32)
$title.TextFrame.TextRange.Text = '19 November run'
Set-RunFont $title.TextFrame.TextRange 28 (Rgb 242 246 255) $false
$sub = $s1.Shapes.AddTextbox(1, 36, 44, 880, 22)
$sub.TextFrame.TextRange.Text = 'All moving channels, 18 Nov 7:00 PM to 19 Nov 3:44 PM EST. Identical traces shown once. One-minute samples.'
Set-RunFont $sub.TextFrame.TextRange 11 (Rgb 154 168 190) $false

$shape = $s1.Shapes.AddChart2(240, 4, 24, 70, 912, 420)
$chart = $shape.Chart
$chart.ChartData.Activate()
$wb = $chart.ChartData.Workbook
$ws = $wb.Worksheets.Item(1)
$ws.Cells.Clear() | Out-Null
$nR = $rows.Count
$nC = $keep.Count + 1
$arr = New-Object 'object[,]' ($nR + 1), $nC
$arr[0, 0] = 'Time'
for ($k = 0; $k -lt $keep.Count; $k++) { $col = $k + 1; $arr[0, $col] = $keep[$k].n }
for ($r = 0; $r -lt $nR; $r++) {
  $row = $rows[$r]
  $rr = $r + 1
  $arr[$rr, 0] = $row.T
  for ($k = 0; $k -lt $keep.Count; $k++) {
    $col = $k + 1
    if ($null -ne $row.V[$k]) { $arr[$rr, $col] = [double]$row.V[$k] }
  }
}
$dest = $ws.Range('A1').Resize($nR + 1, $nC)
$dest.Value2 = $arr
Write-Host 'chart data written'
$chart.SetSourceData($dest, 2)
$chart.ChartType = 4
$chart.HasTitle = $false
$chart.HasLegend = $true
$chart.Legend.Position = -4107  # xlLegendPositionBottom
$chart.Legend.Font.Name = 'Lato'
$chart.Legend.Font.Size = 8
$chart.Legend.Font.Color = (Rgb 242 246 255)
$chart.ChartArea.Format.Fill.ForeColor.RGB = (Rgb 10 16 24)
$chart.ChartArea.Format.Line.Visible = 0
$chart.PlotArea.Format.Fill.ForeColor.RGB = (Rgb 16 24 32)
$colors = @(
  (Rgb 123 255 206), (Rgb 126 182 255), (Rgb 245 193 108), (Rgb 255 139 123),
  (Rgb 201 166 255), (Rgb 94 224 197), (Rgb 255 107 181), (Rgb 155 227 106),
  (Rgb 240 160 90), (Rgb 230 224 122), (Rgb 138 180 255), (Rgb 255 158 107),
  (Rgb 184 242 208), (Rgb 212 165 255), (Rgb 255 214 120), (Rgb 120 210 255),
  (Rgb 255 180 160)
)
for ($k = 1; $k -le $keep.Count; $k++) {
  $ser = $chart.SeriesCollection($k)
  $ser.AxisGroup = $keep[$k - 1].ax
  $ser.Format.Line.ForeColor.RGB = $colors[$k - 1]
  $ser.Format.Line.Weight = 1.25
  $ser.Smooth = $false
}
$axis = $chart.Axes(1)
$axis.TickLabels.Font.Name = 'Lato'
$axis.TickLabels.Font.Size = 9
$axis.TickLabels.Font.Color = (Rgb 154 168 190)
$axis.TickLabels.NumberFormat = 'm/d h:mm AM/PM'
$axis.Format.Line.ForeColor.RGB = (Rgb 36 48 68)
$axis.HasMajorGridlines = $false
foreach ($axId in 1, 2) {
  try {
    $ya = $chart.Axes(2, $axId)
    $ya.TickLabels.Font.Name = 'Lato'
    $ya.TickLabels.Font.Size = 9
    $ya.TickLabels.Font.Color = (Rgb 154 168 190)
    $ya.Format.Line.ForeColor.RGB = (Rgb 36 48 68)
    $ya.MajorGridlines.Format.Line.ForeColor.RGB = (Rgb 36 48 68)
  } catch {}
}
$wb.Close($true)
Add-Chrome $s1 '10  /  11'

# ----- slide: end calibration -----
$s2 = $pres.Slides.Add($pres.Slides.Count + 1, 12)
$s2.FollowMasterBackground = 0
$s2.Background.Fill.ForeColor.RGB = (Rgb 10 16 24)
$t2 = $s2.Shapes.AddTextbox(1, 36, 12, 880, 32)
$t2.TextFrame.TextRange.Text = 'End-of-run calibration'
Set-RunFont $t2.TextFrame.TextRange 28 (Rgb 242 246 255) $false
$n2 = $s2.Shapes.AddTextbox(1, 36, 46, 880, 36)
$n2.TextFrame.WordWrap = $true
$n2.TextFrame.TextRange.Text = 'Feed gas after the reactor. The 11:54–12:16 injections (datafile_001 to datafile_003) are discarded. Certified feed: H2 26.0%, CO 64.93%, CO2 7.95%, CH4 1.12%.'
Set-RunFont $n2.TextFrame.TextRange 13 (Rgb 154 168 190) $false

$tblShape = $s2.Shapes.AddTable(5, 4, 36, 100, 888, 220)
$tbl = $tblShape.Table
$headers = @('File', 'File time', 'CO2 area (µV·s)', 'CO area (µV·s)')
$data = @(
  @('datafile_045', '8:39 PM', '—', '6,871,413'),
  @('datafile_046', '8:50 PM', '—', '4,851,093'),
  @('datafile_047', '9:01 PM', '1,847,879', '—'),
  @('datafile_048', '9:12 PM', '1,819,140', '—')
)
for ($c = 1; $c -le 4; $c++) {
  $cell = $tbl.Cell(1, $c).Shape.TextFrame.TextRange
  $cell.Text = $headers[$c - 1]
  Set-RunFont $cell 14 (Rgb 242 246 255) $true
  $cell.ParagraphFormat.Alignment = 2
  $tbl.Cell(1, $c).Shape.Fill.ForeColor.RGB = (Rgb 24 36 51)
}
for ($r = 0; $r -lt 4; $r++) {
  $fill = $(if ($r % 2 -eq 0) { Rgb 16 24 32 } else { Rgb 20 28 40 })
  for ($c = 0; $c -lt 4; $c++) {
    $cell = $tbl.Cell($r + 2, $c + 1).Shape.TextFrame.TextRange
    $cell.Text = $data[$r][$c]
    Set-RunFont $cell 14 (Rgb 242 246 255) $false
    $cell.ParagraphFormat.Alignment = 2
    $tbl.Cell($r + 2, $c + 1).Shape.Fill.ForeColor.RGB = $fill
  }
}

$note = $s2.Shapes.AddTextbox(1, 36, 340, 888, 150)
$note.TextFrame.WordWrap = $true
$note.TextFrame.TextRange.Text = "CO2 on the two complete end injections agrees: mean area 1,833,510 at 7.95%, so area = 230,630 × CO2%. The two CO-only injections do not agree (6.87 million vs 4.85 million), so no CO factor is used. Hydrogen was not integrated on these files."
Set-RunFont $note.TextFrame.TextRange 14 (Rgb 242 246 255) $false
Add-Chrome $s2 '11  /  11'

$pres.Save()
$outDir = 'C:\Users\dmahajan\Jay\minipilot\_slide_check'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$s1.Export((Join-Path $outDir 'slide10.png'), 'PNG', 1600, 900)
$s2.Export((Join-Path $outDir 'slide11.png'), 'PNG', 1600, 900)
Write-Host ("saved slides {0}" -f $pres.Slides.Count)
$pres.Close()
$ppt.Quit()
Write-Host 'done'
