using System.Globalization;
using TuoiTho.Core.Policy;
namespace TuoiTho.Parent;
public sealed class ParentControlForm : Form
{
 private static readonly Color Navy=Color.FromArgb(20,50,82);
 private static readonly Color NavySoft=Color.FromArgb(32,72,112);
 private static readonly Color Accent=Color.FromArgb(24,151,143);
 private static readonly Color AccentSoft=Color.FromArgb(224,246,243);
 private static readonly Color Canvas=Color.FromArgb(244,248,252);
 private static readonly Color Card=Color.White;
 private static readonly Color TextMain=Color.FromArgb(25,48,73);
 private static readonly Color TextMuted=Color.FromArgb(105,123,145);
 private static readonly Color Danger=Color.FromArgb(196,64,72);
 private static readonly Color Success=Color.FromArgb(43,139,91);
 private static readonly Color Warning=Color.FromArgb(204,139,36);
 private readonly ParentDesktopController controller; private readonly IParentAuthorizationGate authorization; private readonly TabControl tabs=new(){Dock=DockStyle.Fill}; private TabPage? diagnosticsTab; private readonly System.Windows.Forms.Timer refreshTimer=new(){Interval=1000}; private readonly Dictionary<string,Label> overview=[]; private readonly Dictionary<string,Label> diagnostics=[]; private readonly Label message=new(){AutoSize=true,Font=new Font("Segoe UI",10,FontStyle.Bold),Padding=new Padding(8)}; private readonly Label countdown=new(){AutoSize=true,Text="00:00:00",Font=new Font("Segoe UI",36,FontStyle.Bold),ForeColor=Navy,Padding=new Padding(4)}; private Button? reset; private bool autoRefreshing; private int manualActions; private AppPolicyPanel? apps; private TimePolicyPanel? timePolicy; private WebPolicyPanel? web; private AccountProtectionPanel? accountProtection;
 public ParentControlForm(ParentDesktopController controller,IParentAuthorizationGate? authorization=null){this.controller=controller;this.authorization=authorization??new ParentAuthorizationGate();Text="Quản lý thời gian – Điều khiển phụ huynh";StartPosition=FormStartPosition.CenterScreen;MinimumSize=new Size(1040,700);Size=new Size(1120,760);Font=new Font("Segoe UI",10);BackColor=Canvas;ForeColor=TextMain;ConfigureTheme();tabs.TabPages.Add(OverviewTab());tabs.TabPages.Add(TimeTab());tabs.TabPages.Add(AppTab());tabs.TabPages.Add(WebTab());tabs.TabPages.Add(AccountProtectionTab());tabs.TabPages.Add(RemoteTab());diagnosticsTab=DiagnosticsTab();Controls.Add(tabs);var status=new StatusStrip{BackColor=Card,SizingGrip=false,Padding=new Padding(10,4,10,4)};status.Items.Add(new ToolStripControlHost(message));Controls.Add(status);refreshTimer.Tick+=async(_,_)=>await RefreshFromTimerAsync();Shown+=async(_,_)=>{await RefreshAfterManualAsync();refreshTimer.Start();};FormClosed+=(_,_)=>{refreshTimer.Stop();refreshTimer.Dispose();controller.Dispose();};}

