// Owned-window experiment only. No hooks are installed in other applications.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Web.Script.Serialization;

internal static class ActivationExperiment {
    internal delegate IntPtr Hook(int code,IntPtr w,IntPtr l);
    [StructLayout(LayoutKind.Sequential)] internal struct WindowPos { internal IntPtr window,after; internal int x,y,cx,cy; internal uint flags; }
    [DllImport("user32.dll")] internal static extern IntPtr SetWindowsHookEx(int kind,Hook callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] internal static extern IntPtr SetActiveWindow(IntPtr w);
    [DllImport("user32.dll")] internal static extern IntPtr SetFocus(IntPtr w);
    [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr w,uint kind);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr w,uint kind);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr w,uint msg,IntPtr wp,IntPtr lp);
    internal static Guard guard;
    internal static bool protectOrder;
    internal sealed class Guard:IDisposable {
        internal IntPtr target; internal int activationBlocks,focusBlocks,orderBlocks;
        long expires; IntPtr handle; Hook callback;
        internal bool Active {get{return handle!=IntPtr.Zero&&Stopwatch.GetTimestamp()<expires;}}
        internal Guard(IntPtr target,int milliseconds){
            this.target=target;expires=Stopwatch.GetTimestamp()+(long)(Stopwatch.Frequency*(milliseconds/1000.0));
            callback=Filter;handle=SetWindowsHookEx(5,callback,IntPtr.Zero,GetCurrentThreadId());
            if(handle==IntPtr.Zero)throw new Exception("CBT hook installation failed");
        }
        IntPtr Filter(int code,IntPtr w,IntPtr l){
            if(code>=0&&Active&&(w==target||GetAncestor(w,2)==target)){
                if(code==5){activationBlocks++;return new IntPtr(1);}
                if(code==9){focusBlocks++;return new IntPtr(1);}
            }
            return CallNextHookEx(handle,code,w,l);
        }
        public void Dispose(){if(handle!=IntPtr.Zero){if(!UnhookWindowsHookEx(handle))throw new Exception("CBT hook removal failed");handle=IntPtr.Zero;}GC.KeepAlive(callback);}
    }
    internal sealed class TestWindow:Form {
        internal bool guarded;
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override void WndProc(ref Message message){
            if(message.Msg==0x46&&guarded&&protectOrder&&guard!=null&&guard.Active){
                var pos=(WindowPos)Marshal.PtrToStructure(message.LParam,typeof(WindowPos));
                if((pos.flags&4)==0)guard.orderBlocks++;
                pos.flags|=0x14;Marshal.StructureToPtr(pos,message.LParam,false);
            }
            base.WndProc(ref message);
        }
    }
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    static bool Above(IntPtr first,IntPtr second){for(var p=GetWindow(second,3);p!=IntPtr.Zero;p=GetWindow(p,3))if(p==first)return true;return false;}
    static void PlaceBehind(IntPtr target,IntPtr cover){Check(SetWindowPos(cover,new IntPtr(1),0,0,0,0,0x13),"cover placement");Check(SetWindowPos(target,new IntPtr(1),0,0,0,0,0x13),"target placement");Check(Above(cover,target),"fixture order");}
    static void RaiseAboveCover(IntPtr target,IntPtr cover){var before=GetWindow(cover,3);Check(before!=IntPtr.Zero,"missing insertion anchor");Check(SetWindowPos(target,before,0,0,0,0,0x13),"raise request");}
    [STAThread] static int Main(){
        var report=new Dictionary<string,object>();IntPtr foreground=GetForegroundWindow();
        try{
            Application.EnableVisualStyles();
            using(var target=new TestWindow{Text="AgentMouse guard experiment",guarded=true,Bounds=new Rectangle(80,80,320,200),StartPosition=FormStartPosition.Manual})
            using(var cover=new TestWindow{Text="AgentMouse guard cover",Bounds=new Rectangle(80,80,320,200),StartPosition=FormStartPosition.Manual}){
                int clicks=0;var button=new Button{Text="Owned test",Bounds=new Rectangle(30,40,200,60)};target.Controls.Add(button);
                button.Click+=delegate{clicks++;SetActiveWindow(target.Handle);SetFocus(button.Handle);};
                target.Show();cover.Show();PlaceBehind(target.Handle,cover.Handle);Application.DoEvents();
                SetActiveWindow(target.Handle);Check(GetActiveWindow()==target.Handle,"activation positive control unavailable");
                SetFocus(button.Handle);Check(GetFocus()==button.Handle,"focus positive control unavailable");
                SetActiveWindow(cover.Handle);SetFocus(cover.Handle);PlaceBehind(target.Handle,cover.Handle);
                report["activationPositiveControl"]=true;
                using(guard=new Guard(target.Handle,5000)){
                    var point=new IntPtr((20<<16)|20);
                    SendMessage(button.Handle,0x201,new IntPtr(1),point);SendMessage(button.Handle,0x202,IntPtr.Zero,point);
                    report["rawMouseButtonCallbacks"]=clicks;
                    // WinForms can reject synthetic mouse messages based on real cursor state.
                    // This experiment isolates activation interception from input compatibility.
                    int beforeBusiness=clicks;button.PerformClick();
                    Check(clicks==beforeBusiness+1,"business callback did not execute once");Check(GetActiveWindow()==cover.Handle,"CBT did not block activation");Check(GetFocus()!=button.Handle,"CBT did not block focus");
                    Check(guard.activationBlocks>0&&guard.focusBlocks>0,"missing interception evidence");
                    RaiseAboveCover(target.Handle,cover.Handle);Check(Above(target.Handle,cover.Handle),"CBT-only layer positive control unavailable");
                    report["cbtAloneAllowsZOrderChange"]=true;PlaceBehind(target.Handle,cover.Handle);
                    protectOrder=true;RaiseAboveCover(target.Handle,cover.Handle);Check(Above(cover.Handle,target.Handle),"position guard did not preserve order");
                    Check(guard.orderBlocks>0,"no position interception observed");
                    report["buttonCallbacks"]=clicks;report["activationBlocks"]=guard.activationBlocks;report["focusBlocks"]=guard.focusBlocks;report["orderBlocks"]=guard.orderBlocks;
                }
                protectOrder=false;SetActiveWindow(target.Handle);Check(GetActiveWindow()==target.Handle,"activation not restored after dispose");
                SetActiveWindow(cover.Handle);
                using(guard=new Guard(target.Handle,60)){
                    System.Threading.Thread.Sleep(90);SetActiveWindow(target.Handle);Check(GetActiveWindow()==target.Handle,"expired guard blocked activation");
                }
                report["disposeRestoresActivation"]=true;report["expiryRestoresActivation"]=true;
                report["foregroundUnchangedAtFinalCheck"]=GetForegroundWindow()==foreground;
                Check(GetForegroundWindow()==foreground,"foreground changed during experiment");
            }
            report["ok"]=true;report["scope"]="same-process owned windows; no cross-process DLL or WeChat validation";
            Console.WriteLine(new JavaScriptSerializer().Serialize(report));return 0;
        }catch(Exception error){report["ok"]=false;report["error"]=error.Message;Console.WriteLine(new JavaScriptSerializer().Serialize(report));return 1;}
        finally{if(guard!=null)guard.Dispose();}
    }
}
