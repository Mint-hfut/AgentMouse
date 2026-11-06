using System;using System.Diagnostics;using System.Runtime.InteropServices;using System.Web.Script.Serialization;using System.Collections.Generic;
class GuardDriver {
 [StructLayout(LayoutKind.Sequential)]struct Point {public int x,y;}
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int l,t,r,b;}
 [StructLayout(LayoutKind.Sequential)]struct Gui {public uint size,flags;public IntPtr active,focus,capture,menu,move,caret;public Rect rect;}
 [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref Gui info);
 [DllImport("user32.dll")]static extern bool GetPhysicalCursorPos(out Point point);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibrary(string file);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
 [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
 [DllImport("user32.dll")]static extern IntPtr SetWindowsHookEx(int kind,IntPtr callback,IntPtr module,uint thread);
 [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr w,uint command);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr w,uint msg,IntPtr wp,IntPtr lp);
 [DllImport("user32.dll")]static extern IntPtr SendMessageTimeout(IntPtr w,uint msg,IntPtr wp,IntPtr lp,uint flags,uint timeout,out UIntPtr result);
 static long Send(IntPtr w,uint msg){UIntPtr result;Check(SendMessageTimeout(w,msg,IntPtr.Zero,IntPtr.Zero,2,500,out result)!=IntPtr.Zero,"target message timeout");return (long)result.ToUInt64();}
 static void Restore(IntPtr w,IntPtr foreground){UIntPtr result;Check(SendMessageTimeout(w,0x8000+314,foreground,IntPtr.Zero,2,500,out result)!=IntPtr.Zero,"fixture foreground cleanup timeout");var wait=Stopwatch.StartNew();while(GetForegroundWindow()!=foreground&&wait.ElapsedMilliseconds<500)System.Threading.Thread.Sleep(10);}
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
 static bool Above(IntPtr first,IntPtr second){for(var w=GetWindow(second,3);w!=IntPtr.Zero;w=GetWindow(w,3))if(w==first)return true;return false;}
 static void Click(IntPtr button,IntPtr target,long expected){
   var point=new IntPtr((20<<16)|20);bool down=false,up=false;
   try{down=PostMessage(button,0x201,new IntPtr(1),point);Check(down,"down queue failed");up=PostMessage(button,0x202,IntPtr.Zero,point);Check(up,"up queue failed");}
   finally{if(down&&!up){PostMessage(button,0x1f,IntPtr.Zero,IntPtr.Zero);PostMessage(button,0x202,IntPtr.Zero,point);}}
   var wait=Stopwatch.StartNew();while(Send(target,0x8000+310)!=expected&&wait.ElapsedMilliseconds<500)System.Threading.Thread.Sleep(10);
   Check(Send(target,0x8000+310)==expected,"native button click not delivered exactly once");
 }
 static int Main(string[] args){
   var result=new Dictionary<string,object>();Process child=null;IntPtr dll=IntPtr.Zero,cbt=IntPtr.Zero,messages=IntPtr.Zero,w=IntPtr.Zero;var foreground=GetForegroundWindow();
   try{
     child=Process.Start(new ProcessStartInfo(args[0]){Arguments=args.Length>2?"\""+args[2]+"\"":"",UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true});
     var ready=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(child.StandardOutput.ReadLine());
     w=new IntPtr(Convert.ToInt64(ready["window"]));var cover=new IntPtr(Convert.ToInt64(ready["cover"]));var button=new IntPtr(Convert.ToInt64(ready["button"]));uint pid;uint thread=GetWindowThreadProcessId(w,out pid);
     Check(!child.HasExited&&pid==child.Id&&thread==Convert.ToUInt32(ready["thread"]),"owned target identity mismatch");
     result["foregroundBefore"]=foreground.ToInt64();result["foregroundAfterStartup"]=GetForegroundWindow().ToInt64();
     Click(button,w,1);Check(Send(w,0x8000+312)==1,"activation positive control failed");Check(Send(w,0x8000+313)==1,"focus positive control failed");Check(Above(w,cover),"z-order positive control failed");
     result["foregroundAfterPositiveControl"]=GetForegroundWindow().ToInt64();
     Send(w,0x8000+311);Check(Above(cover,w),"target reset failed");
     Restore(w,foreground);Check(GetForegroundWindow()==foreground,"cannot restore foreground after positive control");
     dll=LoadLibrary(args[1]);Check(dll!=IntPtr.Zero,"DLL load failed");
     cbt=SetWindowsHookEx(5,GetProcAddress(dll,"GuardCbt"),dll,thread);messages=SetWindowsHookEx(4,GetProcAddress(dll,"GuardMessages"),dll,thread);Check(cbt!=IntPtr.Zero&&messages!=IntPtr.Zero,"thread hook installation failed");
     var guardedForeground=GetForegroundWindow();result["guardedForegroundBefore"]=guardedForeground.ToInt64();
     var keyboardBefore=new Gui{size=(uint)Marshal.SizeOf(typeof(Gui))};Check(GetGUIThreadInfo(0,ref keyboardBefore)&&keyboardBefore.active==guardedForeground&&keyboardBefore.focus!=IntPtr.Zero,"foreground keyboard state unavailable before action");
     Check(guardedForeground!=w&&guardedForeground!=cover,"fixture owns foreground before guarded action");
     Send(w,0x8000+300);Check(Send(w,0x8000+303)==1,"guard did not acknowledge readiness; no input sent");
     if(Environment.GetEnvironmentVariable("AGENTMOUSE_EARLY_API_TEST")=="1"){
       // Let installation finish, then establish the measured layer baseline without activation.
       Send(w,0x8000+301);Send(w,0x8000+317);Send(w,0x8000+300);Check(Send(w,0x8000+303)==1,"guard rearm failed");
     }
     result["coverAboveTargetAtArm"]=Above(cover,w);
     Check(Above(cover,w),"layer baseline changed before input");
     if(Environment.GetEnvironmentVariable("AGENTMOUSE_EARLY_API_TEST")=="1"){
       Check(Send(w,0x8000+318)==1,"guard blocked application child-window rendering order");
       if(args.Length==2){Check(Send(w,0x8000+319)==1,"tray protocol negotiation or deletion metadata failed");result["trayVersionNegotiationVerified"]=true;}
     }
     Point pointerBefore,pointerAfter;Check(GetPhysicalCursorPos(out pointerBefore),"pointer read failed");
     Send(w,0x8000+315);Click(button,w,2);Send(w,0x8000+316);
     Check(GetPhysicalCursorPos(out pointerAfter),"pointer read failed");
     result["guardedPointerUnchanged"]=pointerBefore.x==pointerAfter.x&&pointerBefore.y==pointerAfter.y;
     Check((bool)result["guardedPointerUnchanged"],"pointer changed during guarded action; inspect external activity");
     result["guardedForegroundAfter"]=GetForegroundWindow().ToInt64();Check(GetForegroundWindow()==guardedForeground,"foreground changed during guarded input");
     long stats=Send(w,0x8000+302);result["activationBlocks"]=stats&255;result["focusBlocks"]=(stats>>8)&255;result["positionBlocks"]=(stats>>16)&255;
     Check((stats&255)>0&&((stats>>8)&255)>0&&((stats>>16)&255)>0,"missing interception counters");
     var keyboardAfter=new Gui{size=(uint)Marshal.SizeOf(typeof(Gui))};Check(GetGUIThreadInfo(0,ref keyboardAfter)&&keyboardAfter.active==guardedForeground&&keyboardAfter.focus==keyboardBefore.focus,"foreground keyboard focus changed");
     result["foregroundKeyboardFocusPreserved"]=true;
     result["targetPrivateQueueActive"]=Send(w,0x8000+312)==1;result["targetPrivateQueueFocused"]=Send(w,0x8000+313)==1;
     if(Environment.GetEnvironmentVariable("AGENTMOUSE_EARLY_API_TEST")!="1")Check(Send(w,0x8000+312)==0&&Send(w,0x8000+313)==0,"CBT activation/focus escaped");
     result["coverAboveTargetAfter"]=Above(cover,w);result["targetAboveUserForeground"]=Above(w,guardedForeground);
     Check(Above(cover,w),"guarded z-order escaped");
     Send(w,0x8000+301);Click(button,w,3);Check(Send(w,0x8000+312)==1&&Above(w,cover),"explicit disarm did not restore behavior");
     Send(w,0x8000+311);Send(w,0x8000+300);System.Threading.Thread.Sleep(1200);Click(button,w,4);Check(Send(w,0x8000+312)==1&&Above(w,cover),"expiry did not restore behavior");
     Send(w,0x8000+311);Send(w,0x8000+300);
     Check(UnhookWindowsHookEx(messages),"message hook cleanup failed");messages=IntPtr.Zero;
     Check(UnhookWindowsHookEx(cbt),"CBT hook cleanup failed");cbt=IntPtr.Zero;
     System.Threading.Thread.Sleep(1200);Click(button,w,5);Check(Send(w,0x8000+312)==1&&Above(w,cover),"lease did not expire after external hooks removed");
     result["foregroundAfterRecoveryControls"]=GetForegroundWindow().ToInt64();
     result["ok"]=true;result["nativeButtonClicks"]=5;result["separateProcessHook"]=true;result["explicitDisarmRestored"]=true;result["expiryRestored"]=true;result["externalHookRemovalRecovered"]=true;result["guardedForegroundUnchanged"]=true;
     return 0;
   }catch(Exception e){result["ok"]=false;result["error"]=e.Message;return 1;}
   finally{
     if(w!=IntPtr.Zero&&child!=null&&!child.HasExited){try{Send(w,0x8000+301);Restore(w,foreground);}catch{}}
     if(messages!=IntPtr.Zero)UnhookWindowsHookEx(messages);if(cbt!=IntPtr.Zero)UnhookWindowsHookEx(cbt);
     if(child!=null&&!child.HasExited){PostMessage(w,0x10,IntPtr.Zero,IntPtr.Zero);if(!child.WaitForExit(2000)){child.Kill();child.WaitForExit();}}
     if(dll!=IntPtr.Zero)FreeLibrary(dll);if(child!=null)child.Dispose();Console.WriteLine(new JavaScriptSerializer().Serialize(result));
   }
 }
}
