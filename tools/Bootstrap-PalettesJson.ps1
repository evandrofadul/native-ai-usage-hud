# One-time bootstrap: extract the colors from the existing WPF palette dictionaries
# (src/AiUsageBar.App/Themes/Palettes/*.xaml) into tools/palettes.json, which becomes
# the single source of truth for both heads. After this, edit palettes.json and run
# Generate-Palettes.ps1 to regenerate the C# table and the per-framework dictionaries.
#
# The palette file base name equals the ThemeId enum member name (see ThemeId.PaletteFile()),
# so palettes.json is keyed by that name.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $root 'src/AiUsageBar.App/Themes/Palettes'
$out  = Join-Path $PSScriptRoot 'palettes.json'

# Canonical key order — must match PaletteColors.cs field order.
$keys = @(
  'BgBrush','Bg2Brush','Bg3Brush','HoverBrush','FgBrush','DimBrush','AccentBrush',
  'BorderBrush','BarEmptyBrush','SevLowBrush','SevMidBrush','SevHighBrush',
  'SevCriticalBrush','CloseHoverBrush','ClosePressedBrush'
)

$result = [ordered]@{}
Get-ChildItem (Join-Path $src '*.xaml') | Sort-Object Name | ForEach-Object {
  $name = $_.BaseName
  $text = Get-Content $_.FullName -Raw
  $colors = [ordered]@{}
  foreach ($k in $keys) {
    $m = [regex]::Match($text, 'x:Key="' + [regex]::Escape($k) + '"\s+Color="(#[0-9A-Fa-f]{6})"')
    if (-not $m.Success) { throw "Palette $name missing key $k" }
    $colors[$k] = $m.Groups[1].Value.ToUpperInvariant()
  }
  $result[$name] = $colors
}

$result | ConvertTo-Json -Depth 5 | Set-Content -Path $out -Encoding UTF8
Write-Host "Wrote $($result.Count) palettes to $out"
