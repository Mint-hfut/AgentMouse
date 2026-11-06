#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shellapi.h>
#include <stdio.h>

static HWND cover, button;
static unsigned clicks;
static BOOL stress;
static LRESULT CALLBACK WindowProc(HWND w,UINT msg,WPARAM wp,LPARAM lp) {
    if (msg==WM_COMMAND && LOWORD(wp)==100 && HIWORD(wp)==BN_CLICKED) {
        clicks++;
        if (stress) {
            SetForegroundWindow(w);
            BringWindowToTop(w);
            SetWindowPos(w,HWND_TOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE);
            ShowWindow(w,SW_SHOW);
        }
        SetActiveWindow(w);
        SetFocus(button);
        /* Raise only above our cover, without changing position relative to user windows. */
        HWND anchor=GetWindow(cover,GW_HWNDPREV);
        if (anchor && anchor!=w) SetWindowPos(w,anchor,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);
        return 0;
    }
    if (msg==WM_APP+310) return clicks;
    if (msg==WM_APP+311) {SetActiveWindow(cover);SetFocus(cover);SetWindowPos(cover,HWND_BOTTOM,0,0,0,0,0x13);SetWindowPos(w,HWND_BOTTOM,0,0,0,0,0x13);return 1;}
    if (msg==WM_APP+312) return GetActiveWindow()==w;
    if (msg==WM_APP+313) return GetFocus()==button;
    if (msg==WM_APP+315) {stress=TRUE;return 1;}
    if (msg==WM_APP+316) {stress=FALSE;return 1;}
    if (msg==WM_APP+317) {SetWindowPos(cover,HWND_BOTTOM,0,0,0,0,0x13);SetWindowPos(w,HWND_BOTTOM,0,0,0,0,0x13);return 1;}
    if (msg==WM_APP+318) {SetWindowPos(button,HWND_TOP,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);return GetWindow(button,GW_HWNDPREV)==NULL;}
    if (msg==WM_APP+319) {
        NOTIFYICONDATAW icon={0};
        icon.cbSize=sizeof(icon);icon.hWnd=w;icon.uID=42;
        icon.uFlags=NIF_MESSAGE|NIF_ICON;icon.uCallbackMessage=WM_APP+321;
        icon.hIcon=LoadIconW(NULL,IDI_INFORMATION);
        if(!Shell_NotifyIconW(NIM_ADD,&icon))return 0;
        BOOL legacy=GetPropW(w,L"AgentMouse.TrayVersion.v2")== (HANDLE)1;
        icon.uVersion=NOTIFYICON_VERSION_4;
        BOOL changed=Shell_NotifyIconW(NIM_SETVERSION,&icon);
        BOOL recorded=GetPropW(w,L"AgentMouse.TrayVersion.v2")== (HANDLE)5;
        BOOL removed=Shell_NotifyIconW(NIM_DELETE,&icon);
        return legacy&&changed&&recorded&&removed&&!GetPropW(w,L"AgentMouse.TrayCallback.v2")&&!GetPropW(w,L"AgentMouse.TrayVersion.v2");
    }
    if (msg==WM_APP+314) {
        HWND foreground=GetForegroundWindow();
        if (foreground==w || foreground==cover) return SetForegroundWindow((HWND)wp);
        return foreground==(HWND)wp;
    }
    if (msg==WM_DESTROY) {PostQuitMessage(0);return 0;}
    if (msg==WM_TIMER && wp==1) {DestroyWindow(w);return 0;}
    return DefWindowProc(w,msg,wp,lp);
}
int main(void) {
    HINSTANCE module=GetModuleHandle(NULL);
    WNDCLASSW cls={0};cls.lpfnWndProc=WindowProc;cls.hInstance=module;cls.lpszClassName=L"AMGuardFixture";
    if (!RegisterClassW(&cls)) return 1;
    HWND w=CreateWindowExW(0,cls.lpszClassName,L"AgentMouse native guard target",WS_OVERLAPPEDWINDOW,80,80,320,200,NULL,NULL,module,NULL);
    cover=CreateWindowExW(0,L"STATIC",L"AgentMouse native guard cover",WS_OVERLAPPEDWINDOW,80,80,320,200,NULL,NULL,module,NULL);
    button=CreateWindowExW(0,L"BUTTON",L"Native button",WS_CHILD|WS_VISIBLE|BS_PUSHBUTTON,30,40,200,60,w,(HMENU)100,module,NULL);
    CreateWindowExW(0,L"STATIC",L"Owned child rendering layer",WS_CHILD|WS_VISIBLE,30,40,200,60,w,NULL,module,NULL);
    if(!w||!cover||!button)return 2;
    ShowWindow(w,SW_SHOWNOACTIVATE);ShowWindow(cover,SW_SHOWNOACTIVATE);
    SendMessage(w,WM_APP+311,0,0);
    SetTimer(w,1,25000,NULL);
    printf("{\"window\":%llu,\"cover\":%llu,\"button\":%llu,\"thread\":%lu,\"pid\":%lu}\n",(unsigned long long)(UINT_PTR)w,(unsigned long long)(UINT_PTR)cover,(unsigned long long)(UINT_PTR)button,GetCurrentThreadId(),GetCurrentProcessId());fflush(stdout);
    MSG message;while(GetMessage(&message,NULL,0,0)>0){TranslateMessage(&message);DispatchMessage(&message);}DestroyWindow(cover);return 0;
}
