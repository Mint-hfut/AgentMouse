"""Real Qt lifecycle regression, with an optional registered tray capability."""
import ctypes as c
import json
import os
import subprocess
import sys
import time
from ctypes import wintypes as w
from PyQt5.QtCore import Qt, QTimer
from PyQt5.QtWidgets import QApplication, QWidget, QPushButton, QSystemTrayIcon, QStyle

u = c.WinDLL('user32', use_last_error=True)
u.SetProcessDpiAwarenessContext(c.c_void_p(-4))
u.SetWindowPos.argtypes = [w.HWND,w.HWND,c.c_int,c.c_int,c.c_int,c.c_int,w.UINT]
u.PostMessageW.argtypes = [w.HWND,w.UINT,w.WPARAM,w.LPARAM]
u.IsWindowVisible.argtypes = [w.HWND]
u.GetPropW.argtypes = [w.HWND,w.LPCWSTR]
u.GetPropW.restype = w.HANDLE
u.GetWindowThreadProcessId.argtypes = [w.HWND,c.POINTER(w.DWORD)]
enumerate_window = c.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)

def tray_versions():
    versions = []
    @enumerate_window
    def inspect(hwnd, unused):
        pid = w.DWORD()
        u.GetWindowThreadProcessId(hwnd,c.byref(pid))
        if pid.value == os.getpid():
            value = u.GetPropW(hwnd,'AgentMouse.TrayVersion.v2')
            if value:
                versions.append(int(value)-1)
        return True
    u.EnumWindows(inspect,0)
    return versions

QApplication.setAttribute(Qt.AA_DisableHighDpiScaling)
app = QApplication([])
window = QWidget()
window.setAttribute(Qt.WA_ShowWithoutActivating)
window.setWindowTitle('AgentMouse generic Qt lifecycle regression')
window.setGeometry(100,100,320,200)
button = QPushButton('Business counter',window)
button.setGeometry(30,40,230,70)
count = 0
rows = []
with_tray = '--tray' in sys.argv

def clicked():
    global count
    count += 1
button.clicked.connect(clicked)
window.show()
hwnd = int(window.winId())
u.SetWindowPos(hwnd,1,0,0,0,0,0x13)
tray = None
if with_tray:
    tray = QSystemTrayIcon(app.style().standardIcon(QStyle.SP_ComputerIcon),app)
    tray.setToolTip('AgentMouse generic Qt tray')
    def activate(reason):
        if reason == QSystemTrayIcon.Trigger:
            window.showNormal()
            u.SetWindowPos(hwnd,1,0,0,0,0,0x13)
    tray.activated.connect(activate)
    tray.show()

def click():
    point = (60<<16)|90
    assert u.PostMessageW(hwnd,0x201,1,point)
    assert u.PostMessageW(hwnd,0x202,0,point)

def row(label):
    rows.append(dict(stage=label,nativeVisible=bool(u.IsWindowVisible(hwnd)),qtVisible=window.isVisible(),buttonVisible=button.isVisible(),clicks=count))

def finish_native_restore():
    window.hide()
    assert u.SetWindowPos(hwnd,1,0,0,0,0,0x53)
    QTimer.singleShot(100,click)
    QTimer.singleShot(300,application_restore)

def native_restore():
    row('normal')
    window.hide()
    if len(sys.argv) <= 1:
        finish_native_restore()
        return
    child = subprocess.Popen([sys.argv[1],'--desktop-query',json.dumps(dict(method='show_window',windowId=str(hwnd)))],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    started = time.monotonic()
    def completed():
        if child.poll() is None:
            if time.monotonic()-started>6:
                child.kill()
                rows.append(dict(stage='restore_timeout'))
                app.quit()
                return
            QTimer.singleShot(20,completed)
            return
        output,_ = child.communicate()
        reply = json.loads(output)
        rows.append(dict(stage='shared_show_guard',rejected=reply.get('error',{}).get('code')=='application_restore_required',restored=reply.get('ok') is True,nativeVisible=bool(u.IsWindowVisible(hwnd)),qtVisible=window.isVisible(),trayVersions=tray_versions()))
        finish_native_restore()
    QTimer.singleShot(20,completed)

def application_restore():
    row('native_only_restore')
    window.show()
    u.SetWindowPos(hwnd,1,0,0,0,0,0x13)
    QTimer.singleShot(100,click)
    QTimer.singleShot(300,done)

def done():
    row('qt_restore')
    print(json.dumps(rows),flush=True)
    if tray:
        tray.hide()
    app.quit()
QTimer.singleShot(150,click)
QTimer.singleShot(450,native_restore)
QTimer.singleShot(10000,app.quit)
app.exec_()
assert rows[0]['clicks']==1, rows
native = next(r for r in rows if r['stage']=='native_only_restore')
restored = next(r for r in rows if r['stage']=='qt_restore')
assert native['nativeVisible'] and not native['qtVisible'] and not native['buttonVisible'], rows
assert native['clicks']==2, rows
assert restored['qtVisible'] and restored['buttonVisible'] and restored['clicks']==3, rows
if len(sys.argv)>1:
    guarded = next(r for r in rows if r['stage']=='shared_show_guard')
    if with_tray:
        assert guarded['restored'] and guarded['nativeVisible'] and guarded['qtVisible'], rows
        assert 4 in guarded['trayVersions'], rows
    else:
        assert guarded['rejected'] and not guarded['nativeVisible'] and not guarded['qtVisible'], rows
