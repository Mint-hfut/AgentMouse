/* Pointer queries are virtual only on the protected UI thread during a gesture. */
static BOOL (WINAPI *system_cursor)(LPPOINT);
static BOOL (WINAPI *system_physical_cursor)(LPPOINT);
static DWORD (WINAPI *system_message_position)(void);
static SHORT (WINAPI *system_key_state)(int);
static SHORT (WINAPI *system_async_key_state)(int);
static BOOL (WINAPI *system_keyboard_state)(PBYTE);
static HWND (WINAPI *system_window_point)(POINT);
static BOOL (WINAPI *system_set_cursor)(int,int);
static BOOL (WINAPI *system_set_physical_cursor)(int,int);
static BOOL (WINAPI *system_clip_cursor)(const RECT*);
static HCURSOR (WINAPI *system_cursor_shape)(void);
static HCURSOR (WINAPI *system_set_cursor_shape)(HCURSOR);
static BOOL (WINAPI *system_cursor_info)(PCURSORINFO);
static UINT (WINAPI *system_send_input)(UINT,LPINPUT,int);
static void (WINAPI *system_keyboard_event)(BYTE,BYTE,DWORD,ULONG_PTR);
static void (WINAPI *system_mouse_event)(DWORD,DWORD,DWORD,DWORD,ULONG_PTR);
static __thread LONG pointer_sequence;
static __thread DWORD pointer_buttons;
static __thread POINT pointer_position;
static __thread BOOL pointer_cached;
static __thread UINT pointer_reads;
static __thread HCURSOR pointer_shape;
static __thread BOOL pointer_shape_assigned;
static UINT pointer_hook_error;
static BOOL cursor_api_shared,set_cursor_api_shared;

