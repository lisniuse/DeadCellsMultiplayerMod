# DeadCellsMultiplayerMod 二进制补丁记录

本目录用 **Mono.Cecil** 对原作者发布的 `DeadCellsMultiplayerMod.dll`(**v0.3.1**)做 IL 层修补,
而不是从源码重新编译(本地没有完整的 DCCM/HaxeProxy 构建链)。

`apply_patches.ps1` 是这些补丁的**可复现源码记录**:输入原始 0.3.1 DLL,
确定性地输出补丁版。已验证可从 `*.original.bak` 精确复现部署中的 DLL。

## 用法

```powershell
# 先关游戏(否则 DLL 被占用)
powershell -ExecutionPolicy Bypass -File apply_patches.ps1 -GameRoot "D:\games\Dead.Cells.v20240827"
```

- 默认:读 `<mod目录>\DeadCellsMultiplayerMod.dll.original.bak`,写 `<mod目录>\DeadCellsMultiplayerMod.dll`。
- 幂等:已打过的补丁会跳过。
- 回退:把 `.original.bak` 复制回 `DeadCellsMultiplayerMod.dll`,或重跑脚本重建。
- **两台机器(主机 + 客户端)都要打**,因为同步逻辑双向生效、角色可互换。

## 补丁清单

| # | 方法 | 动作 | 修复的问题 |
|---|------|------|-----------|
| 1 | `ModEntry::TryPollSteamOverlayJoinFromLaunchData` | 方法体清空为 `ret` | 非 Steam(Goldberg)环境下 Steam overlay join 轮询崩溃 |
| 2 | `ModEntry::TryDeferredSteamOverlayCallbackRegistration` | 方法体清空为 `ret` | 同上,Steam overlay 回调注册崩溃 |
| 3 | `GameDataSync::TryTriggerLevelGraphReload` | 方法体清空为 `ret` | 见下「死亡重启」 |
| 4 | `GameDataSync::TryTriggerBossRuneReload` | 方法体清空为 `ret` | 见下「死亡重启」(Boss 符文路径孪生) |
| 5 | `ModEntry::ResetDownedPlayersForRestart` | `sendNetworkUpState` 实参 `false`→`true`(唯一的 `ldc.i4.0`→`ldc.i4.1`) | 见下「复活后残留尸体」 |

> 补丁 1、2 是对部署 DLL 做的 **Steam 崩溃二进制修补**(把这两个方法清空为 `ret`)。
> 它对应源码仓库的提交 `8eebde0 fix(steam): guard overlay features against incomplete Steam API implementations`,
> 但**两者实现不同**:源码版是更优雅的 `try-catch` 守卫 + `s_steamOverlayDisabled` 标志(真 Steam 下保留 overlay 功能,
> 仅在 Goldberg 等崩溃时禁用);二进制版受手写 IL 所限,只能**整方法清空**(= overlay join 完全禁用)。
> 在 Goldberg / 非 Steam 环境下两者等效(overlay 本就不可用)。此处纳入清空版,仅为保证从原始 0.3.1 DLL **一步复现部署版**。
> 补丁 3–5 为本次新增的死亡重启同步修复。

## 根因说明

### 死亡重启卡 Game Over / `Null access .curCine` 崩溃(补丁 3、4)
全员倒地后主机重启发新 run:广播新 seed + 新关卡图。客户端:
- 收到 seed → `QueueClientRestartFromHostSeed` 排队**完整重启**(launchGame→newGame);
- 紧接着收到关卡图 → `TryTriggerLevelGraphReload` 触发**原地关卡重载** `reloadAfterBossRuneModif`,
  **抢先执行**,把客户端原地重载回**旧 run**,完整重启被吞掉。

结果:客户端卡在旧 run、1 血、Game Over,无法交互;`curCine` 处于坏状态时还会 `Null access` 崩溃。
这两个方法只在客户端运行(`if(net.IsHost) return;`),清空后让完整重启独占。
地图一致性不受影响(仍由 `generateGraph` + `TryApplyRemoteLevelGraph` 同步)。
代价:host 中途改 Boss 符文时客户端不再原地热重载,下次进关才反映(边缘优化)。

### 复活后对端仍看到一具尸体 / 无法开门(补丁 5)
客户端重启走 `ResetDownedPlayersForRestart`,其中 `sendNetworkUpState=false`,
**从不广播 `isDowned=false`**。于是主机的 `_remoteDowned[客户端]` 永远是倒地:
- `ReceiveGhostCoords` 把客户端 ghost 钉死在尸体位置;
- `LevelExitSync` 把客户端当倒地 → 出口门/交互被卡。

翻成 `true` 后,客户端重启时广播 `isDowned=false`,主机清掉倒地状态、销毁尸体、ghost 跟随真实坐标、门恢复可用。
TCP 有序保证该 `false` 是最后一个倒地包,不会被旧包覆盖。

## 对应的源码版修复(参考)
上游源码仓库(v0.3.2)里有一套**更精准**的等价修复(`UI/GameMenu.cs` + `LevelSync.cs` 用守卫只在
倒地/重启时跳过重载,保留 Boss 符文热重载;`FakeDeath.cs` 同样翻转 `sendNetworkUpState`)。
二进制补丁因手写 IL 受限,采用了较粗的「整方法清空」,效果等价。
