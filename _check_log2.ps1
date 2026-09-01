$path = 'C:\Projects\BackHome\Logs\Editor.log'
Get-Item $path | Format-List Length,LastWriteTime
Write-Host "=== CS errors ==="
Select-String -Path $path -Pattern 'error CS' -Encoding UTF8 | Select-Object -Last 10
Write-Host "=== PlayerModelSwapTool mentions (last 10) ==="
Select-String -Path $path -Pattern 'PlayerModelSwapTool' -Encoding UTF8 | Select-Object -Last 10
