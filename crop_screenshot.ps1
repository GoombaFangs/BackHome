param(
    [string]$InputPath = "C:\Projects\BackHome\unity_screenshot.png",
    [string]$OutputPath = "C:\Projects\BackHome\unity_screenshot_crop.png",
    [int]$X = 0,
    [int]$Y = 0,
    [int]$Width = 800,
    [int]$Height = 600
)
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile($InputPath)
$rect = New-Object System.Drawing.Rectangle($X, $Y, $Width, $Height)
$bmp = New-Object System.Drawing.Bitmap $Width, $Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle(0,0,$Width,$Height)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
$bmp.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $src.Dispose()
Write-Host "Saved crop to $OutputPath"
