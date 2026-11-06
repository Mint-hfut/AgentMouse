/* Optional experiment: reject application activation before entering user32. */
#include "MinHook.h"
#include <shellapi.h>
#include <stddef.h>
static BOOL (WINAPI *original_foreground)(HWND);
static HWND (WINAPI *original_active)(HWND);
static HWND (WINAPI *original_focus)(HWND);
static BOOL (WINAPI *original_position)(HWND,HWND,int,int,int,int,UINT);
static BOOL (WINAPI *original_show)(HWND,int);
static BOOL (WINAPI *original_top)(HWND);
static BOOL early_installed;
static HWND (WINAPI *original_create)(DWORD,LPCWSTR,LPCWSTR,DWORD,int,int,int,int,HWND,HMENU,HINSTANCE,LPVOID);
static BOOL (WINAPI *original_notify)(DWORD,PNOTIFYICONDATAW);
static BOOL WINAPI early_notify(DWORD command,PNOTIFYICONDATAW icon){
    BOOL result=original_notify(command,icon);
    // A TaskbarCreated replay can re-add an existing icon; GetRect corroborates it before use.
    if((!result && command!=NIM_ADD) || !active() || !icon || icon->cbSize<offsetof(NOTIFYICONDATAW,uCallbackMessage)+sizeof(UINT))return result;
    DWORD pid=0;
    GetWindowThreadProcessId(icon->hWnd,&pid);
    if(pid!=GetCurrentProcessId())return result;
    UINT_PTR previous=(UINT_PTR)GetPropW(icon->hWnd,L"AgentMouse.TrayIdentifier.v2");
    UINT_PTR identifier=(UINT_PTR)icon->uID+1;
    if((command==NIM_ADD||command==NIM_MODIFY) && (icon->uFlags&NIF_MESSAGE)){
        if(previous&&previous!=identifier)SetPropW(icon->hWnd,L"AgentMouse.TrayAmbiguous.v2",(HANDLE)1);
        SetPropW(icon->hWnd,L"AgentMouse.TrayCallback.v2",(HANDLE)(UINT_PTR)icon->uCallbackMessage);
        SetPropW(icon->hWnd,L"AgentMouse.TrayIdentifier.v2",(HANDLE)identifier);
        if(command==NIM_ADD || !GetPropW(icon->hWnd,L"AgentMouse.TrayVersion.v2")){
            UINT version=0;
            // shortcut: duplicate ADD exposes declared mode only; require full refresh for mismatching apps.
            if(!result && icon->cbSize>=offsetof(NOTIFYICONDATAW,uVersion)+sizeof(UINT))version=icon->uVersion;
            SetPropW(icon->hWnd,L"AgentMouse.TrayVersion.v2",(HANDLE)((UINT_PTR)version+1));
        }
    }
    if(command==NIM_SETVERSION && previous==identifier && icon->cbSize>=offsetof(NOTIFYICONDATAW,uVersion)+sizeof(UINT)){
        SetPropW(icon->hWnd,L"AgentMouse.TrayVersion.v2",(HANDLE)((UINT_PTR)icon->uVersion+1));
    }
    if(command==NIM_DELETE && previous==identifier){
        RemovePropW(icon->hWnd,L"AgentMouse.TrayCallback.v2");
        RemovePropW(icon->hWnd,L"AgentMouse.TrayIdentifier.v2");
        RemovePropW(icon->hWnd,L"AgentMouse.TrayVersion.v2");
    }
    return result;
}

