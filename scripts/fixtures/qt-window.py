"""Disposable Qt 5 button plus the transparent native render layer seen in WeChat.

Test dependency only: PyQt5==5.15.11; not included in either product edition.
"""
import ctypes as c
import json
import sys
from ctypes import wintypes as w
from pathlib import Path

u = c.WinDLL("user32", use_last_error=True)
u.SetProcessDpiAwarenessContext(c.c_void_p(-4))
u.GetForegroundWindow.restype = w.HWND
u.GetWindowRect.argtypes = [w.HWND, c.POINTER(w.RECT)]
u.SetWindowPos.argtypes = [w.HWND, w.HWND, c.c_int, c.c_int, c.c_int, c.c_int, w.UINT]
u.CreateWindowExW.argtypes = [w.DWORD, w.LPCWSTR, w.LPCWSTR, w.DWORD, c.c_int, c.c_int, c.c_int, c.c_int, w.HWND, w.HMENU, w.HINSTANCE, c.c_void_p]
u.CreateWindowExW.restype = w.HWND

from PyQt5.QtCore import Qt, QTimer, QT_VERSION_STR
from PyQt5.QtWidgets import QApplication, QWidget, QPushButton

QApplication.setAttribute(Qt.AA_DisableHighDpiScaling)
app = QApplication([])
window = QWidget()
window.setAttribute(Qt.WA_ShowWithoutActivating)
window.setWindowTitle(sys.argv[1])
front = u.GetForegroundWindow()
rect = w.RECT()
assert u.GetWindowRect(front, c.byref(rect))
window.setGeometry(rect.left + 50, rect.top + 50, 300, 180)
button = QPushButton("Business clicks: 0", window)
button.setGeometry(30, 40, 230, 70)
count = 0
state = Path(sys.argv[2])


def clicked():
    global count
    count += 1
    button.setText(f"Business clicks: {count}")
    state.write_text(json.dumps({"businessClicks": count}), encoding="utf-8")


button.clicked.connect(clicked)
window.show()
hwnd = int(window.winId())
# A drawing-only child must never receive our mouse messages.
render = u.CreateWindowExW(0x80020, "STATIC", "", 0x50000000, 0, 0, 300, 180, hwnd, None, None, None)
assert render, c.get_last_error()
assert u.SetWindowPos(hwnd, 1, 0, 0, 0, 0, 0x13)
state.write_text(json.dumps({"businessClicks": 0}), encoding="utf-8")


def ready():
    rect = w.RECT()
    assert u.GetWindowRect(hwnd, c.byref(rect))
    origin = window.mapToGlobal(button.geometry().center())
    print(json.dumps({"windowId": str(hwnd), "loc": [origin.x(), origin.y()], "qt": QT_VERSION_STR, "foregroundUnchanged": u.GetForegroundWindow() == front}), flush=True)


QTimer.singleShot(300, ready)
QTimer.singleShot(60000, app.quit)
app.exec_()
