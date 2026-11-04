using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentPointer {
internal static class Native {
    internal const uint MOVE=0x200, LEFTDOWN=0x201, LEFTUP=0x202, RIGHTDOWN=0x204, RIGHTUP=0x205;
    internal const uint LEFTDOUBLE=0x203,RIGHTDOUBLE=0x206;
    internal const uint WHEEL=0x20a, CANCELMODE=0x1f;
    internal const uint INRANGE=2, INCONTACT=4, DOWN=0x10000, UPDATE=0x20000, UP=0x40000, CANCELED=0x8000;
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; public Point(int x,int y){X=x;Y=y;} }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect {
        public int Left,Top,Right,Bottom;
        public int Width { get {return Right-Left;} } public int Height { get {return Bottom-Top;} }
        public bool Contains(int x,int y){return x>=Left&&x<Right&&y>=Top&&y<Bottom;}
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PointerInfo {
        public uint pointerType,pointerId,frameId,pointerFlags;
        public IntPtr sourceDevice,hwndTarget;
        public Point pixel,himetric,pixelRaw,himetricRaw;
        public uint time,historyCount;
        public int inputData;
        public uint keyStates;
        public ulong performanceCount;
        public uint buttonChangeType;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct TouchInfo {
        public PointerInfo pointer;
        public uint flags,mask;
        public Rect contact,contactRaw;
        public uint orientation,pressure;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RawDevice {
        public ushort page,usage;
        public uint flags;
        public IntPtr target;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct RawHeader {
        public uint type,size;
        public IntPtr device,wParam;
    }
    internal delegate bool EnumCallback(IntPtr window,IntPtr data);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr w,StringBuilder text,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr w,StringBuilder text,int count);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr w);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr w);
    [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr w);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr w);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr w,out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr w,out Rect rect);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr w,ref Point point);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr w,ref Point point);
    [DllImport("user32.dll")] internal static extern IntPtr ChildWindowFromPointEx(IntPtr parent,Point point,uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr w,uint flags);
    [DllImport("user32.dll")] internal static extern bool IsChild(IntPtr parent,IntPtr child);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    // Keep pointer observations in physical pixels even across callers with different DPI contexts.
    [DllImport("user32.dll",EntryPoint="GetPhysicalCursorPos")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    internal static extern IntPtr SendMessageTimeout(IntPtr w,uint message,UIntPtr wp,IntPtr lp,uint flags,uint timeout,out UIntPtr result);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool PostMessage(IntPtr w,uint message,UIntPtr wp,IntPtr lp);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool InitializeTouchInjection(uint contacts,uint mode);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool InjectTouchInput(uint count,[In] TouchInfo[] contacts);
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
    [DllImport("user32.dll")] internal static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint headerSize);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr w,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr w,int id);
    internal static string Title(IntPtr w){var b=new StringBuilder(512);GetWindowText(w,b,b.Capacity);return b.ToString();}
    internal static string Class(IntPtr w){var b=new StringBuilder(256);GetClassName(w,b,b.Capacity);return b.ToString();}
    internal static IntPtr Pack(int x,int y){return new IntPtr(unchecked((int)(((uint)(ushort)y<<16)|(ushort)x)));}
    internal static void Send(IntPtr w,uint msg,UIntPtr wp,IntPtr lp){
        UIntPtr result;
        if(SendMessageTimeout(w,msg,wp,lp,2,200,out result)==IntPtr.Zero)
            throw new Fault("delivery_failed","目标窗口没有及时接收输入（系统错误 "+Marshal.GetLastWin32Error()+"，消息 "+msg+"）。请重新观察后判断结果，不要直接重试。");
    }
    internal static void Post(IntPtr w,uint msg,UIntPtr wp,IntPtr lp){
        if(!PostMessage(w,msg,wp,lp))throw new Fault("delivery_failed","窗口消息未能排队："+Marshal.GetLastWin32Error());
    }
    internal static void DpiAware(){try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}}
}
}
