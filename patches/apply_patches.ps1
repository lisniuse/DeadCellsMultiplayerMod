<#
=====================================================================
 DeadCellsMultiplayerMod - 二进制补丁脚本 (reproducible binary patcher)
=====================================================================
 把原作者发布的 0.3.1 DLL 经 Mono.Cecil 修补成"修复版"。
 这是确定性操作: 同一份原始 DLL 跑本脚本, 永远得到同样的补丁版。
 因此本脚本就是这些二进制补丁的"可复现源码记录"。

 应用的补丁 (详见同目录 PATCHES.md):
   1. ModEntry::TryPollSteamOverlayJoinFromLaunchData        -> 清空为 ret  (Steam overlay 崩溃防护)
   2. ModEntry::TryDeferredSteamOverlayCallbackRegistration  -> 清空为 ret  (Steam overlay 崩溃防护)
   3. GameDataSync::TryTriggerLevelGraphReload               -> 清空为 ret  (死亡重启 reload 抢断/卡 GameOver/崩溃)
   4. GameDataSync::TryTriggerBossRuneReload                 -> 清空为 ret  (同上, Boss 符文路径的孪生)
   5. ModEntry::ResetDownedPlayersForRestart                 -> sendNetworkUpState: false->true
                                                               (重启后广播"已复活", 否则对端把本玩家钉成尸体/无法开门)

 用法 (在装有该 mod 的机器上, 先关游戏):
   powershell -ExecutionPolicy Bypass -File apply_patches.ps1 -GameRoot "D:\games\Dead.Cells.v20240827"

 默认: 读 <mod目录>\DeadCellsMultiplayerMod.dll.original.bak (原作者版),
        写 <mod目录>\DeadCellsMultiplayerMod.dll。
 脚本幂等: 已打过的补丁会自动跳过。
=====================================================================
#>
param(
    [string]$GameRoot = "D:\games\Dead.Cells.v20240827",
    [string]$Source   = "",   # 原始 DLL; 默认 <ModDir>\DeadCellsMultiplayerMod.dll.original.bak
    [string]$Output   = "",   # 输出 DLL; 默认 <ModDir>\DeadCellsMultiplayerMod.dll
    [string]$CecilPath= ""    # Mono.Cecil.dll; 默认 <GameRoot>\coremod\core\host\Mono.Cecil.dll
)

$ErrorActionPreference = "Stop"

$modDir  = Join-Path $GameRoot "coremod\mods\DeadCellsMultiplayerMod"
$hostDir = Join-Path $GameRoot "coremod\core\host"
$cacheDir= Join-Path $GameRoot "coremod\cache"
if (-not $Source)    { $Source    = Join-Path $modDir "DeadCellsMultiplayerMod.dll.original.bak" }
if (-not $Output)    { $Output    = Join-Path $modDir "DeadCellsMultiplayerMod.dll" }
if (-not $CecilPath) { $CecilPath = Join-Path $hostDir "Mono.Cecil.dll" }

if (-not (Test-Path $Source))    { Write-Host "找不到原始 DLL: $Source" -ForegroundColor Red; return }
if (-not (Test-Path $CecilPath)) { Write-Host "找不到 Mono.Cecil: $CecilPath" -ForegroundColor Red; return }

# 检查 Output 是否被占用 (游戏开着)
if (Test-Path $Output) {
    try { $fs=[System.IO.File]::Open($Output,'Open','ReadWrite','None'); $fs.Close() }
    catch { Write-Host "目标 DLL 被占用, 请先完全关闭游戏: $Output" -ForegroundColor Red; return }
}

Add-Type -Path $CecilPath

# 清空方法体 -> 仅一条 ret  (这几个方法都是 void)
$EmptyTargets = @(
    @{ Type="DeadCellsMultiplayerMod.ModEntry";     Method="TryPollSteamOverlayJoinFromLaunchData" },
    @{ Type="DeadCellsMultiplayerMod.ModEntry";     Method="TryDeferredSteamOverlayCallbackRegistration" },
    @{ Type="DeadCellsMultiplayerMod.GameDataSync"; Method="TryTriggerLevelGraphReload" },
    @{ Type="DeadCellsMultiplayerMod.GameDataSync"; Method="TryTriggerBossRuneReload" }
)

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($hostDir); $resolver.AddSearchDirectory($modDir); $resolver.AddSearchDirectory($cacheDir)
$rp = New-Object Mono.Cecil.ReaderParameters; $rp.AssemblyResolver = $resolver

