using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentPointer {
internal sealed class Transport : IDisposable {
    private readonly Engine engine;
    private readonly string name;
    private Thread worker;
    private volatile bool stopping;
    private NamedPipeServerStream active;
    private EventWaitHandle pauseEvent;
    private RegisteredWaitHandle pauseWatch;
    internal static string DefaultName {get{return "agentmouse-"+WindowsIdentity.GetCurrent().User.Value;}}
    internal Transport(Engine core,string pipe){engine=core;name=pipe;}
    internal void Start(){
        var security=new EventWaitHandleSecurity();security.SetAccessRuleProtection(true,false);
        security.AddAccessRule(new EventWaitHandleAccessRule(WindowsIdentity.GetCurrent().User,EventWaitHandleRights.FullControl,AccessControlType.Allow));
        bool created;pauseEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\"+name+"-pause",out created,security);
        pauseWatch=ThreadPool.RegisterWaitForSingleObject(pauseEvent,delegate(object state,bool timeout){if(!stopping)engine.Pause("Agent 已请求暂停。");},null,Timeout.Infinite,false);
        worker=new Thread(Run){IsBackground=true,Name="Agent pointer input"};worker.Start();
    }
    internal static void SignalPause(string name){
        try{using(var signal=EventWaitHandle.OpenExisting("Local\\"+name+"-pause",EventWaitHandleRights.Modify)){signal.Set();}}
        catch(WaitHandleCannotBeOpenedException){throw new Fault("not_running","请先打开 AgentMouse.exe。");}
    }
    internal static string ReadLine(TextReader reader,int max){
        var line=new StringBuilder();
        while(true){int c=reader.Read();if(c<0){if(line.Length==0)return null;throw new Fault("incomplete_request","请求必须以换行结束。");}
            if(c=='\n')return line.ToString();if(c!='\r')line.Append((char)c);
            if(line.Length>max)throw new Fault("request_too_large","请求长度超过限制。");}
    }
    private void Run(){
        try{
            while(!stopping){
                var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
                acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
                using(var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,acl)){
                    active=pipe;if(stopping)break;
                    try{pipe.WaitForConnection();}catch(ObjectDisposedException){if(stopping)break;throw;}
                    using(var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true))
                    using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true}){
                        object response;
                        try{
                            var task=Task.Run(()=>ReadLine(reader,128*1024));
                            if(!task.Wait(4000))throw new Fault("request_timeout","请求读取超时。");
                            if(task.Result==null)continue;
                            var request=Json.Read(task.Result);
                            response=new{ok=true,result=engine.Dispatch(request)};
                        }catch(Exception ex){
                            if(ex is AggregateException)ex=((AggregateException)ex).GetBaseException();
                            var fault=ex as Fault;
                            response=new{ok=false,error=new{code=fault==null?"internal_error":fault.Code,message=ex.Message}};
                        }
                        try{writer.WriteLine(Json.Write(response));}catch(IOException){}
                    }
                }
                active=null;
            }
        }catch(Exception ex){if(!stopping)engine.Pause("本地连接已停止："+ex.Message);}
        finally{try{engine.Touch.Cancel();}catch(Exception){engine.Message="触点释放失败，请停止使用触摸通道。";}}
    }
    internal static string Call(string pipeName,string request){
        using(var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous)){
            try{pipe.Connect(2500);}catch(TimeoutException){throw new Fault("not_running","请先打开 AgentMouse.exe。");}
            using(var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true))
            using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true}){
                writer.WriteLine(request);
                var task=Task.Run(()=>ReadLine(reader,32*1024*1024));
                if(!task.Wait(12000))throw new Fault("response_timeout","响应超时，动作结果未知。请先观察，不要自动重试。");
                if(task.Result==null)throw new Fault("disconnected","连接已断开，动作结果未知。");return task.Result;
            }
        }
    }
    public void Dispose(){
        stopping=true;engine.Stopped=true;engine.Pause("程序正在退出。");
        if(pauseWatch!=null)pauseWatch.Unregister(null);if(pauseEvent!=null)pauseEvent.Dispose();
        var p=active;if(p!=null)p.Dispose();if(worker!=null)worker.Join(2000);
    }
}
}
