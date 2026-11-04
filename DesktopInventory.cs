using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Automation;

namespace AgentPointer {
internal static class DesktopInventory {
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
        try{using(var process=Process.GetProcessById((int)pid))if(Quarantined(process.ProcessName)&&(accessibility||NativePilot(pid)==null))throw new Fault("target_quarantined","微信自动化因卡死事件已临时隔离；只有显式授权的当前进程可进行原生截图/点击试验，控件查询仍被禁止。");}
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
        if(Native.IsIconic(target.Handle))throw new Fault("unsupported","Minimized windows cannot yet be restored without activation; no foreground fallback.");
        int cloaked;if(Native.DwmGetWindowAttribute(target.Handle,14,out cloaked,4)==0&&cloaked!=0)throw new Fault("unsupported","Window belongs to a hidden desktop.");
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
