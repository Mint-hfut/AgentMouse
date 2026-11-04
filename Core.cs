using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
    internal int ZOrder;
    public override string ToString(){return Name+"  ·  "+ProcessName;}
    internal object Export(){
        Native.Rect r;Native.GetWindowRect(Handle,out r);int cloaked;bool hidden=Native.DwmGetWindowAttribute(Handle,14,out cloaked,4)==0&&cloaked!=0;
        bool visible=Native.IsWindowVisible(Handle),minimized=Native.IsIconic(Handle);
        var covering=new List<string>();
        if(visible&&!minimized&&!hidden){
            IntPtr above=Native.GetWindow(Handle,3);
            for(int i=0;above!=IntPtr.Zero&&i<1024;i++,above=Native.GetWindow(above,3)){
                Native.Rect b;int c;long style=Native.GetWindowLongPtr(above,-20).ToInt64();
                if(!Native.IsWindowVisible(above)||Native.IsIconic(above)||(style&0x80020)==0x80020||(Native.DwmGetWindowAttribute(above,14,out c,4)==0&&c!=0))continue;
                if(Native.GetWindowRect(above,out b)&&b.Right>r.Left&&b.Left<r.Right&&b.Bottom>r.Top&&b.Top<r.Bottom)covering.Add(above.ToInt64().ToString());
            }
        }
        return new {windowId=Handle.ToInt64().ToString(),title=Name,process=ProcessName,pid=Pid,className=Native.Class(Handle),ownerWindowId=Native.GetWindow(Handle,4).ToInt64().ToString(),zOrder=ZOrder,zOrderDirection="front-to-back",foreground=Native.GetForegroundWindow()==Handle,visible=visible,minimized=minimized,cloaked=hidden,canCapture=visible&&!minimized&&!hidden,automationQuarantined=DesktopInventory.Quarantined(ProcessName),bounds=new{x=r.Left,y=r.Top,width=r.Width,height=r.Height},overlappingWindowsAbove=covering,occlusionAssessment="bounding-rectangle overlap; transparent/shaped windows may differ"};
    }
}
internal sealed class Observation {
    internal string Token;
    internal Native.Rect Bounds;
    internal long At;
    internal List<ObservedWindow> Windows;
    internal ObservedWindow Target;
    internal long FrameTimestamp;
}
internal sealed class CaptureImage {
    public string mimeType,data,captureMethod;
    public int x,y,width,height,sourceWidth,sourceHeight;
    public double scaleX,scaleY;
    public long frameTimestamp100ns;
    public bool frameChanged;
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
    private readonly Dictionary<IntPtr,Tuple<uint,long>> actionFrames=new Dictionary<IntPtr,Tuple<uint,long>>();
    private Task<object> windowCapture;
    internal volatile bool Armed,Stopped,PointerVisible=true;
    internal volatile bool MessageButtonHeld;
    internal volatile string Mode="background";
    internal volatile string Message="已暂停。点击允许全桌面操作即可启用。";
    internal volatile string PauseShortcut="尚未注册";
    internal volatile int X,Y;
    private int cancelGeneration;
    internal IntPtr Owner;
    internal IntPtr OverlayWindow;
    internal IntPtr FixtureWindow;
    internal bool ShowPointer {get{return Armed&&PointerVisible&&!Stopped;}}
    internal void Pause(string reason){Armed=false;Interlocked.Increment(ref cancelGeneration);Message=reason;if(windowCapture!=null)WindowCapture.Reset();}
    internal static List<WindowItem> Windows(IntPtr exclude){
        var items=new List<WindowItem>();
        Native.EnumWindows(delegate(IntPtr w,IntPtr unused){
            uint pid;Native.GetWindowThreadProcessId(w,out pid);
            if(w==exclude)return true;
            if((Native.GetWindowLongPtr(w,-20).ToInt64()&0x80020)==0x80020)return true;
            string title=Native.Title(w);if(title.Length==0&&!Native.IsWindowVisible(w))return true;
            Native.Rect r;if(!Native.GetWindowRect(w,out r)||(!Native.IsIconic(w)&&(r.Width<20||r.Height<20)))return true;
            string process="未知程序";try{process=Process.GetProcessById((int)pid).ProcessName;}catch(ArgumentException){}
            items.Add(new WindowItem{Handle=w,Pid=pid,Name=title,ProcessName=process,ZOrder=items.Count});return true;
        },IntPtr.Zero);return items;
    }
    internal object Status(){
        Native.Point real;bool available=Native.GetCursorPos(out real);Native.Rect bounds;object shown=null;
        if(OverlayWindow!=IntPtr.Zero&&Native.GetWindowRect(OverlayWindow,out bounds))shown=new{x=bounds.Left,y=bounds.Top,width=bounds.Width,height=bounds.Height};
        return new{controllerVersion="0.7.0-beta.4",backgroundWindowCapture=true,windowIdentityGuard=true,targetQuarantine=true,postActionFrameGuard=true,windowMessageGestures=true,foregroundWindowId=Native.GetForegroundWindow().ToInt64().ToString(),windowZOrder=CaptureWindows().ConvertAll<string>(w=>w.Handle.ToInt64().ToString()),humanInputSequence=Human.Sequence,backgroundButtonHeld=MessageButtonHeld,armed=Armed,mode=Mode,scope="desktop",pointer=new{x=X,y=Y,visible=ShowPointer},
        systemPointer=new{x=real.X,y=real.Y,available=available},overlayBounds=shown,message=Message,pauseShortcut=PauseShortcut,
        limitations=new[]{"后台消息点击和拖动的兼容性取决于目标程序；触摸可能影响真实指针和焦点。","不支持独立键盘、跨窗口系统拖放和独立悬停；后台滚轮需改用滚动条点击。","当前同一时刻仅支持一个 Agent 控制会话。","没有 SendInput 或真实鼠标瞬移兜底。"}};}
    internal object Dispatch(Dictionary<string,object> request){
        string method=Json.Text(request,"method","");
        switch(method){
        case "status":return Status();
        case "list_windows":var ws=Windows(Owner);return ws.ConvertAll<object>(w=>w.Export());
        case "pause":Pause("Agent 已请求暂停。");return Status();
        case "release_capture":if(windowCapture!=null)WindowCapture.Reset();return Status();
        case "observe":lock(operation){return Observe(Json.Flag(request,"screenshot",true),Json.Text(request,"windowId",""),Json.Text(request,"capture","desktop"));}
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
    private object Observe(bool screenshot,string windowId,string capture="desktop"){
        lock(sync){observation=null;}
        if(capture!="desktop"&&capture!="window")throw new Fault("invalid_argument","capture 必须为 desktop 或 window。");
        if(windowId!=""&&capture=="desktop")throw new Fault("invalid_argument","提供 windowId 时必须使用 capture=window，不能把前台屏幕裁剪误认为后台画面。");
        if(capture=="window"&&windowId=="")throw new Fault("invalid_argument","后台窗口采集需要 windowId；这只是本次观察的地址，不是长期绑定。");
        if(capture=="window"&&!screenshot)throw new Fault("invalid_argument","后台窗口操作必须先取得真实后台截图，不能跳过画面采集。");
        var desktop=DesktopBounds();Rectangle region=new Rectangle(desktop.Left,desktop.Top,desktop.Width,desktop.Height);object window=null;
        ObservedWindow target=null;
        if(windowId!=""){
            long id;if(!long.TryParse(windowId,out id)||id<=0)throw new Fault("invalid_argument","windowId 必须是有效的窗口编号。");
            IntPtr handle=new IntPtr(id);
            uint observedPid;Native.GetWindowThreadProcessId(handle,out observedPid);if(observedPid!=0)DesktopInventory.RequireAutomationAllowed(observedPid);
            if(!Native.IsWindow(handle)||!Native.IsWindowVisible(handle)||Native.IsIconic(handle))throw new Fault("window_missing","要查看的窗口已关闭、不可见或最小化。");
            var r=Bounds(handle);region=Rectangle.Intersect(region,new Rectangle(r.Left,r.Top,r.Width,r.Height));
            var listed=Windows(Owner).Find(w=>w.Handle==handle);if(listed==null)throw new Fault("window_missing","目标窗口不在当前窗口清单内，请重新列出窗口。");window=listed.Export();
            if(capture=="window"){
                if(Native.GetAncestor(handle,2)!=handle)throw new Fault("invalid_argument","windowId 必须来自可见顶层窗口列表。");
                uint pid;Native.GetWindowThreadProcessId(handle,out pid);target=new ObservedWindow{Handle=handle,Pid=pid,Bounds=r};
            }
        }
        if(region.Width<=0||region.Height<=0)throw new Fault("offscreen","截图区域不在显示器内。");
        if(target!=null)ValidateTarget(target);
        IntPtr captureForeground=Native.GetForegroundWindow();bool coveredBefore=target!=null&&Above(captureForeground,target.Handle);
        object picture=null;
        if(screenshot&&target!=null){
            if(windowCapture!=null&&!windowCapture.IsCompleted)throw new Fault("capture_busy","上次后台画面采集尚未结束，请稍后再观察。");
            windowCapture=Task.Run(()=>WindowCapture.Capture(target.Handle,target.Bounds));
            try{if(!windowCapture.Wait(3000))throw new Fault("capture_timeout","后台画面采集超时；没有激活目标窗口。");picture=windowCapture.Result;}
            catch(AggregateException error){throw error.GetBaseException();}
        }else if(screenshot){
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
        if(target!=null)ValidateTarget(target);
        var captured=picture as CaptureImage;
        var o=new Observation{Token=Guid.NewGuid().ToString("N"),Bounds=desktop,At=InputTracker.Now,Windows=CaptureWindows(),Target=target,FrameTimestamp=captured==null?0:captured.frameTimestamp100ns};
        if(target!=null&&(Native.GetForegroundWindow()!=captureForeground||(coveredBefore&&Above(target.Handle,captureForeground)))){
            Pause("后台画面采集期间前台窗口或层级发生变化，已暂停。");throw new Fault("capture_interference","采集期间焦点或层级发生变化，没有把本次画面当成无干扰操作。");
        }
        lock(sync){observation=o;}
        return new{scope=target==null?"desktop":"window",capture=capture,observationId=o.Token,validForMs=15000,window=window,inputAllowed=FreshAfterAction(o),foregroundWindowId=Native.GetForegroundWindow().ToInt64().ToString(),bounds=new{x=desktop.Left,y=desktop.Top,width=desktop.Width,height=desktop.Height},screenshot=picture,
            note=target==null?"桌面落点路由。windowId 仅裁剪屏幕截图。":"本次观察按窗口句柄采集和操作，允许被遮挡；不切到前台。x/y 仍为屏幕物理像素，请按截图原点和 scaleX/scaleY 换算。"};
    }
    private Observation Validate(Dictionary<string,object> a){
        Observation o;lock(sync){o=observation;}
        if(o==null||o.Token!=Json.Text(a,"observationId",""))throw new Fault("stale_observation","请先 observe，再使用返回的 observationId。");
        if(InputTracker.Milliseconds(o.At)>15000)throw new Fault("stale_observation","观察已过期，请重新观察。");
        if(!SameBounds(DesktopBounds(),o.Bounds))throw new Fault("window_changed","显示器布局已改变，请重新观察。");
        return o;
    }
    private bool FreshAfterAction(Observation o){
        Tuple<uint,long> previous;
        return o.Target==null||!actionFrames.TryGetValue(o.Target.Handle,out previous)||FrameAdvanced(o.Target.Pid,o.FrameTimestamp,previous);
    }
    internal static bool FrameAdvanced(uint pid,long timestamp,Tuple<uint,long> previous){return previous.Item1!=pid||timestamp>previous.Item2;}
    private static Native.Point ReadPoint(Dictionary<string,object> a,string x,string y){
        var p=new Native.Point(Json.Number(a,x,-100000,100000),Json.Number(a,y,-100000,100000));
        bool inside=false;foreach(Screen s in Screen.AllScreens)if(s.Bounds.Contains(p.X,p.Y)){inside=true;break;}
        if(!inside)throw new Fault("offscreen","坐标不在任何实际显示器内。");return p;
    }
    private ObservedWindow Hit(Observation o,Native.Point p){
        if(o.Target!=null){ValidateTarget(o.Target);if(!o.Target.Bounds.Contains(p.X,p.Y))throw new Fault("offscreen","坐标超出本次后台窗口观察的范围。");return o.Target;}
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
    private static void ValidateTarget(ObservedWindow target){
        uint pid;Native.GetWindowThreadProcessId(target.Handle,out pid);
        if(!Native.IsWindow(target.Handle)||!Native.IsWindowVisible(target.Handle)||Native.IsIconic(target.Handle)||pid!=target.Pid||!SameBounds(Bounds(target.Handle),target.Bounds))throw new Fault("window_changed","后台目标窗口已关闭、移动、最小化或发生变化，请重新观察。");
        DesktopInventory.RequireAutomationAllowed(pid);
        if(!Native.IsWindowEnabled(target.Handle))throw new Fault("disabled","后台窗口当前不可用，请处理其对话框。");
        int cloaked;if(Native.DwmGetWindowAttribute(target.Handle,14,out cloaked,4)==0&&cloaked!=0)throw new Fault("window_changed","后台窗口被系统隐藏，请在原桌面恢复可采集状态。");
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
    private static IntPtr ChildAt(IntPtr root,Native.Point p,bool backgroundWindow=false){
        IntPtr current=root;
        for(int i=0;i<16;i++){
            var local=p;Native.ScreenToClient(current,ref local);
            IntPtr child=Native.ChildWindowFromPointEx(current,local,backgroundWindow?3u:7u);
            if(child==IntPtr.Zero||child==current)break;current=child;
        }return current;
    }
    private static bool Above(IntPtr first,IntPtr second){
        bool result=false;Native.EnumWindows(delegate(IntPtr window,IntPtr unused){if(window==first){result=true;return false;}return window!=second;},IntPtr.Zero);return first!=second&&result;
    }
    private static List<Native.Point> GesturePath(Dictionary<string,object> request,Native.Point from,Native.Point to){
        var result=new List<Native.Point>{from};object value;
        if(request.TryGetValue("path",out value)){
            var points=value as System.Collections.IList;if(points==null||points.Count>128)throw new Fault("invalid_argument","path 必须是最多 128 个中间坐标的数组。");
            foreach(object item in points){var row=item as Dictionary<string,object>;if(row==null||row.Count!=2||!row.ContainsKey("x")||!row.ContainsKey("y"))throw new Fault("invalid_argument","path 每个坐标只能包含整数 x、y。");result.Add(ReadPoint(row,"x","y"));}
        }
        result.Add(to);return result;
    }
    private static Native.Point ClientPoint(IntPtr receiver,Native.Point screen){
        if(!Native.ScreenToClient(receiver,ref screen)||screen.X<short.MinValue||screen.X>short.MaxValue||screen.Y<short.MinValue||screen.Y>short.MaxValue)throw new Fault("unsupported","控件坐标超出窗口消息范围。");return screen;
    }
    private void MessageGesture(IntPtr receiver,ObservedWindow target,List<Native.Point> path,bool drag,int duration,int count,int hold,string button,int generation,int humanSequence,IntPtr foreground){
        uint receiverPid;Native.GetWindowThreadProcessId(receiver,out receiverPid);
        bool wasBehind=Above(foreground,target.Handle);
        foreach(var point in path)ClientPoint(receiver,point);
        var cumulative=new double[path.Count];for(int i=1;i<path.Count;i++){double dx=path[i].X-path[i-1].X,dy=path[i].Y-path[i-1].Y;cumulative[i]=cumulative[i-1]+Math.Sqrt(dx*dx+dy*dy);}
        uint down=button=="left"?Native.LEFTDOWN:Native.RIGHTDOWN,up=button=="left"?Native.LEFTUP:Native.RIGHTUP,twice=button=="left"?Native.LEFTDOUBLE:Native.RIGHTDOUBLE;
        uint pressed=button=="left"?1u:2u;var last=ClientPoint(receiver,path[0]);
        Native.Send(receiver,Native.MOVE,UIntPtr.Zero,Native.Pack(last.X,last.Y));
        for(int click=0;click<count;click++){
            Gate(generation);if(Human.Sequence!=humanSequence)throw new Fault("human_busy","检测到你的新输入，本次手势已取消。");
            ValidateTarget(target);if(Native.GetForegroundWindow()!=foreground||(wasBehind&&Above(target.Handle,foreground)))throw new Fault("foreground_interference","前台或窗口层级变化，已取消后续手势。");
            bool started=false;
            try{
                started=true;MessageButtonHeld=true;Native.Send(receiver,click==0?down:twice,new UIntPtr(pressed),Native.Pack(last.X,last.Y));
                long start=InputTracker.Now;int span=drag?duration:hold;
                while(span>0){
                    Gate(generation);if(Human.Sequence!=humanSequence)throw new Fault("interrupted","检测到你的输入，后台手势已取消并释放按键。");
                    ValidateTarget(target);uint pid;Native.GetWindowThreadProcessId(receiver,out pid);
                    if(!Native.IsWindow(receiver)||pid!=receiverPid)throw new Fault("window_changed","手势接收控件已经变化。");
                    if(Native.GetForegroundWindow()!=foreground||(wasBehind&&Above(target.Handle,foreground)))throw new Fault("foreground_interference","手势期间前台或层级变化，已取消并释放。");
                    double fraction=Math.Min(1,InputTracker.Milliseconds(start)/span);
                    if(drag){
                        double distance=fraction*cumulative[cumulative.Length-1];int segment=1;while(segment<cumulative.Length-1&&cumulative[segment]<distance)segment++;
                        double length=cumulative[segment]-cumulative[segment-1],t=length<=0?1:(distance-cumulative[segment-1])/length;
                        var point=new Native.Point((int)Math.Round(path[segment-1].X+(path[segment].X-path[segment-1].X)*t),(int)Math.Round(path[segment-1].Y+(path[segment].Y-path[segment-1].Y)*t));
                        last=ClientPoint(receiver,point);Native.Send(receiver,Native.MOVE,new UIntPtr(pressed),Native.Pack(last.X,last.Y));X=point.X;Y=point.Y;
                    }
                    if(fraction>=1)break;Thread.Sleep(12);
                }
            }finally{
                if(started){
                    uint pid;Native.GetWindowThreadProcessId(receiver,out pid);
                    if(!Native.IsWindow(receiver)||pid!=receiverPid)MessageButtonHeld=false;
                    else try{Native.Send(receiver,up,UIntPtr.Zero,Native.Pack(last.X,last.Y));MessageButtonHeld=false;}
                    catch{Pause("后台按键释放未确认，请手动取消目标操作后重开控制器。");try{Native.Post(receiver,Native.CANCELMODE,UIntPtr.Zero,IntPtr.Zero);}catch{}throw new Fault("button_release_unconfirmed","按键释放未确认，禁止继续输入；请手动取消目标操作后重开控制器。");}
                }
            }
            if(click+1<count)Thread.Sleep(40);
        }
    }
    private object Act(Dictionary<string,object> a,bool drag,bool scroll=false){
        int generation=Interlocked.CompareExchange(ref cancelGeneration,0,0);Gate(generation);
        if(Touch.Active||MessageButtonHeld){Pause("仍有未确认释放的按键或触点，请手动取消目标操作。");throw new Fault("contact_unreleased","仍有未确认释放的按键或触点，请手动取消目标操作后重开控制器。");}
        string mode=Mode;
        int count=!drag&&!scroll?Json.Number(a,"clickCount",1,2,1):1,hold=!drag&&!scroll?Json.Number(a,"holdMs",0,2000,0):0;
        if(count>1&&hold>0)throw new Fault("invalid_argument","双击不能同时设置长按。");
        if(mode=="touch"&&(count>1||hold>0||a.ContainsKey("path")))throw new Fault("unsupported","双击、长按和路径拖动仅支持后台消息模式。");
        int delta=scroll?Json.Number(a,"delta",-1200,1200):0;
        if(scroll&&delta==0)throw new Fault("invalid_argument","delta 不能为零；负数向下滚动，正数向上。");
        string button=Json.Text(a,"button","left");
        if(button!="left"&&button!="right")throw new Fault("invalid_argument","button 只能为 left 或 right。");
        if(mode=="touch"&&button!="left")throw new Fault("unsupported","触摸通道尚不提供右键。");
        var from=ReadPoint(a,"x","y");var to=drag?ReadPoint(a,"toX","toY"):from;
        if(scroll&&mode=="touch"){
            to=ReadPoint(new Dictionary<string,object>{{"x",from.X},{"y",from.Y+delta}},"x","y");drag=true;
        }
        int duration=drag?Json.Number(a,"durationMs",100,mode=="touch"?2000:5000,400):40;
        var path=drag?GesturePath(a,from,to):new List<Native.Point>{from,to};
        var o=Validate(a);
        if(!FreshAfterAction(o))throw new Fault("unverified_frame","上次操作后，目标仍只返回旧画面。已阻止重复点击；请检查控件树或等待新帧，不得改点前台窗口或重新启动应用。");
        if(o.Target!=null&&mode!="background")throw new Fault("unsupported","被遮挡窗口仅支持后台消息通道；不会使用触摸或置前窗口兜底。");
        if(o.Target!=null&&scroll)throw new Fault("unsupported","后台窗口不发送可能按前台落点重定向的滚轮消息。请根据后台截图，使用 pointer_click 点击滚动条或翻页按钮。");
        var initialTarget=Hit(o,from);DesktopInventory.RequireAutomationAllowed(initialTarget.Pid);foreach(var point in path)if(mode=="background"&&Hit(o,point).Handle!=initialTarget.Handle)throw new Fault("unsupported","后台拖动仅支持同一窗口内部的手势，不执行跨窗口系统拖放。");
        WaitForIdle(generation);
        o=Validate(a);var target=Hit(o,from);Hit(o,to);
        string expected=Json.Text(a,"windowId",target.Handle.ToInt64().ToString());
        if(expected!=target.Handle.ToInt64().ToString())throw new Fault("wrong_target","声明的目标窗口与观察/落点窗口不一致，未发送输入。请重新列出并观察目标窗口。");
        if(FixtureWindow!=IntPtr.Zero&&target.Handle!=FixtureWindow)throw new Fault("unsupported","隔离测试仅允许操作自建测试窗口。");
        if(mode=="background"&&Native.GetForegroundWindow()!=target.Handle&&Native.Class(target.Handle).StartsWith("Chrome_",StringComparison.Ordinal))throw new Fault("browser_channel_required","该 Chromium 窗口可能主动激活或置前。后台网页操作请使用 Browser Bridge 的 browser: 标签页，不能回退为原生鼠标消息。");
        if(Mode!=mode)throw new Fault("mode_changed","执行通道已更换，请重新观察。");
        lock(sync){observation=null;}
        IntPtr foreground=Native.GetForegroundWindow();Native.Point cursor;Native.GetCursorPos(out cursor);
        bool behindForeground=o.Target!=null&&Above(foreground,target.Handle);
        int humanSequence=Human.Sequence;bool delivered=false;
        IntPtr receiver=IntPtr.Zero;Native.Point receiverPoint=new Native.Point();
        X=from.X;Y=from.Y;
        try{
            if(o.Target!=null){if(actionFrames.Count>=64)actionFrames.Clear();actionFrames[target.Handle]=Tuple.Create(target.Pid,o.FrameTimestamp);}
            Message=mode=="touch"?"正在执行触摸操作…":"正在执行后台点击…";
            if(mode=="background"){
                IntPtr child=ChildAt(target.Handle,from,o.Target!=null);var p=ClientPoint(child,from);
                receiver=child;receiverPoint=p;
                // LPARAM mouse coordinates are signed 16-bit client pixels.
                if(p.X<short.MinValue||p.X>short.MaxValue||p.Y<short.MinValue||p.Y>short.MaxValue)throw new Fault("unsupported","控件坐标超出窗口消息范围。");
                if(scroll){
                    if(from.X<short.MinValue||from.X>short.MaxValue||from.Y<short.MinValue||from.Y>short.MaxValue)throw new Fault("unsupported","滚轮坐标超出窗口消息范围。");
                    Native.Send(child,Native.MOVE,UIntPtr.Zero,Native.Pack(p.X,p.Y));
                    Gate(generation);if(Human.Sequence!=humanSequence)throw new Fault("human_busy","检测到你的新输入，滚动已取消。");
                    Native.Post(child,Native.WHEEL,new UIntPtr(unchecked((uint)(ushort)delta<<16)),Native.Pack(from.X,from.Y));
                }else{
                    MessageGesture(child,target,path,drag,duration,count,hold,button,generation,humanSequence,foreground);
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
            bool raisedAboveForeground=behindForeground&&Above(target.Handle,foreground);
            bool cursorChanged=Human.Sequence==humanSequence&&(cursor.X!=after.X||cursor.Y!=after.Y);
            bool externalInput=Human.Sequence!=humanSequence;
            if(o.Target!=null&&(focusChanged||raisedAboveForeground))Pause("后台目标应用改变了焦点或层级，已暂停；本次不算无干扰操作。");
            else if(cursorChanged||externalInput)Pause(externalInput?"检测到新的外部输入，已暂停。":"检测到系统指针位置变化，已暂停。");
            else Message=focusChanged?"输入已送达，前台窗口发生变化；请重新观察后继续。":"输入已送达；请重新观察目标窗口确认结果。";
            IntPtr over=Native.WindowFromPoint(from);IntPtr overRoot=Native.GetAncestor(over,2);
            return new{inputDelivered=delivered,businessResultVerified=false,backend=mode,dragCompleted=drag&&delivered,clickCount=count,holdMs=hold,buttonReleased=!MessageButtonHeld,focusChanged=focusChanged,raisedAboveForeground=raisedAboveForeground,backgroundTarget=o.Target!=null,targetOccludedAtPoint=o.Target!=null&&overRoot!=target.Handle,backgroundInterferenceFree=o.Target!=null&&!focusChanged&&!raisedAboveForeground&&!cursorChanged&&!externalInput,
                windowId=target.Handle.ToInt64().ToString(),receiverClass=receiver==IntPtr.Zero?null:Native.Class(receiver),receiverPoint=new{x=receiverPoint.X,y=receiverPoint.Y},
                cursorChangedWithoutHumanInput=cursorChanged,systemPointerBefore=new{x=cursor.X,y=cursor.Y},systemPointerAfter=new{x=after.X,y=after.Y},
                externalInputDuringAction=externalInput,paused=!Armed};
        }catch(Exception){Pause("动作中断，已暂停。结果可能未完成，请重新观察。");throw;}
    }
}
}
