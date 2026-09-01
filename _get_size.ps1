Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile('C:\Projects\BackHome\unity_screenshot.png')
Write-Host "$($img.Width) x $($img.Height)"
$img.Dispose()
