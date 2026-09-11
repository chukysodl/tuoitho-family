namespace TuoiTho.Core.Policy;
public interface IDeviceTimePolicyStore
{
 Task<DeviceTimePolicy?> LoadAsync(string profileId, CancellationToken cancellationToken=default);
 Task SaveAsync(DeviceTimePolicy policy, CancellationToken cancellationToken=default);
 Task AddGrantAsync(string profileId, TemporaryGrant grant, CancellationToken cancellationToken=default);
 Task<IReadOnlyList<TemporaryGrant>> GetGrantsAsync(string profileId, CancellationToken cancellationToken=default);
}