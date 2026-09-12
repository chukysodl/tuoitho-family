using TuoiTho.Parent;
var profile=Environment.GetEnvironmentVariable("M1_PROFILE_ID")??"m1-child";
var session=int.TryParse(Environment.GetEnvironmentVariable("M1_SESSION_ID"),out var parsed)?parsed:System.Diagnostics.Process.GetCurrentProcess().SessionId;
ApplicationConfiguration.Initialize();
Application.Run(new ParentControlForm(new ParentDesktopController(new LocalParentControlClient(),profile,session)));