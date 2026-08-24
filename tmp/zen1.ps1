$dll = "$env:USERPROFILE\.nuget\packages\radzen.blazor\11.2.5\lib\net10.0\Radzen.Blazor.dll"
$asm = [System.Reflection.Assembly]::LoadFrom($dll)
$type = $asm.GetType('Radzen.Blazor.RadzenDialog')
Write-Host "Type found: $($null -ne $type)"
$props = $type.GetProperty([string[]]@('Visible','VisibleChanged','Width','Height','Title','CloseOnEsc','RenderMode','DialogStyle','FullWidth','ShowCloseButton','Class','Style'))
foreach($p in $props){ if($p){ Write-Host ("{0} : {1}" -f $p.Name, $p.PropertyType.Name) } }
Write-Host "--- ALL PUBLIC PARAMETER-PROPERTIES on RadzenDialog ---"
$props = $type.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | Where-Object { $_.GetCustomAttribute([Microsoft.AspNetCore.Components.ParameterAttribute]) -ne $null }
$props | ForEach-Object { Write-Host $_.Name }
