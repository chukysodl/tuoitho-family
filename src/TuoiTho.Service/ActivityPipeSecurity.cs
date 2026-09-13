using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TuoiTho.Service;

public static class ActivityPipeSecurity
{
    public static PipeSecurity Create(SecurityIdentifier managedChildSid)
    {
        ArgumentNullException.ThrowIfNull(managedChildSid);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(managedChildSid, PipeAccessRights.Write | PipeAccessRights.ReadAttributes | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return security;
    }

    public static NamedPipeServerStream CreateServer(string name, SecurityIdentifier managedChildSid) =>
        NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, Create(managedChildSid));
}