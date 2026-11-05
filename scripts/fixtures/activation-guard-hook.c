/* Experimental DLL: accepts only our owned AMGuardFixture window class. */
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <commctrl.h>

static HWND target;
static ULONGLONG deadline;
static HMODULE retained_module;
static UINT_PTR timer_id;
static UINT activation_blocks, focus_blocks, position_blocks;
#define BEGIN_GUARD (WM_APP + 300)
#define END_GUARD (WM_APP + 301)
#define GUARD_STATS (WM_APP + 302)
#define GUARD_READY (WM_APP + 303)
#define SUBCLASS_ID 0x414d4752

static BOOL active(void) { return target && GetTickCount64() < deadline; }
static LRESULT CALLBACK subclass(HWND,UINT,WPARAM,LPARAM,UINT_PTR,DWORD_PTR);
static void disarm(void) {
    HWND old = target;
    deadline = 0;
    target = NULL;
    if (timer_id) { KillTimer(old,timer_id); timer_id=0; }
    if (old) RemoveWindowSubclass(old,subclass,SUBCLASS_ID);
}
static LRESULT CALLBACK subclass(HWND w,UINT msg,WPARAM wp,LPARAM lp,UINT_PTR id,DWORD_PTR data) {
    (void)id; (void)data;
    if (msg == GUARD_STATS) return activation_blocks | (focus_blocks<<8) | (position_blocks<<16);
    if (msg == GUARD_READY) return active();
    if (msg == END_GUARD || msg == WM_NCDESTROY || (msg==WM_TIMER && wp==timer_id && !active())) {
        disarm();
        return DefSubclassProc(w,msg,wp,lp);
    }
    if (msg==WM_WINDOWPOSCHANGING && active()) {
        WINDOWPOS *position=(WINDOWPOS*)lp;
        if (!(position->flags & SWP_NOZORDER)) position_blocks++;
        position->flags |= SWP_NOACTIVATE | SWP_NOZORDER;
    }
    return DefSubclassProc(w,msg,wp,lp);
}
__declspec(dllexport) LRESULT CALLBACK GuardCbt(int code,WPARAM wp,LPARAM lp) {
    if (code>=0 && active() && ((HWND)wp==target || GetAncestor((HWND)wp,GA_ROOT)==target)) {
        if (code==HCBT_ACTIVATE) { activation_blocks++; return 1; }
        if (code==HCBT_SETFOCUS) { focus_blocks++; return 1; }
    }
    return CallNextHookEx(NULL,code,wp,lp);
}
__declspec(dllexport) LRESULT CALLBACK GuardMessages(int code,WPARAM wp,LPARAM lp) {
    if (code>=0) {
        CWPSTRUCT *message=(CWPSTRUCT*)lp;
        if (message->message==BEGIN_GUARD) {
            WCHAR name[64],title[80];
            DWORD pid=0;
            GetWindowThreadProcessId(message->hwnd,&pid);
            GetWindowTextW(message->hwnd,title,80);
            BOOL owned_class=GetClassNameW(message->hwnd,name,64) && (!lstrcmpW(name,L"AMGuardFixture") || (!lstrcmpW(name,L"Qt5152QWindowIcon") && !lstrcmpW(title,L"AgentMouse guard Qt fixture")));
            if (pid==GetCurrentProcessId() && owned_class) {
                disarm();
                /* Retain until the disposable target exits: never unload live subclass code. */
                if (!retained_module) GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,(LPCWSTR)GuardMessages,&retained_module);
                if (retained_module && SetWindowSubclass(message->hwnd,subclass,SUBCLASS_ID,0)) {
                    target=message->hwnd;
                    activation_blocks=focus_blocks=position_blocks=0;
                    deadline=GetTickCount64()+1000;
                    timer_id=SetTimer(target,0,50,NULL);
                    if (!timer_id) disarm();
                }
            }
        }
    }
    return CallNextHookEx(NULL,code,wp,lp);
}
