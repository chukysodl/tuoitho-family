using TuoiTho.Core.Policy;

namespace TuoiTho.Parent;

public sealed class WebPolicyPanel : UserControl
{
    private readonly ParentDesktopController controller;
    private readonly TextBox youtube = new() { Width = 260, PlaceholderText = "Dán URL kênh hoặc @handle" };
    private readonly TextBox tiktok = new() { Width = 260, PlaceholderText = "Dán URL hoặc @username" };
    private readonly TextBox customWebsite = new() { Width = 390, PlaceholderText = "Nhập tên miền hoặc URL" };
    private readonly RadioButton wholeSite = new() { Text = "Chặn toàn bộ website", Checked = true, AutoSize = true };
    private readonly RadioButton pathPrefix = new() { Text = "Chặn đường dẫn này", AutoSize = true };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly Label message = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
    private readonly Label runtime = new() { AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Padding = new Padding(0, 0, 0, 8) };
    private IReadOnlyList<WebRule> rules = [];

    public WebPolicyPanel(ParentDesktopController controller)
    {
        this.controller = controller;
        Dock = DockStyle.Fill;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "WEB", Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = true }, 0, 0);
        root.Controls.Add(runtime, 0, 1);
        var providerActions = new FlowLayoutPanel { AutoSize = true };
        Add(providerActions, "Cho phép YouTube", () => SetSite(BrowserProvider.YouTube, WebRuleDecision.Allow)); Add(providerActions, "Chặn YouTube", () => SetSite(BrowserProvider.YouTube, WebRuleDecision.Block)); providerActions.Controls.Add(youtube); Add(providerActions, "Cho phép kênh", () => Channel(WebRuleDecision.Allow)); Add(providerActions, "Chặn kênh", () => Channel(WebRuleDecision.Block));
        Add(providerActions, "Cho phép TikTok", () => SetSite(BrowserProvider.TikTok, WebRuleDecision.Allow)); Add(providerActions, "Chặn TikTok", () => SetSite(BrowserProvider.TikTok, WebRuleDecision.Block)); providerActions.Controls.Add(tiktok); Add(providerActions, "Cho phép tài khoản", () => Creator(WebRuleDecision.Allow)); Add(providerActions, "Chặn tài khoản", () => Creator(WebRuleDecision.Block));
        root.Controls.Add(providerActions, 0, 2);
        root.Controls.Add(new Label { Text = "TRANG WEB TÙY CHỈNH", Font = new Font("Segoe UI", 12, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 16, 0, 4) }, 0, 3);
        var customActions = new FlowLayoutPanel { AutoSize = true }; customActions.Controls.Add(customWebsite); customActions.Controls.Add(wholeSite); customActions.Controls.Add(pathPrefix); Add(customActions, "CHẶN", () => Custom(WebRuleDecision.Block)); Add(customActions, "CHO PHÉP", () => Custom(WebRuleDecision.Allow)); Add(customActions, "XÓA", Delete); root.Controls.Add(customActions, 0, 4);
        grid.Columns.Add("Provider", "Website / URL"); grid.Columns.Add("Scope", "Phạm vi"); grid.Columns.Add("Decision", "Quy tắc"); grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; grid.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells; grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        root.Controls.Add(grid, 0, 5); root.Controls.Add(message, 0, 6); Controls.Add(root);
    }

    public void Update(ParentWebControlStatus? status)
    {
        rules = status?.Rules ?? []; grid.Rows.Clear();
        foreach (var rule in rules) grid.Rows.Add(rule.DisplayLabel, ScopeText(rule.Scope), rule.Decision == WebRuleDecision.Allow ? "CHO PHÉP" : "CHẶN");
        var health = status?.RuntimeStatus;
        runtime.Text = health is null ? "Trình duyệt: CHƯA KẾT NỐI" : $"Trình duyệt: {(health.ExtensionConnected ? "KẾT NỐI" : "CHƯA KẾT NỐI")}    BrowserPolicy: {health.BrowserPolicyState}    Revision: {status?.PolicyRevision ?? 0}    Custom rules in policy: {health.CustomPolicyRuleCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"}    DNR rules active: {health.ActiveDnrRuleCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—"}    DNR_SYNC: {health.DnrSyncState ?? "—"}{(health.DnrError is null ? "" : "    DNR_ERROR: " + health.DnrError)}";
    }

    private static string ScopeText(WebRuleScope scope) => scope switch { WebRuleScope.Domain => "Toàn website", WebRuleScope.PathPrefix => "Đường dẫn", WebRuleScope.YouTubeChannel => "Kênh YouTube", WebRuleScope.TikTokCreator => "Tài khoản TikTok", _ => "Toàn nền tảng" };
    private static Button Add(FlowLayoutPanel panel, string text, Func<Task> action) { var button = new Button { Text = text, AutoSize = true }; button.Click += async (_, _) => await action(); panel.Controls.Add(button); return button; }
    private Task SetSite(BrowserProvider provider, WebRuleDecision decision) => Send(ParentControlAction.SaveWebRule, new("m1-child", provider, WebRuleScope.Site, decision, WebPolicyEngine.SiteKey(provider), provider == BrowserProvider.YouTube ? "YouTube" : "TikTok"));
    private Task Channel(WebRuleDecision decision) { if (!WebIdentityNormalizer.TryNormalizeYouTubeChannel(youtube.Text, out var key, out var label)) { message.Text = "URL hoặc @handle YouTube không hợp lệ."; return Task.CompletedTask; } return Send(ParentControlAction.SaveWebRule, new("m1-child", BrowserProvider.YouTube, WebRuleScope.YouTubeChannel, decision, key, label)); }
    private Task Creator(WebRuleDecision decision) { if (!WebIdentityNormalizer.TryNormalizeTikTokCreator(tiktok.Text, out var key, out var label)) { message.Text = "URL hoặc @username TikTok không hợp lệ."; return Task.CompletedTask; } return Send(ParentControlAction.SaveWebRule, new("m1-child", BrowserProvider.TikTok, WebRuleScope.TikTokCreator, decision, key, label)); }
    private Task Custom(WebRuleDecision decision)
    {
        var scope = wholeSite.Checked ? WebRuleScope.Domain : WebRuleScope.PathPrefix;
        if (!WebIdentityNormalizer.TryNormalizeCustomWebsite(customWebsite.Text, scope, out var identity)) { message.Text = "Tên miền hoặc URL không hợp lệ."; return Task.CompletedTask; }
        return Send(ParentControlAction.SaveWebRule, new("m1-child", BrowserProvider.GenericWeb, scope, decision, identity.NormalizedKey, identity.DisplayValue));
    }
    private Task Delete() { if (grid.CurrentRow?.Index is not int index || index < 0 || index >= rules.Count) return Task.CompletedTask; return Send(ParentControlAction.RemoveWebRule, rules[index]); }
    private async Task Send(ParentControlAction action, WebRule rule) { var result = await controller.SendWebRuleAsync(action, rule); message.Text = result.Message; Update(result.Status?.Web); }
}
