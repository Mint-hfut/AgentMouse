using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AgentPointer {
internal static class SelfTest {
    internal static int StateOnly(){
        var e=new Engine();
        Assert(!e.ShowPointer,"Paused startup must hide the overlay");
        e.Armed=true;Assert(e.ShowPointer,"Enabled input must show the requested overlay");
        e.Pause("state check");Assert(!e.ShowPointer,"Pause must hide the overlay");
        e.Armed=true;e.PointerVisible=false;Assert(!e.ShowPointer,"Display preference must stay effective");
        e.PointerVisible=true;e.Stopped=true;Assert(!e.ShowPointer,"Stopped input must hide the overlay");
        var status=Obj(e.Status());Assert(!(bool)Obj(status["pointer"])["visible"],"Status must agree with overlay state");
        e.X=120;e.Y=240;e.Human.TrackPointer(new Native.Point(10,20));e.Human.TrackPointer(new Native.Point(30,40));
        Assert(e.X==120&&e.Y==240,"Human pointer tracking must not change Agent coordinates");
        Assert(e.Human.Sequence==1,"Human pointer motion must be detected");
        Assert(IntPtr.Size==8&&Marshal.SizeOf(typeof(Native.PointerInfo))==96&&Marshal.SizeOf(typeof(Native.TouchInfo))==144,"Native x64 structure layout");
        Expect("invalid_json",()=>Json.Read("null"));Expect("invalid_json",()=>Json.Read("{"));
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
