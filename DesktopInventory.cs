using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Automation;

namespace AgentPointer {
internal static class DesktopInventory {
    internal static object Query(Dictionary<string,object> request){
        string method=Json.Text(request,"method","");
        if(method=="list_windows")return Engine.Windows(IntPtr.Zero).ConvertAll<object>(w=>w.Export());
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
    internal static bool Matches(string value,string query){
        if(value.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)return true;
        bool wechat=query=="微信"||query.Equals("wechat",StringComparison.OrdinalIgnoreCase)||query.Equals("weixin",StringComparison.OrdinalIgnoreCase);
        return wechat&&(value.IndexOf("wechat",StringComparison.OrdinalIgnoreCase)>=0||value.IndexOf("weixin",StringComparison.OrdinalIgnoreCase)>=0||value.Contains("微信"));
    }
    private static object Inspect(string windowId){
        long id;if(!long.TryParse(windowId,out id)||id<=0)throw new Fault("invalid_argument","A native windowId is required.");
        var window=Engine.Windows(IntPtr.Zero).Find(w=>w.Handle.ToInt64()==id);
        if(window==null)throw new Fault("window_missing","Window no longer exists; list windows/apps again.");
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
