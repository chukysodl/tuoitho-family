create table if not exists public.remote_devices (
    device_id uuid primary key,
    device_name text not null check (length(device_name) between 1 and 100),
    credential_sha256 text not null check (credential_sha256 ~ '^[0-9a-f]{64}$'),
    owner_id uuid references auth.users(id) on delete cascade,
    status jsonb not null default '{}'::jsonb,
    last_seen_at timestamptz,
    created_at timestamptz not null default now()
);

create table if not exists public.remote_pairings (
    code_sha256 text primary key check (code_sha256 ~ '^[0-9a-f]{64}$'),
    device_id uuid not null references public.remote_devices(device_id) on delete cascade,
    expires_at timestamptz not null,
    consumed_at timestamptz,
    created_at timestamptz not null default now()
);

create table if not exists public.remote_commands (
    command_id uuid primary key,
    device_id uuid not null references public.remote_devices(device_id) on delete cascade,
    nonce text not null check (length(nonce) between 16 and 128),
    kind text not null check (kind in ('lockNow','unlock','grantTime','syncPolicy')),
    payload jsonb not null,
    issued_at timestamptz not null,
    expires_at timestamptz not null,
    acknowledgement jsonb,
    created_at timestamptz not null default now(),
    unique (device_id, nonce),
    check (expires_at > issued_at and expires_at <= issued_at + interval '15 minutes')
);

alter table public.remote_devices enable row level security;
alter table public.remote_pairings enable row level security;
alter table public.remote_commands enable row level security;
revoke all on public.remote_devices, public.remote_pairings, public.remote_commands from anon, authenticated;
grant all on public.remote_devices, public.remote_pairings, public.remote_commands to service_role;

create or replace function public.claim_remote_pairing(p_code_sha256 text, p_owner_id uuid)
returns uuid
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_device_id uuid;
begin
    update public.remote_pairings
       set consumed_at = now()
     where code_sha256 = p_code_sha256
       and consumed_at is null
       and expires_at > now()
     returning device_id into v_device_id;

    if v_device_id is null then
        raise exception 'PAIRING_INVALID_OR_EXPIRED';
    end if;

    update public.remote_devices
       set owner_id = p_owner_id
     where device_id = v_device_id
       and owner_id is null;

    if not found then
        raise exception 'DEVICE_ALREADY_PAIRED';
    end if;
    return v_device_id;
end;
$$;

revoke all on function public.claim_remote_pairing(text, uuid) from public, anon, authenticated;
grant execute on function public.claim_remote_pairing(text, uuid) to service_role;

create or replace function public.register_remote_pairing(
    p_device_id uuid,
    p_device_name text,
    p_credential_sha256 text,
    p_code_sha256 text,
    p_expires_at timestamptz
)
returns void
language plpgsql
security definer
set search_path = ''
as $$
declare
    v_credential text;
    v_owner uuid;
begin
    if p_expires_at <= now() or p_expires_at > now() + interval '6 minutes' then
        raise exception 'PAIRING_EXPIRY_INVALID';
    end if;
    select credential_sha256, owner_id into v_credential, v_owner
      from public.remote_devices where device_id = p_device_id for update;
    if found then
        if v_credential <> p_credential_sha256 or v_owner is not null then
            raise exception 'DEVICE_CREDENTIAL_MISMATCH_OR_ALREADY_PAIRED';
        end if;
        update public.remote_devices set device_name = p_device_name where device_id = p_device_id;
    else
        insert into public.remote_devices(device_id, device_name, credential_sha256)
        values (p_device_id, p_device_name, p_credential_sha256);
    end if;
    delete from public.remote_pairings where device_id = p_device_id;
    insert into public.remote_pairings(code_sha256, device_id, expires_at)
    values (p_code_sha256, p_device_id, p_expires_at);
end;
$$;

revoke all on function public.register_remote_pairing(uuid, text, text, text, timestamptz) from public, anon, authenticated;
grant execute on function public.register_remote_pairing(uuid, text, text, text, timestamptz) to service_role;
