using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using TuoiTho.Core.Policy;
using TuoiTho.Service;
using TuoiTho.SessionAgent;
namespace TuoiTho.Tests;
public sealed class SessionAgentRuntimeTests
{
 [Fact] public async Task ServicePublisherReachesRealListenerAndSink(){var sid=WindowsIdentity.GetCurrent().User!;var session=System.Diagnostics.Process.GetCurrentProcess().SessionId;var profile="child";var sink=new Sink();var listener=new LocalWarningListener(profile,session,sink,NullLogger<LocalWarningListener>.Instance,sid);var listen=listener.ListenOnceAsync(CancellationToken.None);await Task.Delay(150);var publisher=new LocalSessionWarningPublisher(NullLogger<LocalSessionWarningPublisher>.Instance,id=>$"TuoiTho.Warning.{id}");var sent=false;for(var attempt=0;attempt<5&&!sent;attempt++){sent=await publisher.TryPublishAsync(new SessionWarning(profile,session,0,null,AccessDenyReason.QuotaExhausted));if(!sent)await Task.Delay(50);}Assert.True(sent);await listen.WaitAsync(TimeSpan.FromSeconds(3));Assert.Contains("QUOTA_EXHAUSTED",sink.Messages);}
 private sealed class Sink:IChildWarningSink{public List<string> Messages=[];public void Show(string text)=>Messages.Add(text);}
}