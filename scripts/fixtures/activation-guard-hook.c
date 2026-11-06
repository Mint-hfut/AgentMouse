/* Experimental DLL: accepts only our owned AMGuardFixture window class. */
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <commctrl.h>
#ifdef AM_PILOT_CONFIG
#include AM_PILOT_CONFIG
#endif

static HWND target;
static ULONGLONG deadline;
static HMODULE retained_module;
static UINT_PTR timer_id;
static UINT activation_blocks, focus_blocks, position_blocks;
static DWORD owner_thread,input_tick;
#ifndef BEGIN_GUARD
#define BEGIN_GUARD (WM_APP + 300)
#define END_GUARD (WM_APP + 301)
#define GUARD_STATS (WM_APP + 302)
#define GUARD_READY (WM_APP + 303)
#endif
#define SUBCLASS_ID 0x414d4752

static BOOL active(void) {
    if(GetCurrentThreadId()!=owner_thread || !target || GetTickCount64()>=deadline)return FALSE;
    LASTINPUTINFO input={sizeof(input),0};
    return GetLastInputInfo(&input) && input.dwTime==input_tick;
}
#ifdef AM_EARLY_API
#include "../../native/activation-policy.h"
#endif
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
#ifndef AM_EARLY_API
    if (code>=0 && active() && ((HWND)wp==target || GetAncestor((HWND)wp,GA_ROOT)==target)) {
        if (code==HCBT_ACTIVATE) { activation_blocks++; return 1; }
        if (code==HCBT_SETFOCUS) { focus_blocks++; return 1; }
    }
#endif
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
#ifdef AM_PILOT_PID
            FILETIME created,exited,kernel,user;
            ULARGE_INTEGER creation;
            creation.QuadPart=0;
            if(GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user)){creation.LowPart=created.dwLowDateTime;creation.HighPart=created.dwHighDateTime;}
            owned_class=pid==AM_PILOT_PID && creation.QuadPart==AM_PILOT_STARTED && (UINT_PTR)message->hwnd==AM_PILOT_HWND && !lstrcmpW(name,L"Qt51514QWindowIcon");
#endif
            if (pid==GetCurrentProcessId() && owned_class) {
                disarm();
                /* Retain before installing code that other threads may call. */
                if (!retained_module) GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,(LPCWSTR)GuardMessages,&retained_module);
                if(!retained_module)return CallNextHookEx(NULL,code,wp,lp);
#ifdef AM_EARLY_API
                if(!install_early())return CallNextHookEx(NULL,code,wp,lp);
#endif
                /* Retain until the disposable target exits: never unload live subclass code. */
                if (retained_module && SetWindowSubclass(message->hwnd,subclass,SUBCLASS_ID,0)) {
                    target=message->hwnd;
                    activation_blocks=focus_blocks=position_blocks=0;
                    LASTINPUTINFO input={sizeof(input),0};
                    owner_thread=GetCurrentThreadId();
                    if(!GetLastInputInfo(&input)){disarm();return CallNextHookEx(NULL,code,wp,lp);}
                    input_tick=input.dwTime;
                    deadline=GetTickCount64()+1000;
                    timer_id=SetTimer(target,0,50,NULL);
                    if (!timer_id) disarm();
                }
            }
        }
    }
    return CallNextHookEx(NULL,code,wp,lp);
}
