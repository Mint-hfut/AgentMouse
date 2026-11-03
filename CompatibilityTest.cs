using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace AgentPointer {
internal static class CompatibilityTest {
    private static Engine engine;
    private static ProbeForm probe;
    private static string output,appName,runId;
    private static bool backgroundOnly;
    private static readonly List<object> results=new List<object>();
    [STAThread] private static int Main(string[] args){
        Native.DpiAware();appName=args.Length>0?args[0]:"chrome";output=Path.GetFullPath(args.Length>1?args[1]:"compatibility-results");
        Directory.CreateDirectory(output);runId=appName+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
        backgroundOnly=args.Length>2&&args[2]=="--background-only";
        engine=new Engine();Application.EnableVisualStyles();probe=new ProbeForm(engine);
        Exception fatal=null;var worker=new Thread(delegate(){
            try{
                if(appName=="chrome"||appName=="edge")Browser();
                else if(appName=="notepad")Notepad();
                else if(appName=="word")Word();
                else if(appName=="mcp")Mcp();
                else if(appName=="mcp-open")McpOpen();
                else if(appName=="mcp-cross")McpCross();
                else throw new Exception("Unknown application");
            }catch(Exception e){fatal=e;Console.WriteLine("BLOCKED "+appName+": "+e.Message);}
            finally{engine.Pause("测试结束");try{engine.Touch.Cancel();}catch{};probe.BeginInvoke((Action)delegate{probe.Close();});}
        });worker.IsBackground=true;worker.SetApartmentState(ApartmentState.STA);
        probe.Shown+=delegate{worker.Start();};Application.Run(probe);worker.Join(2000);
        File.WriteAllText(Path.Combine(output,appName+".json"),Json.Write(new{application=appName,date=DateTimeOffset.Now.ToString("o"),backgroundOnly=backgroundOnly,fatal=fatal==null?null:fatal.Message,results=results,
            scope="Only newly created local test pages/documents were used. Pointer/focus observations do not establish complete input isolation."}),new UTF8Encoding(false));
        return fatal==null?0:1;
    }
    private sealed class ProbeForm : Form {
        private readonly Engine core;
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        internal ProbeForm(Engine e){core=e;Text="AgentMouse · 兼容性测试";Size=new Size(325,150);TopMost=true;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
            var r=Screen.PrimaryScreen.WorkingArea;Location=new Point(r.Right-345,r.Bottom-185);
            Controls.Add(new Label{Text="仅操作自建测试页和测试文档。\n此窗口用于检测焦点是否被切换。\n不会自动移动你的真实鼠标。",Dock=DockStyle.Fill,Padding=new Padding(16),Font=new Font("Microsoft YaHei UI",10)});
            timer.Interval=15;timer.Tick+=delegate{Native.Point p;if(Native.GetCursorPos(out p))core.Human.TrackPointer(p);};
        }
        protected override void OnShown(EventArgs e){base.OnShown(e);core.Owner=Handle;
            var devices=new[]{new Native.RawDevice{page=1,usage=2,flags=0x100,target=Handle},new Native.RawDevice{page=1,usage=6,flags=0x100,target=Handle}};
            Native.RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf(typeof(Native.RawDevice)));timer.Start();}
        protected override void WndProc(ref Message m){if(m.Msg==0xff)core.Human.Read(m.LParam);base.WndProc(ref m);}
        protected override void OnFormClosed(FormClosedEventArgs e){timer.Stop();base.OnFormClosed(e);}
    }
    private static WindowItem WaitWindow(string prefix,int timeout=12000){
        long start=InputTracker.Now;
        while(InputTracker.Milliseconds(start)<timeout){var w=Engine.Windows(IntPtr.Zero).Find(item=>item.Name.IndexOf(prefix,StringComparison.OrdinalIgnoreCase)>=0);if(w!=null)return w;Thread.Sleep(100);}
        throw new Exception("Test window did not appear: "+prefix);
    }
    private static void Place(WindowItem w){
        var r=Screen.PrimaryScreen.WorkingArea;
        if(!Native.SetWindowPos(w.Handle,new IntPtr(-1),r.Left+45,r.Top+65,720,650,0x10))throw new Exception("Cannot place own test window");
        Thread.Sleep(200);probe.Invoke((Action)delegate{probe.Activate();});Thread.Sleep(80);
    }
    private static Dictionary<string,object> Object(object o){return Json.Read(Json.Write(o),32*1024*1024);}
    private static Dictionary<string,object> Observe(WindowItem w,string tag,bool screenshot){
        var value=Object(engine.Dispatch(new Dictionary<string,object>{{"method","observe"},{"screenshot",screenshot},{"windowId",w.Handle.ToInt64().ToString()}}));
        if(screenshot){var image=(Dictionary<string,object>)value["screenshot"];File.WriteAllBytes(Path.Combine(output,appName+"-"+tag+".png"),Convert.FromBase64String((string)image["data"]));}
        return value;
    }
    private static Native.Point Colour(Dictionary<string,object> observation,Color color){
        var s=(Dictionary<string,object>)observation["screenshot"];
        using(var stream=new MemoryStream(Convert.FromBase64String((string)s["data"])))using(var b=new Bitmap(stream)){
            int minX=b.Width,minY=b.Height,maxX=-1,maxY=-1;
            for(int y=0;y<b.Height;y+=3)for(int x=0;x<b.Width;x+=3){var c=b.GetPixel(x,y);
                if(Math.Abs(c.R-color.R)<3&&Math.Abs(c.G-color.G)<3&&Math.Abs(c.B-color.B)<3){minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}}
            if(maxX-minX<20||maxY-minY<20)throw new Exception("Test target color not visible; no input was sent");
            return new Native.Point(Convert.ToInt32(s["x"])+(int)Math.Round((minX+maxX)/2*Convert.ToDouble(s["scaleX"])),Convert.ToInt32(s["y"])+(int)Math.Round((minY+maxY)/2*Convert.ToDouble(s["scaleY"])));
        }
    }
    private static object Action(WindowItem w,string mode,string operation,Native.Point p,Native.Point end,Func<object> snapshot,Func<object,object,bool> succeeded){
        probe.Invoke((Action)delegate{probe.Activate();});Thread.Sleep(80);
        var observation=Observe(w,mode+"-"+operation,false);engine.Mode=mode;engine.Armed=true;
        object before=snapshot(),after=null,result=null;string code=null,error=null;
        Native.Point cursorBefore,cursorAfter;Native.GetCursorPos(out cursorBefore);IntPtr focusBefore=Native.GetForegroundWindow();
        var request=new Dictionary<string,object>{{"method",operation},{"observationId",observation["observationId"]},{"x",p.X},{"y",p.Y}};
        if(operation=="right_click"){request["method"]="click";request["button"]="right";}
        if(operation=="scroll")request["delta"]=-120;
        if(operation=="drag"){request["toX"]=end.X;request["toY"]=end.Y;request["durationMs"]=450;}
        try{result=engine.Dispatch(request);}catch(Exception ex){error=ex.Message;var fault=ex as Fault;code=fault==null?"exception":fault.Code;}
        Thread.Sleep(220);after=snapshot();Native.GetCursorPos(out cursorAfter);
        bool effect=succeeded(before,after),moved=cursorBefore.X!=cursorAfter.X||cursorBefore.Y!=cursorAfter.Y,focus=focusBefore!=Native.GetForegroundWindow();
        var row=new{backend=mode,operation=operation,x=p.X,y=p.Y,effectObserved=effect,systemPointerChanged=moved,foregroundChanged=focus,errorCode=code,error=error,
            classification=error!=null?(effect?"effect_observed_despite_error":"blocked_or_failed"):(effect?(moved||focus?"works_with_interference":"works_in_this_check"):"submitted_but_no_effect"),
            before=before,after=after,inputResult=result};
        results.Add(row);Console.WriteLine(appName+" "+mode+" "+operation+": "+row.classification+"; pointer="+moved+" focus="+focus);
        engine.Pause("case complete");return row;
    }
    private static Dictionary<string,int> BrowserState(WindowItem w){
        var values=new Dictionary<string,int>();foreach(var item in Native.Title(w.Handle).Split('|')){var part=item.Split('=');int n;if(part.Length==2&&int.TryParse(part[1],out n))values[part[0]]=n;}return values;
    }
    private static int State(object value,string key){var values=(Dictionary<string,int>)value;return values.ContainsKey(key)?values[key]:-1;}
    private static void Browser(){
        string browser=appName=="chrome"?@"C:\Program Files\Google\Chrome\Application\chrome.exe":@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        if(!File.Exists(browser))throw new Exception("Browser is not installed");
        string root=Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        string url=new Uri(Path.Combine(root,"browser-fixture.html")).AbsoluteUri+"?id="+runId;
        string profile=Path.Combine(output,runId+"-profile");
        var process=Process.Start(new ProcessStartInfo(browser,"--no-first-run --no-default-browser-check --disable-background-networking --disable-component-update --disable-sync --force-device-scale-factor=1 --user-data-dir=\""+profile+"\" --app=\""+url+"\""){UseShellExecute=false});
        WindowItem w=null;
        try{
            w=WaitWindow("APCOMPAT-"+runId);Place(w);Thread.Sleep(400);
            var observation=Observe(w,"initial",true);
            var button=Colour(observation,Color.FromArgb(237,108,2));var scroll=Colour(observation,Color.FromArgb(41,121,255));var handle=Colour(observation,Color.FromArgb(142,36,170));
            Func<object> snapshot=()=>BrowserState(w);
            Action(w,"background","click",button,button,snapshot,(a,b)=>State(b,"c")>State(a,"c"));
            Action(w,"background","right_click",button,button,snapshot,(a,b)=>State(b,"r")>State(a,"r"));
            Action(w,"background","scroll",scroll,scroll,snapshot,(a,b)=>State(b,"s")>State(a,"s"));
            if(!backgroundOnly){
                Action(w,"touch","click",button,button,snapshot,(a,b)=>State(b,"c")>State(a,"c"));
                Action(w,"touch","scroll",scroll,scroll,snapshot,(a,b)=>State(b,"s")>State(a,"s"));
                Action(w,"touch","drag",handle,new Native.Point(handle.X+95,handle.Y+40),snapshot,(a,b)=>State(b,"d")>State(a,"d"));
            }
            Observe(w,"final",true);
        }finally{CloseOwn(w,"APCOMPAT-"+runId);if(!process.HasExited)process.CloseMainWindow();process.Dispose();}
    }
    private static AutomationElement Document(WindowItem w){
        var root=AutomationElement.FromHandle(w.Handle);
        var nodes=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty,true));
        for(int i=0;i<nodes.Count;i++)if(!nodes[i].Current.IsOffscreen&&nodes[i].Current.BoundingRectangle.Width>200&&nodes[i].Current.BoundingRectangle.Height>100)return nodes[i];
        throw new Exception("No readable text surface was exposed; no click was sent");
    }
    private static object TextState(AutomationElement doc){
        var text=(TextPattern)doc.GetCurrentPattern(TextPattern.Pattern);var selected=text.GetSelection();int caret=-1;
        if(selected.Length>0){var prefix=text.DocumentRange.Clone();prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End,selected[0],TextPatternRangeEndpoint.Start);caret=prefix.GetText(20000).Length;}
        var visible=text.GetVisibleRanges();return new Dictionary<string,object>{{"caret",caret},{"visiblePrefix",visible.Length>0?visible[0].GetText(80):""}};
    }
    private static void Notepad(){
        string name="APCOMPAT-"+runId+".txt",path=Path.Combine(output,name);var text=new StringBuilder();
        for(int i=1;i<=250;i++)text.AppendLine("ROW "+i.ToString("0000")+" — local mouse compatibility fixture. 0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        File.WriteAllText(path,text.ToString(),new UTF8Encoding(true));
        var process=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"notepad.exe"),"\""+path+"\""){UseShellExecute=false});WindowItem w=null;
        try{
            w=WaitWindow(name);Place(w);Thread.Sleep(400);var doc=Document(w);var r=doc.Current.BoundingRectangle;
            Observe(w,"initial",true);var point=new Native.Point((int)r.Left+150,(int)r.Top+100);Func<object> snapshot=()=>TextState(doc);
            Action(w,"background","click",point,point,snapshot,(a,b)=>Convert.ToInt32(((Dictionary<string,object>)b)["caret"])!=Convert.ToInt32(((Dictionary<string,object>)a)["caret"]));
            Action(w,"background","scroll",point,point,snapshot,(a,b)=>(string)((Dictionary<string,object>)b)["visiblePrefix"]!=(string)((Dictionary<string,object>)a)["visiblePrefix"]);
            if(!backgroundOnly){
                point.Y+=85;
                Action(w,"touch","click",point,point,snapshot,(a,b)=>Convert.ToInt32(((Dictionary<string,object>)b)["caret"])!=Convert.ToInt32(((Dictionary<string,object>)a)["caret"]));
                Action(w,"touch","scroll",point,point,snapshot,(a,b)=>(string)((Dictionary<string,object>)b)["visiblePrefix"]!=(string)((Dictionary<string,object>)a)["visiblePrefix"]);
            }
            Observe(w,"final",true);
        }finally{CloseOwn(w,name);process.Dispose();}
    }
    private static void Word(){
        Type type=Type.GetTypeFromProgID("Word.Application");if(type==null)throw new Exception("Microsoft Word is not installed");
        dynamic app=null,doc=null;object applicationObject=null,documentObject=null;WindowItem w=null;
        try{
            app=Activator.CreateInstance(type);applicationObject=(object)app;app.Visible=true;app.DisplayAlerts=0;doc=app.Documents.Add();documentObject=(object)doc;
            var text=new StringBuilder();for(int i=1;i<=160;i++)text.Append("ROW "+i.ToString("0000")+" — local compatibility fixture, no user document.\r");doc.Content.Text=text.ToString();
            string path=Path.Combine(output,"APCOMPAT-"+runId+".docx");doc.SaveAs2(path);app.Selection.SetRange(0,0);
            w=WaitWindow("APCOMPAT-"+runId);Place(w);Thread.Sleep(300);
            int left=0,top=0,width=0,height=0;
            dynamic range=doc.Range(10,11);app.ActiveWindow.GetPoint(out left,out top,out width,out height,range);
            var point=new Native.Point(left+Math.Max(1,width/2),top+Math.Max(1,height/2));
            Func<object> snapshot=()=>new Dictionary<string,object>{{"caret",(int)app.Selection.Start},{"scroll",(int)app.ActiveWindow.VerticalPercentScrolled}};
            Observe(w,"initial",true);
            Action(w,"background","click",point,point,snapshot,(a,b)=>Convert.ToInt32(((Dictionary<string,object>)b)["caret"])!=Convert.ToInt32(((Dictionary<string,object>)a)["caret"]));
            Action(w,"background","scroll",point,point,snapshot,(a,b)=>Convert.ToInt32(((Dictionary<string,object>)b)["scroll"])!=Convert.ToInt32(((Dictionary<string,object>)a)["scroll"]));
            if(!backgroundOnly){
                app.ActiveWindow.VerticalPercentScrolled=0;app.Selection.SetRange(0,0);Thread.Sleep(100);
                range=doc.Range(20,21);app.ActiveWindow.GetPoint(out left,out top,out width,out height,range);
                point=new Native.Point(left+Math.Max(1,width/2),top+Math.Max(1,height/2));
                Action(w,"touch","click",point,point,snapshot,(a,b)=>Convert.ToInt32(((Dictionary<string,object>)b)["caret"])!=Convert.ToInt32(((Dictionary<string,object>)a)["caret"]));
                Action(w,"touch","scroll",point,point,snapshot,(a,b)=>Convert.ToInt32(((Dictionary<string,object>)b)["scroll"])!=Convert.ToInt32(((Dictionary<string,object>)a)["scroll"]));
            }
            Observe(w,"final",true);
        }finally{
            if(documentObject!=null)try{((dynamic)documentObject).Close(0);}catch(COMException){}
            if(applicationObject!=null)try{if(((dynamic)applicationObject).Documents.Count==0)((dynamic)applicationObject).Quit(0);}catch(COMException){}
            if(documentObject!=null)try{Marshal.FinalReleaseComObject(documentObject);}catch(COMException){}
            if(applicationObject!=null)try{Marshal.FinalReleaseComObject(applicationObject);}catch(COMException){}
        }
    }
    private static void Mcp(){
        if(Process.GetProcessesByName("AgentMouse").Length!=0)throw new Exception("Close the normal pointer app before running the isolated MCP fixture test");
        PracticeForm fixture=null;
        probe.Invoke((Action)delegate{fixture=new PracticeForm(engine){NoActivate=true,TopMost=true,Text="APCOMPAT-"+runId};fixture.Location=new Point(60,85);fixture.Show();});
        var w=new WindowItem{Handle=fixture.Handle,Pid=(uint)Process.GetCurrentProcess().Id,Name=fixture.Text,ProcessName="compatibility fixture"};
        engine.Mode="background";engine.Armed=true;
        Native.Point p=new Native.Point(80,26);Native.ClientToScreen(fixture.TestButton.Handle,ref p);
        string scenario=Path.Combine(output,"mcp-scenario.json");
        File.WriteAllText(scenario,Json.Write(new{target=w.Name,windowId=w.Handle.ToInt64().ToString(),x=p.X,y=p.Y}),new UTF8Encoding(false));
        string root=Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        try{
            using(var server=new Transport(engine,Transport.DefaultName)){
                server.Start();
                var process=Process.Start(new ProcessStartInfo("node","\""+Path.Combine(root,"test-sdk.mjs")+"\" --fixture \""+scenario+"\""){UseShellExecute=false,WorkingDirectory=root});
                if(!process.WaitForExit(25000)){process.Kill();throw new Exception("Official MCP client test timed out");}
                if(process.ExitCode!=0)throw new Exception("Official MCP client test failed");
                if(fixture.Clicks!=1)throw new Exception("MCP click did not update the real fixture button exactly once");
                if(engine.Armed)throw new Exception("MCP pause did not pause input");
                results.Add(new{backend="background",operation="official_sdk_mcp_click",effectObserved=true,classification="works_in_this_check",buttonClicks=fixture.Clicks,paused=!engine.Armed});
            }
        }finally{probe.Invoke((Action)delegate{fixture.Close();});}
    }
    private static void McpOpen(){
        if(Process.GetProcessesByName("AgentMouse").Length!=0)throw new Exception("Close the existing pointer app before the startup check");
        string root=Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        Native.Point before,after;Native.GetCursorPos(out before);IntPtr foreground=Native.GetForegroundWindow();
        var process=Process.Start(new ProcessStartInfo("node","\""+Path.Combine(root,"test-sdk.mjs")+"\" --open --report-dir \""+output+"\""){UseShellExecute=false,WorkingDirectory=root});
        try{
            if(!process.WaitForExit(20000)){process.Kill();throw new Exception("MCP startup test timed out");}
            if(process.ExitCode!=0)throw new Exception("MCP startup test failed");
            Native.GetCursorPos(out after);
            results.Add(new{operation="mcp_open_paused",effectObserved=true,classification=Native.GetForegroundWindow()==foreground?"works_in_this_check":"works_with_interference",
                foregroundChanged=Native.GetForegroundWindow()!=foreground,systemPointerChanged=before.X!=after.X||before.Y!=after.Y});
        }finally{
            var windows=Engine.Windows(IntPtr.Zero);foreach(var w in windows)if(w.Name=="AgentMouse · 独立指针原型"){
                try{if(string.Equals(Process.GetProcessById((int)w.Pid).MainModule.FileName,Path.Combine(root,"dist","AgentMouse.exe"),StringComparison.OrdinalIgnoreCase))Native.Post(w.Handle,0x10,UIntPtr.Zero,IntPtr.Zero);}catch(ArgumentException){}
            }
        }
    }
    private static void McpCross(){
        if(Process.GetProcessesByName("AgentMouse").Length!=0)throw new Exception("Close the normal pointer app before the cross-app fixture check");
        string root=Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        string url=new Uri(Path.Combine(root,"browser-fixture.html")).AbsoluteUri+"?id="+runId;
        string profile=Path.Combine(output,runId+"-profile");
        var chrome=Process.Start(new ProcessStartInfo(@"C:\Program Files\Google\Chrome\Application\chrome.exe","--no-first-run --no-default-browser-check --disable-background-networking --disable-component-update --force-device-scale-factor=1 --user-data-dir=\""+profile+"\" --app=\""+url+"\""){UseShellExecute=false});
        string name="APCOMPAT-"+runId+".txt",path=Path.Combine(output,name);
        File.WriteAllText(path,"First line of the local cross-app test.\r\nSecond line checks caret movement.\r\nThird line is read-only fixture content.\r\n",new UTF8Encoding(true));
        var notepad=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"notepad.exe"),"\""+path+"\""){UseShellExecute=false});
        WindowItem browserWindow=null,noteWindow=null;
        try{
            browserWindow=WaitWindow("APCOMPAT-"+runId+"|");Place(browserWindow);
            noteWindow=WaitWindow(name);var area=Screen.PrimaryScreen.WorkingArea;
            Native.SetWindowPos(noteWindow.Handle,new IntPtr(-1),area.Left+825,area.Top+65,720,650,0x10);Thread.Sleep(200);
            var observation=Observe(browserWindow,"cross-browser",true);var first=Colour(observation,Color.FromArgb(237,108,2));
            var doc=Document(noteWindow);var r=doc.Current.BoundingRectangle;var second=new Native.Point((int)r.Left+150,(int)r.Top+55);
            string scenario=Path.Combine(output,"mcp-cross-scenario.json");
            File.WriteAllText(scenario,Json.Write(new{target=browserWindow.Name,windowId=browserWindow.Handle.ToInt64().ToString(),x=first.X,y=first.Y,second=new{windowId=noteWindow.Handle.ToInt64().ToString(),x=second.X,y=second.Y}}));
            probe.Invoke((Action)delegate{probe.Activate();});engine.Mode="background";engine.Armed=true;
            using(var server=new Transport(engine,Transport.DefaultName)){
                server.Start();
                var client=Process.Start(new ProcessStartInfo("node","\""+Path.Combine(root,"test-sdk.mjs")+"\" --fixture \""+scenario+"\""){UseShellExecute=false,WorkingDirectory=root});
                if(!client.WaitForExit(25000)){client.Kill();throw new Exception("Cross-app MCP check timed out");}
                if(client.ExitCode!=0)throw new Exception("Cross-app MCP client check failed");
                Thread.Sleep(300);
                var browserState=BrowserState(browserWindow);var textState=(Dictionary<string,object>)TextState(doc);
                results.Add(new{operation="cross_app_effect_snapshot",browser=browserState,notepad=textState,firstPoint=new{x=first.X,y=first.Y},secondPoint=new{x=second.X,y=second.Y}});
                Observe(browserWindow,"cross-after-browser",true);Observe(noteWindow,"cross-after-notepad",true);
                if(browserState["c"]!=1||Convert.ToInt32(textState["caret"])<=0)throw new Exception("Cross-app input effects were not both observed: "+Json.Write(new{browser=browserState,notepad=textState}));
                results.Add(new{operation="one_enable_chrome_to_notepad_via_mcp",effectObserved=true,classification="works_in_this_check",distinctProcesses=browserWindow.Pid!=noteWindow.Pid});
            }
        }finally{CloseOwn(browserWindow,"APCOMPAT-"+runId);CloseOwn(noteWindow,name);if(!chrome.HasExited)chrome.CloseMainWindow();chrome.Dispose();notepad.Dispose();}
    }
    private static void CloseOwn(WindowItem w,string title){
        if(w!=null&&Native.IsWindow(w.Handle)&&Native.Title(w.Handle).IndexOf(title,StringComparison.OrdinalIgnoreCase)>=0)
            Native.Post(w.Handle,0x10,UIntPtr.Zero,IntPtr.Zero);
    }
}
}
