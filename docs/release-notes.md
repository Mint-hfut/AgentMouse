# 0.7.0-beta.17

- 记录通知图标单独协商的协议版本，并在删除后清理回调元数据。原生协议回归与 Qt 托盘恢复通过。
- 真实 Codex 通过已安装 Full 插件完成自建 Qt 窗口按钮与滚动测试，业务状态、新图像和保护回执均通过。[当前版本验收](test-results/codex-owned-fixture-2026-11-06.json)
- 重放注册遇到已有图标时仍使用应用声明的协议，原始字段与实际协议不一致的应用需要进一步验证。
- 完整邮件验收记录属于 beta.15，beta.16 的复测遇到应用退出登录。beta.17 的完整邮件链路待登录态恢复后验证。

# 0.7.0-beta.16

- 保护租约加入随机标识，避免超时后的旧清理影响后续操作。
- 状态、路由、延迟释放失败回归与 Core/Full 分发检查通过。
- 已安装到 Codex。后台提示点击回执通过，完整邮件复测因账号退出登录暂停。成功的邮件链路记录属于 beta.15。

# 0.7.0-beta.15

- 接入目标线程的原生激活保护与虚拟鼠标查询，保持用户前台、键盘焦点、指针位置和形状。
- 使用应用进程关系保护关联 UI，修复对子窗口绘制层级的过度限制。
- 从实际通知图标注册信息恢复已有应用，支持已验证的隐藏和最小化生命周期。
- 修复原生库载荷清单、构建文件锁、插件升级目录和导出包依赖遗漏。
- Core 与 Full 分别打包，Full 保留隔离的随包 Python。Codex 已通过真实邮件任务。[验收记录](test-results/codex-background-mail-2026-11-06.json)

# Historical 0.7.0-beta.4 audit

New audit fixes: one capture process per screenshot with bounded timeout/cancellation and resource disposal before response; image-hash post-action guards; provider-failure label rejection; worker write/disconnect cleanup; confirmed extension release tracking; explicit input enable for an already-authorized task; Core/Full enable-EOF race cleanup; hidden-window background show and read-only input-state metadata. See [audit](audits/2026-11-04.md) for checks and remaining limits.

The user explicitly requested a live native-only pilot after restarting WeChat. Screenshots returned and the cursor stayed still, but the click raised WeChat and did not open QQ Mail reminders. The controller paused; no email was read. Normal quarantine and old-plugin disablement remain. This build has not been installed into the user's agents or released as a ZIP.

WeChat became unresponsive during an earlier Full session and required the user to restart the app. Keep the old installed plugin disabled; the later explicitly authorized repaired-source pilot above also failed compatibility. Do not use arbitrary scripts as a fallback.

Source containment: quarantine known WeChat process families, with an explicit PID/creation-time native-only diagnostic grant for a user-requested repaired-source pilot. UIA remains quarantined. Default Snapshot is image-only; Full has no arbitrary PowerShell tool (26 tool names). Core remains 11 tools. Ordinary regressions use owned fixtures; the separate failed live pilot is identified above.

Native fixes: skip transparent child render layers in covered-window input, and queue cancel/UP/cancel after uncertain message delivery. Old-backend regressions fail for a real Qt button business event and missing UP after delayed DOWN; both pass with the fixes. Delayed UP, pause and graceful controller shutdown also pass with actual target capture release. No repeated DOWN is sent. Queueing cleanup never counts as confirmed target execution.

Reproduce on an interactive Windows desktop: `npm run test:routing`, `npm run test:native:failures`, `npm run test:fusion:windows`. `npm run test:fusion:qt` additionally uses a separate test interpreter with PyQt5==5.15.11; see [compatibility](compatibility.md). Qt is a development fixture dependency only and is not shipped in Core or Full.

The earlier trace confirms physical-input/foreground fallback through Full PowerShell, but does not establish which operation caused the persistent app state. Target-side evidence was not collected before that restart. No persistent-hang resolution or live WeChat compatibility is claimed. The repaired source was used only for the explicit pilot, not installed into the user's agents or released as a ZIP.
