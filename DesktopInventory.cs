using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Automation;
using System.Runtime.InteropServices;

namespace AgentPointer {
internal static class DesktopInventory {
    [StructLayout(LayoutKind.Sequential)] private struct TrayIdentifier {internal uint Size;internal IntPtr Window;internal uint Id;internal Guid Guid;}
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref TrayIdentifier icon,out Native.Rect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window,string name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    internal static object RestoreFromTray(IntPtr main,uint pid){
        uint owner;
        uint thread=Native.GetWindowThreadProcessId(main,out owner);
        if(owner!=pid)throw new Fault("window_changed","Application owner changed before restore.");
        var candidates=new List<IntPtr>();
        Native.EnumWindows(delegate(IntPtr window,IntPtr unused){
            uint process;
            if(Native.GetWindowThreadProcessId(window,out process)==thread&&process==pid)candidates.Add(window);
            return true;
        },IntPtr.Zero);
        if(candidates.Count>64)throw new Fault("application_restore_required","Application has too many potential tray endpoints for a bounded restore.");
        IntPtr foreground=Native.GetForegroundWindow();
        var keyboardBefore=ReadForegroundInput();
        using(var guard=new ActivationGuard(main,true)){
            foreach(var window in candidates){
                if(GetProp(window,"AgentMouse.TrayCallback.v2")==IntPtr.Zero)
                    Native.Send(window,RegisterWindowMessage("TaskbarCreated"),UIntPtr.Zero,IntPtr.Zero);
            }
            IntPtr tray=IntPtr.Zero;
            long callback=0,version=0;
            uint identifier=0;
            Native.Rect iconBounds=new Native.Rect();
            foreach(var window in candidates){
                long message=GetProp(window,"AgentMouse.TrayCallback.v2").ToInt64();
                long id=GetProp(window,"AgentMouse.TrayIdentifier.v2").ToInt64()-1;
                long protocol=GetProp(window,"AgentMouse.TrayVersion.v2").ToInt64()-1;
                if(message<0x400||message>0xffff||id<0||id>uint.MaxValue||protocol<0||protocol>4||GetProp(window,"AgentMouse.TrayAmbiguous.v2")!=IntPtr.Zero)continue;
                var icon=new TrayIdentifier{Size=(uint)Marshal.SizeOf(typeof(TrayIdentifier)),Window=window,Id=(uint)id};
                Native.Rect bounds;
                if(Shell_NotifyIconGetRect(ref icon,out bounds)<0)continue;
                if(tray!=IntPtr.Zero)throw new Fault("application_restore_required","Multiple verified tray icons belong to this application; an unambiguous restore entry is required.");
                tray=window;callback=message;identifier=(uint)id;version=protocol;iconBounds=bounds;
            }
            if(tray==IntPtr.Zero)throw new Fault("application_restore_required","This application has no verified tray activation entry on the target UI thread.");
            if(version==4&&identifier>65535)throw new Fault("application_restore_required","The tray identifier exceeds the supported version-4 event range.");
            UIntPtr parameter=version==4?new UIntPtr(unchecked((uint)(((iconBounds.Top+iconBounds.Bottom)/2<<16)|((iconBounds.Left+iconBounds.Right)/2&65535)))):new UIntPtr(identifier);
            IntPtr activation=version==4?new IntPtr(unchecked((int)((identifier<<16)|0x400))):new IntPtr(0x202);
            Native.Post(tray,(uint)callback,parameter,activation);
            var wait=Stopwatch.StartNew();
            while((!Native.IsWindowVisible(main)||Native.IsIconic(main))&&wait.ElapsedMilliseconds<1200)System.Threading.Thread.Sleep(20);
            if(!Native.IsWindowVisible(main)||Native.IsIconic(main))throw new Fault("application_restore_required","The application's tray activation did not restore its main window.");
            var keyboardAfter=ReadForegroundInput();
            if(Native.GetForegroundWindow()!=foreground||KeyboardFocusChanged(keyboardBefore,keyboardAfter)==true)throw new Fault("foreground_interference","Application restore changed the user's foreground or keyboard focus.");
            var restored=Engine.Windows(IntPtr.Zero).Find(item=>item.Handle==main&&item.Pid==pid);
            if(restored==null)throw new Fault("window_changed","Application window identity changed during restore.");
            return new{window=restored.Export(),foregroundUnchanged=true,keyboardFocusChanged=KeyboardFocusChanged(keyboardBefore,keyboardAfter),restoreMethod="verified-application-tray",activationProtection=guard.Receipt()};
        }
    }
    internal sealed class ForegroundInput {
        internal IntPtr Window,Active,Focus;
        internal bool Known;
        internal object Export(){return new{foregroundWindowId=Window.ToInt64().ToString(),activeWindowId=Active.ToInt64().ToString(),keyboardFocusWindowId=Focus.ToInt64().ToString(),keyboardFocusKnown=Known};}
    }
    internal static ForegroundInput ReadForegroundInput(){
        var result=new ForegroundInput{Window=Native.GetForegroundWindow()};
        var info=new Native.GuiThreadInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.GuiThreadInfo))};
        bool read=Native.GetGUIThreadInfo(0,ref info);
        result.Active=info.Active;result.Focus=info.Focus;
        // Activation transitions can return stale handles even when the API succeeds.
        result.Known=read&&result.Window!=IntPtr.Zero&&Native.GetForegroundWindow()==result.Window&&info.Active==result.Window&&info.Focus!=IntPtr.Zero&&Native.IsWindow(info.Focus)&&Native.GetAncestor(info.Focus,2)==result.Window;
        return result;
    }
    internal static bool? KeyboardFocusChanged(ForegroundInput before,ForegroundInput after){return before.Known&&after.Known?(bool?)(before.Focus!=after.Focus):null;}
    internal static object InputState(IntPtr window){
        uint pid;uint thread=Native.GetWindowThreadProcessId(window,out pid);
        var info=new Native.GuiThreadInfo{Size=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.GuiThreadInfo))};
        if(!Native.GetGUIThreadInfo(thread,ref info))return null;
        return new{threadId=thread,captureWindowId=info.Capture.ToInt64().ToString(),activeWindowId=info.Active.ToInt64().ToString(),focusWindowId=info.Focus.ToInt64().ToString(),flags=info.Flags};
    }
    internal static bool Quarantined(string name){
        name=name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?name.Substring(0,name.Length-4):name;
        return name.Equals("Weixin",StringComparison.OrdinalIgnoreCase)||name.Equals("WeChat",StringComparison.OrdinalIgnoreCase)||name.Equals("WeChatAppEx",StringComparison.OrdinalIgnoreCase);
    }
    internal static bool PilotMatches(string grant,uint pid,long started){return grant==pid.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+started.ToString(System.Globalization.CultureInfo.InvariantCulture);}
    internal static string NativePilot(uint pid){
        string grant=Environment.GetEnvironmentVariable("AGENTMOUSE_NATIVE_PILOT");if(string.IsNullOrEmpty(grant))return null;
        try{using(var process=Process.GetProcessById((int)pid))return PilotMatches(grant,pid,process.StartTime.ToUniversalTime().Ticks)?grant:null;}
        catch(ArgumentException){return null;}catch(System.ComponentModel.Win32Exception){return null;}
    }
    internal static void RequireAutomationAllowed(uint pid,bool accessibility=false){
        try{using(var process=Process.GetProcessById((int)pid))if(Quarantined(process.ProcessName)&&(accessibility||(!ActivationGuard.Available&&NativePilot(pid)==null)))throw new Fault("target_quarantined","This target requires the installed activation guard for native operations. Accessibility inspection remains quarantined.");}
        catch(ArgumentException){throw new Fault("window_changed","Target process no longer exists.");}
    }
    internal static object Query(Dictionary<string,object> request){
        string method=Json.Text(request,"method","");
        if(method=="list_windows")return Engine.Windows(IntPtr.Zero).ConvertAll<object>(w=>w.Export());
        if(method=="show_window")return ShowBackground(Json.Text(request,"windowId",""));
        if(method=="inspect_window")return Inspect(Json.Text(request,"windowId",""));
        if(method!="list_apps")throw new Fault("unknown_method","Unknown read-only desktop query.");
        string query=Json.Text(request,"query","").Trim();
        if(query.Length==0||query.Length>128)throw new Fault("invalid_argument","Provide an application name (1-128 characters).");
        var windows=Engine.Windows(IntPtr.Zero);var found=new List<object>();
        foreach(var process in Process.GetProcesses())using(process){
            try{
                string name=process.ProcessName;int pid=process.Id;
                var owned=windows.FindAll(w=>w.Pid==(uint)pid);
                bool match=Matches(name,query)||owned.Exists(w=>Matches(w.Name,query));
                if(match)found.Add(new{pid=pid,process=name,running=true,windows=owned.ConvertAll<object>(w=>w.Export()),note=owned.Count==0?"Running process without a listed window; may be in the tray. Do not relaunch automatically.":null});
            }catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        }
        return new{query=query,processes=found,notFoundDoesNotProveNotInstalled=true};
    }
    private static object ShowBackground(string windowId){
        long id;if(!long.TryParse(windowId,out id)||id<=0)throw new Fault("invalid_argument","An exact existing windowId is required.");
        var target=Engine.Windows(IntPtr.Zero).Find(w=>w.Handle.ToInt64()==id);
        if(target==null)throw new Fault("window_missing","Window no longer exists; list apps again.");
        RequireAutomationAllowed(target.Pid);
        if(Native.GetAncestor(target.Handle,2)!=target.Handle)throw new Fault("invalid_argument","A top-level window is required.");
        if(Native.IsIconic(target.Handle)){
            return RestoreFromTray(target.Handle,target.Pid);

        }
        int cloaked;if(Native.DwmGetWindowAttribute(target.Handle,14,out cloaked,4)==0&&cloaked!=0)throw new Fault("unsupported","Window belongs to a hidden desktop.");
        // Native visibility does not restore Qt's hidden widget state.
        if(!Native.IsWindowVisible(target.Handle)){
            return RestoreFromTray(target.Handle,target.Pid);

        }
        IntPtr foreground=Native.GetForegroundWindow();
        if(!Native.IsWindowVisible(target.Handle)&&!Native.SetWindowPos(target.Handle,new IntPtr(1),0,0,0,0,0x53))throw new Fault("window_show_failed","Could not show the existing window without activation.");
        System.Threading.Thread.Sleep(150);
        uint pid;Native.GetWindowThreadProcessId(target.Handle,out pid);if(pid!=target.Pid||!Native.IsWindow(target.Handle))throw new Fault("window_changed","Window identity changed while showing it.");
        if(Native.GetForegroundWindow()!=foreground)throw new Fault("foreground_interference","Foreground changed while showing the window; inspect before continuing.");
        return new{window=target.Export(),foregroundUnchanged=true,note="Existing window shown at the bottom without activation. Take a fresh background screenshot before input."};
    }
    internal static bool Matches(string value,string query){
        if(value.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)return true;
        bool wechat=query=="微信"||query.Equals("wechat",StringComparison.OrdinalIgnoreCase)||query.Equals("weixin",StringComparison.OrdinalIgnoreCase);
        return wechat&&(value.IndexOf("wechat",StringComparison.OrdinalIgnoreCase)>=0||value.IndexOf("weixin",StringComparison.OrdinalIgnoreCase)>=0||value.Contains("微信"));
    }
    private static object Inspect(string windowId){
        long id;if(!long.TryParse(windowId,out id)||id<=0)throw new Fault("invalid_argument","A native windowId is required.");
        var window=Engine.Windows(IntPtr.Zero).Find(w=>w.Handle.ToInt64()==id);
        if(window==null)throw new Fault("window_missing","Window no longer exists; list windows/apps again.");
        RequireAutomationAllowed(window.Pid,true);
        var nodes=new List<object>();bool truncated=false;var started=Stopwatch.StartNew();
        try{
            var root=AutomationElement.FromHandle(window.Handle);var queue=new Queue<Tuple<AutomationElement,int,int>>();
            queue.Enqueue(Tuple.Create(root,0,-1));
            while(queue.Count>0){
                if(nodes.Count>=200||started.ElapsedMilliseconds>2000){truncated=true;break;}
                var item=queue.Dequeue();var element=item.Item1;var state=element.Current;
                // Do not enter another application's provider or collect password values.
                if(state.ProcessId!=(int)window.Pid)continue;
                int index=nodes.Count;var r=state.BoundingRectangle;if(r.IsEmpty)r=new System.Windows.Rect(0,0,0,0);
                nodes.Add(new{index=index,parentIndex=item.Item3,name=state.IsPassword?"[password]":state.Name,controlType=state.ControlType.ProgrammaticName,automationId=state.AutomationId,enabled=state.IsEnabled,offscreen=state.IsOffscreen,password=state.IsPassword,bounds=new{x=r.X,y=r.Y,width=r.Width,height=r.Height}});
                if(item.Item2>=8){truncated=true;continue;}
                var child=TreeWalker.ControlViewWalker.GetFirstChild(element);
                while(child!=null){
                    if(queue.Count+nodes.Count>=400||started.ElapsedMilliseconds>2000){truncated=true;break;}
                    queue.Enqueue(Tuple.Create(child,item.Item2+1,index));child=TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
            }
        }catch(ElementNotAvailableException){throw new Fault("window_changed","Accessibility elements changed; inspect again.");}
        uint pid;Native.GetWindowThreadProcessId(window.Handle,out pid);
        if(!Native.IsWindow(window.Handle)||pid!=window.Pid)throw new Fault("window_changed","Window identity changed during inspection.");
        return new{window=window.Export(),source="Windows.UIAutomation",readOnly=true,nodes=nodes,truncated=truncated,note="Control names/roles only; no activation, focus, invocation or input. Empty trees may mean the app exposes no accessibility provider. These nodes are not mouse observation tokens."};
    }
}
}
