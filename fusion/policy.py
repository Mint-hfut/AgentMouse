"""Incident quarantine with an explicit PID/start-time-scoped native pilot; UIA stays blocked."""
import os
QUARANTINED = {"weixin", "wechat", "wechatappex"}


def require_allowed_process(name):
    if name.casefold().removesuffix(".exe") in QUARANTINED:
        raise ValueError("target_quarantined: WeChat automation is suspended after an application hang; do not bypass via UIA, scripts or physical input")


def require_allowed_window(window, accessibility=False):
    if window.get("automationQuarantined"):
        raise ValueError("target_quarantined: target automation is suspended")
    if not accessibility and window.get("activationGuard") is True:
        return
    grant=os.environ.get("AGENTMOUSE_NATIVE_PILOT")
    if not accessibility and grant and grant==window.get("nativePilot") and grant.split(":",1)[0]==str(window.get("pid")):
        return
    require_allowed_process(window.get("process", ""))
