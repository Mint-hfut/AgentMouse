using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AgentPointer {
internal sealed class Fault : Exception {
    internal readonly string Code;
    internal Fault(string code,string message):base(message){Code=code;}
}
internal static class Json {
    internal static string Write(object value){return new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(value);}
    internal static Dictionary<string,object> Read(string value,int maxLength=128*1024){
        try{var result=new JavaScriptSerializer{MaxJsonLength=maxLength,RecursionLimit=20}.Deserialize<Dictionary<string,object>>(value);if(result==null)throw new Exception();return result;}
        catch(Exception){throw new Fault("invalid_json","请求必须是有效的 JSON 对象。");}
    }
    internal static string Text(Dictionary<string,object> a,string key,string fallback){
        object v;if(!a.TryGetValue(key,out v))return fallback;
        if(!(v is string))throw new Fault("invalid_argument",key+" 必须是字符串。");return (string)v;
    }
    internal static int Number(Dictionary<string,object> a,string key,int min,int max,int? fallback=null){
        object v;if(!a.TryGetValue(key,out v)){if(fallback.HasValue)return fallback.Value;throw new Fault("invalid_argument","缺少 "+key);}
        if(!(v is int)&&!(v is long))throw new Fault("invalid_argument",key+" 必须是整数。");
        long n=Convert.ToInt64(v);if(n<min||n>max)throw new Fault("invalid_argument",key+" 超出允许范围。");return (int)n;
    }
    internal static bool Flag(Dictionary<string,object> a,string key,bool fallback){
        object v;if(!a.TryGetValue(key,out v))return fallback;
        if(!(v is bool))throw new Fault("invalid_argument",key+" 必须是布尔值。");return (bool)v;
    }
}
internal sealed class WindowItem {
    internal IntPtr Handle;
    internal uint Pid;
    internal string Name,ProcessName;
    public override string ToString(){return Name+"  ·  "+ProcessName;}
    internal object Export(){return new {windowId=Handle.ToInt64().ToString(),title=Name,process=ProcessName};}
}
internal sealed class Observation {
    internal string Token;
    internal Native.Rect Bounds;
    internal long At;
    internal List<ObservedWindow> Windows;
}
internal sealed class ObservedWindow {
    internal IntPtr Handle;
    internal uint Pid;
    internal Native.Rect Bounds;
}
internal sealed class InputTracker {
    private long last;
    private int sequence;
    private readonly object pointerLock=new object();
    private bool hasPointer;
    private Native.Point previousPointer;
    internal long Last {get{return Interlocked.Read(ref last);}}
    internal int Sequence {get{return Interlocked.CompareExchange(ref sequence,0,0);}}
    internal static long Now {get{return Stopwatch.GetTimestamp();}}
    internal static double Milliseconds(long start){return (Now-start)*1000.0/Stopwatch.Frequency;}
    internal void Mark(){Interlocked.Exchange(ref last,Now);Interlocked.Increment(ref sequence);}
    internal void TrackPointer(Native.Point point){
        lock(pointerLock){
            if(hasPointer&&(point.X!=previousPointer.X||point.Y!=previousPointer.Y))Mark();
            previousPointer=point;hasPointer=true;
        }
    }
    internal void Read(IntPtr raw){
        uint size=0,header=(uint)Marshal.SizeOf(typeof(Native.RawHeader));
        if(Native.GetRawInputData(raw,0x10000003,IntPtr.Zero,ref size,header)==uint.MaxValue||size<header||size>4096)return;
        IntPtr data=Marshal.AllocHGlobal((int)size);
        try{
            if(Native.GetRawInputData(raw,0x10000003,data,ref size,header)!=size)return;
            var h=(Native.RawHeader)Marshal.PtrToStructure(data,typeof(Native.RawHeader));
            if(h.device==IntPtr.Zero&&h.type!=1)return;
            if(h.type==0&&size>=header+24){
                IntPtr m=IntPtr.Add(data,(int)header);
                if(Marshal.ReadInt16(m,4)!=0||Marshal.ReadInt32(m,12)!=0||Marshal.ReadInt32(m,16)!=0)Mark();
            } else if(h.type==1&&size>=header+16)Mark();
        }finally{Marshal.FreeHGlobal(data);}
    }
    internal bool Busy(){
        Native.Point p;if(Native.GetCursorPos(out p))TrackPointer(p);
        if(Last!=0&&Milliseconds(Last)<650)return true;
        // Key-up may predate startup; query held keys without recording their contents.
        for(int key=1;key<255;key++)if((Native.GetAsyncKeyState(key)&0x8000)!=0)return true;
        return false;
    }
}
internal sealed class TouchBackend {
    internal volatile bool Active;
    internal Native.Point Last;
    private bool ready;
    private void Ensure(){if(!ready){if(!Native.InitializeTouchInjection(1,3))throw new Fault("touch_unavailable","系统无法初始化触摸输入："+Marshal.GetLastWin32Error());ready=true;}}
    private void Frame(Native.Point point,uint flags){
        Ensure();
        var c=new Native.TouchInfo();
        c.pointer.pointerType=2;c.pointer.pointerId=0;c.pointer.pointerFlags=flags;c.pointer.pixel=point;
        c.mask=7;c.contact=new Native.Rect{Left=point.X-2,Top=point.Y-2,Right=point.X+2,Bottom=point.Y+2};
        c.orientation=90;c.pressure=512;
        for(int attempt=0;attempt<5;attempt++){
            if(Native.InjectTouchInput(1,new[]{c})){Last=point;return;}
            int error=Marshal.GetLastWin32Error();
            if(error!=21)throw new Fault("touch_failed","触摸输入失败："+error+" (flags="+flags+")。结果可能未完成，请重新观察。");
            Thread.Sleep(2);
        }
        throw new Fault("touch_failed","触摸通道暂未就绪，请重新观察。");
    }
    internal void Down(Native.Point p){Frame(p,Native.INRANGE|Native.INCONTACT|Native.DOWN);Active=true;}
    internal void Move(Native.Point p){Frame(p,Native.INRANGE|Native.INCONTACT|Native.UPDATE);}
    internal void Up(){if(!Active)return;Frame(Last,Native.UP);Active=false;}
    internal void Cancel(){if(!Active)return;Frame(Last,Native.CANCELED|Native.UP);Active=false;}
}
internal sealed class Engine {
    internal readonly InputTracker Human=new InputTracker();
    internal readonly TouchBackend Touch=new TouchBackend();
    private readonly object sync=new object();
    private readonly object operation=new object();
    private Observation observation;
    internal volatile bool Armed,Stopped,PointerVisible=true;
    internal volatile string Mode="background";
    internal volatile string Message="已暂停。点击允许全桌面操作即可启用。";
    internal volatile string PauseShortcut="尚未注册";
    internal volatile int X,Y;
    private int cancelGeneration;
    internal IntPtr Owner;
    internal IntPtr OverlayWindow;
    internal bool ShowPointer {get{return Armed&&PointerVisible&&!Stopped;}}
    internal void Pause(string reason){Armed=false;Interlocked.Increment(ref cancelGeneration);Message=reason;}
    internal static List<WindowItem> Windows(IntPtr exclude){
        var items=new List<WindowItem>();
        Native.EnumWindows(delegate(IntPtr w,IntPtr unused){
            uint pid;Native.GetWindowThreadProcessId(w,out pid);
            if(w==exclude||pid==(uint)Process.GetCurrentProcess().Id||!Native.IsWindowVisible(w)||Native.IsIconic(w))return true;
            string title=Native.Title(w);if(title.Length==0)return true;
            Native.Rect r;if(!Native.GetWindowRect(w,out r)||r.Width<20||r.Height<20)return true;
            string process="未知程序";try{process=Process.GetProcessById((int)pid).ProcessName;}catch(ArgumentException){}
            items.Add(new WindowItem{Handle=w,Pid=pid,Name=title,ProcessName=process});return true;
        },IntPtr.Zero);return items;
    }
    internal object Status(){
        Native.Point real;bool available=Native.GetCursorPos(out real);Native.Rect bounds;object shown=null;
        if(OverlayWindow!=IntPtr.Zero&&Native.GetWindowRect(OverlayWindow,out bounds))shown=new{x=bounds.Left,y=bounds.Top,width=bounds.Width,height=bounds.Height};
        return new{armed=Armed,mode=Mode,scope="desktop",pointer=new{x=X,y=Y,visible=ShowPointer},
        systemPointer=new{x=real.X,y=real.Y,available=available},overlayBounds=shown,message=Message,pauseShortcut=PauseShortcut,
        limitations=new[]{"后台点击兼容性取决于目标程序；触摸可能移动真实指针并激活窗口。","不支持独立键盘；后台鼠标拖拽和原生悬停尚未实现。","当前同一时刻仅支持一个 Agent 控制会话。","没有 SendInput 或真实鼠标瞬移兜底。"}};}
    internal object Dispatch(Dictionary<string,object> request){
        string method=Json.Text(request,"method","");
        switch(method){
        case "status":return Status();
        case "list_windows":var ws=Windows(Owner);return ws.ConvertAll<object>(w=>w.Export());
        case "pause":Pause("Agent 已请求暂停。");return Status();
        case "observe":lock(operation){return Observe(Json.Flag(request,"screenshot",true),Json.Text(request,"windowId",""));}
        case "move":{
            var p=ReadPoint(request,"x","y");X=p.X;Y=p.Y;return new{x=X,y=Y,note="仅更新 Agent 坐标，未请求系统鼠标移动。"};
        }
        case "click":lock(operation){return Act(request,false);}
        case "drag":lock(operation){return Act(request,true);}
        case "scroll":lock(operation){return Act(request,false,true);}
        default:throw new Fault("unknown_method","未知操作："+method);
        }
    }
    private static Native.Rect Bounds(IntPtr window){Native.Rect r;if(!Native.GetWindowRect(window,out r)||r.Width<=0||r.Height<=0)throw new Fault("window_missing","无法取得窗口位置。");return r;}
    private static Native.Rect DesktopBounds(){var r=SystemInformation.VirtualScreen;return new Native.Rect{Left=r.Left,Top=r.Top,Right=r.Right,Bottom=r.Bottom};}
    private static bool SameBounds(Native.Rect a,Native.Rect b){return a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom;}
    private List<ObservedWindow> CaptureWindows(){
        var list=new List<ObservedWindow>();
        Native.EnumWindows(delegate(IntPtr w,IntPtr unused){
            if(w==OverlayWindow||!Native.IsWindowVisible(w)||Native.IsIconic(w))return true;
            long style=Native.GetWindowLongPtr(w,-20).ToInt64();int cloaked;
            if((style&0x80020)==0x80020||(Native.DwmGetWindowAttribute(w,14,out cloaked,4)==0&&cloaked!=0))return true;
            Native.Rect bounds;if(!Native.GetWindowRect(w,out bounds)||bounds.Width<=0||bounds.Height<=0)return true;
            uint pid;Native.GetWindowThreadProcessId(w,out pid);list.Add(new ObservedWindow{Handle=w,Pid=pid,Bounds=bounds});return true;
        },IntPtr.Zero);return list;
    }
    private object Observe(bool screenshot,string windowId){
        var desktop=DesktopBounds();Rectangle region=new Rectangle(desktop.Left,desktop.Top,desktop.Width,desktop.Height);object window=null;
        if(windowId!=""){
            long id;if(!long.TryParse(windowId,out id)||id<=0)throw new Fault("invalid_argument","windowId 必须是有效的窗口编号。");
            IntPtr handle=new IntPtr(id);
            if(!Native.IsWindow(handle)||!Native.IsWindowVisible(handle)||Native.IsIconic(handle))throw new Fault("window_missing","要查看的窗口已关闭、不可见或最小化。");
            var r=Bounds(handle);region=Rectangle.Intersect(region,new Rectangle(r.Left,r.Top,r.Width,r.Height));
            window=new{windowId=id.ToString(),title=Native.Title(handle)};
        }
        if(region.Width<=0||region.Height<=0)throw new Fault("offscreen","截图区域不在显示器内。");
        object picture=null;
        if(screenshot){
            double scale=Math.Min(1,Math.Sqrt(4000000.0/((long)region.Width*region.Height)));
            int width=Math.Max(1,(int)(region.Width*scale)),height=Math.Max(1,(int)(region.Height*scale));
            double scaleX=(double)region.Width/width,scaleY=(double)region.Height/height;
            using(var image=new Bitmap(width,height))using(var g=Graphics.FromImage(image))using(var stream=new MemoryStream()){
                g.Clear(Color.Black);
                foreach(Screen monitor in Screen.AllScreens){
                    var piece=Rectangle.Intersect(region,monitor.Bounds);if(piece.Width<=0||piece.Height<=0)continue;
                    using(var tile=new Bitmap(piece.Width,piece.Height))using(var tg=Graphics.FromImage(tile)){
                        tg.CopyFromScreen(piece.Left,piece.Top,0,0,piece.Size);
                        g.DrawImage(tile,new RectangleF((float)((piece.Left-region.Left)/scaleX),(float)((piece.Top-region.Top)/scaleY),(float)(piece.Width/scaleX),(float)(piece.Height/scaleY)));
                    }
                }
                image.Save(stream,ImageFormat.Png);
                picture=new{mimeType="image/png",data=Convert.ToBase64String(stream.ToArray()),x=region.Left,y=region.Top,width=width,height=height,sourceWidth=region.Width,sourceHeight=region.Height,scaleX=scaleX,scaleY=scaleY};
            }
        }
        var o=new Observation{Token=Guid.NewGuid().ToString("N"),Bounds=desktop,At=InputTracker.Now,Windows=CaptureWindows()};
        lock(sync){observation=o;}
        return new{scope="desktop",observationId=o.Token,validForMs=15000,window=window,bounds=new{x=desktop.Left,y=desktop.Top,width=desktop.Width,height=desktop.Height},screenshot=picture,
            note="全桌面操作，无窗口绑定。x/y 为屏幕物理像素；截图像素须按 screenX=screenshot.x+imageX*scaleX，screenY=screenshot.y+imageY*scaleY 换算。"};
    }
    private Observation Validate(Dictionary<string,object> a){
        Observation o;lock(sync){o=observation;}
        if(o==null||o.Token!=Json.Text(a,"observationId",""))throw new Fault("stale_observation","请先 observe，再使用返回的 observationId。");
        if(InputTracker.Milliseconds(o.At)>15000)throw new Fault("stale_observation","观察已过期，请重新观察。");
        if(!SameBounds(DesktopBounds(),o.Bounds))throw new Fault("window_changed","显示器布局已改变，请重新观察。");
        return o;
    }
    private static Native.Point ReadPoint(Dictionary<string,object> a,string x,string y){
        var p=new Native.Point(Json.Number(a,x,-100000,100000),Json.Number(a,y,-100000,100000));
        bool inside=false;foreach(Screen s in Screen.AllScreens)if(s.Bounds.Contains(p.X,p.Y)){inside=true;break;}
        if(!inside)throw new Fault("offscreen","坐标不在任何实际显示器内。");return p;
    }
    private ObservedWindow Hit(Observation o,Native.Point p){
        IntPtr hit=Native.WindowFromPoint(p);
        IntPtr root=Native.GetAncestor(hit,2);if(root==IntPtr.Zero)root=hit;
        var saved=o.Windows.Find(w=>w.Handle==root);
        uint pid;Native.GetWindowThreadProcessId(root,out pid);
        if(saved==null||saved.Pid!=pid||!SameBounds(Bounds(root),saved.Bounds))
            throw new Fault("window_changed","落点处的窗口或遮挡关系已变化，请重新观察。");
        // Compare the covering stack, so rounded/transparent corners can still hit the window underneath.
        var previous=o.Windows.FindAll(w=>w.Bounds.Contains(p.X,p.Y));
        var current=CaptureWindows().FindAll(w=>w.Bounds.Contains(p.X,p.Y));
        if(previous.Count!=current.Count)throw new Fault("window_changed","落点处的窗口或遮挡关系已变化，请重新观察。");
        for(int i=0;i<previous.Count;i++)if(previous[i].Handle!=current[i].Handle||previous[i].Pid!=current[i].Pid||!SameBounds(previous[i].Bounds,current[i].Bounds))
            throw new Fault("window_changed","落点处的窗口或遮挡关系已变化，请重新观察。");
        if(!Native.IsWindowEnabled(hit))throw new Fault("disabled","目标控件当前不可用。");
        return saved;
    }
    private void Gate(int generation){
        if(!Armed||Stopped||generation!=Interlocked.CompareExchange(ref cancelGeneration,0,0))throw new Fault("paused","操作已暂停。");
    }
    private void WaitForIdle(int generation){
        long start=InputTracker.Now;
        while(Human.Busy()){
            Gate(generation);Message="等待你的键鼠操作结束…";
            if(InputTracker.Milliseconds(start)>2500)throw new Fault("human_busy","你正在操作键鼠；本次动作没有执行。");
            Thread.Sleep(25);
        }
        Gate(generation);
    }
    private static IntPtr ChildAt(IntPtr root,Native.Point p){
        IntPtr current=root;
        for(int i=0;i<16;i++){
            var local=p;Native.ScreenToClient(current,ref local);
            IntPtr child=Native.ChildWindowFromPointEx(current,local,7);
            if(child==IntPtr.Zero||child==current)break;current=child;
        }return current;
    }
    private object Act(Dictionary<string,object> a,bool drag,bool scroll=false){
        int generation=Interlocked.CompareExchange(ref cancelGeneration,0,0);Gate(generation);
        if(Touch.Active){Pause("仍有未确认释放的触点，请退出并重新打开程序。");throw new Fault("contact_unreleased","仍有未确认释放的触点，请退出并重新打开程序。");}
        string mode=Mode;
        if(drag&&mode!="touch")throw new Fault("unsupported","后台点击通道不执行拖拽，请在界面选择触摸通道。触摸拖动与鼠标拖动的语义可能不同。");
        int delta=scroll?Json.Number(a,"delta",-1200,1200):0;
        if(scroll&&delta==0)throw new Fault("invalid_argument","delta 不能为零；负数向下滚动，正数向上。");
        string button=Json.Text(a,"button","left");
        if(button!="left"&&button!="right")throw new Fault("invalid_argument","button 只能为 left 或 right。");
        if(mode=="touch"&&button!="left")throw new Fault("unsupported","触摸通道尚不提供右键。");
        var from=ReadPoint(a,"x","y");var to=drag?ReadPoint(a,"toX","toY"):from;
        if(scroll&&mode=="touch"){
            to=ReadPoint(new Dictionary<string,object>{{"x",from.X},{"y",from.Y+delta}},"x","y");drag=true;
        }
        int duration=drag?Json.Number(a,"durationMs",100,2000,400):40;
        var o=Validate(a);Hit(o,from);Hit(o,to);
        WaitForIdle(generation);
        o=Validate(a);var target=Hit(o,from);Hit(o,to);
        if(Mode!=mode)throw new Fault("mode_changed","执行通道已更换，请重新观察。");
        lock(sync){observation=null;}
        IntPtr foreground=Native.GetForegroundWindow();Native.Point cursor;Native.GetCursorPos(out cursor);
        int humanSequence=Human.Sequence;bool delivered=false;
        IntPtr receiver=IntPtr.Zero;Native.Point receiverPoint=new Native.Point();
        X=from.X;Y=from.Y;
        try{
            Message=mode=="touch"?"正在执行触摸操作…":"正在执行后台点击…";
            if(mode=="background"){
                IntPtr child=ChildAt(target.Handle,from);var p=from;Native.ScreenToClient(child,ref p);
                receiver=child;receiverPoint=p;
                // LPARAM mouse coordinates are signed 16-bit client pixels.
                if(p.X<short.MinValue||p.X>short.MaxValue||p.Y<short.MinValue||p.Y>short.MaxValue)throw new Fault("unsupported","控件坐标超出窗口消息范围。");
                if(scroll){
                    if(from.X<short.MinValue||from.X>short.MaxValue||from.Y<short.MinValue||from.Y>short.MaxValue)throw new Fault("unsupported","滚轮坐标超出窗口消息范围。");
                    Gate(generation);if(Human.Sequence!=humanSequence)throw new Fault("human_busy","检测到你的新输入，滚动已取消。");
                    // Wheel handlers may synchronously call back into the sender; queue instead of timing out after a successful scroll.
                    Native.Post(child,Native.WHEEL,new UIntPtr(unchecked((uint)(ushort)delta<<16)),Native.Pack(from.X,from.Y));
                }else{
                    uint down=button=="left"?Native.LEFTDOWN:Native.RIGHTDOWN,up=button=="left"?Native.LEFTUP:Native.RIGHTUP;
                    Native.Send(child,Native.MOVE,UIntPtr.Zero,Native.Pack(p.X,p.Y));
                    Gate(generation);if(Human.Sequence!=humanSequence)throw new Fault("human_busy","检测到你的新输入，点击已取消。");
                    bool started=false;
                    try{started=true;Native.Send(child,down,new UIntPtr(button=="left"?1u:2u),Native.Pack(p.X,p.Y));}
                    finally{if(started){Native.Send(child,up,UIntPtr.Zero,Native.Pack(p.X,p.Y));}}
                }
                delivered=true;
            }else{
                try{
                    Touch.Down(from);
                    if(drag){
                        long start=InputTracker.Now;
                        while(true){
                            Gate(generation);
                            if(Human.Sequence!=humanSequence||Human.Busy())throw new Fault("interrupted","检测到你的输入，Agent 触摸已取消。");
                            double t=Math.Min(1,InputTracker.Milliseconds(start)/duration);
                            var point=new Native.Point((int)Math.Round(from.X+(to.X-from.X)*t),(int)Math.Round(from.Y+(to.Y-from.Y)*t));
                            if(!Native.IsWindow(target.Handle))throw new Fault("window_missing","起始窗口已经关闭。");
                            Touch.Move(point);X=point.X;Y=point.Y;
                            if(t>=1)break;Thread.Sleep(12);
                        }
                    }else{
                        Thread.Sleep(duration);Gate(generation);
                        if(Human.Sequence!=humanSequence)throw new Fault("interrupted","检测到你的输入，Agent 触摸已取消。");
                    }
                    Touch.Up();delivered=true;
                }finally{Touch.Cancel();}
            }
            Thread.Sleep(60);
            Native.Point after;Native.GetCursorPos(out after);
            bool focusChanged=Native.GetForegroundWindow()!=foreground;
            bool cursorChanged=Human.Sequence==humanSequence&&(cursor.X!=after.X||cursor.Y!=after.Y);
            bool externalInput=Human.Sequence!=humanSequence;
            if(cursorChanged||externalInput)Pause(externalInput?"检测到新的外部输入，已暂停。":"检测到系统指针位置变化，已暂停。");
            else Message=focusChanged?"输入已送达，前台窗口发生变化；请重新观察后继续。":"输入已送达；请重新观察目标窗口确认结果。";
            return new{inputDelivered=delivered,businessResultVerified=false,backend=mode,focusChanged=focusChanged,
                windowId=target.Handle.ToInt64().ToString(),receiverClass=receiver==IntPtr.Zero?null:Native.Class(receiver),receiverPoint=new{x=receiverPoint.X,y=receiverPoint.Y},
                cursorChangedWithoutHumanInput=cursorChanged,systemPointerBefore=new{x=cursor.X,y=cursor.Y},systemPointerAfter=new{x=after.X,y=after.Y},
                externalInputDuringAction=externalInput,paused=!Armed};
        }catch(Exception){Pause("动作中断，已暂停。结果可能未完成，请重新观察。");throw;}
    }
}
}
