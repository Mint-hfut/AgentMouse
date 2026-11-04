using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentPointer {
internal static class SelfTest {
    internal static int FusionFixture(string pipe){
        Guid identity;if(!pipe.StartsWith("agentmouse-background-",StringComparison.Ordinal)||!Guid.TryParse(pipe.Substring("agentmouse-background-".Length),out identity))throw new Exception("Private fixture pipe required");
        var engine=new Engine{Armed=true};
        using(var form=new PracticeForm(engine){NoActivate=true,ShowInTaskbar=false,Text="AgentMouse fusion fixture"})using(var timer=new System.Windows.Forms.Timer())using(var server=new Transport(engine,pipe)){
            var probe=new RoutingButton{Bounds=form.TestButton.Bounds,Text="fusion fixture received 0",TrackReceipt=true};form.TestButton.Hide();form.Controls.Add(probe);
            IntPtr foreground=Native.GetForegroundWindow();Native.Rect bounds;Native.GetWindowRect(foreground,out bounds);form.Location=new Point(bounds.Left+40,bounds.Top+40);
            form.Shown+=delegate{
                engine.FixtureWindow=form.Handle;Native.SetWindowPos(form.Handle,new IntPtr(1),0,0,0,0,0x13);
                var devices=new[]{new Native.RawDevice{page=1,usage=2,flags=0x100,target=form.Handle},new Native.RawDevice{page=1,usage=6,flags=0x100,target=form.Handle}};
                Native.RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf(typeof(Native.RawDevice)));server.Start();
                Console.WriteLine(Json.Write(new{windowId=form.Handle.ToInt64().ToString(),foregroundUnchanged=Native.GetForegroundWindow()==foreground}));
                Task.Run(()=>Console.ReadLine()).ContinueWith(done=>{try{form.BeginInvoke((Action)delegate{form.Close();});}catch(InvalidOperationException){}});
            };
            long started=InputTracker.Now;timer.Interval=33;timer.Tick+=delegate{Native.Point point;if(Native.GetCursorPos(out point))engine.Human.TrackPointer(point);if(InputTracker.Milliseconds(started)>60000)form.Close();};timer.Start();
            Application.Run(form);engine.Pause("fixture done");return 0;
        }
    }
    internal static int Routing(){
        var engine=new Engine{Armed=true};PracticeForm target=null,cover=null;RoutingButton targetProbe=null,coverProbe=null;Exception startup=null;var ready=new ManualResetEventSlim();
        var ui=new Thread(delegate(){
            try{
                target=new PracticeForm(engine){NoActivate=true,Text="AgentMouse routing target",ShowInTaskbar=false};
                cover=new PracticeForm(new Engine()){NoActivate=true,Text="AgentMouse routing decoy",ShowInTaskbar=false};
                targetProbe=new RoutingButton{Bounds=target.TestButton.Bounds,Text="Routing target"};target.TestButton.Hide();target.Controls.Add(targetProbe);
                // Reproduce a transparent render child over its input surface.
                var renderLayer=new TransparentRenderLayer{Bounds=targetProbe.Bounds};target.Controls.Add(renderLayer);
                coverProbe=new RoutingButton{Bounds=cover.TestButton.Bounds,Text="Routing decoy"};cover.TestButton.Hide();cover.Controls.Add(coverProbe);
                var area=Screen.PrimaryScreen.WorkingArea;target.Location=new Point(area.Left+50,area.Top+50);cover.Location=target.Location;
                target.Shown+=delegate{renderLayer.BringToFront();cover.Show();Native.SetWindowPos(cover.Handle,new IntPtr(1),0,0,0,0,0x13);Native.SetWindowPos(target.Handle,cover.Handle,0,0,0,0,0x13);ready.Set();};
                Application.Run(target);
            }catch(Exception error){startup=error;ready.Set();}
        });ui.IsBackground=true;ui.SetApartmentState(ApartmentState.STA);ui.Start();
        if(!ready.Wait(7000)||startup!=null)throw new Exception("Routing fixture startup failed: "+(startup==null?"no Shown event":startup.ToString()),startup);
        try{
            engine.FixtureWindow=target.Handle;IntPtr foreground=Native.GetForegroundWindow();
            var listed=Engine.Windows(IntPtr.Zero).Find(w=>w.Handle==target.Handle);Assert(listed!=null,"Target missing from inventory");
            var info=Obj(listed.Export());Assert(((System.Collections.IList)info["overlappingWindowsAbove"]).Contains(cover.Handle.ToInt64().ToString()),"Covering window missing from layers");
            var tree=Obj(DesktopInventory.Query(new Dictionary<string,object>{{"method","inspect_window"},{"windowId",target.Handle.ToInt64().ToString()}}));
            Assert(((System.Collections.IList)tree["nodes"]).Count>1,"Standard WinForms controls were not inspected");
            Native.Point point=new Native.Point(targetProbe.Width/2,targetProbe.Height/2);Native.ClientToScreen(targetProbe.Handle,ref point);
            var observed=Obj(engine.Dispatch(new Dictionary<string,object>{{"method","observe"},{"windowId",target.Handle.ToInt64().ToString()},{"capture","window"}}));
            var action=new Dictionary<string,object>{{"method","click"},{"windowId",cover.Handle.ToInt64().ToString()},{"observationId",observed["observationId"]},{"x",point.X},{"y",point.Y}};
            Expect("wrong_target",()=>engine.Dispatch(action));Assert(targetProbe.Ups==0&&coverProbe.Ups==0,"Wrong-target rejection delivered input");
            action["windowId"]=target.Handle.ToInt64().ToString();var delivered=Obj(engine.Dispatch(action));
            Assert((bool)delivered["inputDelivered"]&&targetProbe.Ups==1&&coverProbe.Ups==0,"Covered native messages reached the wrong receiver");
            var stale=Obj(engine.Dispatch(new Dictionary<string,object>{{"method","observe"},{"windowId",target.Handle.ToInt64().ToString()},{"capture","window"}}));
            // Replay the exact pre-action frame to reproduce the recorded stuck WeChat capture.
            var saved=(Observation)typeof(Engine).GetField("observation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(engine);
            saved.FrameTimestamp=Convert.ToInt64(Obj(observed["screenshot"])["frameTimestamp100ns"]);action["observationId"]=stale["observationId"];
            Expect("unverified_frame",()=>engine.Dispatch(action));Assert(targetProbe.Ups==1&&coverProbe.Ups==0,"A repeated frame permitted another click");
            target.Invoke((Action)delegate{target.Hide();});
            listed=Engine.Windows(IntPtr.Zero).Find(w=>w.Handle==target.Handle);Assert(listed!=null&&!(bool)Obj(listed.Export())["canCapture"],"Hidden application window disappeared from inventory");
            Expect("window_missing",()=>engine.Dispatch(new Dictionary<string,object>{{"method","observe"},{"windowId",target.Handle.ToInt64().ToString()},{"capture","window"}}));
            var shown=Obj(DesktopInventory.Query(new Dictionary<string,object>{{"method","show_window"},{"windowId",target.Handle.ToInt64().ToString()}}));
            Assert((bool)shown["foregroundUnchanged"]&&Native.IsWindowVisible(target.Handle),"Hidden fixture did not show without activation");
            Assert(((System.Collections.IList)Obj(Engine.Windows(IntPtr.Zero).Find(w=>w.Handle==target.Handle).Export())["overlappingWindowsAbove"]).Contains(cover.Handle.ToInt64().ToString()),"Showing the hidden fixture raised it above the cover");
            target.Invoke((Action)delegate{Native.ShowWindow(target.Handle,7);});
            listed=Engine.Windows(IntPtr.Zero).Find(w=>w.Handle==target.Handle);Assert(listed!=null&&(bool)Obj(listed.Export())["minimized"],"Minimized application window disappeared from inventory");
            Expect("unsupported",()=>DesktopInventory.Query(new Dictionary<string,object>{{"method","show_window"},{"windowId",target.Handle.ToInt64().ToString()}}));
            Assert(Native.GetForegroundWindow()==foreground,"Read/fixture test changed foreground");
            Console.WriteLine("PASS real own-window routing: covering layer inventory, UIAutomation, wrong-target rejection, covered native-message recipient, frozen post-action frame rejection, hidden-window discovery; foreground retained. Native message receipt is not a WinForms Click/business-success assertion.");return 0;
        }finally{
            engine.Pause("test finished");if(target!=null&&!target.IsDisposed)target.Invoke((Action)delegate{cover.Close();target.Close();});ui.Join(3000);
        }
    }
    private sealed class RoutingButton : Button {
        internal volatile int Ups;
        internal bool TrackReceipt;
        protected override void WndProc(ref System.Windows.Forms.Message message){if(message.Msg==Native.LEFTUP){Ups++;if(TrackReceipt)Text="fusion fixture received "+Ups;}base.WndProc(ref message);}
    }
    private sealed class TransparentRenderLayer : Control {
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x20;return p;}}
    }
    private sealed class ReleaseProbe : Control {
        internal int DownDelay,UpDelay,Downs,Ups,Cancels;
        internal readonly ManualResetEventSlim Pressed=new ManualResetEventSlim(),Released=new ManualResetEventSlim();
        protected override void WndProc(ref System.Windows.Forms.Message m){
            if(m.Msg==Native.LEFTDOWN){Downs++;Thread.Sleep(DownDelay);Capture=true;Pressed.Set();return;}
            if(m.Msg==Native.LEFTUP){Ups++;if(Ups==1)Thread.Sleep(UpDelay);Capture=false;Released.Set();return;}
            if(m.Msg==Native.CANCELMODE){Cancels++;Capture=false;}
            base.WndProc(ref m);
        }
    }
    internal static int MessageFailures(){
        PracticeForm form=null;ReleaseProbe probe=null;var ready=new ManualResetEventSlim();Exception startup=null;
        IntPtr foreground=Native.GetForegroundWindow();Native.Point before,after;Native.GetCursorPos(out before);
        var ui=new Thread(delegate(){try{
            form=new PracticeForm(new Engine()){NoActivate=true,ShowInTaskbar=false,Text="AgentMouse release fixture"};
            probe=new ReleaseProbe{Bounds=new Rectangle(20,220,180,50)};form.Controls.Add(probe);
            form.Shown+=delegate{Native.SetWindowPos(form.Handle,new IntPtr(1),0,0,0,0,0x13);ready.Set();};Application.Run(form);
        }catch(Exception error){startup=error;ready.Set();}});ui.IsBackground=true;ui.SetApartmentState(ApartmentState.STA);ui.Start();
        if(!ready.Wait(7000)||startup!=null)throw new Exception("Release fixture startup failed",startup);
        try{
            foreach(string scenario in new[]{"down-timeout","up-timeout","pause","controller-exit"}){
                form.Invoke((Action)delegate{probe.DownDelay=scenario=="down-timeout"?700:0;probe.UpDelay=scenario=="up-timeout"?700:0;probe.Downs=probe.Ups=probe.Cancels=0;probe.Pressed.Reset();probe.Released.Reset();});
                var engine=new Engine{Armed=true,FixtureWindow=form.Handle};
                Native.Rect bounds;Native.GetWindowRect(form.Handle,out bounds);uint pid;Native.GetWindowThreadProcessId(form.Handle,out pid);
                var desktop=SystemInformation.VirtualScreen;
                var observation=new Observation{Token=Guid.NewGuid().ToString("N"),At=InputTracker.Now,Bounds=new Native.Rect{Left=desktop.Left,Top=desktop.Top,Right=desktop.Right,Bottom=desktop.Bottom},Target=new ObservedWindow{Handle=form.Handle,Pid=pid,Bounds=bounds},FrameTimestamp=1};
                // Inject only the owned fixture's observation to separate input failure tests from WGC/UIA.
                typeof(Engine).GetField("observation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(engine,observation);
                var point=new Native.Point(80,25);Native.ClientToScreen(probe.Handle,ref point);
                var request=new Dictionary<string,object>{{"method","click"},{"windowId",form.Handle.ToInt64().ToString()},{"observationId",observation.Token},{"x",point.X},{"y",point.Y},{"holdMs",scenario.EndsWith("timeout")?0:1500}};
                if(scenario.EndsWith("timeout")){
                    Expect("button_release_unconfirmed",()=>engine.Dispatch(request));
                    Assert(!engine.Armed&&engine.MessageButtonHeld,"Uncertain release was reported as safe");
                    engine.Armed=true;Expect("contact_unreleased",()=>engine.Dispatch(request));
                }else if(scenario=="pause"){
                    var action=Task.Run(()=>engine.Dispatch(request));Assert(probe.Pressed.Wait(3000),"Fixture never pressed");engine.Pause("fixture pause");
                    try{action.GetAwaiter().GetResult();throw new Exception("Paused hold succeeded");}catch(Fault error){Assert(error.Code=="paused","Unexpected pause failure");}
                }else{
                    string pipe="agentmouse-background-"+Guid.NewGuid();
                    using(var server=new Transport(engine,pipe)){
                        server.Start();var action=Task.Run(()=>Transport.Call(pipe,Json.Write(request)));
                        Assert(probe.Pressed.Wait(3000),"Fixture never pressed before shutdown");server.Dispose();
                        try{action.GetAwaiter().GetResult();}catch(IOException){}catch(Fault){}
                    }
                }
                Assert(probe.Released.Wait(4000),"Target did not process an UP after "+scenario);
                form.Invoke((Action)delegate{Assert(!probe.Capture,"Target retained mouse capture after "+scenario);Assert(probe.Downs==1,"DOWN was replayed after "+scenario);if(scenario.EndsWith("timeout"))Assert(probe.Cancels>=2,"Missing ordered cancellation after "+scenario);});
            }
            Native.GetCursorPos(out after);Assert(Native.GetForegroundWindow()==foreground&&before.X==after.X&&before.Y==after.Y,"Release fixtures changed foreground/cursor");
            Console.WriteLine("PASS native release failures: delayed DOWN, delayed UP, pause during hold, controller exit during hold; no repeated DOWN, queued cancel/UP processed, target capture released, uncertainty blocks new input; own windows only.");return 0;
        }finally{if(form!=null&&!form.IsDisposed)form.Invoke((Action)delegate{form.Close();});ui.Join(3000);}
    }
    internal static int ProbeBackgroundCapture(string processName,string directory,string title=""){
        var candidates=Engine.Windows(IntPtr.Zero).FindAll(w=>string.Equals(w.ProcessName,processName,StringComparison.OrdinalIgnoreCase)&&(title==""||w.Name.Contains(title)));
        if(candidates.Count==0)throw new Exception("No visible, non-minimized window for process "+processName);
        candidates.Sort(delegate(WindowItem a,WindowItem b){Native.Rect x,y;Native.GetWindowRect(a.Handle,out x);Native.GetWindowRect(b.Handle,out y);return ((long)y.Width*y.Height).CompareTo((long)x.Width*x.Height);});
        var target=candidates[0];IntPtr foreground=Native.GetForegroundWindow();Native.Point before,after;Native.GetCursorPos(out before);
        var engine=new Engine();var response=Obj(engine.Dispatch(new Dictionary<string,object>{{"method","observe"},{"windowId",target.Handle.ToInt64().ToString()},{"capture","window"}}));
        var picture=Obj(response["screenshot"]);Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory,"background-window.png"),Convert.FromBase64String((string)picture["data"]));picture.Remove("data");
        Native.GetCursorPos(out after);Native.Rect bounds;Native.GetWindowRect(target.Handle,out bounds);
        var center=new Native.Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);IntPtr over=Native.WindowFromPoint(center);IntPtr overRoot=Native.GetAncestor(over,2);
        Native.Rect frontBounds;Native.GetWindowRect(foreground,out frontBounds);
        var report=new{process=processName,windowId=target.Handle.ToInt64().ToString(),capture=picture,background=foreground!=target.Handle,centerCovered=overRoot!=target.Handle,fullyCoveredByForeground=frontBounds.Left<=bounds.Left&&frontBounds.Top<=bounds.Top&&frontBounds.Right>=bounds.Right&&frontBounds.Bottom>=bounds.Bottom,foregroundUnchanged=Native.GetForegroundWindow()==foreground,cursorUnchanged=before.X==after.X&&before.Y==after.Y,inputSubmitted=false};
        File.WriteAllText(Path.Combine(directory,"background-window.json"),Json.Write(report),new System.Text.UTF8Encoding(false));Console.WriteLine(Json.Write(report));return 0;
    }
    internal static int ForegroundState(){
        var handle=Native.GetForegroundWindow();Native.Rect bounds;Native.GetWindowRect(handle,out bounds);Native.Point pointer;Native.GetCursorPos(out pointer);
        uint pid;Native.GetWindowThreadProcessId(handle,out pid);string process="";try{process=Process.GetProcessById((int)pid).ProcessName;}catch(ArgumentException){}
        Console.WriteLine(Json.Write(new{windowId=handle.ToInt64().ToString(),process=process,dpiScale=Native.GetDpiForWindow(handle)/96.0,bounds=new{x=bounds.Left,y=bounds.Top,width=bounds.Width,height=bounds.Height},pointer=new{x=pointer.X,y=pointer.Y}}));return 0;
    }
    internal static int ImagePoint(string path,string rgb){
        var pieces=rgb.Split(',');var color=Color.FromArgb(int.Parse(pieces[0]),int.Parse(pieces[1]),int.Parse(pieces[2]));long best=0;object result=null;
        // Find the largest connected fixture region; color management can shift its RGB values.
        using(var image=new Bitmap(path)){
            int width=image.Width,height=image.Height;var mask=new bool[width*height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){var pixel=image.GetPixel(x,y);mask[y*width+x]=Math.Abs(pixel.R-color.R)<=48&&Math.Abs(pixel.G-color.G)<=48&&Math.Abs(pixel.B-color.B)<=48;}
            var queue=new Queue<int>();
            for(int start=0;start<mask.Length;start++){
                if(!mask[start])continue;queue.Enqueue(start);mask[start]=false;long sumX=0,sumY=0,count=0;int minX=width,minY=height,maxX=0,maxY=0;
                while(queue.Count>0){int index=queue.Dequeue(),x=index%width,y=index/width;sumX+=x;sumY+=y;count++;minX=Math.Min(minX,x);minY=Math.Min(minY,y);maxX=Math.Max(maxX,x);maxY=Math.Max(maxY,y);
                    foreach(int next in new[]{x>0?index-1:-1,x+1<width?index+1:-1,y>0?index-width:-1,y+1<height?index+width:-1})if(next>=0&&mask[next]){mask[next]=false;queue.Enqueue(next);}
                }
                if(count>best){best=count;result=new{x=(double)sumX/count,y=(double)sumY/count,pixels=count,minX=minX,minY=minY,maxX=maxX,maxY=maxY};}
            }
        }
        Assert(best>100,"Expected fixture color was not found in the captured background image");Console.WriteLine(Json.Write(result));return 0;
    }
    internal static int BackgroundFixtureServer(string title,string pipe,uint fixturePid=0){
        if(!title.StartsWith("APCOMPAT-background-",StringComparison.Ordinal)||!pipe.StartsWith("agentmouse-background-",StringComparison.Ordinal))throw new Exception("Only an isolated background fixture is accepted");
        if(fixturePid!=0){Guid nonce;Assert(Guid.TryParse(pipe.Substring("agentmouse-background-".Length),out nonce)&&title=="APCOMPAT-background-"+nonce.ToString(),"Native fixture title must match its private pipe nonce");}
        var targets=Engine.Windows(IntPtr.Zero).FindAll(w=>(fixturePid==0?w.ProcessName=="chrome":w.Pid==fixturePid)&&w.Name.StartsWith(title,StringComparison.Ordinal));
        Assert(targets.Count==1,"Expected one owned fixture window");
        Assert(Native.SetWindowPos(targets[0].Handle,new IntPtr(1),0,0,0,0,0x13),"Could not lower the owned fixture without activation");
        var engine=new Engine{Armed=true,FixtureWindow=targets[0].Handle};using(var server=new Transport(engine,pipe)){
            using(var sink=new FixtureInputSink(engine))using(var timer=new System.Windows.Forms.Timer()){
                IntPtr handle=sink.Handle;var context=new ApplicationContext();
                var devices=new[]{new Native.RawDevice{page=1,usage=2,flags=0x100,target=handle},new Native.RawDevice{page=1,usage=6,flags=0x100,target=handle}};
                Assert(Native.RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf(typeof(Native.RawDevice))),"Raw input monitoring failed");
                long start=InputTracker.Now;timer.Interval=33;timer.Tick+=delegate{Native.Point point;if(Native.GetCursorPos(out point))engine.Human.TrackPointer(point);if(InputTracker.Milliseconds(start)>60000)context.ExitThread();};timer.Start();
                server.Start();Console.WriteLine(Json.Write(new{windowId=targets[0].Handle.ToInt64().ToString(),pipe=pipe}));
                Task.Run(()=>Console.ReadLine()).ContinueWith(done=>{try{sink.BeginInvoke((Action)delegate{context.ExitThread();});}catch(InvalidOperationException){}});
                Application.Run(context);timer.Stop();
            }
        }return 0;
    }
    private sealed class FixtureInputSink : Form {
        private readonly Engine engine;
        internal FixtureInputSink(Engine value){engine=value;ShowInTaskbar=false;}
        protected override void WndProc(ref System.Windows.Forms.Message message){if(message.Msg==0xff)engine.Human.Read(message.LParam);base.WndProc(ref message);}
    }
    internal static int StateOnly(){
        var e=new Engine();
        Assert(!e.ShowPointer,"Paused startup must hide the overlay");
        e.Armed=true;Assert(e.ShowPointer,"Enabled input must show the requested overlay");
        e.Pause("state check");Assert(!e.ShowPointer,"Pause must hide the overlay");
        e.Armed=true;e.PointerVisible=false;Assert(!e.ShowPointer,"Display preference must stay effective");
        e.PointerVisible=true;e.Stopped=true;Assert(!e.ShowPointer,"Stopped input must hide the overlay");
        var status=Obj(e.Status());Assert(!(bool)Obj(status["pointer"])["visible"],"Status must agree with overlay state");
        Assert(!Engine.FrameAdvanced(10,100,"new",Tuple.Create(10u,100L,"old")),"Repeated post-action timestamp must block input");
        Assert(!Engine.FrameAdvanced(10,99,"new",Tuple.Create(10u,100L,"old")),"Older post-action frame must block input");
        Assert(!Engine.FrameAdvanced(10,101,"old",Tuple.Create(10u,100L,"old")),"New capture timestamps with identical pixels must block repeat input");
        Assert(Engine.FrameAdvanced(10,101,"new",Tuple.Create(10u,100L,"old")),"A new image may permit reassessment");
        Assert(Engine.FrameAdvanced(11,99,"old",Tuple.Create(10u,100L,"old")),"A reused HWND in another process must not inherit the old frame receipt");
        var enabling=new Engine();Expect("input_monitor_unavailable",()=>enabling.Dispatch(Json.Read("{\"method\":\"enable\"}")));
        enabling.InputMonitoringReady=true;enabling.Dispatch(Json.Read("{\"method\":\"enable\"}"));Assert(enabling.Armed,"Explicit enable failed after input monitoring was ready");enabling.Pause("fixture");enabling.MessageButtonHeld=true;Expect("contact_unreleased",()=>enabling.Dispatch(Json.Read("{\"method\":\"enable\"}")));
        enabling.MessageButtonHeld=false;enabling.Mode="touch";Expect("unsupported",()=>enabling.Dispatch(Json.Read("{\"method\":\"enable\"}")));
        Expect("capture_timeout",()=>WindowCapture.RunHelper("--capture-stall-self-test",150));Assert(!WindowCapture.HelperActive,"Timed-out capture helper remained registered");
        var cancelled=Task.Run(()=>WindowCapture.RunHelper("--capture-stall-self-test",4000));
        Assert(SpinWait.SpinUntil(()=>WindowCapture.HelperActive,2000),"Capture cancellation fixture did not start");WindowCapture.Reset();
        Expect("capture_interrupted",()=>cancelled.GetAwaiter().GetResult());Assert(!WindowCapture.HelperActive,"Cancelled capture helper remained registered");
        Assert(DesktopInventory.Matches("Weixin","微信")&&DesktopInventory.Matches("WeChatAppEx","wechat"),"WeChat process aliases");
        Assert(DesktopInventory.Quarantined("Weixin.exe")&&DesktopInventory.Quarantined("WECHAT")&&DesktopInventory.Quarantined("WeChatAppEx")&&!DesktopInventory.Quarantined("AgentMouse.Cli"),"Incident quarantine covers WeChat processes without blocking the owned fixture");
        Assert(DesktopInventory.PilotMatches("42:123",42,123)&&!DesktopInventory.PilotMatches("42:122",42,123)&&!DesktopInventory.PilotMatches("41:123",42,123)&&!DesktopInventory.PilotMatches("",42,123),"Native pilot must match both PID and process creation time");
        e.X=120;e.Y=240;e.Human.TrackPointer(new Native.Point(10,20));e.Human.TrackPointer(new Native.Point(30,40));
        Assert(e.X==120&&e.Y==240,"Human pointer tracking must not change Agent coordinates");
        Assert(e.Human.Sequence==1,"Human pointer motion must be detected");
        Assert(IntPtr.Size==8&&Marshal.SizeOf(typeof(Native.PointerInfo))==96&&Marshal.SizeOf(typeof(Native.TouchInfo))==144,"Native x64 structure layout");
        Expect("invalid_json",()=>Json.Read("null"));Expect("invalid_json",()=>Json.Read("{"));
        Expect("invalid_argument",()=>e.Dispatch(Json.Read("{\"method\":\"observe\",\"capture\":\"window\"}")));
        Expect("invalid_argument",()=>e.Dispatch(Json.Read("{\"method\":\"observe\",\"capture\":\"window\",\"windowId\":\"123\",\"screenshot\":false}")));
        Expect("invalid_argument",()=>e.Dispatch(Json.Read("{\"method\":\"observe\",\"capture\":\"invalid\"}")));
        Expect("invalid_argument",()=>e.Dispatch(Json.Read("{\"method\":\"move\",\"x\":1.5,\"y\":1}")));
        Expect("offscreen",()=>e.Dispatch(Json.Read("{\"method\":\"move\",\"x\":-100000,\"y\":-100000}")));
        Expect("paused",()=>e.Dispatch(Json.Read("{\"method\":\"click\",\"x\":0,\"y\":0}")));
        Expect("request_too_large",()=>Transport.ReadLine(new StringReader("abcdef\n"),5));
        Expect("incomplete_request",()=>Transport.ReadLine(new StringReader("abc"),5));
        Console.WriteLine("PASS visibility, independent Agent coordinates, input tracking, x64 layouts, request validation and paused-action rejection; no GUI or input was started");return 0;
    }
    private static readonly List<object> checks=new List<object>();
    private static int failures;
    private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Check(string name,Action run){
        try{run();checks.Add(new{name=name,status="passed"});Console.WriteLine("PASS "+name);}
        catch(Exception e){failures++;checks.Add(new{name=name,status="failed",detail=e.Message});Console.WriteLine("FAIL "+name+": "+e.Message);}
    }
    private static void Expect(string code,Action run){try{run();}catch(Fault f){Assert(f.Code==code,"Expected "+code+", got "+f.Code);return;}throw new Exception("Expected error "+code);}
    private static Dictionary<string,object> Obj(object o){return Json.Read(Json.Write(o),32*1024*1024);}
    private static Dictionary<string,object> Observe(Engine engine){return Obj(engine.Dispatch(Json.Read("{\"method\":\"observe\",\"screenshot\":false}")));}
    private static Dictionary<string,object> Click(Engine e,Native.Point p){return new Dictionary<string,object>{{"method","click"},{"x",p.X},{"y",p.Y},{"observationId",Observe(e)["observationId"]}};}
    internal static int Run(string report){
        Native.DpiAware();checks.Clear();failures=0;
        var engine=new Engine();PracticeForm fixture=null;var ready=new ManualResetEventSlim();Exception startup=null;
        var ui=new Thread(delegate(){
            try{
                Application.EnableVisualStyles();
                fixture=new PracticeForm(engine){NoActivate=true,TopMost=true};
                var area=Screen.PrimaryScreen.WorkingArea;fixture.Location=new Point(area.Right-fixture.Width-35,area.Top+40);
                fixture.Shown+=delegate{
                    var devices=new[]{new Native.RawDevice{page=1,usage=2,flags=0x100,target=fixture.Handle},new Native.RawDevice{page=1,usage=6,flags=0x100,target=fixture.Handle}};
                    Native.RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf(typeof(Native.RawDevice)));ready.Set();
                };
                Application.Run(fixture);
            }catch(Exception ex){startup=ex;ready.Set();}
        });ui.IsBackground=true;ui.SetApartmentState(ApartmentState.STA);ui.Start();
        if(!ready.Wait(7000)||startup!=null)throw new Exception("Test window could not open",startup);
        var handle=fixture.Handle;
        Native.Point button=new Native.Point(100,60),blank=new Native.Point(210,275),end=new Native.Point(280,285);
        Native.ClientToScreen(handle,ref button);Native.ClientToScreen(handle,ref blank);Native.ClientToScreen(handle,ref end);
        try{
            Check("native-structure-layout",delegate{
                Assert(IntPtr.Size==8,"Only x64 builds are supported");
                Assert(Marshal.SizeOf(typeof(Native.PointerInfo))==96,"POINTER_INFO layout");
                Assert(Marshal.SizeOf(typeof(Native.TouchInfo))==144,"POINTER_TOUCH_INFO layout");
            });
            Check("pointer-motion-is-observed-without-a-raw-device",delegate{
                var tracker=new InputTracker();tracker.TrackPointer(new Native.Point(10,10));int before=tracker.Sequence;
                tracker.TrackPointer(new Native.Point(10,10));Assert(tracker.Sequence==before,"Stationary pointer reported movement");
                tracker.TrackPointer(new Native.Point(20,30));Assert(tracker.Sequence>before&&tracker.Last!=0,"Pointer motion was missed");
            });
            Check("reject-malformed-and-null-json",delegate{Expect("invalid_json",()=>Json.Read("null"));Expect("invalid_json",()=>Json.Read("{"));});
            Check("reject-non-integer-coordinates",delegate{Expect("invalid_argument",()=>engine.Dispatch(Json.Read("{\"method\":\"move\",\"x\":1.5,\"y\":1}")));});
            Check("reject-offscreen-coordinates",delegate{Expect("offscreen",()=>engine.Dispatch(Json.Read("{\"method\":\"move\",\"x\":-100000,\"y\":-100000}")));});
            Check("reject-oversized-and-incomplete-requests",delegate{
                Expect("request_too_large",()=>Transport.ReadLine(new StringReader("abcdef\n"),5));
                Expect("incomplete_request",()=>Transport.ReadLine(new StringReader("abc"),5));
            });
            Check("paused-input-is-rejected",delegate{engine.Pause("test");Expect("paused",()=>engine.Dispatch(Click(engine,button)));Assert(fixture.Clicks==0,"Paused input reached the button");});
            Check("virtual-move-does-not-move-system-pointer",delegate{
                Native.Point before,after;Native.GetCursorPos(out before);int n=engine.Human.Sequence;
                engine.Dispatch(new Dictionary<string,object>{{"method","move"},{"x",blank.X},{"y",blank.Y}});
                Native.GetCursorPos(out after);Assert(engine.X==blank.X&&engine.Y==blank.Y,"Virtual pointer did not move");
                Assert(engine.Human.Sequence!=n||(before.X==after.X&&before.Y==after.Y),"System pointer moved without human input");
            });
            Check("visible-screenshot-is-valid-png",delegate{
                var response=Obj(engine.Dispatch(new Dictionary<string,object>{{"method","observe"},{"screenshot",true},{"windowId",handle.ToInt64().ToString()}}));
                var image=Obj(response["screenshot"]);var bytes=Convert.FromBase64String((string)image["data"]);
                Assert(bytes.Length>8&&bytes[0]==137&&bytes[1]==80&&bytes[2]==78&&bytes[3]==71,"Invalid PNG");
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"practice-window.png"),bytes);
            });
            Check("desktop-observation-needs-no-window-selection",delegate{
                var response=Obj(engine.Dispatch(new Dictionary<string,object>{{"method","observe"},{"screenshot",true}}));
                Assert((string)response["scope"]=="desktop"&&response["window"]==null,"Desktop observation unexpectedly bound a window");
                var picture=Obj(response["screenshot"]);var bounds=Obj(response["bounds"]);
                Assert(Convert.ToInt32(picture["sourceWidth"])==Convert.ToInt32(bounds["width"]),"Desktop screenshot width mismatch");
                Assert((long)Convert.ToInt32(picture["width"])*Convert.ToInt32(picture["height"])<=4000000,"Desktop preview exceeded image size cap");
                Assert(Convert.ToDouble(picture["scaleX"])>=1&&Convert.ToDouble(picture["scaleY"])>=1,"Invalid image-to-screen mapping");
            });
            Check("background-click-reaches-real-button-without-focus-change",delegate{
                engine.Armed=true;engine.Mode="background";int before=fixture.Clicks;
                var request=Click(engine,button);var result=Obj(engine.Dispatch(request));
                Assert(fixture.Clicks==before+1,"Button click was not delivered exactly once");
                Assert(!(bool)result["focusChanged"],"Click changed foreground window");
                Assert(!(bool)result["cursorChangedWithoutHumanInput"],"Click moved system pointer: "+Json.Write(result));
                Assert(!(bool)result["businessResultVerified"],"Input delivery was incorrectly reported as task success");
                Expect("stale_observation",()=>engine.Dispatch(request));
            });
            Check("one-enable-crosses-windows-and-focus-without-rearming",delegate{
                PracticeForm other=null;Form focus=null;
                fixture.Invoke((Action)delegate{
                    var area=Screen.PrimaryScreen.WorkingArea;
                    other=new PracticeForm(null){TopMost=true,Location=new Point(area.Left+35,area.Top+40)};other.Show();
                    focus=new Form{Text="Global input focus fixture",TopMost=true,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Size=new Size(200,90),Location=new Point(area.Left+area.Width/2,area.Bottom-120)};
                    other.TestButton.Click+=delegate{other.Activate();};focus.Show();focus.Activate();
                });
                try{
                    var point=new Native.Point(80,25);Native.ClientToScreen(other.TestButton.Handle,ref point);
                    engine.Mode="background";engine.Armed=true;int first=fixture.Clicks;
                    engine.Dispatch(Click(engine,button));Assert(fixture.Clicks==first+1&&engine.Armed,"First window input failed or paused");
                    engine.Dispatch(Click(engine,point));Assert(other.Clicks==1&&engine.Armed,"Second window required rearming");
                }finally{fixture.Invoke((Action)delegate{other.Close();focus.Close();});}
            });
            Check("moved-window-invalidates-observation",delegate{
                engine.Armed=true;var request=Click(engine,button);
                fixture.Invoke((Action)delegate{fixture.Left-=8;});
                Expect("window_changed",()=>engine.Dispatch(request));
                fixture.Invoke((Action)delegate{fixture.Left+=8;});
            });
            Check("background-wheel-scrolls-a-real-list",delegate{
                engine.Armed=true;engine.Mode="background";var point=new Native.Point(280,65);Native.ClientToScreen(handle,ref point);
                int before=(int)fixture.Invoke((Func<int>)(()=>fixture.ScrollList.TopIndex));
                var request=Click(engine,point);request["method"]="scroll";request["delta"]=-120;
                var result=Obj(engine.Dispatch(request));
                int after=(int)fixture.Invoke((Func<int>)(()=>fixture.ScrollList.TopIndex));
                Assert(after>before,"List did not scroll");Assert(!(bool)result["focusChanged"],"Scroll changed foreground window");
            });
            Check("new-window-after-observation-is-rejected",delegate{
                Form cover=null;
                engine.Armed=true;var request=Click(engine,blank);
                fixture.Invoke((Action)delegate{cover=new CoverForm{Location=new Point(blank.X-20,blank.Y-20),Size=new Size(80,80),TopMost=true};cover.Show();});
                try{Expect("window_changed",()=>engine.Dispatch(request));}
                finally{fixture.Invoke((Action)delegate{cover.Close();});}
            });
            Check("touch-tap-reaches-window-and-releases-contact",delegate{
                engine.Armed=true;engine.Mode="touch";int down=fixture.TouchDowns,up=fixture.TouchUps;
                var result=Obj(engine.Dispatch(Click(engine,blank)));
                long start=InputTracker.Now;while((fixture.TouchDowns==down||fixture.TouchUps==up)&&InputTracker.Milliseconds(start)<1000)Thread.Sleep(10);
                Assert(fixture.TouchDowns>down&&fixture.TouchUps>up,"Touch down/up did not reach fixture");
                Assert(!engine.Touch.Active,"Touch contact remained held");
                Assert(!(bool)result["cursorChangedWithoutHumanInput"],"Touch moved the system pointer");
            });
            Check("touch-drag-delivers-updates-and-releases-contact",delegate{
                engine.Armed=true;int updates=fixture.TouchUpdates;
                var request=Click(engine,blank);request["method"]="drag";request["toX"]=end.X;request["toY"]=end.Y;request["durationMs"]=160;
                engine.Dispatch(request);Assert(fixture.TouchUpdates>updates,"No touch movement arrived");Assert(!engine.Touch.Active,"Drag did not release contact");
            });
            Check("human-input-cancels-an-active-touch-gesture",delegate{
                engine.Armed=true;int downs=fixture.TouchDowns;
                var marker=new Thread(delegate(){long start=InputTracker.Now;while(fixture.TouchDowns==downs&&InputTracker.Milliseconds(start)<4000)Thread.Sleep(2);if(fixture.TouchDowns>downs)engine.Human.Mark();});
                var request=Click(engine,blank);request["method"]="drag";request["toX"]=end.X;request["toY"]=end.Y;request["durationMs"]=500;
                marker.Start();try{Expect("interrupted",()=>engine.Dispatch(request));}finally{marker.Join(4500);}
                Assert(!engine.Touch.Active,"Interrupted gesture left a contact held");Assert(!engine.Armed,"Interrupted engine did not pause");
            });
            Check("local-pipe-status-and-pause",delegate{
                string pipe="agentmouse-selftest-"+Guid.NewGuid().ToString("N");
                using(var server=new Transport(engine,pipe)){
                    server.Start();
                    var response=Json.Read(Transport.Call(pipe,"{\"method\":\"status\"}"));Assert((bool)response["ok"],"Pipe status failed");
                    engine.Armed=true;response=Json.Read(Transport.Call(pipe,"{\"method\":\"pause\"}"));Assert((bool)response["ok"]&&!engine.Armed,"Pipe pause failed");
                    engine.Armed=true;engine.Mode="touch";int downs=fixture.TouchDowns;
                    var request=Click(engine,blank);request["method"]="drag";request["toX"]=end.X;request["toY"]=end.Y;request["durationMs"]=1800;
                    string actionReply=null;Exception actionError=null;
                    var caller=new Thread(delegate(){try{actionReply=Transport.Call(pipe,Json.Write(request));}catch(Exception ex){actionError=ex;}});caller.Start();
                    long start=InputTracker.Now;while(fixture.TouchDowns==downs&&InputTracker.Milliseconds(start)<3500)Thread.Sleep(2);
                    Assert(fixture.TouchDowns>downs,"Pipe gesture did not start");
                    Transport.SignalPause(pipe);start=InputTracker.Now;
                    while(engine.Armed&&InputTracker.Milliseconds(start)<500)Thread.Sleep(2);
                    Assert(!engine.Armed,"Pause signal was stuck behind active input");
                    Assert(caller.Join(2000),"Interrupted call did not complete");Assert(actionError==null,"Interrupted transport failed");
                    Assert(!(bool)Json.Read(actionReply)["ok"]&&!engine.Touch.Active,"Interrupted input did not cancel/release");
                }
            });
        }finally{
            engine.Pause("tests complete");try{engine.Touch.Cancel();}catch(Exception){}
            if(fixture!=null&&!fixture.IsDisposed)fixture.Invoke((Action)delegate{fixture.Close();});ui.Join(3000);
        }
        var document=new{generatedAt=DateTimeOffset.Now.ToString("o"),passed=checks.Count-failures,failed=failures,checks=checks,
            scope="Only self-created WinForms fixtures were exercised. Cross-app, IME and concurrent real-human typing are not verified."};
        File.WriteAllText(report,Json.Write(document),new System.Text.UTF8Encoding(false));return failures==0?0:1;
    }
    private sealed class CoverForm : Form {
        internal CoverForm(){FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;BackColor=Color.LightGray;ShowInTaskbar=false;}
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x8000000;return p;}}
    }
}
}
