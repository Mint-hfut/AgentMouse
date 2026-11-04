"""Temporary incident quarantine; this is containment, not an app compatibility fix."""
QUARANTINED = {"weixin", "wechat", "wechatappex"}


def require_allowed_process(name):
    if name.casefold().removesuffix(".exe") in QUARANTINED:
        raise ValueError("target_quarantined: WeChat automation is suspended after an application hang; do not bypass via UIA, scripts or physical input")


def require_allowed_window(window):
    require_allowed_process(window.get("process", ""))
    if window.get("automationQuarantined"):
        raise ValueError("target_quarantined: target automation is suspended")
