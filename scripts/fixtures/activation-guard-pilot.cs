// Explicitly scoped live experiment. No launch, focus, retry, or process termination.
using System;using System.IO;using System.Diagnostics;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Threading;using System.Web.Script.Serialization;
class GuardPilot {
 [StructLayout(LayoutKind.Sequential)]struct P{public int x,y;}
 [StructLayout(LayoutKind.Sequential)]struct R{public int left,top,right,bottom;}
 [StructLayout(LayoutKind.Sequential)]struct G{public uint size,flags;public IntPtr active,focus,capture,menu,move,caret;public R rect;}
 [StructLayout(LayoutKind.Sequential)]struct L{public uint size,time;}
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibrary(string s);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr m,string s);
 [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr m);
 [DllImport("user32.dll")]static extern IntPtr SetWindowsHookEx(int k,IntPtr p,IntPtr m,uint t);
 [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string s);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr w,out uint p);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr w,uint k);
 [DllImport("user32.dll")]static extern bool GetPhysicalCursorPos(out P p);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr w,out R r);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr w);
 [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint t,ref G g);
 [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref L l);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr w,uint m,IntPtr p,IntPtr l);
 [DllImport("user32.dll")]static extern IntPtr SendMessageTimeout(IntPtr w,uint m,IntPtr p,IntPtr l,uint f,uint time,out UIntPtr r);
 static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
 static long Send(IntPtr w,uint m){UIntPtr r;Check(SendMessageTimeout(w,m,IntPtr.Zero,IntPtr.Zero,2,300,out r)!=IntPtr.Zero,"message timed out; outcome unknown");return (long)r.ToUInt64();}
 static bool Above(IntPtr first,IntPtr second){for(var w=GetWindow(second,3);w!=IntPtr.Zero;w=GetWindow(w,3))if(w==first)return true;return false;}
 static int Main(string[] args){
   SetThreadDpiAwarenessContext(new IntPtr(-4));var json=new JavaScriptSerializer();var report=new Dictionary<string,object>();IntPtr module=IntPtr.Zero,cbt=IntPtr.Zero,messages=IntPtr.Zero,target=IntPtr.Zero;uint end=0,ready=0;bool armed=false;
   try{
     var config=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[0]));target=new IntPtr(Convert.ToInt64(config["window"]));uint pid;uint thread=GetWindowThreadProcessId(target,out pid);
     Check(pid==Convert.ToUInt32(config["pid"]),"window process changed");using(var process=Process.GetProcessById((int)pid))Check(process.StartTime.ToUniversalTime().ToFileTimeUtc()==Convert.ToInt64(config["started"]),"process instance changed");
     R rect;Check(GetWindowRect(target,out rect),"window rectangle unavailable");Check(rect.left==Convert.ToInt32(config["left"])&&rect.top==Convert.ToInt32(config["top"])&&rect.right-rect.left==Convert.ToInt32(config["width"])&&rect.bottom-rect.top==Convert.ToInt32(config["height"]),"window geometry changed");
     Check(IsWindowVisible(target)&&!IsIconic(target),"target is hidden or minimized; no input sent");
     var foreground=GetForegroundWindow();Check(foreground!=IntPtr.Zero&&foreground!=target&&Above(foreground,target),"target must be behind current foreground");
     G keyboardBefore=new G{size=(uint)Marshal.SizeOf(typeof(G))};Check(GetGUIThreadInfo(0,ref keyboardBefore)&&keyboardBefore.active==foreground&&keyboardBefore.focus!=IntPtr.Zero,"foreground keyboard focus unknown; no input sent");
     L input=new L{size=8};Check(GetLastInputInfo(ref input),"input activity unavailable");Check(unchecked((uint)Environment.TickCount-input.time)>1000,"human input active; no click sent");
     P before;Check(GetPhysicalCursorPos(out before),"pointer unavailable");
     string channel=(string)config["channel"];uint begin=RegisterWindowMessage(channel+".Begin"),stats=RegisterWindowMessage(channel+".Stats");end=RegisterWindowMessage(channel+".End");ready=RegisterWindowMessage(channel+".Ready");Check(begin!=0&&stats!=0&&end!=0&&ready!=0,"message registration failed");
     module=LoadLibrary((string)config["dll"]);Check(module!=IntPtr.Zero,"scoped DLL load failed");
     var cb=GetProcAddress(module,"GuardCbt");var cm=GetProcAddress(module,"GuardMessages");Check(cb!=IntPtr.Zero&&cm!=IntPtr.Zero,"guard exports missing");
     cbt=SetWindowsHookEx(5,cb,module,thread);messages=SetWindowsHookEx(4,cm,module,thread);Check(cbt!=IntPtr.Zero&&messages!=IntPtr.Zero,"guard hooks unavailable");
     armed=true;Send(target,begin);Check(Send(target,ready)==1,"guard not ready; no click sent");
     Check(GetForegroundWindow()==foreground,"foreground changed before input");L latest=new L{size=8};Check(GetLastInputInfo(ref latest)&&latest.time==input.time,"new input before dispatch");
     Check(IsWindowVisible(target)&&!IsIconic(target),"target visibility changed before input");
     int x=Convert.ToInt32(config["x"]),y=Convert.ToInt32(config["y"]);Check(x>=0&&y>=0&&x<rect.right-rect.left-20&&y<rect.bottom-rect.top-20,"point outside target client safety margin");
     IntPtr point=new IntPtr((y<<16)|(x&65535));bool down=false,up=false;
     try{Check(PostMessage(target,0x200,IntPtr.Zero,point),"move queue failed");down=PostMessage(target,0x201,new IntPtr(1),point);Check(down,"down queue failed");up=PostMessage(target,0x202,IntPtr.Zero,point);Check(up,"up queue failed");report["queued"]=true;}
     finally{if(down&&!up){PostMessage(target,0x1f,IntPtr.Zero,IntPtr.Zero);PostMessage(target,0x202,IntPtr.Zero,point);PostMessage(target,0x1f,IntPtr.Zero,IntPtr.Zero);}}
     bool focusChanged=false,raised=false,externalInput=false;for(int i=0;i<30;i++){Thread.Sleep(10);focusChanged|=GetForegroundWindow()!=foreground;raised|=Above(target,foreground);L current=new L{size=8};externalInput|=!GetLastInputInfo(ref current)||current.time!=input.time;if(focusChanged||raised||externalInput)break;}
     long counters=Send(target,stats);report["activationBlocks"]=counters&255;report["focusBlocks"]=(counters>>8)&255;report["positionBlocks"]=(counters>>16)&255;
     P after;GetPhysicalCursorPos(out after);latest=new L{size=8};GetLastInputInfo(ref latest);
     report["foregroundChangedDuringSamples"]=focusChanged;report["raisedDuringSamples"]=raised;report["pointerUnchangedAtChecks"]=before.x==after.x&&before.y==after.y;report["inputTimestampChanged"]=latest.time!=input.time;
     report["backgroundInterferenceFree"]=!focusChanged&&!raised&&!externalInput&&before.x==after.x&&before.y==after.y;
     G keyboardAfter=new G{size=(uint)Marshal.SizeOf(typeof(G))};bool keyboardKnown=GetGUIThreadInfo(0,ref keyboardAfter)&&keyboardAfter.active==GetForegroundWindow()&&keyboardAfter.focus!=IntPtr.Zero;
     report["keyboardFocusKnown"]=keyboardKnown;report["keyboardFocusChanged"]=keyboardKnown?(object)(keyboardAfter.focus!=keyboardBefore.focus):null;
     report["backgroundInterferenceFree"]=(bool)report["backgroundInterferenceFree"]&&keyboardKnown&&keyboardAfter.focus==keyboardBefore.focus;
     G gui=new G{size=(uint)Marshal.SizeOf(typeof(G))};Check(GetGUIThreadInfo(thread,ref gui),"thread state unavailable");report["captureAfter"]=gui.capture.ToInt64();report["businessResultVerified"]=false;
     report["ok"]=true;return 0;
   }catch(Exception error){report["ok"]=false;report["error"]=error.Message;return 1;}
   finally{
     if(armed){try{Send(target,end);report["guardDisabled"]=Send(target,ready)==0;}catch(Exception e){report["cleanupError"]=e.Message;}}
     if(messages!=IntPtr.Zero)report["messageHookRemoved"]=UnhookWindowsHookEx(messages);if(cbt!=IntPtr.Zero)report["cbtHookRemoved"]=UnhookWindowsHookEx(cbt);if(module!=IntPtr.Zero)FreeLibrary(module);
     Console.WriteLine(json.Serialize(report));
   }
 }
}
