namespace TuoiTho.Storage;

internal sealed record Migration(string Id, string Sql);

internal static class SqliteSchema
{
    public const int CurrentVersion = 9;

    public static IReadOnlyList<Migration> Migrations { get; } =
    [
        new Migration(
            "0001-bootstrap",
            """
            CREATE TABLE IF NOT EXISTS device_config (
                device_id TEXT NOT NULL PRIMARY KEY,
                display_name TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS child_profiles (
                profile_id TEXT NOT NULL PRIMARY KEY,
                display_name TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS app_rules (
                rule_id TEXT NOT NULL PRIMARY KEY,
                profile_id TEXT NOT NULL,
                executable_identity TEXT NOT NULL,
                decision TEXT NOT NULL,
                FOREIGN KEY (profile_id) REFERENCES child_profiles (profile_id)
            );

            CREATE TABLE IF NOT EXISTS web_rules (
                rule_id TEXT NOT NULL PRIMARY KEY,
                profile_id TEXT NOT NULL,
                host_pattern TEXT NOT NULL,
                decision TEXT NOT NULL,
                FOREIGN KEY (profile_id) REFERENCES child_profiles (profile_id)
            );

            CREATE TABLE IF NOT EXISTS youtube_rules (
                rule_id TEXT NOT NULL PRIMARY KEY,
                profile_id TEXT NOT NULL,
                rule_type TEXT NOT NULL,
                rule_value TEXT NOT NULL,
                decision TEXT NOT NULL,
                FOREIGN KEY (profile_id) REFERENCES child_profiles (profile_id)
            );

            CREATE TABLE IF NOT EXISTS quota_counters (
                counter_id TEXT NOT NULL PRIMARY KEY,
                profile_id TEXT NOT NULL,
                counter_date TEXT NOT NULL,
                minutes_used INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (profile_id) REFERENCES child_profiles (profile_id)
            );

            CREATE TABLE IF NOT EXISTS temporary_grants (
                grant_id TEXT NOT NULL PRIMARY KEY,
                profile_id TEXT NOT NULL,
                granted_minutes INTEGER NOT NULL,
                expires_at_utc TEXT NOT NULL,
                FOREIGN KEY (profile_id) REFERENCES child_profiles (profile_id)
            );

            CREATE TABLE IF NOT EXISTS pending_requests (
                request_id TEXT NOT NULL PRIMARY KEY,
                profile_id TEXT NOT NULL,
                request_type TEXT NOT NULL,
                request_value TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                status TEXT NOT NULL,
                FOREIGN KEY (profile_id) REFERENCES child_profiles (profile_id)
            );

            CREATE TABLE IF NOT EXISTS operational_events (
                event_id TEXT NOT NULL PRIMARY KEY,
                event_name TEXT NOT NULL,
                outcome TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_app_rules_profile_id
                ON app_rules (profile_id);
            CREATE INDEX IF NOT EXISTS ix_web_rules_profile_id
                ON web_rules (profile_id);
            CREATE INDEX IF NOT EXISTS ix_youtube_rules_profile_id
                ON youtube_rules (profile_id);
            CREATE INDEX IF NOT EXISTS ix_quota_counters_profile_date
                ON quota_counters (profile_id, counter_date);
            """),
        new Migration(
            "0002-time-accounting",
            """
            CREATE TABLE IF NOT EXISTS time_usage_daily (
                profile_id TEXT NOT NULL,
                usage_date TEXT NOT NULL,
                active_milliseconds INTEGER NOT NULL DEFAULT 0,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (profile_id, usage_date)
            );

            CREATE TABLE IF NOT EXISTS time_tracking_checkpoints (
                profile_id TEXT NOT NULL PRIMARY KEY,
                session_id INTEGER NOT NULL,
                state TEXT NOT NULL,
                last_observed_at_utc TEXT NOT NULL,
                boot_started_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_time_usage_daily_date
                ON time_usage_daily (usage_date);
            """),
        new Migration(
            "0003-device-time-policy",
            """
            CREATE TABLE IF NOT EXISTS device_time_policies (
                profile_id TEXT NOT NULL PRIMARY KEY,
                policy_json TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """),
        new Migration(
            "0004-app-policy-simulation",
            """
            CREATE TABLE IF NOT EXISTS app_policy (profile_id TEXT NOT NULL PRIMARY KEY, default_policy TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
            ALTER TABLE app_rules ADD COLUMN identity_json TEXT NULL;
            ALTER TABLE app_rules ADD COLUMN enabled INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE app_rules ADD COLUMN daily_quota_minutes INTEGER NULL;
            ALTER TABLE app_rules ADD COLUMN schedule_json TEXT NULL;
            CREATE TABLE IF NOT EXISTS observed_apps (
                profile_id TEXT NOT NULL, normalized_executable_path TEXT NOT NULL, session_id INTEGER NOT NULL,
                identity_json TEXT NOT NULL, first_seen_at_utc TEXT NOT NULL, last_seen_at_utc TEXT NOT NULL,
                PRIMARY KEY (profile_id, normalized_executable_path)
            );
            CREATE INDEX IF NOT EXISTS ix_observed_apps_profile_session ON observed_apps (profile_id, session_id);
            """),
        new Migration(
            "0005-app-observation-classification",
            """
            ALTER TABLE observed_apps ADD COLUMN classification TEXT NOT NULL DEFAULT 'BackgroundHelper';
            """),
        new Migration(
            "0006-browser-content-policy",
            """
            CREATE TABLE IF NOT EXISTS browser_content_rules (
                profile_id TEXT NOT NULL,
                provider TEXT NOT NULL,
                scope TEXT NOT NULL,
                normalized_key TEXT NOT NULL,
                display_label TEXT NOT NULL,
                decision TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (profile_id, provider, scope, normalized_key),
                FOREIGN KEY (profile_id) REFERENCES child_profiles (profile_id)
            );
            CREATE INDEX IF NOT EXISTS ix_browser_content_rules_profile ON browser_content_rules (profile_id, provider);
            """),
        // Repair databases that recorded 0006 while an earlier build omitted its DDL.
        new Migration(
            "0007-browser-content-policy-repair",
            """
            CREATE TABLE IF NOT EXISTS browser_content_rules (
                profile_id TEXT NOT NULL,
                provider TEXT NOT NULL,
                scope TEXT NOT NULL,
                normalized_key TEXT NOT NULL,
                display_label TEXT NOT NULL,
                decision TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY (profile_id, provider, scope, normalized_key),
                FOREIGN KEY (profile_id) REFERENCES child_profiles (profile_id)
            );
            CREATE INDEX IF NOT EXISTS ix_browser_content_rules_profile ON browser_content_rules (profile_id, provider);
            """),
        new Migration(
            "0008-custom-website-policy-revision",
            """
            CREATE TABLE IF NOT EXISTS browser_policy_revisions (
                profile_id TEXT NOT NULL PRIMARY KEY,
                revision INTEGER NOT NULL DEFAULT 0,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (profile_id) REFERENCES child_profiles (profile_id)
            );
            """),
        new Migration(
            "0009-remote-control-state",
            """
            CREATE TABLE IF NOT EXISTS remote_devices (
                device_id TEXT NOT NULL PRIMARY KEY,
                device_name TEXT NOT NULL,
                protected_credential BLOB NOT NULL,
                created_at_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS remote_pairings (
                code_sha256 TEXT NOT NULL PRIMARY KEY,
                device_id TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                consumed_at_utc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_remote_pairings_expiry ON remote_pairings (expires_at_utc);
            CREATE TABLE IF NOT EXISTS remote_command_receipts (
                command_id TEXT NOT NULL PRIMARY KEY,
                device_id TEXT NOT NULL,
                nonce TEXT NOT NULL,
                received_at_utc TEXT NOT NULL,
                acknowledgement_json TEXT NULL,
                acknowledgement_sent_at_utc TEXT NULL,
                UNIQUE(device_id, nonce)
            );
            CREATE TABLE IF NOT EXISTS remote_policy_state (
                device_id TEXT NOT NULL PRIMARY KEY,
                applied_revision INTEGER NOT NULL,
                applied_at_utc TEXT NOT NULL
            );
            """)
    ];
}
