using TuoiTho.Core.Policy;
namespace TuoiTho.Parent;
public sealed class AppPolicyPanel : GroupBox
{
 private readonly ParentDesktopController controller;
 private readonly ListBox apps=new(){Dock=DockStyle.Top,Height=150};
 private readonly Label defaultPolicy=new(){AutoSize=true,Text="Ứng dụng chưa duyệt: CHẶN MẶC ĐỊNH",Padding=new Padding(6)};
 private IReadOnlyList<ParentObservedApp> observed=[];
 public AppPolicyPanel(ParentDesktopController controller){this.controller=controller;Text="ỨNG DỤNG (M2 mô phỏng — không chặn thật)";Dock=DockStyle.Top;Height=250;var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=55};Add(buttons,"CHO PHÉP",ParentControlAction.AllowApp);Add(buttons,"CHẶN",ParentControlAction.BlockApp);Add(buttons,"XÓA QUY TẮC",ParentControlAction.RemoveAppRule);var refresh=new Button{Text="LÀM MỚI",AutoSize=true};refresh.Click+=(_,_)=>RefreshRequested?.Invoke(this,EventArgs.Empty);buttons.Controls.Add(refresh);Controls.Add(buttons);Controls.Add(apps);Controls.Add(defaultPolicy);}
 public event EventHandler? RefreshRequested;
 public void Update(ParentAppControlStatus? status){observed=status?.ObservedApps??[];apps.Items.Clear();foreach(var app in observed)apps.Items.Add($"{app.Identity.DisplayName??app.Identity.FileName} — {app.Decision} ({app.Reason})");defaultPolicy.Text=$"Ứng dụng chưa duyệt: {(status?.DefaultPolicy==DefaultAppPolicy.AllowUnknown?"CHO PHÉP MẶC ĐỊNH":"CHẶN MẶC ĐỊNH")}";}
 private void Add(FlowLayoutPanel panel,string text,ParentControlAction action){var b=new Button{Text=text,AutoSize=true};b.Click+=async(_,_)=>{if(apps.SelectedIndex<0)return;var result=await controller.SendAppAsync(action,observed[apps.SelectedIndex].Identity);Update(result.Status?.Apps);};panel.Controls.Add(b);}
}