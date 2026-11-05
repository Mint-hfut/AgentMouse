"""Owned Qt target for the cross-process guard experiment; never a user app."""
import ctypes as c
import json
from ctypes import wintypes as w
from PyQt5.QtWidgets import QApplication, QWidget, QPushButton
from PyQt5.QtCore import Qt, QTimer

u = c.WinDLL('user32', use_last_error=True)
k = c.WinDLL('kernel32')
for name in ('GetForegroundWindow', 'GetActiveWindow', 'GetFocus'):
    getattr(u, name).restype = w.HWND
for name in ('SetActiveWindow', 'SetFocus', 'SetForegroundWindow', 'DestroyWindow'):
    getattr(u, name).argtypes = [w.HWND]
u.GetWindow.argtypes = [w.HWND, w.UINT]
u.GetWindow.restype = w.HWND
u.SetWindowPos.argtypes = [w.HWND,w.HWND,c.c_int,c.c_int,c.c_int,c.c_int,w.UINT]
u.SetProcessDpiAwarenessContext(c.c_void_p(-4))
QApplication.setAttribute(Qt.AA_DisableHighDpiScaling)
app = QApplication([])
count = 0
stress = False

class Target(QWidget):
    def nativeEvent(self, kind, ptr):
        global stress
        msg = w.MSG.from_address(int(ptr))
        if msg.message == 0x8000+310:
            return True, count
        if msg.message == 0x8000+311:
            u.SetActiveWindow(ch)
            u.SetFocus(ch)
            u.SetWindowPos(ch,1,0,0,0,0,0x13)
            u.SetWindowPos(wh,1,0,0,0,0,0x13)
            return True, 1
        if msg.message == 0x8000+312:
            return True, int(u.GetActiveWindow()==wh)
        if msg.message == 0x8000+313:
            return True, int(u.GetFocus()==bh)
        if msg.message == 0x8000+314:
            fg=u.GetForegroundWindow()
            return True, int(bool(u.SetForegroundWindow(msg.wParam)) if fg in (wh,ch) else fg==msg.wParam)
        if msg.message in (0x8000+315,0x8000+316):
            stress=msg.message==0x8000+315
            return True, 1
        return super().nativeEvent(kind,ptr)

target=Target()
cover=QWidget()
for widget,title in ((target,'AgentMouse guard Qt fixture'),(cover,'AgentMouse Qt cover')):
    widget.setAttribute(Qt.WA_ShowWithoutActivating)
    widget.setWindowTitle(title)
    widget.setGeometry(80,80,320,200)
button=QPushButton('Actual Qt button',target)
button.setGeometry(30,40,200,60)
wh=int(target.winId())
ch=int(cover.winId())
bh=int(button.winId())

def clicked():
    global count
    count+=1
    if stress:
        u.SetForegroundWindow(wh)
        u.BringWindowToTop(c.c_void_p(wh))
        u.SetWindowPos(wh,-1,0,0,0,0,3)
        u.ShowWindow(c.c_void_p(wh),5)
    u.SetActiveWindow(wh)
    u.SetFocus(bh)
    anchor=u.GetWindow(ch,3)
    if anchor and anchor!=wh:
        u.SetWindowPos(wh,anchor,0,0,0,0,0x13)

button.clicked.connect(clicked)
target.show()
cover.show()
u.SetWindowPos(ch,1,0,0,0,0,0x13)
u.SetWindowPos(wh,1,0,0,0,0,0x13)
def ready():
    print(json.dumps(dict(window=wh,cover=ch,button=bh,thread=k.GetCurrentThreadId(),pid=k.GetCurrentProcessId())),flush=True)
QTimer.singleShot(100,ready)
QTimer.singleShot(25000,app.quit)
app.exec_()