static BOOL protected_window(HWND w) {
#ifdef AM_PROCESS_SCOPE
    DWORD pid=0;
    if(active()&&control->scope&&w){
        GetWindowThreadProcessId(w,&pid);
        if(pid==GetCurrentProcessId())return TRUE;
        GetWindowThreadProcessId(GetAncestor(w,GA_ROOT),&pid);
        return pid==GetCurrentProcessId();
    }
#endif
    return active() && (w==target || GetAncestor(w,GA_ROOT)==target);
}
#ifdef AM_VIRTUAL_POINTER
#include "virtual-pointer.h"
#endif
static BOOL WINAPI early_foreground(HWND w) {
    if(protected_window(w)){activation_blocks++;SetLastError(ERROR_ACCESS_DENIED);return FALSE;}
    return original_foreground(w);
}
static HWND WINAPI early_active(HWND w) {
    if(protected_window(w)){activation_blocks++;SetLastError(ERROR_ACCESS_DENIED);return NULL;}
    return original_active(w);
}
static HWND WINAPI early_focus(HWND w) {
    if(protected_window(w)){focus_blocks++;SetLastError(ERROR_ACCESS_DENIED);return NULL;}
    return original_focus(w);
}
static BOOL WINAPI early_position(HWND w,HWND after,int x,int y,int cx,int cy,UINT flags) {
    if(protected_window(w)){
        flags|=SWP_NOACTIVATE;
        if(GetAncestor(w,GA_ROOT)==w){
            if(!(flags&SWP_NOZORDER))position_blocks++;
            flags|=SWP_NOZORDER;
        }
    }
    return original_position(w,after,x,y,cx,cy,flags);
}
static BOOL WINAPI early_show(HWND w,int command) {
    if(protected_window(w) && GetAncestor(w,GA_ROOT)==w && (command==SW_SHOWNORMAL || command==SW_SHOW || command==SW_RESTORE || command==SW_SHOWDEFAULT)) {
        /* Even SW_SHOWNOACTIVATE can raise an existing Qt window. */
        activation_blocks++;
#ifdef AM_PROCESS_SCOPE
        if(control->scope&&IsIconic(w))original_show(w,SW_SHOWNOACTIVATE);
        if(control->scope&&!IsWindowVisible(w))original_position(w,HWND_BOTTOM,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW);
#endif
        return IsWindowVisible(w);
    }
    return original_show(w,command);
}
static BOOL WINAPI early_top(HWND w) {
    if(protected_window(w)){
        if(GetAncestor(w,GA_ROOT)!=w)return original_position(w,HWND_TOP,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE);
        position_blocks++;SetLastError(ERROR_ACCESS_DENIED);return FALSE;
    }
    return original_top(w);
}
static HWND WINAPI early_create(DWORD ex,LPCWSTR cls,LPCWSTR title,DWORD style,int x,int y,int width,int height,HWND parent,HMENU menu,HINSTANCE instance,LPVOID parameter){
    BOOL suppress=FALSE;
#ifdef AM_PROCESS_SCOPE
    suppress=active()&&control->scope&&!(style&WS_CHILD)&&(style&WS_VISIBLE);
#endif
    HWND window=original_create(ex,cls,title,suppress?style&~WS_VISIBLE:style,x,y,width,height,parent,menu,instance,parameter);
    if(window&&suppress)original_position(window,HWND_BOTTOM,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW);
    return window;
}
static BOOL install_early(void) {
    if(early_installed)return TRUE;
    if(MH_Initialize()!=MH_OK)return FALSE;
#define INSTALL(name,replacement,original) if(MH_CreateHookApi(L"user32",name,(LPVOID)replacement,(LPVOID*)&original)!=MH_OK)goto failed
    INSTALL("SetForegroundWindow",early_foreground,original_foreground);
    INSTALL("SetActiveWindow",early_active,original_active);
    INSTALL("SetFocus",early_focus,original_focus);
    INSTALL("SetWindowPos",early_position,original_position);
    INSTALL("ShowWindow",early_show,original_show);
    INSTALL("BringWindowToTop",early_top,original_top);
    INSTALL("CreateWindowExW",early_create,original_create);
#undef INSTALL
    if(GetModuleHandleW(L"shell32")&&MH_CreateHookApi(L"shell32","Shell_NotifyIconW",(LPVOID)early_notify,(LPVOID*)&original_notify)!=MH_OK)goto failed;
#ifdef AM_VIRTUAL_POINTER
    if(!install_pointer_hooks())goto failed;
#endif
    if(MH_EnableHook(MH_ALL_HOOKS)!=MH_OK)goto failed;
    early_installed=TRUE;return TRUE;
failed:
    MH_Uninitialize();return FALSE;
}
