# 防激活原型：自建窗口实验

当前状态：原生窗口与 Qt 5.15.2 的跨进程真实按钮测试通过，但后续真实微信试验失败，用户报告人工也无法点击，经授权重启后恢复。防激活原型不应接入正式 MCP。见 [自建窗口记录](test-results/activation-guard-2026-11-05.json) 和 [微信故障记录](incidents/2026-11-05-wechat-guard-interaction.md)。

## 实现

`scripts/fixtures/activation-guard-hook.c` 使用目标 UI 线程的 WH_CBT 阻止 HCBT_ACTIVATE/HCBT_SETFOCUS，通过 WH_CALLWNDPROC 在目标线程内安装窗口 subclass。后者在 WM_WINDOWPOSCHANGING 中保持现有窗口层级并禁止激活。

没有全局桌面 hook。DLL 只接受自建原生测试类或指定标题的 Qt 5.15.2 测试窗口。驱动仅向自己启动、核对 PID/线程后的子进程安装 hook。开始前必须收到保护就绪应答，未就绪不发送测试点击。

保护固定一秒到期，可主动解除；回调不向控制器同步请求。DLL 保留一个模块引用直到测试子进程退出，以防 subclass 仍存在时卸载回调代码。这是实验生命周期，不是已完成的生产卸载方案。

## 复现

Windows x64 交互会话中，从项目根目录执行：

```powershell
node scripts/test-activation-guard.mjs

$env:AGENTMOUSE_GCC = '完整路径\x86_64-w64-mingw32-gcc.exe'
node scripts/test-activation-guard.mjs --cross-process

$env:AGENTMOUSE_QT_PYTHON = '测试用 Python 的完整路径'
$env:PYTHONPATH = '安装了 PyQt5==5.15.11 的测试依赖目录'
node scripts/test-activation-guard.mjs --cross-process --qt
```

编译器与 PyQt 均为可选实验依赖，不是 Core 用户运行依赖。本机使用从官方发布下载、校验 SHA256 的 llvm-mingw 便携工具链，保存在被 Git 忽略的 compatibility-results 中。脚本将编译产物放到独立临时目录。

测试会短暂显示自建窗口。未保护的正对照故意请求激活与提高层级，用于证明 API 确实有作用；恢复用户前台后，才开始保护中的测量。恢复失败会拒绝继续。测试结束关闭自己创建的进程，不关闭微信或其他应用。

## 已验证与剩余边界

- 原生与 Qt 按钮收到真实排队鼠标按下/释放，业务回调执行。
- 保护期间对 SetForegroundWindow、SetActiveWindow、SetFocus、BringWindowToTop、SetWindowPos(HWND_TOPMOST)、ShowWindow 的本次组合请求保持焦点与层级。
- 显式解除、期限到达、撤下外部 hooks 后期限到达，正常行为恢复。
- 同进程 WinForms 的原始鼠标消息没有形成点击；其 PerformClick 仅验证拦截机制，不能作为鼠标兼容性证据。

尚未覆盖新窗口的首次显示、其他 UI 线程、WeChatAppEx 新进程、跨位数、完整的进程异常退出矩阵、连续前台事件监测和实际微信任务。普通聊天/邮件内容没有进入此实验。

用户已确认另一位 agent 停止操作后进行了受限试验。实际失败撤销了将自建窗口结果外推到微信的依据；当前保持常规微信隔离，不以再次启用原型作为恢复方法。
