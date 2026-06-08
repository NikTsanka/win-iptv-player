# Captures screenshots of IptvPlayer for the README.
# Serves a demo playlist of PUBLIC test streams over localhost (so no private
# provider URL is ever shown), launches the built exe, and grabs the window.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$root      = Split-Path -Parent $PSScriptRoot
$exe       = Join-Path $root 'publish\IptvPlayer.exe'
$m3u       = Join-Path $root 'scripts\demo-playlist.m3u'
$outDir    = Join-Path $root 'assets\screenshots'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$settings  = Join-Path $env:APPDATA 'IptvPlayer\settings.json'
$backup    = "$settings.capture-bak"
$port      = 8099
$url       = "http://localhost:$port/demo.m3u"

# --- Win32 helpers ----------------------------------------------------------
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref P p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(int f, int x, int y, int d, int e);
  public struct R { public int L, T, Rr, B; }
  public struct P { public int X, Y; }
}
"@

function Capture($hwnd, $path) {
  [W]::SetForegroundWindow($hwnd) | Out-Null
  Start-Sleep -Milliseconds 400
  $r = New-Object W+R
  [W]::GetClientRect($hwnd, [ref]$r) | Out-Null
  $tl = New-Object W+P; $tl.X = 0; $tl.Y = 0
  [W]::ClientToScreen($hwnd, [ref]$tl) | Out-Null
  $w = $r.Rr - $r.L; $h = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($tl.X, $tl.Y, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Host "saved $path ($w x $h)"
}

function Click($hwnd, $cx, $cy) {
  $p = New-Object W+P; $p.X = $cx; $p.Y = $cy
  [W]::ClientToScreen($hwnd, [ref]$p) | Out-Null
  [W]::SetCursorPos($p.X, $p.Y) | Out-Null
  Start-Sleep -Milliseconds 120
  [W]::mouse_event(0x0002,0,0,0,0); Start-Sleep -Milliseconds 60; [W]::mouse_event(0x0004,0,0,0,0)
}

# --- Local HTTP server (background job) -------------------------------------
$job = Start-Job -ScriptBlock {
  param($port, $m3u)
  $listener = [System.Net.HttpListener]::new()
  $listener.Prefixes.Add("http://localhost:$port/")
  $listener.Start()
  $body = [System.IO.File]::ReadAllBytes($m3u)
  while ($listener.IsListening) {
    $ctx = $listener.GetContext()
    $ctx.Response.ContentType = 'application/x-mpegurl'
    $ctx.Response.OutputStream.Write($body, 0, $body.Length)
    $ctx.Response.Close()
  }
} -ArgumentList $port, $m3u
Start-Sleep -Seconds 1

try {
  # Point settings at our demo URL so the app auto-loads it on startup.
  if (Test-Path $settings) { Copy-Item $settings $backup -Force }
  $cfg = [ordered]@{ Volume = 0; AspectRatio = 'Auto'; LastPlaylistPath = $null; LastPlaylistUrl = $url }
  $cfg | ConvertTo-Json | Set-Content -Path $settings -Encoding utf8

  $proc = Start-Process -FilePath $exe -PassThru
  Start-Sleep -Seconds 2
  for ($i=0; $i -lt 30 -and $proc.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 300; $proc.Refresh() }
  $hwnd = $proc.MainWindowHandle
  Write-Host "hwnd=$hwnd"
  Start-Sleep -Seconds 4           # let the playlist finish loading

  Capture $hwnd (Join-Path $outDir '01-browse.png')

  # Click the first channel (Big Buck Bunny — confirmed-working Mux stream) inside
  # the 320px panel, wait for the stream to buffer, then capture a playing frame.
  Click $hwnd 150 118
  Start-Sleep -Seconds 14
  Capture $hwnd (Join-Path $outDir '02-playing.png')

  Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
}
finally {
  if (Test-Path $backup) { Move-Item $backup $settings -Force }
  Stop-Job $job -ErrorAction SilentlyContinue
  Remove-Job $job -Force -ErrorAction SilentlyContinue
}
Write-Host 'done'
