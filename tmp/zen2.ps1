$dll = "$env:USERPROFILE\.nuget\packages\radzen.blazor\11.2.5\lib\net10.0\Radzen.Blazor.dll"
$asm = [System.Reflection.Assembly]::LoadFrom($dll)
$type = $asm.GetType('Radzen.Blazor.RadzenDialog')
Write-Host "Type found: $($null -ne $type)"
Write-Host "--- ALL PARAMETER-PROPERTIES on RadzenDialog ---"
$props = $type.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | Where-Object { $_.GetCustomAttribute([Microsoft.AspNetCore.Components.ParameterAttribute]) -ne $null }
$props | ForEach-Object { Write-Host $_.Name }