 private void ConfigureTheme(){
  tabs.Appearance=TabAppearance.FlatButtons;
  tabs.DrawMode=TabDrawMode.OwnerDrawFixed;
  tabs.SizeMode=TabSizeMode.Fixed;
  tabs.ItemSize=new Size(155,40);
  tabs.Padding=new Point(0,0);
  tabs.DrawItem+=(_,e)=>{
   var selected=e.Index==tabs.SelectedIndex;
   var rect=e.Bounds;
   using var bg=new SolidBrush(selected?Navy:Color.FromArgb(232,239,247));
   e.Graphics.FillRectangle(bg,rect);
   var text=tabs.TabPages[e.Index].Text;
   TextRenderer.DrawText(e.Graphics,text,new Font("Segoe UI Semibold",9.5f),rect,selected?Color.White:TextMain,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
  };
 }
 private TabPage OverviewTab(){
  var tab=NewTab("TỔNG QUAN");
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(26,22,26,24),ColumnCount=1,RowCount=3,BackColor=Canvas};
  root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
  root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

  var header=new DecorativeHeaderPanel{Dock=DockStyle.Top,Height=118,Margin=new Padding(0,0,0,18),Padding=new Padding(24,18,24,14)};
  var headerGrid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,BackColor=Color.Transparent};
  headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
  headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
  var headerText=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.Transparent,Margin=Padding.Empty};
  headerText.Controls.Add(new Label{Text="QUẢN LÝ THỜI GIAN",Font=new Font("Segoe UI",25,FontStyle.Bold),ForeColor=Color.White,AutoSize=true,Margin=Padding.Empty});
  headerText.Controls.Add(new Label{Text="Quản lý thời gian sử dụng an toàn và dễ hiểu",Font=new Font("Segoe UI",10.5f),ForeColor=Color.FromArgb(220,235,247),AutoSize=true,Margin=new Padding(2,4,0,0)});
  var badge=new Label{Text="PHỤ HUYNH",AutoSize=true,Font=new Font("Segoe UI",9,FontStyle.Bold),ForeColor=Navy,BackColor=Color.FromArgb(232,250,247),Padding=new Padding(14,8,14,8),Margin=new Padding(8,10,0,0)};
  headerGrid.Controls.Add(headerText,0,0);headerGrid.Controls.Add(badge,1,0);header.Controls.Add(headerGrid);root.Controls.Add(header,0,0);

  var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,BackColor=Canvas,Margin=Padding.Empty};
  body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,43));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,57));
  var timeCard=new InfoCardPanel{Dock=DockStyle.Fill,Margin=new Padding(0,0,10,0),Padding=new Padding(24),BackColor=Card};
  var timeStack=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Card};
  timeStack.Controls.Add(new Label{Text="THỜI GIAN CÒN LẠI",Font=new Font("Segoe UI",10,FontStyle.Bold),ForeColor=TextMuted,AutoSize=true});
  timeStack.Controls.Add(countdown);
  timeStack.Controls.Add(new Label{Text="Thời gian có thể sử dụng thiết bị hôm nay",Font=new Font("Segoe UI",9.5f),ForeColor=TextMuted,AutoSize=true,Margin=new Padding(0,3,0,0)});
  timeStack.Controls.Add(new Label{Text="●  Trạng thái được cập nhật theo thời gian thực",Font=new Font("Segoe UI",9),ForeColor=Accent,AutoSize=true,Margin=new Padding(0,16,0,0)});
  timeCard.Controls.Add(timeStack);body.Controls.Add(timeCard,0,0);

  var statusCard=new InfoCardPanel{Dock=DockStyle.Fill,Margin=new Padding(10,0,0,0),Padding=new Padding(22),BackColor=Card};
  var statusRoot=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=Card};
  statusRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));statusRoot.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  statusRoot.Controls.Add(new Label{Text="TỔNG QUAN HÔM NAY",Font=new Font("Segoe UI",11,FontStyle.Bold),ForeColor=Navy,AutoSize=true,Margin=new Padding(0,0,0,12)},0,0);
  var cards=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,BackColor=Card};
  cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
  foreach(var item in new[]{("Đã dùng hôm nay","Used"),("Hạn mức hôm nay","Quota"),("Thời gian được cộng","Grants"),("Trạng thái","State"),("Danh sách ứng dụng","AppState")})AddField(cards,item.Item1,item.Item2,overview);
  statusRoot.Controls.Add(cards,0,1);statusCard.Controls.Add(statusRoot);body.Controls.Add(statusCard,1,0);
  root.Controls.Add(body,0,1);

  var actionCard=new InfoCardPanel{Dock=DockStyle.Top,AutoSize=true,Margin=new Padding(0,18,0,0),Padding=new Padding(18),BackColor=Card};
  var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,BackColor=Card};
  AddButton(actions,"+15 phút",()=>RunAsync(ParentControlAction.GrantMinutes,15));
  AddButton(actions,"+30 phút",()=>RunAsync(ParentControlAction.GrantMinutes,30));
  AddButton(actions,"+60 phút",()=>RunAsync(ParentControlAction.GrantMinutes,60));
  AddButton(actions,"Cộng tùy chọn",CustomGrantAsync);
  AddButton(actions,"Khóa",()=>RunAsync(ParentControlAction.SetParentLock));
  AddButton(actions,"Bỏ khóa",()=>RunAsync(ParentControlAction.ClearParentLock));
  AddButton(actions,"Override",()=>RunAsync(ParentControlAction.EmergencyOverride));
  reset=AddButton(actions,"Đặt lại dữ liệu thử nghiệm M1",()=>RunAsync(ParentControlAction.ResetM1));reset.Visible=false;
  actionCard.Controls.Add(actions);root.Controls.Add(actionCard,0,2);

  tab.Controls.Add(root);return tab;
 }
 private static TabPage NewTab(string title)=>new(title){BackColor=Canvas,ForeColor=TextMain,Padding=new Padding(0)};
 private TabPage TimeTab(){var tab=new TabPage("THỜI GIAN");timePolicy=new TimePolicyPanel(controller,authorization);timePolicy.ManualActionStarting+=(_,_)=>Interlocked.Increment(ref manualActions);timePolicy.PolicySaved+=async(_,result)=>{await DisplayAsync(result);};timePolicy.ManualActionCompleted+=async(_,_)=>{try{await RefreshAfterManualAsync();}finally{Interlocked.Decrement(ref manualActions);}};tab.Controls.Add(timePolicy);return tab;} private TabPage AppTab(){var tab=new TabPage("ỨNG DỤNG");apps=new AppPolicyPanel(controller,authorization);apps.RefreshRequested+=async(_,_)=>await RefreshAppsAsync();apps.ManualActionStarting+=(_,_)=>Interlocked.Increment(ref manualActions);apps.ManualActionCompleted+=async(_,_)=>{try{await RefreshAfterManualAsync();}finally{Interlocked.Decrement(ref manualActions);}};tab.Controls.Add(apps);return tab;}
 private TabPage DiagnosticsTab(){var tab=new TabPage("CHẨN ĐOÁN");var root=new TableLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(22),ColumnCount=2};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));foreach(var item in new[]{("Hồ sơ","Profile"),("Phiên Windows","Session"),("Chế độ an toàn","TestMode"),("SessionAgent","Agent"),("Hoạt động","Activity"),("Nguồn hoạt động","ActivitySource"),("Nhàn rỗi thiết bị","Idle"),("Nhàn rỗi Windows","WindowsIdle"),("Tuổi mẫu","SampleAge"),("Phiên theo dõi","TrackedSession"),("Đã ghi hôm nay","RecordedSeconds"),("Lần ghi cuối","Checkpoint"),("Khóa rõ ràng","LockLatch"),("WTS kết nối","WtsConnection"),("WTS SessionFlags","WtsFlags"),("Thông báo phiên","Notifications"),("Lỗi thông báo","NotificationError"),("Ngưỡng nhàn rỗi","IdleThreshold"),("Quét ứng dụng lần cuối","AppScanAt"),("Process đã xem","AppProcesses"),("Ứng dụng người dùng","AppUsers"),("Ứng dụng nền","AppBackground"),("Hệ thống được bảo vệ","AppSystem"),("Lỗi quét ứng dụng","AppError"),("M2 chặn thử nghiệm","AppEnforcement"),("Kết quả thực thi gần nhất","AppEnforcementLast")})AddField(root,item.Item1,item.Item2,diagnostics);tab.Controls.Add(root);return tab;} private TabPage WebTab(){var tab=new TabPage("WEB");web=new WebPolicyPanel(controller,authorization);tab.Controls.Add(web);return tab;}
 private TabPage AccountProtectionTab(){var tab=new TabPage("TÀI KHOẢN");accountProtection=new AccountProtectionPanel(new AccountProtectionManager(),authorization);tab.Controls.Add(accountProtection);return tab;}
 private TabPage RemoteTab(){var tab=new TabPage("ĐIỀU KHIỂN TỪ XA");var panel=new RemotePairingPanel(controller,authorization);panel.ManualActionStarting+=(_,_)=>Interlocked.Increment(ref manualActions);panel.ManualActionCompleted+=async(_,_)=>{try{await RefreshAfterManualAsync();}finally{Interlocked.Decrement(ref manualActions);}};tab.Controls.Add(panel);return tab;}
 private static void AddField(TableLayoutPanel panel,string label,string key,Dictionary<string,Label> bag){
  var row=panel.RowCount++;panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
  var value=new Label{Text="—",AutoSize=true,Padding=new Padding(6,8,6,8),Font=new Font("Segoe UI",10.5f,FontStyle.Bold),ForeColor=TextMain};
  bag[key]=value;
  panel.Controls.Add(new Label{Text=label,AutoSize=true,Padding=new Padding(6,8,6,8),ForeColor=TextMuted},0,row);
  panel.Controls.Add(value,1,row);
 }
 private static Button AddButton(FlowLayoutPanel panel,string text,Func<Task> action){
  var isLock=text.Equals("Khóa",StringComparison.OrdinalIgnoreCase);
  var isUnlock=text.Equals("Bỏ khóa",StringComparison.OrdinalIgnoreCase);
  var isOverride=text.Equals("Override",StringComparison.OrdinalIgnoreCase);
  var bg=isLock?Danger:isUnlock?Success:isOverride?Warning:Accent;
  var b=new Button{Text=text,AutoSize=true,MinimumSize=new Size(128,44),Margin=new Padding(6),FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=Color.White,Font=new Font("Segoe UI Semibold",9.5f),Cursor=Cursors.Hand,UseVisualStyleBackColor=false};
  b.FlatAppearance.BorderSize=0;b.FlatAppearance.MouseOverBackColor=ControlPaint.Light(bg);
  b.Click+=async(_,_)=>await action();panel.Controls.Add(b);return b;
 }
 public static string FormatCountdown(double seconds){var total=Math.Max(0,(long)Math.Floor(seconds));return $"{total/3600:D2}:{total%3600/60:D2}:{total%60:D2}";}
 private async Task CustomGrantAsync(){using var dialog=new Form{Text="Cộng phút tùy chọn",StartPosition=FormStartPosition.CenterParent,Size=new Size(330,155)};var input=new NumericUpDown{Minimum=1,Maximum=1440,Value=15,Location=new Point(20,25),Width=130};var ok=new Button{Text="Cộng phút",DialogResult=DialogResult.OK,Location=new Point(170,22)};dialog.Controls.AddRange([new Label{Text="Số phút:",Location=new Point(20,5),AutoSize=true},input,ok]);if(dialog.ShowDialog(this)==DialogResult.OK)await RunAsync(ParentControlAction.GrantMinutes,(int)input.Value);}
 private async Task RunAsync(ParentControlAction action,int? minutes=null){if(Volatile.Read(ref manualActions)>0)return;if(!authorization.EnsureAuthorized(this))return;Interlocked.Increment(ref manualActions);try{await DisplayAsync(await controller.SendAsync(action,minutes));await RefreshAfterManualAsync();}finally{Interlocked.Decrement(ref manualActions);}}
 private async Task RefreshAppsAsync(){if(Volatile.Read(ref manualActions)>0)return;Interlocked.Increment(ref manualActions);try{await DisplayAsync(await controller.RefreshAppsAsync());await RefreshAfterManualAsync();}finally{Interlocked.Decrement(ref manualActions);}}
 private async Task RefreshFromTimerAsync(){if(Volatile.Read(ref manualActions)>0||autoRefreshing)return;autoRefreshing=true;try{var result=await controller.TryAutoRefreshAsync();if(result is not null)await DisplayAsync(result);}finally{autoRefreshing=false;}}
 private async Task RefreshAfterManualAsync(){await DisplayAsync(await controller.RefreshAsync());}
 private Task DisplayAsync(ParentUiResult result){message.Text=result.Message;message.ForeColor=result.Success?Color.DarkGreen:Color.Firebrick;if(result.Status is not { } s)return Task.CompletedTask;countdown.Text=FormatCountdown(s.RemainingSeconds);overview["Used"].Text=$"{s.UsedMinutes} phút";overview["Quota"].Text=$"{s.QuotaMinutes} phút";overview["Grants"].Text=$"{s.GrantMinutes} phút";overview["State"].Text=ParentPolicyStateText.ToVietnamese(s.State);overview["AppState"].Text=s.Apps?.Enforcement?.Armed==true?"BẬT":"TẮT";timePolicy?.Update(s);apps?.Update(s.Apps);web?.Update(s.Web);diagnostics["Profile"].Text=s.ProfileId;diagnostics["Session"].Text=s.ManagedSessionId.ToString(CultureInfo.CurrentCulture);diagnostics["TestMode"].Text=s.TestMode?"BẬT (không khóa thật)":"TẮT";if(s.Diagnostics is { } d){diagnostics["Agent"].Text=d.SessionAgentConnected?"KẾT NỐI":"MẤT KẾT NỐI";diagnostics["Activity"].Text=d.ActivityState;diagnostics["ActivitySource"].Text=d.ActivitySource;diagnostics["Idle"].Text=d.IdleSeconds is { } i?$"{i:F1} giây":"—";diagnostics["WindowsIdle"].Text=d.WindowsIdleSeconds is { } wi?$"{wi:F1} giây":"—";diagnostics["SampleAge"].Text=d.SampleAgeSeconds is { } a?$"{a:F1} giây":"—";diagnostics["TrackedSession"].Text=d.TrackedSessionId.ToString(CultureInfo.CurrentCulture);diagnostics["RecordedSeconds"].Text=$"{d.RecordedTodaySeconds:F1} giây";diagnostics["Checkpoint"].Text=d.LastCheckpointAtUtc?.ToLocalTime().ToString("HH:mm:ss",CultureInfo.CurrentCulture)??"—";diagnostics["LockLatch"].Text=d.ExplicitLockLatched?"LOCKED":"UNLOCKED";diagnostics["WtsConnection"].Text=d.WtsConnectionState?.ToString(CultureInfo.CurrentCulture)??"—";diagnostics["WtsFlags"].Text=d.WtsSessionFlags?.ToString(CultureInfo.CurrentCulture)??"—";diagnostics["Notifications"].Text=d.SessionNotificationsAvailable?"AVAILABLE":"DEGRADED";diagnostics["NotificationError"].Text=d.NotificationError??"—";diagnostics["IdleThreshold"].Text=$"{d.IdleThresholdMinutes} phút";}if(s.Apps?.Enforcement is { } enforcement){diagnostics["AppEnforcement"].Text=enforcement.Armed?$"BẬT — {enforcement.Mode}":"TẮT — mô phỏng";diagnostics["AppEnforcementLast"].Text=enforcement.LastEvent is { } last?$"{last.Action}: {Path.GetFileName(last.ExecutablePath)}":"—";}if(s.TestMode&&diagnosticsTab is not null&&!tabs.TabPages.Contains(diagnosticsTab))tabs.TabPages.Add(diagnosticsTab);else if(!s.TestMode&&diagnosticsTab is not null&&tabs.TabPages.Contains(diagnosticsTab))tabs.TabPages.Remove(diagnosticsTab);if(s.Apps?.Discovery is { } scan){diagnostics["AppScanAt"].Text=scan.LastScanAtUtc?.ToLocalTime().ToString("HH:mm:ss",CultureInfo.CurrentCulture)??"—";diagnostics["AppProcesses"].Text=scan.ProcessesExamined.ToString(CultureInfo.CurrentCulture);diagnostics["AppUsers"].Text=scan.UserApplications.ToString(CultureInfo.CurrentCulture);diagnostics["AppBackground"].Text=scan.BackgroundHelpers.ToString(CultureInfo.CurrentCulture);diagnostics["AppSystem"].Text=scan.SystemProtected.ToString(CultureInfo.CurrentCulture);diagnostics["AppError"].Text=scan.DiscoveryError??"—";}if(reset is not null)reset.Visible=s.TestMode&&s.ProfileId=="m1-child";return Task.CompletedTask;}
}


sealed class DecorativeHeaderPanel:Panel{
 public DecorativeHeaderPanel(){DoubleBuffered=true;BackColor=Color.Transparent;}
 protected override void OnPaintBackground(PaintEventArgs e){
  var r=ClientRectangle;if(r.Width<=0||r.Height<=0)return;
  using var brush=new System.Drawing.Drawing2D.LinearGradientBrush(r,Color.FromArgb(20,50,82),Color.FromArgb(22,142,135),0f);
  e.Graphics.FillRectangle(brush,r);
  e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  using var glow=new SolidBrush(Color.FromArgb(34,255,255,255));
  e.Graphics.FillEllipse(glow,r.Width-180,-55,220,220);
  e.Graphics.FillEllipse(glow,r.Width-310,58,92,92);
  using var dot=new SolidBrush(Color.FromArgb(45,255,255,255));
  for(var i=0;i<6;i++)e.Graphics.FillEllipse(dot,26+i*22,r.Height-25-(i%2)*7,6,6);
 }
}

sealed class InfoCardPanel:Panel{
 public InfoCardPanel(){DoubleBuffered=true;}
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);
  using var pen=new Pen(Color.FromArgb(222,230,239));
  e.Graphics.DrawRectangle(pen,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
 }
}
