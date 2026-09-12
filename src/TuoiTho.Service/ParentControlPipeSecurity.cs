using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
namespace TuoiTho.Service;
public static class ParentControlPipeSecurity
{
 public static PipeSecurity Create(IEnumerable<string> parentSids){ArgumentNullException.ThrowIfNull(parentSids);var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));foreach(var sid in parentSids.Distinct(StringComparer.OrdinalIgnoreCase))security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid),PipeAccessRights.ReadWrite,AccessControlType.Allow));return security;}
 public static NamedPipeServerStream CreateServer(string name,IEnumerable<string> parentSids)=>NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,0,0,Create(parentSids));
}