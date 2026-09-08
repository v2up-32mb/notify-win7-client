# v3.0.0（双客户端联合首发）

一次齐活：C# 版 + 原生版 + 服务端打包产物。

## 下载

| 文件 | 说明 |
|---|---|
| `NotifyClient.exe` | C# 版（.NET 4.0 x86，需系统自带 .NET Framework） |
| `NotifyClientNative.exe` | 原生版（C++/Win32 单文件 ~184KB，无运行时依赖，常驻个位数 MB） |
| `_worker.js` | 服务端 Worker 打包产物（wrangler dry-run 构建，可直接部署） |

两客户端二选一即可，共用 `%USERPROFILE%\.notifyclient\config.ini` 与 `cursor.txt`，可来回切换。

## 内容

- C# 端：最小化启动自锁修复、气泡外观设置、测试通知按钮、堆叠动画修复、状态栏内存、单行日志（v2.0.5~v2.0.7 内容全含）。
- 原生端：托盘/轮询/堆叠气泡/日志/状态条全功能，句柄接线、自动消失、内存显示、单行日志等 bug 已修。
- 服务端：同仓 `server/`（worker.js/admin.html/api_spec.json/wrangler.toml），`ADMIN_TOKEN` 请本地填入真实口令后 `wrangler deploy`（Release 里不含口令）。

## 截图

![主界面](https://raw.githubusercontent.com/v2up-32mb/notify-win7-client/v3.0.0/docs/screenshots/ScreenShot.png)

## 升级

- 客户端：直接替换 exe，配置与游标自动沿用。
- 服务端：已有部署不用动；新部署按 README「部署 Worker」一节操作。
