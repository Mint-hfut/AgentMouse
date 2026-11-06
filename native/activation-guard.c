#define UNICODE
#define _UNICODE
#define WINVER 0x0A00
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <commctrl.h>
#include <stdio.h>

typedef struct {
    DWORD magic,version,pid,thread;
    ULONGLONG window,started,expires;
    DWORD input_tick;
    volatile LONG enabled;
    DWORD scope;
    volatile LONG pointer_sequence;
    LONG pointer_x,pointer_y;
    DWORD pointer_buttons;
    volatile LONG pointer_enabled;
    ULONGLONG nonce;
} CONTROL;
static __thread HWND target;
static __thread DWORD owner_thread;
static __thread HANDLE mapping;
static __thread CONTROL *control;
static HMODULE retained_module;
static __thread UINT_PTR timer_id;
static __thread UINT activation_blocks,focus_blocks,position_blocks;
static BOOL active(void) {
    if(GetCurrentThreadId()!=owner_thread || !target || !control || !control->enabled || GetTickCount64()>=control->expires)return FALSE;
    LASTINPUTINFO input={sizeof(input),0};
    if(!GetLastInputInfo(&input))return FALSE;
    if(input.dwTime!=control->input_tick){
        HWND foreground=GetForegroundWindow();DWORD pid=0;
        GetWindowThreadProcessId(foreground,&pid);
        if(foreground==target || (control->scope&&pid==GetCurrentProcessId()))return FALSE;
    }
    return TRUE;
}
#define AM_PROCESS_SCOPE
#define AM_VIRTUAL_POINTER
#include "activation-policy.h"
__declspec(dllexport) BOOL WINAPI ReadSystemPointer(POINT *point){
    return system_physical_cursor?system_physical_cursor(point):GetPhysicalCursorPos(point);
}
static UINT message(LPCWSTR suffix) {
    WCHAR name[100];swprintf(name,100,L"AgentMouse.Guard.v4.%ls",suffix);return RegisterWindowMessageW(name);
}
static LRESULT CALLBACK subclass(HWND,UINT,WPARAM,LPARAM,UINT_PTR,DWORD_PTR);
static void disarm(void) {
    HWND old=target;target=NULL;
    if(timer_id){KillTimer(old,timer_id);timer_id=0;}
    if(old)RemoveWindowSubclass(old,subclass,0x414d4752);
    if(control){UnmapViewOfFile(control);control=NULL;}
    if(mapping){CloseHandle(mapping);mapping=NULL;}
}
static LRESULT CALLBACK subclass(HWND w,UINT msg,WPARAM wp,LPARAM lp,UINT_PTR id,DWORD_PTR data) {
    (void)id;(void)data;
    BOOL matches=control&&control->nonce==(ULONGLONG)wp;
    if(msg==message(L"Ready"))return matches&&active();
    if(msg==message(L"Stats"))return matches?(activation_blocks | (focus_blocks<<8) | (position_blocks<<16)):0;
    if(msg==message(L"PointerStats"))return matches?pointer_reads:0;
    if((msg==message(L"End")&&matches) || msg==WM_NCDESTROY || (msg==WM_TIMER && wp==timer_id && !active())){
        disarm();return DefSubclassProc(w,msg,wp,lp);
    }
    if(msg==WM_WINDOWPOSCHANGING && active()){
        WINDOWPOS *p=(WINDOWPOS*)lp;
        if(!(p->flags&SWP_NOZORDER))position_blocks++;
        p->flags|=SWP_NOACTIVATE|SWP_NOZORDER;
    }
    return DefSubclassProc(w,msg,wp,lp);
}
__declspec(dllexport) LRESULT CALLBACK GuardMessages(int code,WPARAM wp,LPARAM lp) {
    if(code>=0){
        CWPSTRUCT *event=(CWPSTRUCT*)lp;
        if(event->message==message(L"Begin")){
            DWORD pid=0,thread=GetWindowThreadProcessId(event->hwnd,&pid);
            if(pid!=GetCurrentProcessId() || thread!=GetCurrentThreadId())return CallNextHookEx(NULL,code,wp,lp);
            if(control&&control->enabled&&GetTickCount64()<control->expires){SetPropW(event->hwnd,L"AgentMouse.Guard.Error.v4",(HANDLE)7001);return CallNextHookEx(NULL,code,wp,lp);}
            disarm();WCHAR name[160];swprintf(name,160,L"Local\\AgentMouse.Guard.v4.%lu.%lu.%016llx",pid,thread,(unsigned long long)event->wParam);
            mapping=OpenFileMappingW(FILE_MAP_READ,FALSE,name);
            if(mapping)control=(CONTROL*)MapViewOfFile(mapping,FILE_MAP_READ,0,0,sizeof(CONTROL));
            FILETIME created,exited,kernel,user;ULARGE_INTEGER started;started.QuadPart=0;
            if(GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user)){started.LowPart=created.dwLowDateTime;started.HighPart=created.dwHighDateTime;}
            ULONGLONG now=GetTickCount64();
            if(!control || control->magic!=0x414d4731 || control->version!=4 || control->nonce!=(ULONGLONG)event->wParam || control->scope>1 || control->pid!=pid || control->thread!=thread || control->window!=(UINT_PTR)event->hwnd || control->started!=started.QuadPart || control->expires<=now || control->expires-now>7000 || !control->enabled || (!control->scope&&(!IsWindowVisible(event->hwnd)||IsIconic(event->hwnd)))){
                disarm();return CallNextHookEx(NULL,code,wp,lp);
            }
            if(!retained_module)GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,(LPCWSTR)GuardMessages,&retained_module);
            if(!retained_module || !install_early() || !SetWindowSubclass(event->hwnd,subclass,0x414d4752,0)){
                SetPropW(event->hwnd,L"AgentMouse.Guard.Error.v4",(HANDLE)(UINT_PTR)pointer_hook_error);
                disarm();return CallNextHookEx(NULL,code,wp,lp);
            }
            target=event->hwnd;owner_thread=thread;activation_blocks=focus_blocks=position_blocks=0;
            pointer_cached=pointer_shape_assigned=FALSE;pointer_reads=0;pointer_sequence=0;
            timer_id=SetTimer(target,0,50,NULL);if(!timer_id)disarm();
        }
    }
    return CallNextHookEx(NULL,code,wp,lp);
}
