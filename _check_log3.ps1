$path = 'C:\Projects\BackHome\Logs\Editor.log'
Get-Content -Path $path -Encoding UTF8 -Tail 15
