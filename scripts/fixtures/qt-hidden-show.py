"""Does Win32-only restore preserve Qt visibility and button behavior?"""
import ctypes as c
import json
import subprocess
import sys
from ctypes import wintypes as w
from PyQt5.QtCore import Qt,QTimer
from PyQt5.QtWidgets import QApplication,QWidget,QPushButton
u=c.WinDLL('user32',use_last_error=True)
u.SetProcessDpiAwarenessContext(c.c_void_p(-4))
u.SetWindowPos.argtypes=[w.HWND,w.HWND,c.c_int,c.c_int,c.c_int,c.c_int,w.UINT]
u.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
u.IsWindowVisible.argtypes=[w.HWND]
QApplication.setAttribute(Qt.AA_DisableHighDpiScaling)
app=QApplication([])
window=QWidget();window.setAttribute(Qt.WA_ShowWithoutActivating)
window.setWindowTitle('AgentMouse Qt hidden restore regression')
window.setGeometry(100,100,320,200)
button=QPushButton('Business counter',window);button.setGeometry(30,40,230,70)
count=0;rows=[]
def clicked():
    global count
    count+=1
button.clicked.connect(clicked)
window.show();hwnd=int(window.winId())
u.SetWindowPos(hwnd,1,0,0,0,0,0x13)
def click():
    point=(60<<16)|90
    assert u.PostMessageW(hwnd,0x201,1,point)
    assert u.PostMessageW(hwnd,0x202,0,point)
def row(label):
    rows.append(dict(stage=label,nativeVisible=bool(u.IsWindowVisible(hwnd)),qtVisible=window.isVisible(),buttonVisible=button.isVisible(),clicks=count))
def native_restore():
    row('normal')
    window.hide()
    if len(sys.argv)>1:
        completed=subprocess.run([sys.argv[1],'--desktop-query',json.dumps(dict(method='show_window',windowId=str(hwnd)))],capture_output=True,text=True,timeout=5)
        reply=json.loads(completed.stdout)
        rows.append(dict(stage='shared_show_guard',rejected=reply.get('error',{}).get('code')=='application_restore_required',nativeVisible=bool(u.IsWindowVisible(hwnd)),qtVisible=window.isVisible()))
    assert u.SetWindowPos(hwnd,1,0,0,0,0,0x53)
    QTimer.singleShot(100,click)
    QTimer.singleShot(300,application_restore)
def application_restore():
    row('native_only_restore')
    window.show()
    u.SetWindowPos(hwnd,1,0,0,0,0,0x13)
    QTimer.singleShot(100,click)
    QTimer.singleShot(300,done)
def done():
    row('qt_restore')
    print(json.dumps(rows),flush=True)
    app.quit()
QTimer.singleShot(150,click)
QTimer.singleShot(450,native_restore)
QTimer.singleShot(8000,app.quit)
app.exec_()
assert rows[0]['clicks']==1, rows
native=next(r for r in rows if r['stage']=='native_only_restore')
restored=next(r for r in rows if r['stage']=='qt_restore')
assert native['nativeVisible'] and not native['qtVisible'] and not native['buttonVisible'], rows
assert native['clicks']==2, rows
assert restored['qtVisible'] and restored['buttonVisible'] and restored['clicks']==3, rows
if len(sys.argv)>1:
    guarded=next(r for r in rows if r['stage']=='shared_show_guard')
    assert guarded['rejected'] and not guarded['nativeVisible'] and not guarded['qtVisible'], rows
