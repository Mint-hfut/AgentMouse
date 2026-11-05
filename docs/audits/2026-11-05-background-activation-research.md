# AgentMouse 同桌面后台输入：防激活路线调研

日期：2026-11-05。范围是 AgentMouse；agent-desktop 与独立隐藏桌面由用户交给其他 agent，不在此处修改。

## 结论与证据边界

保留已验证的后台截图、明确 HWND 的消息输入和独立指针。新增能力应针对目标程序的激活请求及窗口层级变化，而非继续替换点击发送 API。进程内拦截是有现成源码支持的候选方案，但尚未在微信验证，不是已修复版本。

本轮完成官方文档与开源实现核查，没有给微信加载 DLL，没有安装 Windhawk，没有更改客户端插件或产品输入实现。前轮真实微信记录见 [输入路径实测](../test-results/wechat-input-paths-2026-11-05.json)。其中一次存在系统输入时间变化，不能把所有前台变化严格归因于注入。

## 候选方法

| 方法 | 文档或源码事实 | 对 AgentMouse 的判断 |
|---|---|---|
| AutoHotkey ControlClick 的 NA 模式 | 避免主动激活目标以及合并输入处理；官方明确不适用于所有控件 | 与已有非激活消息输入思路相近，不能据此承诺阻止微信自身激活 |
| WS_EX_NOACTIVATE | 不能阻止程序主动调用 SetForegroundWindow / SetActiveWindow | 不作为通用防激活开关 |
| LockSetForegroundWindow | 由前台进程调用；用户按 Alt 或改变前台可自动解锁 | 后台 AgentMouse 不能直接替任意前台应用持有这把锁；也不完整保护层级 |
| SetWinEventHook | 提供事件通知 | 用于验收和诊断；事后抢回焦点不记作无干扰成功 |
| WH_CBT | HCBT_ACTIVATE / HCBT_SETFOCUS 可在操作发生前返回 1 阻止；创建回调可改变初始插入层级 | 最小的有文档支持的阻断原型；单独使用不能保证所有后续层级操作被阻止 |
| 目标进程内 API 拦截 | Windhawk 已拦截 CreateWindowExW、ShowWindow、SetForegroundWindow、SetFocus、SetWindowPos 等 | 最贴近当前故障；需要补齐层级保护、动作作用域、失效恢复及多进程覆盖 |

## 最有参考价值的实现

[Windhawk Prevent Focus Stealing 源码](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/prevent-focus-stealing.wh.cpp) 标注 MIT，核查时为 1.0.2。

它在进程内部阻止主动前台和焦点请求，并将部分显示调用替换成不激活显示。但 SetWindowPos 分支主要添加 SWP_NOACTIVATE，没有同时保证 SWP_NOZORDER。因此它能提供设计参考，不能原样当作“不会遮挡用户窗口”的完整方案。代码还有晚加载恢复前台的路径，不能将这种事后恢复计入 AgentMouse 后台成功。

[Microsoft Detours](https://github.com/microsoft/Detours/wiki/Using-Detours) 提供进程内 API 拦截基础设施。选择这一层意味着需要一个匹配目标位数的原生组件，不是给 C# 窗口增加一个样式即可。现阶段先验证最小 CBT 原型，再判断是否确实需要更宽的 API 拦截。

## 最小原型与验收

原型独立于现有输入引擎，不对所有桌面程序安装全局策略。保护对象必须绑定 PID、进程创建时间、线程及 HWND；窗口销毁或身份变化后取消输入。

1. 在自建目标程序中，按钮处理器明确请求激活、设置焦点、提高层级及创建弹窗。分别测量各路径，避免操作系统原本拒绝激活造成假阳性。
2. 先验证 CBT 能否阻止激活但保留实际按钮回调。单独检查层级，不能只看 GetForegroundWindow。
3. 若仍发生层级变化，再加入受限的进程内 API 拦截。已有窗口保持原层级；新弹窗需要合适的后台位置，不能简单统一使用 NOZORDER 假定初始位置安全。
4. 原型必须覆盖 SetActiveWindow、BringWindowToTop、ShowWindowAsync、DeferWindowPos 等潜在遗漏路径；这是待测覆盖清单，不是声称某一个 hook 已拦截全部调用。
5. 微信与 WeChatAppEx 可能使用不同进程和 UI 线程。保护必须在点击前完成；新进程首次显示之前的安装时序尚未解决，事后发现再降层级不能宣称零干扰。
6. 控制器只在有期限的 Agent 动作期间启用保护。断连或超时停止 Agent 输入并撤销保护；真实用户接管时优先恢复其操作，不能永久封住微信。
7. 回调避免同步 IPC、UIA 或向目标递归发消息。回调中的日志和状态查询也必须有边界，防止重入、锁循环和 UI 线程阻塞。

验收同时检查：业务效果、原始输入活动、前台事件、目标及新窗口层级、鼠标坐标、目标响应、捕获释放、暂停/崩溃后的恢复。旧微信卡死根因未确定，不能直接用用户微信作为第一次 hook 实验对象。

## 参考资料

- [ControlClick 官方文档源码](https://github.com/AutoHotkey/AutoHotkeyDocs/blob/v2/docs/lib/ControlClick.htm)
- [CBTProc：可阻止的操作及创建层级](https://learn.microsoft.com/en-us/windows/win32/winmsg/cbtproc)
- [SetWindowsHookEx：线程范围、DLL、位数要求](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw)
- [LockSetForegroundWindow：调用方和自动解锁](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-locksetforegroundwindow)
- [SetWindowPos：激活与层级分别控制](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos)
- [SetWinEventHook：事件通知](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)
- [微软对 WS_EX_NOACTIVATE 限制的解释](https://devblogs.microsoft.com/oldnewthing/20240919-00/?p=110283)

尚未验证：跨进程 hook 装载/卸载、真实微信和内置浏览器的业务兼容性、安全软件兼容性、应用依赖激活成功时的行为。上述方案不能据调研直接宣传为全应用支持。
