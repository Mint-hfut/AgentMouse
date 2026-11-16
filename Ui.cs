using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgentPointer {
internal sealed class MainForm : Form {
    private readonly Engine engine=new Engine();
    private readonly Transport server;
    private readonly Label state=new Label();
    private readonly Button stop=new Button();
    private readonly ToolTip details=new ToolTip();
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    private bool hotkey;

    internal MainForm(string pipe){
        server=new Transport(engine,pipe);
        Text="AgentMouse";
        FormBorderStyle=FormBorderStyle.FixedToolWindow;
        ShowInTaskbar=false;ShowIcon=false;MinimizeBox=false;MaximizeBox=false;TopMost=true;
        StartPosition=FormStartPosition.Manual;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        Font=new Font(SystemFonts.MessageBoxFont.FontFamily,Math.Max(10,SystemFonts.MessageBoxFont.Size));
        BackColor=SystemColors.Window;ForeColor=SystemColors.WindowText;
        ClientSize=new Size(248,52);

        var row=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(12,8,12,8),ColumnCount=2,RowCount=1};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        state.Text="等待任务";state.Dock=DockStyle.Fill;state.TextAlign=ContentAlignment.MiddleLeft;
        state.Margin=new Padding(0,0,12,0);state.AutoEllipsis=true;
        state.AccessibleName="AgentMouse 操作状态";
        stop.Text="终止";stop.AutoSize=true;stop.MinimumSize=new Size(64,32);
        stop.Dock=DockStyle.Fill;stop.Margin=Padding.Empty;stop.FlatStyle=FlatStyle.System;
        stop.AccessibleName="终止 AgentMouse 操作";
        stop.AccessibleDescription="停止输入并关闭状态窗口";
        stop.Click+=delegate{Close();};
        row.Controls.Add(state,0,0);row.Controls.Add(stop,1,0);Controls.Add(row);
        CancelButton=stop;

        timer.Interval=33;
        timer.Tick+=delegate{
            state.Text=!engine.InputMonitoringReady?"输入监测不可用":engine.Armed?"正在操作":"已暂停";
            if(state.AccessibleDescription!=engine.Message){
                state.AccessibleDescription=engine.Message;
                details.SetToolTip(state,engine.Message+"\n"+engine.PauseShortcut+" 暂停");
            }
            Native.Point human;
            if(Native.GetCursorPos(out human))engine.Human.TrackPointer(human);
        };
    }
    private void PositionInCorner(){
        var area=Screen.PrimaryScreen.WorkingArea;
        Location=new Point(Math.Max(area.Left,area.Right-Width-12),Math.Max(area.Top,area.Bottom-Height-12));
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x8000000;return p;}}
    protected override void OnShown(EventArgs e){
        engine.Owner=Handle;PositionInCorner();
        var devices=new[]{new Native.RawDevice{page=1,usage=2,flags=0x100,target=Handle},new Native.RawDevice{page=1,usage=6,flags=0x100,target=Handle}};
        if(!Native.RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf(typeof(Native.RawDevice)))){
            state.Text="输入监测不可用";engine.Message="无法监测你的输入，未启动操作服务。";
            details.SetToolTip(state,engine.Message);base.OnShown(e);return;
        }
        engine.InputMonitoringReady=true;
        hotkey=Native.RegisterHotKey(Handle,1,0x4003,0x50);
        engine.PauseShortcut="Ctrl + Alt + P";
        if(!hotkey){hotkey=Native.RegisterHotKey(Handle,1,0x4007,0x50);engine.PauseShortcut="Ctrl + Alt + Shift + P";}
        if(!hotkey){hotkey=Native.RegisterHotKey(Handle,1,0x4007,0x79);engine.PauseShortcut="Ctrl + Alt + Shift + F10";}
        if(!hotkey)engine.PauseShortcut="状态窗口的终止按钮";
        server.Start();timer.Start();base.OnShown(e);
    }
    protected override void WndProc(ref System.Windows.Forms.Message m){
        if(m.Msg==0x21){m.Result=new IntPtr(3);return;}
        if(m.Msg==0xff)engine.Human.Read(m.LParam);
        if(m.Msg==0x312&&m.WParam.ToInt32()==1)engine.Pause("已通过快捷键暂停。");
        if(m.Msg==0x7e){engine.Pause("显示器布局已改变，请重新观察并允许操作。");PositionInCorner();}
        base.WndProc(ref m);
    }
    protected override void OnFormClosing(FormClosingEventArgs e){
        base.OnFormClosing(e);
        if(e.Cancel)return;
        engine.Stopped=true;engine.Pause("已手动终止操作。");timer.Stop();stop.Enabled=false;
    }
    protected override void OnFormClosed(FormClosedEventArgs e){
        server.Dispose();timer.Dispose();details.Dispose();
        if(hotkey)Native.UnregisterHotKey(Handle,1);
        base.OnFormClosed(e);
    }
}
internal sealed class PracticeForm : Form {
    private readonly Label count=new Label();
    internal readonly Button TestButton=new Button();
    internal readonly ListBox ScrollList=new ListBox();
    internal volatile int Clicks,TouchDowns,TouchUpdates,TouchUps;
    internal readonly Engine Tracker;
    internal bool NoActivate;
    internal PracticeForm(Engine tracker){
        Tracker=tracker;Text="AgentMouse · 练习窗口";ClientSize=new Size(400,340);StartPosition=FormStartPosition.Manual;
        Font=new Font("Microsoft YaHei UI",11);BackColor=Color.White;
        TestButton.Text="点我计数";TestButton.SetBounds(30,35,160,52);TestButton.Click+=delegate{Clicks++;count.Text="点击次数："+Clicks;};Controls.Add(TestButton);
        count.Text="点击次数：0";count.SetBounds(30,103,340,30);Controls.Add(count);
        ScrollList.SetBounds(230,30,145,95);for(int i=1;i<=60;i++)ScrollList.Items.Add("滚动练习 "+i);Controls.Add(ScrollList);
        var note=new Label{Text="下方空白区可测试触摸。\n这里的操作不会修改你的文件。",ForeColor=Color.DimGray};note.SetBounds(30,157,340,60);Controls.Add(note);
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
