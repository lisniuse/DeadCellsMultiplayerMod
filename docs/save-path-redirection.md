# 存档路径重定向说明

## 背景

本地多开测试时，两个游戏副本如果都读取同一个系统目录存档，会互相覆盖进度。

原先发现 `dc.tool.File.PATH` 可以被改到游戏根目录下的 `GSE Saves`，但实际测试后确认：普通存档 `user_0.dat` 仍然会被 GSE/Goldberg 的 Steam Remote Storage 写入系统目录：

```text
C:\Users\Richie\AppData\Roaming\GSE Saves\588650\remote\user_0.dat
```

原因是游戏内的 `dc.tool.File` 路径和 GSE/Goldberg 的 remote storage 路径不是同一层逻辑。只 hook `dc.tool.File.PATH` 不足以完全控制 `user_0.dat` 的最终落盘位置。

## 当前实现

当前实现集中在 `SavePathRedirect.cs`。

MOD 初始化时会做三件事：

1. 写入 GSE/Goldberg 配置文件。
2. 设置 GSE 相关环境变量作为兜底。
3. 强制 `dc.tool.File.Class.PATH` 指向游戏根目录下的 remote storage 目录。

目标路径为：

```text
<游戏根目录>\GSE Saves\588650\remote
```

例如：

```text
D:\dgames\Dead.Cells.v20240827-stability\GSE Saves\588650\remote
```

## 写入的 GSE 配置

MOD 会尝试写入两个位置，覆盖常见的 GSE/Goldberg 查找路径：

```text
<游戏根目录>\steam_settings\configs.user.ini
<游戏根目录>\coremod\core\native\win-x64\goldberg\steam_settings\configs.user.ini
```

内容为：

```ini
[user::saves]
local_save_path=<游戏根目录>/GSE Saves
```

例如：

```ini
[user::saves]
local_save_path=D:/dgames/Dead.Cells.v20240827-stability/GSE Saves
```

GSE 会在这个根目录下按 Steam AppId 创建 remote storage 目录：

```text
GSE Saves\588650\remote
```

## `dc.tool.File.PATH` 的目标

MOD 不再把 `dc.tool.File.PATH` 指到 `GSE Saves` 根目录，而是直接指到：

```text
GSE Saves\588650\remote
```

这样游戏自身的 `user_0.dat` 读写和 GSE remote storage 的实际文件目录保持一致。

## 生效时机

GSE/Goldberg 的存档根目录通常在 `steam_api64.dll` 初始化时读取。

因此，如果第一次启动时 GSE 已经先于 MOD 初始化完成，MOD 写入的 `configs.user.ini` 可能需要下一次完整重启游戏后才稳定生效。

结论：

- 只改 MOD 可以实现长期稳定重定向。
- 如果要求第一次启动前就一定生效，理论上更适合改 core 或启动器，在加载 Goldberg DLL 前写好配置。
- 当前方案不改 core，风险更小，但建议改完后完整重启一次游戏。

## 验证方式

启动游戏后查看日志：

```text
coremod\logs\log_latest.log
```

应该能看到类似日志：

```text
[NetMod] Save path redirected (install): ... -> D:\dgames\Dead.Cells.v20240827-stability\GSE Saves\588650\remote
[NetMod] Save path redirect installed: D:\dgames\Dead.Cells.v20240827-stability\GSE Saves\588650\remote
```

保存游戏后检查下面文件的更新时间：

```text
<游戏根目录>\GSE Saves\588650\remote\user_0.dat
```

如果这个文件更新时间变化，而下面系统目录文件不再变化，则说明重定向成功：

```text
C:\Users\Richie\AppData\Roaming\GSE Saves\588650\remote\user_0.dat
```

## 迁移旧存档

如果系统目录已有旧存档，可以把它复制到游戏根目录：

```text
源：
C:\Users\Richie\AppData\Roaming\GSE Saves\588650\remote\user_0.dat

目标：
<游戏根目录>\GSE Saves\588650\remote\user_0.dat
```

复制前建议先备份目标文件，避免覆盖新的测试进度。

## 注意事项

- `MSave` 是联机 MOD 的多人存档槽位目录，不等于普通 `user_0.dat` 所在的 GSE remote storage。
- 普通存档应该看 `GSE Saves\588650\remote\user_0.dat`。
- 多人存档槽位仍然可能出现 `MSave\user_1.dat` 等文件，这是 MOD 菜单逻辑使用的额外存档空间。
- 每个游戏副本都应该拥有自己的 `GSE Saves` 目录，避免多开时共享系统目录。
