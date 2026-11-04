using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgentPointer {
internal sealed class PointerOverlay : Form {
    internal PointerOverlay(){
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
        Text="AgentMouse Overlay";AutoScaleMode=AutoScaleMode.None;
        BackColor=Color.Magenta;TransparencyKey=Color.Magenta;Size=new Size(112,48);DoubleBuffered=true;
    }
    internal void Present(int x,int y){
        // Position only our overlay in physical pixels; never derive it from the system cursor.
        Native.SetWindowPos(Handle,new IntPtr(-1),x,y,0,0,0x211);
        if(!Visible)Show();
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x80000|0x20|0x8000000|0x80;return p;}}
    protected override void WndProc(ref System.Windows.Forms.Message m){
        if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}
        if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);
    }
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        var shape=new[]{new Point(2,2),new Point(2,31),new Point(10,24),new Point(16,37),new Point(22,34),new Point(16,22),new Point(28,22)};
        using(var fill=new SolidBrush(Color.FromArgb(230,103,24)))using(var edge=new Pen(Color.White,2)){
            e.Graphics.FillPolygon(fill,shape);e.Graphics.DrawPolygon(edge,shape);
            e.Graphics.FillRectangle(fill,32,8,76,25);
        }
        using(var font=new Font("Segoe UI",10,FontStyle.Bold))using(var brush=new SolidBrush(Color.White))
            e.Graphics.DrawString("AGENT",font,brush,38,12);
    }
}
internal sealed class MainForm : Form {
    private readonly Engine engine;
    private readonly Transport server;
    private readonly PointerOverlay overlay=new PointerOverlay();
    private readonly ComboBox mode=new ComboBox();
    private readonly Label state=new Label(),status=new Label();
    private readonly Label shortcut;
    private readonly Label coordinates;
    private readonly Button arm=new Button();
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    private bool hotkey;
    internal MainForm(string pipe){
        engine=new Engine();server=new Transport(engine,pipe);
        Text="AgentMouse · 独立指针原型";ClientSize=new Size(740,470);MinimumSize=new Size(756,509);
        StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(247,249,252);Font=new Font("Microsoft YaHei UI",10);
        AutoScaleMode=AutoScaleMode.Dpi;
        AddLabel("你的鼠标，留给你。",26,22,650,40,22,FontStyle.Bold);
        AddLabel("Agent 使用独立指针；输入兼容性由当前落点的软件决定。",29,69,670,27,10,FontStyle.Regular);
        AddLabel("操作范围：整个桌面",29,113,670,27,12,FontStyle.Bold);
        AddLabel("无需选择软件或窗口；一次启用后，可跨窗口连续操作。",29,149,680,27,10,FontStyle.Regular);
        AddLabel("执行方式",29,191,110,25,10,FontStyle.Bold);
        mode.SetBounds(29,222,365,32);mode.DropDownStyle=ComboBoxStyle.DropDownList;
        mode.Items.AddRange(new object[]{"后台点击 · 不主动切换窗口","触摸实验 · 可能影响真实指针与焦点"});mode.SelectedIndex=0;
        mode.SelectedIndexChanged+=delegate{engine.Mode=mode.SelectedIndex==0?"background":"touch";engine.Pause("执行方式已更换；请重新允许操作。");};Controls.Add(mode);
        var visible=new CheckBox{Text="操作时显示 Agent 指针",Checked=true,AutoSize=true};visible.SetBounds(422,226,280,28);
        visible.CheckedChanged+=delegate{engine.PointerVisible=visible.Checked;};Controls.Add(visible);
        arm.Text="允许全桌面操作";arm.SetBounds(29,278,220,44);arm.FlatStyle=FlatStyle.Flat;arm.FlatAppearance.BorderSize=0;
        arm.BackColor=Color.FromArgb(25,103,221);arm.ForeColor=Color.White;
        arm.Click+=delegate{
            if(engine.Armed){engine.Pause("已手动暂停。");return;}
            if(engine.Touch.Active||engine.MessageButtonHeld){engine.Message="仍有按键或触点未释放，请手动取消目标操作后重开控制器。";return;}
            engine.Armed=true;engine.Message="全桌面操作已启用。外部键鼠活动时，Agent 会等待。";
        };Controls.Add(arm);
        var practice=new Button{Text="打开练习窗口"};practice.SetBounds(265,278,170,44);practice.Click+=delegate{
            var f=new PracticeForm(engine,true);f.Show();
        };Controls.Add(practice);
        state.SetBounds(460,287,250,28);state.Font=new Font(Font,FontStyle.Bold);Controls.Add(state);
        status.SetBounds(29,339,680,43);status.ForeColor=Color.FromArgb(65,78,98);Controls.Add(status);
        coordinates=AddLabel("",29,388,680,24,10,FontStyle.Regular);
        shortcut=AddLabel("正在注册暂停快捷键…",29,423,680,28,9,FontStyle.Regular);
        timer.Interval=33;timer.Tick+=delegate{
            state.Text=engine.Armed?"● 已允许操作":"● 已暂停";state.ForeColor=engine.Armed?Color.FromArgb(22,125,80):Color.FromArgb(112,119,132);
            arm.Text=engine.Armed?"暂停 Agent":"允许全桌面操作";status.Text=engine.Message;
            Native.Point human;Native.GetCursorPos(out human);
            engine.Human.TrackPointer(human);
            coordinates.Text="你的鼠标：("+human.X+", "+human.Y+")       橙色 AGENT：("+engine.X+", "+engine.Y+")";
            if(engine.ShowPointer)overlay.Present(engine.X,engine.Y);else if(overlay.Visible)overlay.Hide();
        };
    }
    private Label AddLabel(string text,int x,int y,int w,int h,int size,FontStyle style){
        var l=new Label{Text=text,Font=new Font("Microsoft YaHei UI",size,style),ForeColor=Color.FromArgb(29,42,61)};l.SetBounds(x,y,w,h);Controls.Add(l);return l;
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override void OnShown(EventArgs e){
        base.OnShown(e);engine.Owner=Handle;
        var area=Screen.PrimaryScreen.WorkingArea;engine.X=area.Right-160;engine.Y=area.Top+45;
        engine.OverlayWindow=overlay.Handle;
        var devices=new[]{new Native.RawDevice{page=1,usage=2,flags=0x100,target=Handle},new Native.RawDevice{page=1,usage=6,flags=0x100,target=Handle}};
        if(!Native.RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf(typeof(Native.RawDevice)))){
            engine.Message="无法监测你的输入，未启动操作服务。";arm.Enabled=false;return;
        }
        hotkey=Native.RegisterHotKey(Handle,1,0x4003,0x50);
        engine.PauseShortcut="Ctrl + Alt + P";
        if(!hotkey){hotkey=Native.RegisterHotKey(Handle,1,0x4007,0x50);engine.PauseShortcut="Ctrl + Alt + Shift + P";}
        if(!hotkey){hotkey=Native.RegisterHotKey(Handle,1,0x4007,0x79);engine.PauseShortcut="Ctrl + Alt + Shift + F10";}
        if(!hotkey){engine.PauseShortcut="窗口内暂停按钮";engine.Message="暂停快捷键被其他程序占用，请使用窗口内暂停按钮。";}
        shortcut.Text=engine.PauseShortcut+" 暂停 · 不安装驱动 · 不自动移动真实鼠标";
        server.Start();timer.Start();
    }
    protected override void WndProc(ref System.Windows.Forms.Message m){
        if(m.Msg==0xff)engine.Human.Read(m.LParam);
        if(m.Msg==0x312&&m.WParam.ToInt32()==1)engine.Pause("已通过快捷键暂停。");
        if(m.Msg==0x7e)engine.Pause("显示器布局已改变，请重新观察并允许操作。");
        base.WndProc(ref m);
    }
    protected override void OnFormClosed(FormClosedEventArgs e){
        timer.Stop();server.Dispose();overlay.Close();if(hotkey)Native.UnregisterHotKey(Handle,1);base.OnFormClosed(e);
    }
}
internal sealed class PracticeForm : Form {
    private readonly Label count=new Label();
    internal readonly Button TestButton=new Button();
    internal readonly ListBox ScrollList=new ListBox();
    internal volatile int Clicks,TouchDowns,TouchUpdates,TouchUps;
    internal readonly Engine Tracker;
    internal bool NoActivate;
    internal PracticeForm(Engine tracker,bool interactiveDemo=false){
        Tracker=tracker;Text="AgentMouse · 练习窗口";ClientSize=new Size(400,340);StartPosition=FormStartPosition.Manual;
        Font=new Font("Microsoft YaHei UI",11);BackColor=Color.White;
        TestButton.Text="点我计数";TestButton.SetBounds(30,35,160,52);TestButton.Click+=delegate{Clicks++;count.Text="点击次数："+Clicks;};Controls.Add(TestButton);
        count.Text="点击次数：0";count.SetBounds(30,103,340,30);Controls.Add(count);
        ScrollList.SetBounds(230,30,145,95);for(int i=1;i<=60;i++)ScrollList.Items.Add("滚动练习 "+i);Controls.Add(ScrollList);
        var note=new Label{Text="下方空白区可测试触摸。\n这里的操作不会修改你的文件。",ForeColor=Color.DimGray};note.SetBounds(30,157,340,60);Controls.Add(note);
        if(interactiveDemo){
            var demo=new Button{Text="让 Agent 点一次上方按钮"};demo.SetBounds(30,245,340,48);Controls.Add(demo);
            demo.Click+=delegate{
                if(!tracker.Armed){note.Text="请在主窗口点击\n“允许全桌面操作”。";return;}
                var p=new Native.Point(TestButton.Width/2,TestButton.Height/2);Native.ClientToScreen(TestButton.Handle,ref p);
                demo.Enabled=false;note.Text="等待你的键鼠空闲，然后执行…";
                Task.Run(delegate{
                    string result;
                    try{
                        var observation=Json.Read(Json.Write(tracker.Dispatch(new Dictionary<string,object>{{"method","observe"},{"screenshot",false}})));
                        tracker.Dispatch(new Dictionary<string,object>{{"method","click"},{"observationId",observation["observationId"]},{"x",p.X},{"y",p.Y}});
                        result="输入已提交，请查看上方计数。\n是否切换焦点，以主窗口提示为准。";
                    }catch(Exception ex){result=ex.Message;}
                    if(!IsDisposed)try{BeginInvoke((Action)delegate{demo.Enabled=true;note.Text=result;});}catch(InvalidOperationException){}
                });
            };
        }
    }
    protected override bool ShowWithoutActivation {get{return NoActivate;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;if(NoActivate)p.ExStyle|=0x8000000;return p;}}
    protected override void WndProc(ref System.Windows.Forms.Message m){
        if(Tracker!=null&&m.Msg==0xff)Tracker.Human.Read(m.LParam);
        if(NoActivate&&(m.Msg==0x21||m.Msg==0x24b)){m.Result=new IntPtr(3);return;}
        if(m.Msg==0x246){TouchDowns++;m.Result=IntPtr.Zero;return;}
        if(m.Msg==0x245){TouchUpdates++;m.Result=IntPtr.Zero;return;}
        if(m.Msg==0x247){TouchUps++;m.Result=IntPtr.Zero;return;}
        base.WndProc(ref m);
    }
}
}
