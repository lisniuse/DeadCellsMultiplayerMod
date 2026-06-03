<#
=====================================================================
 DeadCellsMultiplayerMod - 本地源码构建脚本
=====================================================================
 解决"GameProxy 引用 System.Private.CoreLib 导致 CS0012"的问题:
 把游戏运行时生成的 cache\GameProxy.dll 经 Mono.Cecil 重定向到 mscorlib
 (.NET 引用包自带、可转发基础类型),放进 MDK 的 ref 目录,并让 ref
 搜索优先于 host,然后用 LOCALAPPDATA 里的 SDK 构建。

 前提:
   - 游戏已安装(含 coremod\cache\GameProxy.dll —— 至少启动过一次游戏生成它)
   - 已安装 .NET 10 SDK(本脚本优先用 %LOCALAPPDATA%\Microsoft\dotnet)
   - core 源码仓库在 ..\core(其中 mdk\bin 为 MDK 产物、DCCMTool 已构建)

 用法:
   powershell -ExecutionPolicy Bypass -File build_local.ps1
   powershell -ExecutionPolicy Bypass -File build_local.ps1 -Config Release
=====================================================================
#>
param(
    [string]$GameRoot = "D:\dgames\Dead.Cells.v20240827",
    [string[]]$InstallGameRoots = @(
        "D:\dgames\Dead.Cells.v20240827",
        "D:\dgames\Dead.Cells.v20240827-P2"
    ),
    [string]$ModSrc   = "$PSScriptRoot",
    [string]$CoreRepo = "$PSScriptRoot\..\core",
    [string]$Config   = "Debug",
    [string]$StartScript = "D:\dgames\Start-DeadCells-P1P2.ps1",
    [switch]$StartAfterBuild,
    [switch]$SkipKillGameProcesses
)
$ErrorActionPreference = "Stop"

$resolvedInstallRoots = @()
foreach ($root in $InstallGameRoots) {
    if ([string]::IsNullOrWhiteSpace($root)) { continue }
    try {
        $resolvedInstallRoots += (Resolve-Path -LiteralPath $root -ErrorAction Stop).Path.TrimEnd('\')
    } catch {
        Write-Host "安装目录不存在，跳过进程匹配: $root" -ForegroundColor DarkYellow
    }
}

if ($SkipKillGameProcesses) {
    Write-Host "跳过结束游戏进程(-SkipKillGameProcesses)。如果 DLL 被占用，安装步骤可能失败。" -ForegroundColor Yellow
} elseif ($resolvedInstallRoots.Count -gt 0) {
    Write-Host "扫描并结束正在运行的游戏进程..." -ForegroundColor Yellow
    $gameProcesses = Get-Process -Name "DeadCellsModding" -ErrorAction SilentlyContinue

    foreach ($proc in $gameProcesses) {
        Write-Host "结束进程: $($proc.Id) $($proc.Path)" -ForegroundColor Yellow
        Stop-Process -Id $proc.Id -Force -ErrorAction Stop
    }

    Wait-Process -Name "DeadCellsModding" -Timeout 10 -ErrorAction SilentlyContinue
}

$MdkBin   = Join-Path $CoreRepo "mdk\bin"
$gameHost = Join-Path $GameRoot "coremod\core\host"
$cache    = Join-Path $GameRoot "coremod\cache"
$cecil    = Join-Path $gameHost "Mono.Cecil.dll"
$dccmTool = Join-Path $CoreRepo "mdk\DCCMTool\bin\Release\net10.0"

# --- 找带 SDK 的 dotnet ---
$sdk = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $sdk)) { $sdk = "dotnet" }
$sdkList = & $sdk --list-sdks 2>$null
if (-not $sdkList) { Write-Host "未找到 .NET SDK(只有运行时)。请安装 .NET 10 SDK。" -ForegroundColor Red; return }
Write-Host "使用 SDK: $sdk ($($sdkList | Select-Object -First 1))" -ForegroundColor Green

foreach ($p in @($cecil, "$cache\GameProxy.dll", $MdkBin)) {
    if (-not (Test-Path $p)) { Write-Host "缺少必要文件/目录: $p" -ForegroundColor Red; return }
}

# --- 1) 确保 MDK tools(DCCMTool + Steamworks.NET) ---
$toolsDir = Join-Path $MdkBin "tools"
New-Item -ItemType Directory -Force $toolsDir | Out-Null
if (-not (Test-Path "$toolsDir\DCCMTool.dll")) {
    if (Test-Path $dccmTool) { Copy-Item "$dccmTool\*" $toolsDir -Recurse -Force; Write-Host "已放置 DCCMTool" -ForegroundColor Cyan }
    else { Write-Host "警告: 未找到已构建的 DCCMTool($dccmTool),modinfo 生成可能失败" -ForegroundColor Yellow }
}
if (-not (Test-Path "$toolsDir\Steamworks.NET.dll")) {
    $sw = @("$GameRoot\coremod\core\mdk\tools\Steamworks.NET.dll", "$gameHost\Steamworks.NET.dll") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($sw) { Copy-Item $sw "$toolsDir\Steamworks.NET.dll" -Force }
}