$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Source, $rp)
$ver = $asm.Name.Version
Write-Host "读取原始 DLL: $Source (version $ver)" -ForegroundColor Green

function Get-MethodDef($asm, $typeName, $methodName) {
    $t = $asm.MainModule.GetType($typeName)
    if (-not $t) { return $null }
    return ($t.Methods | Where-Object { $_.Name -eq $methodName } | Select-Object -First 1)
}

# --- Patch 1-4: 清空为 ret ---
foreach ($p in $EmptyTargets) {
    $mm = Get-MethodDef $asm $p.Type $p.Method
    if (-not $mm) { Write-Host ("[跳过] 未找到 {0}::{1}" -f $p.Type, $p.Method) -ForegroundColor Yellow; continue }
    $before = $mm.Body.Instructions.Count
    if ($before -le 1) { Write-Host ("[已是空] {0} (instr={1})" -f $p.Method, $before) -ForegroundColor DarkGray; continue }
    $body = $mm.Body
    $body.Instructions.Clear(); $body.Variables.Clear(); $body.ExceptionHandlers.Clear(); $body.InitLocals = $false
    $il = $body.GetILProcessor()
    $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    Write-Host ("[清空] {0}: {1} -> 1 条指令" -f $p.Method, $before) -ForegroundColor Cyan
}

# --- Patch 5: ResetDownedPlayersForRestart 的 sendNetworkUpState false->true ---
# 该方法体内唯一的 Ldc_I4_0 就是第二个实参 sendNetworkUpState(=false), 翻成 Ldc_I4_1(=true)
$rd = Get-MethodDef $asm "DeadCellsMultiplayerMod.ModEntry" "ResetDownedPlayersForRestart"
if (-not $rd) {
    Write-Host "[跳过] 未找到 ResetDownedPlayersForRestart" -ForegroundColor Yellow
} else {
    $zeros = @($rd.Body.Instructions | Where-Object { $_.OpCode -eq [Mono.Cecil.Cil.OpCodes]::Ldc_I4_0 })
    if ($zeros.Count -eq 0) {
        Write-Host "[已翻转] ResetDownedPlayersForRestart 无 Ldc_I4_0, 视为已打补丁" -ForegroundColor DarkGray
    } elseif ($zeros.Count -ne 1) {
        throw "ResetDownedPlayersForRestart 出现 $($zeros.Count) 个 Ldc_I4_0, 与预期(1)不符, 中止以免误改"
    } else {
        $il = $rd.Body.GetILProcessor()
        $il.Replace($zeros[0], $il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1))
        Write-Host "[翻转] ResetDownedPlayersForRestart sendNetworkUpState: false -> true" -ForegroundColor Cyan
    }
}

# --- 写回 ---
$tmp = "$Output.__patchtmp"
if (Test-Path $tmp) { Remove-Item $tmp -Force }
$asm.Write($tmp)
$asm.Dispose()
Move-Item $tmp $Output -Force
Write-Host "已写出补丁版: $Output" -ForegroundColor Green

# --- 校验 ---
Write-Host "--- 校验 ---" -ForegroundColor Green
$v = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Output, $rp)
foreach ($p in $EmptyTargets) {
    $mm = Get-MethodDef $v $p.Type $p.Method
    if ($mm) { Write-Host ("  {0,-44} instr={1}" -f $p.Method, $mm.Body.Instructions.Count) }
}
$rd2 = Get-MethodDef $v "DeadCellsMultiplayerMod.ModEntry" "ResetDownedPlayersForRestart"
$z2  = @($rd2.Body.Instructions | Where-Object { $_.OpCode -eq [Mono.Cecil.Cil.OpCodes]::Ldc_I4_0 }).Count
Write-Host ("  ResetDownedPlayersForRestart Ldc_I4_0 残留={0} (应为 0)" -f $z2)
$v.Dispose()
Write-Host "完成。回退: 把 .original.bak 复制回 DeadCellsMultiplayerMod.dll, 或重跑本脚本即可重建。" -ForegroundColor Green