static BOOL pointer_state(POINT *point,DWORD *buttons){
    if(!active()||!control->pointer_enabled)return FALSE;
    LONG sequence=control->pointer_sequence;
    if(!(sequence&1) && sequence!=pointer_sequence){
        POINT next={control->pointer_x,control->pointer_y};
        DWORD held=control->pointer_buttons;
        MemoryBarrier();
        if(sequence==control->pointer_sequence){
            pointer_position=next;pointer_buttons=held;pointer_sequence=sequence;
            pointer_cached=TRUE;
        }
    }
    if(!pointer_cached)return FALSE;
    *point=pointer_position;*buttons=pointer_buttons;pointer_reads++;return TRUE;
}
static BOOL WINAPI virtual_physical_cursor(LPPOINT point){
    POINT position;DWORD buttons;
    if(point&&pointer_state(&position,&buttons)){*point=position;return TRUE;}
    return system_physical_cursor(point);
}
static BOOL WINAPI virtual_cursor(LPPOINT point){
    POINT position;DWORD buttons;
    if(point&&pointer_state(&position,&buttons)){
        if(!cursor_api_shared)PhysicalToLogicalPointForPerMonitorDPI(target,&position);*point=position;return TRUE;
    }
    return system_cursor(point);
}
static DWORD WINAPI virtual_message_position(void){
    POINT position;DWORD buttons;
    if(pointer_state(&position,&buttons)){
        PhysicalToLogicalPointForPerMonitorDPI(target,&position);
        return MAKELONG(position.x,position.y);
    }
    return system_message_position();
}
static SHORT virtual_button_state(int key,BOOL asynchronous){
    POINT position;DWORD buttons;
    if((key==VK_SHIFT||key==VK_CONTROL||key==VK_MENU||key==VK_LSHIFT||key==VK_RSHIFT||key==VK_LCONTROL||key==VK_RCONTROL||key==VK_LMENU||key==VK_RMENU||key==VK_LWIN||key==VK_RWIN)&&pointer_state(&position,&buttons))return 0;
    if((key==VK_LBUTTON||key==VK_RBUTTON||key==VK_MBUTTON)&&pointer_state(&position,&buttons)){
        DWORD mask=key==VK_LBUTTON?1:key==VK_RBUTTON?2:4;
        return (buttons&mask)?(SHORT)0x8000:0;
    }
    return asynchronous?system_async_key_state(key):system_key_state(key);
}
static SHORT WINAPI virtual_key_state(int key){return virtual_button_state(key,FALSE);}
static SHORT WINAPI virtual_async_key_state(int key){return virtual_button_state(key,TRUE);}
static BOOL WINAPI virtual_keyboard_state(PBYTE keys){
    BOOL result=system_keyboard_state(keys);POINT position;DWORD buttons;
    if(result&&keys&&pointer_state(&position,&buttons)){
        keys[VK_LBUTTON]=(buttons&1)?0x80:0;keys[VK_RBUTTON]=(buttons&2)?0x80:0;keys[VK_MBUTTON]=(buttons&4)?0x80:0;
        const int modifiers[]={VK_SHIFT,VK_CONTROL,VK_MENU,VK_LSHIFT,VK_RSHIFT,VK_LCONTROL,VK_RCONTROL,VK_LMENU,VK_RMENU,VK_LWIN,VK_RWIN};
        for(UINT i=0;i<sizeof(modifiers)/sizeof(modifiers[0]);i++)keys[modifiers[i]]=0;
    }
    return result;
}
typedef struct {POINT point;HWND found;} HIT_CONTEXT;
static BOOL CALLBACK pointer_window(HWND window,LPARAM parameter){
    HIT_CONTEXT *hit=(HIT_CONTEXT*)parameter;
    DWORD pid=0;
    if(GetWindowThreadProcessId(window,&pid)!=GetCurrentThreadId()||pid!=GetCurrentProcessId()||!IsWindowVisible(window)||IsIconic(window))return TRUE;
    RECT bounds;GetWindowRect(window,&bounds);
    if(PtInRect(&bounds,hit->point)){hit->found=window;return FALSE;}
    return TRUE;
}
static HWND WINAPI virtual_window_point(POINT point){
    POINT position;DWORD buttons;
    if(pointer_state(&position,&buttons)){
        PhysicalToLogicalPointForPerMonitorDPI(target,&position);
        if(point.x==position.x&&point.y==position.y){
            HIT_CONTEXT hit={point,NULL};EnumWindows(pointer_window,(LPARAM)&hit);
            if(hit.found){
                HWND window=hit.found;
                for(int depth=0;depth<16;depth++){
                    POINT local=point;ScreenToClient(window,&local);
                    HWND child=ChildWindowFromPointEx(window,local,CWP_SKIPINVISIBLE|CWP_SKIPDISABLED|CWP_SKIPTRANSPARENT);
                    if(!child||child==window)break;window=child;
                }
                return window;
            }
        }
    }
    return system_window_point(point);
}
static BOOL set_virtual_position(int x,int y,BOOL physical){
    POINT position;DWORD buttons;
    if(pointer_state(&position,&buttons)){
        position.x=x;position.y=y;
        if(!physical)LogicalToPhysicalPointForPerMonitorDPI(target,&position);
        pointer_position=position;return TRUE;
    }
    if(active()){SetLastError(ERROR_ACCESS_DENIED);return FALSE;}
    return physical?system_set_physical_cursor(x,y):system_set_cursor(x,y);
}
static BOOL WINAPI virtual_set_cursor(int x,int y){return set_virtual_position(x,y,set_cursor_api_shared);}
static BOOL WINAPI virtual_set_physical_cursor(int x,int y){return set_virtual_position(x,y,TRUE);}
static BOOL WINAPI virtual_clip_cursor(const RECT *rect){
    if(active())return TRUE;
    return system_clip_cursor(rect);
}
static HCURSOR WINAPI virtual_cursor_shape(void){
    POINT position;DWORD buttons;
    if(pointer_state(&position,&buttons)&&pointer_shape_assigned)return pointer_shape;
    return system_cursor_shape();
}
static HCURSOR WINAPI virtual_set_cursor_shape(HCURSOR shape){
    POINT position;DWORD buttons;
    if(pointer_state(&position,&buttons)){
        HCURSOR previous=pointer_shape_assigned?pointer_shape:system_cursor_shape();
        pointer_shape=shape;pointer_shape_assigned=TRUE;return previous;
    }
    return system_set_cursor_shape(shape);
}
static BOOL WINAPI virtual_cursor_info(PCURSORINFO info){
    BOOL result=system_cursor_info(info);POINT position;DWORD buttons;
    if(result&&info&&pointer_state(&position,&buttons)){
        info->ptScreenPos=position;
        if(pointer_shape_assigned){info->hCursor=pointer_shape;info->flags=pointer_shape?CURSOR_SHOWING:0;}
    }
    return result;
}
static UINT WINAPI guarded_send_input(UINT count,LPINPUT input,int size){
    if(active()){SetLastError(ERROR_ACCESS_DENIED);return 0;}
    return system_send_input(count,input,size);
}
static void WINAPI guarded_keyboard_event(BYTE key,BYTE scan,DWORD flags,ULONG_PTR extra){
    if(!active())system_keyboard_event(key,scan,flags,extra);
}
static void WINAPI guarded_mouse_event(DWORD flags,DWORD x,DWORD y,DWORD data,ULONG_PTR extra){
    if(!active())system_mouse_event(flags,x,y,data,extra);
}
static BOOL install_pointer_hooks(void){
#define POINTER_HOOK(name,replacement,original) { MH_STATUS status=MH_CreateHookApi(L"user32",name,(LPVOID)replacement,(LPVOID*)&original); if(status!=MH_OK){pointer_hook_error=__LINE__*100+(UINT)status;return FALSE;} }
    POINTER_HOOK("GetCursorPos",virtual_cursor,system_cursor);
    HMODULE user=GetModuleHandleW(L"user32");
    cursor_api_shared=GetProcAddress(user,"GetCursorPos")==GetProcAddress(user,"GetPhysicalCursorPos");
    if(cursor_api_shared)system_physical_cursor=system_cursor;
    else {POINTER_HOOK("GetPhysicalCursorPos",virtual_physical_cursor,system_physical_cursor);}
    POINTER_HOOK("GetMessagePos",virtual_message_position,system_message_position);
    POINTER_HOOK("GetKeyState",virtual_key_state,system_key_state);
    POINTER_HOOK("GetAsyncKeyState",virtual_async_key_state,system_async_key_state);
    POINTER_HOOK("GetKeyboardState",virtual_keyboard_state,system_keyboard_state);
    POINTER_HOOK("WindowFromPoint",virtual_window_point,system_window_point);
    POINTER_HOOK("SetCursorPos",virtual_set_cursor,system_set_cursor);
    set_cursor_api_shared=GetProcAddress(user,"SetCursorPos")==GetProcAddress(user,"SetPhysicalCursorPos");
    if(set_cursor_api_shared)system_set_physical_cursor=system_set_cursor;
    else {POINTER_HOOK("SetPhysicalCursorPos",virtual_set_physical_cursor,system_set_physical_cursor);}
    POINTER_HOOK("ClipCursor",virtual_clip_cursor,system_clip_cursor);
    POINTER_HOOK("GetCursor",virtual_cursor_shape,system_cursor_shape);
    POINTER_HOOK("SetCursor",virtual_set_cursor_shape,system_set_cursor_shape);
    POINTER_HOOK("GetCursorInfo",virtual_cursor_info,system_cursor_info);
    POINTER_HOOK("SendInput",guarded_send_input,system_send_input);
    POINTER_HOOK("keybd_event",guarded_keyboard_event,system_keyboard_event);
    POINTER_HOOK("mouse_event",guarded_mouse_event,system_mouse_event);
#undef POINTER_HOOK
    return TRUE;
}
