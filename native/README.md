# NotifyClient Native（C++ / Win32，无 CLR）

给 Win7 的极简托盘客户端：纯 Win32 API + WinHTTP 轮询，单文件（MSVC `/MT` 静态 CRT），常驻内存目标 <10MB。

- 与 C# 版共用同一份 `%USERPROFILE%\.notifyclient\config.ini` 和 `cursor.txt`（同目录同键名，可来回切换客户端）
- 轮询 `GET /api/messages?limit=50&since=cursor`，Bearer 认证，首轮只同步游标
- 右下角气泡堆叠（置顶/不抢焦点/滑动+淡入淡出），外观设置（宽高/字号/边距/间隔）与 C# 版一致，保存即对新气泡生效
- 托盘：单击切换显示/隐藏，双击强制显示，右键菜单（打开设置/测试通知/退出）；开机启动写 HKCU Run；单实例互斥
- 主窗口：设置 + 只读多行消息日志 + 底部状态条（与 C# 版同布局）；日志纯内存，不落盘
- 构建：只走 GitHub Actions（`build-native.yml`，MSVC x86 `/MT /O1`，Win7 API 级别 `_WIN32_WINNT=0x0601`，TLS1.1/1.2 显式开启）；推 `native-v*` tag 自动发 Release

## 文件

```
  native/
    main.cpp     WinMain/主窗口/托盘/设置/日志/状态条
    toast.cpp/h  气泡窗口 + 堆叠管理（重定位置会重启动画计时器）
    poll.cpp/h   后台轮询线程（WinHTTP，15s 超时，SChannel TLS1.2）
    cfg.cpp/h    配置/游标读写（与 C# 版同文件同格式，窄字符 UTF-8 IO）
    json.h       最小 JSON 解析（仅懂 /api/messages 返回）
    app.rc       嵌入托盘/窗口图标（复用上游 app.ico）
```

## 与 C# 版差异

- 无历史文件（本来就没有了），只留游标；无 crash.log（Win32 结构简单，异常直接忽略式保护）
- 数字输入框是普通 EDIT（ES_NUMBER），保存时钳位；没有 NumericUpDown 微调键
- 气泡用 GDI `DrawText` 自绘（自动换行），字体固定微软雅黑
