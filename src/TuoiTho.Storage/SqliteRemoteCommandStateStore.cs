using System.Text.Json;
using TuoiTho.Core.Remote;

namespace TuoiTho.Storage;

public sealed class SqliteRemoteCommandStateStore(SqliteDatabase database) : IRemoteCommandStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RemoteStoredDevice?> LoadDeviceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT device_id,device_name,protected_credential,created_at_utc FROM remote_devices LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetString(0), reader.GetString(1), (byte[])reader[2], DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }

    public async Task SaveDeviceAsync(RemoteStoredDevice device, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        command.CommandText = "INSERT INTO remote_devices(device_id,device_name,protected_credential,created_at_utc) VALUES($id,$name,$credential,$created) ON CONFLICT(device_id) DO UPDATE SET device_name=excluded.device_name,protected_credential=excluded.protected_credential;";
        command.Parameters.AddWithValue("$id", device.DeviceId);
        command.Parameters.AddWithValue("$name", device.DeviceName);
        command.Parameters.AddWithValue("$credential", device.ProtectedCredential);
        command.Parameters.AddWithValue("$created", device.CreatedAtUtc.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SavePendingPairingAsync(PendingRemotePairing pairing, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM remote_pairings WHERE device_id=$device;";
            delete.Parameters.AddWithValue("$device", pairing.DeviceId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO remote_pairings(code_sha256,device_id,expires_at_utc,consumed_at_utc) VALUES($hash,$device,$expires,NULL);";
            insert.Parameters.AddWithValue("$hash", pairing.CodeSha256);
            insert.Parameters.AddWithValue("$device", pairing.DeviceId);
            insert.Parameters.AddWithValue("$expires", pairing.ExpiresAtUtc.ToUniversalTime().ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> TryConsumePairingAsync(string codeSha256, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE remote_pairings SET consumed_at_utc=$now WHERE code_sha256=$hash AND consumed_at_utc IS NULL AND expires_at_utc>$now;";
        command.Parameters.AddWithValue("$hash", codeSha256);
        command.Parameters.AddWithValue("$now", nowUtc.ToUniversalTime().ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryRecordCommandAsync(RemoteCommandEnvelope command, DateTimeOffset receivedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var dbCommand = connection.CreateCommand();
        dbCommand.CommandText = "INSERT OR IGNORE INTO remote_command_receipts(command_id,device_id,nonce,received_at_utc) VALUES($id,$device,$nonce,$received);";
        dbCommand.Parameters.AddWithValue("$id", command.CommandId);
        dbCommand.Parameters.AddWithValue("$device", command.DeviceId);
        dbCommand.Parameters.AddWithValue("$nonce", command.Nonce);
        dbCommand.Parameters.AddWithValue("$received", receivedAtUtc.ToUniversalTime().ToString("O"));
        return await dbCommand.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task CompleteCommandAsync(RemoteCommandAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE remote_command_receipts SET acknowledgement_json=$ack WHERE command_id=$id AND device_id=$device AND acknowledgement_json IS NULL;";
        command.Parameters.AddWithValue("$ack", JsonSerializer.Serialize(acknowledgement, JsonOptions));
        command.Parameters.AddWithValue("$id", acknowledgement.CommandId);
        command.Parameters.AddWithValue("$device", acknowledgement.DeviceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RemoteCommandAcknowledgement>> GetUnacknowledgedResultsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<RemoteCommandAcknowledgement>();
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT acknowledgement_json FROM remote_command_receipts WHERE acknowledgement_json IS NOT NULL AND acknowledgement_sent_at_utc IS NULL ORDER BY received_at_utc;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var result = JsonSerializer.Deserialize<RemoteCommandAcknowledgement>(reader.GetString(0), JsonOptions);
            if (result is not null) results.Add(result);
        }
        return results;
    }

    public async Task MarkAcknowledgementSentAsync(string commandId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE remote_command_receipts SET acknowledgement_sent_at_utc=$now WHERE command_id=$id AND acknowledgement_json IS NOT NULL;";
        command.Parameters.AddWithValue("$id", commandId);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
