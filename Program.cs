using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AgentPointer {
internal static class Program {
    [STAThread] private static int Main(string[] args){
#if CLI
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
#endif
        Native.DpiAware();
        try{
            if(args.Length>0&&args[0]=="--state-self-test")return SelfTest.StateOnly();
            if(args.Length==1&&args[0]=="--foreground-state")return SelfTest.ForegroundState();
            if(args.Length==3&&args[0]=="--image-color-point")return SelfTest.ImagePoint(args[1],args[2]);
            if(args.Length==3&&args[0]=="--background-fixture-server")return SelfTest.BackgroundFixtureServer(args[1],args[2]);
            if((args.Length==3||args.Length==4)&&args[0]=="--probe-background-capture")return SelfTest.ProbeBackgroundCapture(args[1],args[2],args.Length==4?args[3]:"");
            if(args.Length>0&&args[0]=="--self-test")return SelfTest.Run(args.Length>1?args[1]:"test-report.json");
            string pipe=Transport.DefaultName;
            if(args.Length>=2&&args[0]=="--pipe"){pipe=args[1];var rest=new string[args.Length-2];Array.Copy(args,2,rest,0,rest.Length);args=rest;}
            if(args.Length>=2&&(args[0]=="--call"||args[0]=="--request-file")){
                string request=args[0]=="--request-file"?File.ReadAllText(args[1]):args[1];
                if(!request.TrimStart().StartsWith("{"))request=Json.Write(new{method=request});
                if(Json.Text(Json.Read(request),"method","")=="pause")Transport.SignalPause(pipe);
                string result=Transport.Call(pipe,request);Console.WriteLine(result);return Json.Flag(Json.Read(result,32*1024*1024),"ok",false)?0:2;
            }
#if CLI
            Console.Error.WriteLine("用法：AgentMouse.Cli.exe --call status | --request-file request.json | --self-test report.json");return 2;
#else
            bool created;
            using(var mutex=new Mutex(true,"Local\\"+pipe,out created)){
                if(!created){MessageBox.Show("AgentMouse 已经运行。","AgentMouse");return 0;}
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm(pipe));
            }
            return 0;
#endif
        }catch(Exception ex){
#if CLI
            var fault=ex as Fault;Console.WriteLine(Json.Write(new{ok=false,error=new{code=fault==null?"internal_error":fault.Code,message=ex.Message}}));
#else
            MessageBox.Show(ex.Message,"AgentMouse",MessageBoxButtons.OK,MessageBoxIcon.Error);
#endif
            return 1;
        }
    }
}
}
