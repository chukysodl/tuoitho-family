using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using TuoiTho.Core.Policy;
using TuoiTho.SessionAgent;
namespace TuoiTho.Tests;
public sealed class WarningPipeSecurityTests
{
 [Fact] public void OnlyManagedUserAndLocalSystemAreAllowed(){var user=WindowsIdentity.GetCurrent().User!;var security=WarningPipeSecurity.Create(user);var rules=security.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();Assert.Contains(rules,r=>r.IdentityReference==user&&r.AccessControlType==AccessControlType.Allow&&(r.PipeAccessRights&PipeAccessRights.ReadData)!=0);Assert.Contains(rules,r=>r.IdentityReference==new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)&&r.AccessControlType==AccessControlType.Allow&&(r.PipeAccessRights&PipeAccessRights.WriteData)!=0);foreach(var sidType in new[]{WellKnownSidType.WorldSid,WellKnownSidType.AuthenticatedUserSid,WellKnownSidType.BuiltinUsersSid})Assert.DoesNotContain(rules,r=>r.IdentityReference==new SecurityIdentifier(sidType,null));}
 [Fact] public void ManagedSessionCanCreateSecurePipe(){using var pipe=WarningPipeSecurity.CreateServer($"TuoiTho.Test.{Guid.NewGuid():N}",WindowsIdentity.GetCurrent().User!);Assert.False(pipe.IsConnected);}
 [Fact] public async Task MismatchedAndMalformedWarningsAreRejected(){var warning=new SessionWarning("child",3,15,15,AccessDenyReason.None);Assert.True(LocalWarningListener.Accepts(warning,"child",3));Assert.False(LocalWarningListener.Accepts(warning,"other",3));Assert.False(LocalWarningListener.Accepts(warning,"child",4));Assert.False(LocalWarningListener.Accepts(null,"child",3));await using var malformed=new MemoryStream(Encoding.UTF8.GetBytes("{not-json"));Assert.Null(await LocalWarningListener.ReadWarningAsync(malformed,CancellationToken.None));}
 [Fact] public void MissingManagedSidFailsClosed()=>Assert.Throws<ArgumentNullException>(()=>WarningPipeSecurity.Create(null!));
}