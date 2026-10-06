using TuoiTho.Parent;

ApplicationConfiguration.Initialize();

var accountManager = new AccountProtectionManager();
var configureIndex = Array.FindIndex(
    args,
    argument => string.Equals(
        argument,
        "--configure-child-sid",
        StringComparison.OrdinalIgnoreCase));

if (configureIndex >= 0)
{
    if (configureIndex + 1 >= args.Length)
    {
        Environment.ExitCode = 2;
        return;
    }

    var result = accountManager.ConfigureChild(args[configureIndex + 1]);
    MessageBox.Show(
        result.Message,
        "Account Protection Mode",
        MessageBoxButtons.OK,
        result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
    Environment.ExitCode = result.Success ? 0 : 3;
    return;
}

var profile=Environment.GetEnvironmentVariable("M1_PROFILE_ID")??"m1-child";
var session=int.TryParse(Environment.GetEnvironmentVariable("M1_SESSION_ID"),out var parsed)?parsed:System.Diagnostics.Process.GetCurrentProcess().SessionId;
Application.Run(new ParentControlForm(
    new ParentDesktopController(new LocalParentControlClient(),profile,session)));
