using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AgentPointer {
// Direct WinRT/D3D11 ABI keeps the .NET Framework build independent of Windows SDK/WinMD packages.
internal static class WindowCapture {
    [StructLayout(LayoutKind.Sequential)] private struct Size {internal int Width,Height;}
    [StructLayout(LayoutKind.Sequential)] private struct Texture {internal uint Width,Height,Mips,Array,Format,Samples,Quality,Usage,Bind,Access,Misc;}
    [StructLayout(LayoutKind.Sequential)] private struct Mapped {internal IntPtr Data;internal uint Pitch,Depth;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetObject(IntPtr self,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetSize(IntPtr self,out Size value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetTime(IntPtr self,out long value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateItem(IntPtr self,IntPtr window,ref Guid iid,out IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreatePool(IntPtr self,IntPtr device,int format,int buffers,Size size,out IntPtr pool);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateSession(IntPtr self,IntPtr item,out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Invoke(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetBoolean(IntPtr self,[MarshalAs(UnmanagedType.U1)]bool value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Protect(IntPtr self,[MarshalAs(UnmanagedType.Bool)]bool value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInterface(IntPtr self,ref Guid iid,out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void GetDescription(IntPtr self,out Texture description);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateTexture(IntPtr self,ref Texture description,IntPtr data,out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyResource(IntPtr self,IntPtr destination,IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapResource(IntPtr self,IntPtr resource,uint subresource,uint mode,uint flags,out Mapped mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapResource(IntPtr self,IntPtr resource,uint subresource);
    [DllImport("combase.dll")] private static extern int RoInitialize(uint mode);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll",CharSet=CharSet.Unicode)] private static extern int WindowsCreateString(string text,uint length,out IntPtr value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter,int type,IntPtr software,uint flags,IntPtr levels,uint count,uint version,out IntPtr device,out int level,out IntPtr context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device,out IntPtr result);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] private static extern int ExtendedBounds(IntPtr window,int attribute,out Native.Rect bounds,int bytes);
    private static T Method<T>(IntPtr value,int slot){return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value),slot*IntPtr.Size),typeof(T));}
    private static void Check(int hr,string step){if(hr<0)throw new Fault("capture_unavailable",step+" failed (0x"+hr.ToString("X8")+"). No foreground/screen-copy fallback was used.");}
    private static IntPtr Query(IntPtr value,string iid){IntPtr result;Guid id=new Guid(iid);Check(Marshal.QueryInterface(value,ref id,out result),"QueryInterface");return result;}
    private static IntPtr Factory(string name,string iid){
        IntPtr text=IntPtr.Zero,result;Guid id=new Guid(iid);Check(WindowsCreateString(name,(uint)name.Length,out text),"WindowsCreateString");
        try{Check(RoGetActivationFactory(text,ref id,out result),"Windows Graphics Capture (requires Windows 10 1903+)");return result;}finally{WindowsDeleteString(text);}
    }
    private static void Close(IntPtr value){
        IntPtr close=IntPtr.Zero;Guid iid=new Guid("30d5a829-7fa4-4026-83bb-d75bae4ea99e");
        if(value!=IntPtr.Zero&&Marshal.QueryInterface(value,ref iid,out close)>=0){try{Method<Invoke>(close,6)(close);}finally{Marshal.Release(close);}}
    }
    private sealed class Session : IDisposable {
        internal IntPtr Window,Device,Context,Pool,CaptureSession,Frame;
        internal uint Pid;
        internal Native.Rect Bounds;
        internal readonly List<IntPtr> Owned=new List<IntPtr>();
        public void Dispose(){if(Frame!=IntPtr.Zero){Close(Frame);Marshal.Release(Frame);Frame=IntPtr.Zero;}Close(CaptureSession);CaptureSession=IntPtr.Zero;Close(Pool);Pool=IntPtr.Zero;for(int i=Owned.Count-1;i>=0;i--)Marshal.Release(Owned[i]);Owned.Clear();}
    }
    private static Session active;
    private static readonly BlockingCollection<Action> jobs=new BlockingCollection<Action>();
    static WindowCapture(){
        var worker=new Thread(delegate(){
            int hr=RoInitialize(1);
            try{foreach(var work in jobs.GetConsumingEnumerable())work();}
            finally{if(active!=null)active.Dispose();if(hr>=0)RoUninitialize();}
        }){IsBackground=true,Name="AgentMouse window capture"};worker.SetApartmentState(ApartmentState.MTA);worker.Start();
    }
    internal static object Capture(IntPtr window,Native.Rect outer){
        var done=new TaskCompletionSource<object>();
        jobs.Add(delegate(){try{done.SetResult(Snapshot(window,outer));}catch(Exception error){if(active!=null){active.Dispose();active=null;}done.SetException(error);}});
        return done.Task.GetAwaiter().GetResult();
    }
    internal static void Reset(){jobs.Add(delegate(){if(active!=null){active.Dispose();active=null;}});}
    private static Session Start(IntPtr window,Native.Rect outer){
        var state=new Session{Window=window,Bounds=outer};
        Native.GetWindowThreadProcessId(window,out state.Pid);
        if(IntPtr.Size!=8||Marshal.SizeOf(typeof(Texture))!=44||Marshal.SizeOf(typeof(Mapped))!=16||Marshal.SizeOf(typeof(Size))!=8)throw new Fault("capture_unavailable","Unexpected x64 graphics ABI layout.");
        Func<IntPtr,IntPtr> keep=delegate(IntPtr value){if(value==IntPtr.Zero)throw new Fault("capture_unavailable","A capture object was unavailable.");state.Owned.Add(value);return value;};
        try{
            IntPtr device,context,pool,session;int level;
            int hr=D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out device,out level,out context);
            if(hr<0)hr=D3D11CreateDevice(IntPtr.Zero,5,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out device,out level,out context);
            Check(hr,"D3D11CreateDevice");keep(device);keep(context);
            IntPtr protection=keep(Query(context,"9b7e4e00-342c-4106-a19f-4f2704f689f0"));Method<Protect>(protection,5)(protection,true);
            IntPtr dxgi=keep(Query(device,"54ec77fa-1377-44e6-8c32-88fd5f44c84c")),inspectable;
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out inspectable),"CreateDirect3D11Device");keep(inspectable);
            IntPtr direct=keep(Query(inspectable,"a37624ab-8d5f-4650-9d3e-9eae3d9bc670"));
            IntPtr itemFactory=keep(Factory("Windows.Graphics.Capture.GraphicsCaptureItem","3628e81b-3cac-4c60-b7f4-23ce0e0c3356")),item;
            Guid itemId=new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");Check(Method<CreateItem>(itemFactory,3)(itemFactory,window,ref itemId,out item),"CreateForWindow");keep(item);
            Size size;Check(Method<GetSize>(item,7)(item,out size),"Capture item size");
            if(size.Width<=0||size.Height<=0||(long)size.Width*size.Height>32000000)throw new Fault("capture_unavailable","Invalid or oversized window capture.");
            IntPtr factory=keep(Factory("Windows.Graphics.Capture.Direct3D11CaptureFramePool","589b103f-6bbc-5df5-a991-02e28b3b66d5"));
            Check(Method<CreatePool>(factory,6)(factory,direct,87,2,size,out pool),"CreateFreeThreaded");keep(pool);state.Pool=pool;
            Check(Method<CreateSession>(pool,10)(pool,item,out session),"CreateCaptureSession");keep(session);state.CaptureSession=session;
            IntPtr cursor=IntPtr.Zero;Guid cursorId=new Guid("2c39ae40-7d2e-5044-804e-8b6799d4cf9e");
            if(Marshal.QueryInterface(session,ref cursorId,out cursor)>=0){keep(cursor);Check(Method<SetBoolean>(cursor,7)(cursor,false),"Disable captured cursor");}
            Check(Method<Invoke>(session,6)(session),"StartCapture");
            state.Device=device;state.Context=context;state.Pool=pool;state.CaptureSession=session;return state;
        }catch{state.Dispose();throw;}
    }
    private static object Snapshot(IntPtr window,Native.Rect outer){
        uint pid;Native.GetWindowThreadProcessId(window,out pid);
        if(active==null||active.Window!=window||active.Pid!=pid||active.Bounds.Width!=outer.Width||active.Bounds.Height!=outer.Height){
            if(active!=null){active.Dispose();active=null;}active=Start(window,outer);
        }
        IntPtr pool=active.Pool,device=active.Device,context=active.Context,frame=active.Frame;bool changed=false;
        var owned=new List<IntPtr>();
        Func<IntPtr,IntPtr> keep=delegate(IntPtr value){if(value==IntPtr.Zero)throw new Fault("capture_unavailable","A frame object was unavailable.");owned.Add(value);return value;};
        try{
            long start=InputTracker.Now;
            while(InputTracker.Milliseconds(start)<2200){
                IntPtr next;Check(Method<GetObject>(pool,7)(pool,out next),"TryGetNextFrame");
                if(next!=IntPtr.Zero){if(frame!=IntPtr.Zero){Close(frame);Marshal.Release(frame);}active.Frame=frame=next;changed=true;}
                if(frame!=IntPtr.Zero&&InputTracker.Milliseconds(start)>200)break;Thread.Sleep(8);
            }
            if(frame==IntPtr.Zero)throw new Fault("capture_unavailable","Windows Graphics Capture produced no frame; the window was not activated.");
            IntPtr surface;Check(Method<GetObject>(frame,6)(frame,out surface),"Capture surface");keep(surface);
            Size content;Check(Method<GetSize>(frame,8)(frame,out content),"Frame size");
            long timestamp;Check(Method<GetTime>(frame,7)(frame,out timestamp),"Frame timestamp");
            IntPtr access=keep(Query(surface,"a9b3d012-3df2-4ee3-b8d1-8695f457d3c1")),texture;
            Guid textureId=new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");Check(Method<GetInterface>(access,3)(access,ref textureId,out texture),"Capture texture");keep(texture);
            Texture description;Method<GetDescription>(texture,10)(texture,out description);
            int width=Math.Min(content.Width,(int)description.Width),height=Math.Min(content.Height,(int)description.Height);
            if(width<=0||height<=0||(long)width*height>32000000||description.Format!=87)throw new Fault("capture_unavailable","Unexpected capture texture format/size.");
            Native.Rect source=outer,extended;
            if(ExtendedBounds(window,9,out extended,Marshal.SizeOf(typeof(Native.Rect)))==0&&extended.Width==width&&extended.Height==height)source=extended;
            else if(outer.Width!=width||outer.Height!=height)throw new Fault("window_changed","Capture geometry changed; observe the window again.");
            description.Usage=3;description.Bind=0;description.Access=0x20000;description.Misc=0;
            IntPtr staging;Check(Method<CreateTexture>(device,5)(device,ref description,IntPtr.Zero,out staging),"Create staging texture");keep(staging);
            Method<CopyResource>(context,47)(context,staging,texture);Mapped mapped;
            Check(Method<MapResource>(context,14)(context,staging,0,1,0,out mapped),"Read capture pixels");
            try{
                if(mapped.Data==IntPtr.Zero||mapped.Pitch<(uint)width*4)throw new Fault("capture_unavailable","Invalid mapped capture buffer.");
                using(var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb)){
                    var bits=bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                    try{var row=new byte[checked(width*4)];for(int y=0;y<height;y++){Marshal.Copy(IntPtr.Add(mapped.Data,checked(y*(int)mapped.Pitch)),row,0,row.Length);Marshal.Copy(row,0,IntPtr.Add(bits.Scan0,y*bits.Stride),row.Length);}}finally{bitmap.UnlockBits(bits);}
                    double scale=Math.Min(1,Math.Sqrt(4000000.0/((long)width*height)));int w=Math.Max(1,(int)(width*scale)),h=Math.Max(1,(int)(height*scale));
                    using(var resized=new Bitmap(w,h))using(var graphics=Graphics.FromImage(resized))using(var bytes=new MemoryStream()){
                        graphics.DrawImage(bitmap,new Rectangle(0,0,w,h));resized.Save(bytes,ImageFormat.Png);
                        return new{mimeType="image/png",data=Convert.ToBase64String(bytes.ToArray()),x=source.Left,y=source.Top,width=w,height=h,sourceWidth=width,sourceHeight=height,scaleX=(double)width/w,scaleY=(double)height/h,captureMethod="Windows.Graphics.Capture",frameTimestamp100ns=timestamp,frameChanged=changed};
                    }
                }
            }finally{Method<UnmapResource>(context,15)(context,staging,0);}
        }finally{
            for(int i=owned.Count-1;i>=0;i--)Marshal.Release(owned[i]);
        }
    }
}
}