# --- 2) refasm GameProxy: System.Private.CoreLib -> mscorlib ---
Add-Type -Path $cecil
$refDir = Join-Path $MdkBin "ref"
New-Item -ItemType Directory -Force $refDir | Out-Null
$gp = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("$cache\GameProxy.dll")
$m  = $gp.MainModule
$spcl  = $m.AssemblyReferences | Where-Object { $_.Name -eq "System.Private.CoreLib" }
if ($spcl) {
    $mscor = $m.AssemblyReferences | Where-Object { $_.Name -eq "mscorlib" }
    if (-not $mscor) {
        $mscor = New-Object Mono.Cecil.AssemblyNameReference("mscorlib", (New-Object Version "4.0.0.0"))
        $mscor.PublicKeyToken = [byte[]]@(0xb7,0x7a,0x5c,0x56,0x19,0x34,0xe0,0x89)
        $m.AssemblyReferences.Add($mscor)
    }
    $n = 0
    foreach ($tr in $m.GetTypeReferences()) { if ([object]::ReferenceEquals($tr.Scope, $spcl)) { $tr.Scope = $mscor; $n++ } }
    [void]$m.AssemblyReferences.Remove($spcl)
    $gp.Write("$refDir\GameProxy.dll")
    Write-Host "已 refasm GameProxy -> ref(remap $n typerefs: System.Private.CoreLib -> mscorlib)" -ForegroundColor Cyan
} else {
    $gp.Write("$refDir\GameProxy.dll")
    Write-Host "GameProxy 未引用 System.Private.CoreLib,直接放入 ref" -ForegroundColor Cyan
}
$gp.Dispose()

# --- 3) 让 ref 搜索优先于 host(幂等改 build.targets) ---
$bt  = Join-Path $MdkBin "build\types\mod\build.targets"
$old = "@(ResolvedMods->'%(ModRoot)');`$(_DCCM_Core_Host_Root);`$(_DCCM_MDK_Root)ref;`$(AssemblySearchPaths);"
$new = "@(ResolvedMods->'%(ModRoot)');`$(_DCCM_MDK_Root)ref;`$(_DCCM_Core_Host_Root);`$(AssemblySearchPaths);"
$c = Get-Content $bt -Raw
if ($c.Contains($old)) { Set-Content $bt ($c.Replace($old, $new)) -NoNewline; Write-Host "build.targets: ref 已提前于 host" -ForegroundColor Cyan }
elseif ($c.Contains($new)) { Write-Host "build.targets: 顺序已是 ref 优先(跳过)" -ForegroundColor DarkGray }
else { Write-Host "警告: build.targets 搜索路径模式未匹配,可能版本不同,请手动确认 ref 在 host 之前" -ForegroundColor Yellow }

# --- 4) 构建 ---
$env:DCCM_MDK_ROOT = $MdkBin
$env:DEAD_CELLS_GAME_PATH = $GameRoot
Write-Host "构建中 ($Config) ..." -ForegroundColor Green
& $sdk build $ModSrc -c $Config -p:AutoInstallMod=false -p:"SteamworksNetHintPath=$toolsDir\Steamworks.NET.dll" --nologo
if ($LASTEXITCODE -eq 0) {
    $outMod = Join-Path $ModSrc "bin\$Config\net10.0\output\DeadCellsMultiplayerMod"
    Write-Host "`n构建成功。打包好的 mod 在:`n  $outMod" -ForegroundColor Green

    if (Test-Path -LiteralPath $outMod) {
        foreach ($root in $InstallGameRoots) {
            if ([string]::IsNullOrWhiteSpace($root)) { continue }

            $target = Join-Path $root "coremod\mods\DeadCellsMultiplayerMod"
            $targetDll = Join-Path $target "DeadCellsMultiplayerMod.dll"
            $targetLocked = $false
            if (Test-Path -LiteralPath $targetDll) {
                for ($i = 0; $i -lt 20; $i++) {
                    try {
                        $stream = [System.IO.File]::Open($targetDll, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
                        $stream.Dispose()
                        break
                    } catch {
                        if ($i -eq 19) {
                            if ($SkipKillGameProcesses) {
                                $targetLocked = $true
                                Write-Host "目标 DLL 被占用，跳过安装: $targetDll" -ForegroundColor Yellow
                                break
                            }
                            throw
                        }
                        Start-Sleep -Milliseconds 250
                    }
                }
            }
            if ($targetLocked) { continue }
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            Copy-Item -Path (Join-Path $outMod "*") -Destination $target -Recurse -Force
            Write-Host "已安装到: $target" -ForegroundColor Cyan
        }
    }

    if ($StartAfterBuild) {
        if (Test-Path -LiteralPath $StartScript) {
            Write-Host "`n启动双开游戏: $StartScript" -ForegroundColor Green
            & pwsh -NoProfile -ExecutionPolicy Bypass -File $StartScript
        } else {
            Write-Host "未找到启动脚本，跳过自动启动: $StartScript" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "`n构建失败(见上方错误)。" -ForegroundColor Red
}
