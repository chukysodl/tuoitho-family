using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
namespace TuoiTho.SessionAgent;
public static class WarningPipeSecurity
{
 public static PipeSecurity Create(SecurityIdentifier managedUserSid)
 {
  ArgumentNullException.ThrowIfNull(managedUserSid);
  var security=new PipeSecurity();
  security.SetAccessRuleProtection(true,false);
  security.AddAccessRule(new PipeAccessRule(managedUserSid,PipeAccessRights.ReadData,AccessControlType.Allow));
  security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.WriteData,AccessControlType.Allow));
  return security;
 }
 public static NamedPipeServerStream CreateServer(string name,SecurityIdentifier managedUserSid)=>NamedPipeServerStreamAcl.Create(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,0,0,Create(managedUserSid));
}