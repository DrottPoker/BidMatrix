create table user_sessions (
    id uuid primary key,
    user_id uuid not null references users(id),
    token_hash text unique not null check (token_hash ~ '^[0-9a-f]{64}$'),
    created_at timestamptz not null,
    last_seen_at timestamptz not null,
    absolute_expires_at timestamptz not null,
    revoked_at timestamptz null,
    revoked_reason text null,
    version integer not null check (version > 0),
    check (last_seen_at >= created_at),
    check (absolute_expires_at > created_at),
    check (revoked_at is null or revoked_at >= created_at),
    check (revoked_reason is null or length(revoked_reason) between 1 and 80)
);

create index user_sessions_user_id_created_at_idx
    on user_sessions (user_id, created_at desc);
create index user_sessions_active_expiry_idx
    on user_sessions (absolute_expires_at)
    where revoked_at is null;

create table account_recovery_tokens (
    id uuid primary key,
    user_id uuid not null references users(id),
    token_hash text unique not null check (token_hash ~ '^[0-9a-f]{64}$'),
    status text not null check (status in ('pending', 'used', 'revoked', 'expired')),
    requested_by_user_id uuid not null references users(id),
    created_at timestamptz not null,
    expires_at timestamptz not null,
    used_at timestamptz null,
    revoked_at timestamptz null,
    revoked_reason text null,
    updated_at timestamptz not null,
    version integer not null check (version > 0),
    check (expires_at > created_at),
    check (used_at is null or used_at >= created_at),
    check (revoked_at is null or revoked_at >= created_at),
    check (revoked_reason is null or length(revoked_reason) between 1 and 80),
    check (
        (status = 'pending' and used_at is null and revoked_at is null)
        or (status = 'used' and used_at is not null and revoked_at is null)
        or (status = 'revoked' and used_at is null and revoked_at is not null)
        or (status = 'expired' and used_at is null and revoked_at is null)
    )
);

create index account_recovery_tokens_user_id_created_at_idx
    on account_recovery_tokens (user_id, created_at desc);
create index account_recovery_tokens_pending_expiry_idx
    on account_recovery_tokens (expires_at)
    where status = 'pending';

alter table user_sessions enable row level security;
alter table account_recovery_tokens enable row level security;

create function record_login_failure(
    p_user_id uuid,
    p_recorded_at timestamptz,
    p_lockout_threshold integer,
    p_lockout_end timestamptz
)
returns boolean
language plpgsql
security definer
set search_path = public, pg_temp
as $$
begin
    if p_lockout_threshold < 1 or p_lockout_end <= p_recorded_at then
        return false;
    end if;

    update user_credentials credential
    set failed_access_count = credential.failed_access_count + 1,
        lockout_end = case
            when credential.failed_access_count + 1 >= p_lockout_threshold then p_lockout_end
            else credential.lockout_end
        end,
        updated_at = p_recorded_at,
        version = credential.version + 1
    where credential.user_id = p_user_id;

    return found;
end;
$$;

create function record_login_success(
    p_user_id uuid,
    p_replacement_password_hash text,
    p_recorded_at timestamptz
)
returns boolean
language plpgsql
security definer
set search_path = public, pg_temp
as $$
begin
    update user_credentials credential
    set failed_access_count = 0,
        lockout_end = null,
        password_hash = coalesce(p_replacement_password_hash, credential.password_hash),
        updated_at = p_recorded_at,
        version = credential.version + 1
    where credential.user_id = p_user_id;

    if not found then
        return false;
    end if;

    update users user_record
    set last_login_at = p_recorded_at,
        updated_at = p_recorded_at
    where user_record.id = p_user_id;

    return found;
end;
$$;

create function get_user_password_identity(p_user_id uuid)
returns table (
    password_hash text,
    security_stamp uuid,
    status text
)
language sql
stable
security definer
set search_path = public, pg_temp
as $$
    select
        credential.password_hash,
        credential.security_stamp,
        user_record.status
    from user_credentials credential
    join users user_record on user_record.id = credential.user_id
    where credential.user_id = p_user_id
$$;

create function create_user_session(
    p_session_id uuid,
    p_user_id uuid,
    p_token_hash text,
    p_created_at timestamptz,
    p_absolute_expires_at timestamptz,
    p_trace_id text
)
returns table (
    result_status text,
    session_id uuid,
    created_at timestamptz,
    last_seen_at timestamptz,
    absolute_expires_at timestamptz,
    version integer
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
begin
    if p_absolute_expires_at <= p_created_at or
       not exists (
           select 1
           from users user_record
           join user_credentials credential on credential.user_id = user_record.id
           where user_record.id = p_user_id
             and user_record.status = 'active'
       ) then
        return query select
            'unavailable'::text,
            null::uuid,
            null::timestamptz,
            null::timestamptz,
            null::timestamptz,
            null::integer;
        return;
    end if;

    insert into user_sessions (
        id,
        user_id,
        token_hash,
        created_at,
        last_seen_at,
        absolute_expires_at,
        revoked_at,
        revoked_reason,
        version
    )
    values (
        p_session_id,
        p_user_id,
        p_token_hash,
        p_created_at,
        p_created_at,
        p_absolute_expires_at,
        null,
        null,
        1
    );

    perform append_audit_event(
        gen_random_uuid(),
        'user',
        p_user_id::text,
        'identity.session.created',
        'user_session',
        p_session_id::text,
        null,
        p_trace_id,
        p_trace_id,
        'Created authenticated user session',
        jsonb_build_object(
            'sessionId', p_session_id,
            'absoluteExpiresAt', p_absolute_expires_at
        ),
        p_created_at
    );

    return query select
        'created'::text,
        p_session_id,
        p_created_at,
        p_created_at,
        p_absolute_expires_at,
        1;
end;
$$;

create function validate_user_session(
    p_user_id uuid,
    p_token_hash text,
    p_security_stamp uuid,
    p_validated_at timestamptz,
    p_idle_timeout interval
)
returns table (
    result_status text,
    session_id uuid
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    stored_session_id uuid;
    stored_last_seen_at timestamptz;
    stored_absolute_expires_at timestamptz;
    stored_revoked_at timestamptz;
    stored_security_stamp uuid;
    stored_user_status text;
begin
    if p_idle_timeout <= interval '0' then
        return query select 'invalid'::text, null::uuid;
        return;
    end if;

    select
        session_record.id,
        session_record.last_seen_at,
        session_record.absolute_expires_at,
        session_record.revoked_at,
        credential.security_stamp,
        user_record.status
    into
        stored_session_id,
        stored_last_seen_at,
        stored_absolute_expires_at,
        stored_revoked_at,
        stored_security_stamp,
        stored_user_status
    from user_sessions session_record
    join user_credentials credential on credential.user_id = session_record.user_id
    join users user_record on user_record.id = session_record.user_id
    where session_record.user_id = p_user_id
      and session_record.token_hash = p_token_hash
    for update of session_record;

    if not found then
        return query select 'invalid'::text, null::uuid;
        return;
    end if;

    if stored_user_status <> 'active' or stored_security_stamp <> p_security_stamp then
        return query select 'invalid'::text, stored_session_id;
        return;
    end if;

    if stored_revoked_at is not null then
        return query select 'revoked'::text, stored_session_id;
        return;
    end if;

    if stored_absolute_expires_at <= p_validated_at then
        update user_sessions session_record
        set revoked_at = p_validated_at,
            revoked_reason = 'absolute_timeout',
            version = session_record.version + 1
        where session_record.id = stored_session_id
          and session_record.revoked_at is null;

        return query select 'expired'::text, stored_session_id;
        return;
    end if;

    if stored_last_seen_at + p_idle_timeout <= p_validated_at then
        update user_sessions session_record
        set revoked_at = p_validated_at,
            revoked_reason = 'idle_timeout',
            version = session_record.version + 1
        where session_record.id = stored_session_id
          and session_record.revoked_at is null;

        return query select 'idle'::text, stored_session_id;
        return;
    end if;

    if stored_last_seen_at <= p_validated_at - interval '1 minute' then
        update user_sessions session_record
        set last_seen_at = p_validated_at,
            version = session_record.version + 1
        where session_record.id = stored_session_id;
    end if;

    return query select 'valid'::text, stored_session_id;
end;
$$;

create function list_user_sessions(
    p_user_id uuid,
    p_current_token_hash text,
    p_limit integer,
    p_listed_at timestamptz,
    p_idle_timeout interval
)
returns table (
    session_id uuid,
    created_at timestamptz,
    last_seen_at timestamptz,
    absolute_expires_at timestamptz,
    revoked_at timestamptz,
    revoked_reason text,
    session_status text,
    is_current boolean,
    version integer
)
language sql
stable
security definer
set search_path = public, pg_temp
as $$
    select
        session_record.id,
        session_record.created_at,
        session_record.last_seen_at,
        session_record.absolute_expires_at,
        session_record.revoked_at,
        session_record.revoked_reason,
        case
            when session_record.revoked_at is not null then 'revoked'
            when session_record.absolute_expires_at <= p_listed_at then 'expired'
            when session_record.last_seen_at + p_idle_timeout <= p_listed_at then 'idle'
            else 'active'
        end,
        session_record.token_hash = p_current_token_hash,
        session_record.version
    from user_sessions session_record
    where session_record.user_id = p_user_id
    order by session_record.created_at desc
    limit greatest(1, least(p_limit, 100))
$$;

create function revoke_user_session(
    p_session_id uuid,
    p_user_id uuid,
    p_revoked_at timestamptz,
    p_reason text,
    p_trace_id text
)
returns table (
    result_status text,
    revoked_session_id uuid,
    revoked_at timestamptz
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    stored_revoked_at timestamptz;
begin
    select session_record.revoked_at
    into stored_revoked_at
    from user_sessions session_record
    where session_record.id = p_session_id
      and session_record.user_id = p_user_id
    for update;

    if not found then
        return query select 'not_found'::text, null::uuid, null::timestamptz;
        return;
    end if;

    if stored_revoked_at is not null then
        return query select 'already_revoked'::text, p_session_id, stored_revoked_at;
        return;
    end if;

    update user_sessions session_record
    set revoked_at = p_revoked_at,
        revoked_reason = left(coalesce(nullif(p_reason, ''), 'user_revoked'), 80),
        version = session_record.version + 1
    where session_record.id = p_session_id;

    perform append_audit_event(
        gen_random_uuid(),
        'user',
        p_user_id::text,
        'identity.session.revoked',
        'user_session',
        p_session_id::text,
        null,
        p_trace_id,
        p_trace_id,
        'Revoked authenticated user session',
        jsonb_build_object(
            'sessionId', p_session_id,
            'reason', left(coalesce(nullif(p_reason, ''), 'user_revoked'), 80)
        ),
        p_revoked_at
    );

    return query select 'revoked'::text, p_session_id, p_revoked_at;
end;
$$;

create function revoke_other_user_sessions(
    p_user_id uuid,
    p_current_token_hash text,
    p_revoked_at timestamptz,
    p_trace_id text
)
returns integer
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    revoked_count integer;
begin
    update user_sessions session_record
    set revoked_at = p_revoked_at,
        revoked_reason = 'user_revoked_others',
        version = session_record.version + 1
    where session_record.user_id = p_user_id
      and session_record.token_hash <> p_current_token_hash
      and session_record.revoked_at is null;

    get diagnostics revoked_count = row_count;

    perform append_audit_event(
        gen_random_uuid(),
        'user',
        p_user_id::text,
        'identity.sessions.revoked_others',
        'user',
        p_user_id::text,
        null,
        p_trace_id,
        p_trace_id,
        'Revoked other authenticated user sessions',
        jsonb_build_object('revokedSessionCount', revoked_count),
        p_revoked_at
    );

    return revoked_count;
end;
$$;

create function change_user_password(
    p_user_id uuid,
    p_expected_security_stamp uuid,
    p_password_hash text,
    p_new_security_stamp uuid,
    p_changed_at timestamptz,
    p_trace_id text
)
returns table (
    result_status text,
    revoked_session_count integer
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    stored_security_stamp uuid;
    revoked_count integer;
begin
    select credential.security_stamp
    into stored_security_stamp
    from user_credentials credential
    join users user_record on user_record.id = credential.user_id
    where credential.user_id = p_user_id
      and user_record.status = 'active'
    for update of credential;

    if not found then
        return query select 'unavailable'::text, 0;
        return;
    end if;

    if stored_security_stamp <> p_expected_security_stamp then
        return query select 'conflict'::text, 0;
        return;
    end if;

    update user_credentials credential
    set password_hash = p_password_hash,
        failed_access_count = 0,
        lockout_end = null,
        security_stamp = p_new_security_stamp,
        password_changed_at = p_changed_at,
        updated_at = p_changed_at,
        version = credential.version + 1
    where credential.user_id = p_user_id;

    update user_sessions session_record
    set revoked_at = p_changed_at,
        revoked_reason = 'password_changed',
        version = session_record.version + 1
    where session_record.user_id = p_user_id
      and session_record.revoked_at is null;

    get diagnostics revoked_count = row_count;

    update account_recovery_tokens recovery
    set status = 'revoked',
        revoked_at = p_changed_at,
        revoked_reason = 'password_changed',
        updated_at = p_changed_at,
        version = recovery.version + 1
    where recovery.user_id = p_user_id
      and recovery.status = 'pending';

    perform append_audit_event(
        gen_random_uuid(),
        'user',
        p_user_id::text,
        'identity.password.changed',
        'user',
        p_user_id::text,
        null,
        p_trace_id,
        p_trace_id,
        'Changed account password and revoked sessions',
        jsonb_build_object('revokedSessionCount', revoked_count),
        p_changed_at
    );

    return query select 'changed'::text, revoked_count;
end;
$$;

create function create_account_recovery_token(
    p_recovery_id uuid,
    p_normalized_email text,
    p_token_hash text,
    p_requested_by_user_id uuid,
    p_created_at timestamptz,
    p_expires_at timestamptz,
    p_trace_id text
)
returns table (
    result_status text,
    recovery_id uuid,
    user_id uuid,
    email text,
    display_name text,
    recovery_status text,
    created_at timestamptz,
    expires_at timestamptz,
    version integer
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    target_user_id uuid;
    target_email text;
    target_display_name text;
begin
    if not exists (
        select 1
        from user_platform_roles platform_role
        join users actor on actor.id = platform_role.user_id
        where platform_role.user_id = p_requested_by_user_id
          and platform_role.role = 'platform_owner'
          and actor.status = 'active'
    ) then
        return query select
            'forbidden'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::text,
            null::timestamptz,
            null::timestamptz,
            null::integer;
        return;
    end if;

    select user_record.id, user_record.email, user_record.display_name
    into target_user_id, target_email, target_display_name
    from users user_record
    join user_credentials credential on credential.user_id = user_record.id
    where user_record.normalized_email = p_normalized_email
      and user_record.status = 'active'
    for update of user_record;

    if not found then
        return query select
            'not_found'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::text,
            null::timestamptz,
            null::timestamptz,
            null::integer;
        return;
    end if;

    if p_expires_at <= p_created_at then
        return query select
            'invalid_expiry'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::text,
            null::timestamptz,
            null::timestamptz,
            null::integer;
        return;
    end if;

    update account_recovery_tokens recovery
    set status = case when recovery.expires_at <= p_created_at then 'expired' else 'revoked' end,
        revoked_at = case when recovery.expires_at <= p_created_at then null else p_created_at end,
        revoked_reason = case when recovery.expires_at <= p_created_at then null else 'superseded' end,
        updated_at = p_created_at,
        version = recovery.version + 1
    where recovery.user_id = target_user_id
      and recovery.status = 'pending';

    insert into account_recovery_tokens (
        id,
        user_id,
        token_hash,
        status,
        requested_by_user_id,
        created_at,
        expires_at,
        used_at,
        revoked_at,
        revoked_reason,
        updated_at,
        version
    )
    values (
        p_recovery_id,
        target_user_id,
        p_token_hash,
        'pending',
        p_requested_by_user_id,
        p_created_at,
        p_expires_at,
        null,
        null,
        null,
        p_created_at,
        1
    );

    perform append_audit_event(
        gen_random_uuid(),
        'user',
        p_requested_by_user_id::text,
        'identity.recovery.created',
        'user',
        target_user_id::text,
        null,
        p_trace_id,
        p_trace_id,
        'Created account recovery link',
        jsonb_build_object(
            'recoveryId', p_recovery_id,
            'userId', target_user_id,
            'expiresAt', p_expires_at
        ),
        p_created_at
    );

    return query select
        'created'::text,
        p_recovery_id,
        target_user_id,
        target_email,
        target_display_name,
        'pending'::text,
        p_created_at,
        p_expires_at,
        1;
end;
$$;

create function list_account_recovery_tokens(
    p_requested_by_user_id uuid,
    p_limit integer,
    p_listed_at timestamptz
)
returns table (
    recovery_id uuid,
    user_id uuid,
    email text,
    display_name text,
    recovery_status text,
    created_at timestamptz,
    expires_at timestamptz,
    used_at timestamptz,
    revoked_at timestamptz,
    version integer
)
language plpgsql
stable
security definer
set search_path = public, pg_temp
as $$
begin
    if not exists (
        select 1
        from user_platform_roles platform_role
        join users actor on actor.id = platform_role.user_id
        where platform_role.user_id = p_requested_by_user_id
          and platform_role.role = 'platform_owner'
          and actor.status = 'active'
    ) then
        raise exception using errcode = '42501', message = 'Platform owner access is required.';
    end if;

    return query
    select
        recovery.id,
        recovery.user_id,
        user_record.email,
        user_record.display_name,
        case
            when recovery.status = 'pending' and recovery.expires_at <= p_listed_at then 'expired'
            else recovery.status
        end,
        recovery.created_at,
        recovery.expires_at,
        recovery.used_at,
        recovery.revoked_at,
        recovery.version
    from account_recovery_tokens recovery
    join users user_record on user_record.id = recovery.user_id
    order by recovery.created_at desc
    limit greatest(1, least(p_limit, 100));
end;
$$;

create function revoke_account_recovery_token(
    p_recovery_id uuid,
    p_revoked_by_user_id uuid,
    p_revoked_at timestamptz,
    p_trace_id text
)
returns table (
    result_status text,
    recovery_id uuid,
    user_id uuid,
    email text,
    display_name text,
    recovery_status text,
    created_at timestamptz,
    expires_at timestamptz,
    used_at timestamptz,
    revoked_at timestamptz,
    version integer
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    recovery_record account_recovery_tokens%rowtype;
    target_email text;
    target_display_name text;
begin
    if not exists (
        select 1
        from user_platform_roles platform_role
        join users actor on actor.id = platform_role.user_id
        where platform_role.user_id = p_revoked_by_user_id
          and platform_role.role = 'platform_owner'
          and actor.status = 'active'
    ) then
        return query select
            'forbidden'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::text,
            null::timestamptz,
            null::timestamptz,
            null::timestamptz,
            null::timestamptz,
            null::integer;
        return;
    end if;

    select recovery.*
    into recovery_record
    from account_recovery_tokens recovery
    where recovery.id = p_recovery_id
    for update;

    if not found then
        return query select
            'not_found'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::text,
            null::timestamptz,
            null::timestamptz,
            null::timestamptz,
            null::timestamptz,
            null::integer;
        return;
    end if;

    select user_record.email, user_record.display_name
    into target_email, target_display_name
    from users user_record
    where user_record.id = recovery_record.user_id;

    if recovery_record.status = 'pending' and recovery_record.expires_at <= p_revoked_at then
        update account_recovery_tokens recovery
        set status = 'expired',
            updated_at = p_revoked_at,
            version = recovery.version + 1
        where recovery.id = p_recovery_id
        returning recovery.* into recovery_record;
    elsif recovery_record.status = 'pending' then
        update account_recovery_tokens recovery
        set status = 'revoked',
            revoked_at = p_revoked_at,
            revoked_reason = 'operator_revoked',
            updated_at = p_revoked_at,
            version = recovery.version + 1
        where recovery.id = p_recovery_id
        returning recovery.* into recovery_record;

        perform append_audit_event(
            gen_random_uuid(),
            'user',
            p_revoked_by_user_id::text,
            'identity.recovery.revoked',
            'user',
            recovery_record.user_id::text,
            null,
            p_trace_id,
            p_trace_id,
            'Revoked account recovery link',
            jsonb_build_object(
                'recoveryId', p_recovery_id,
                'userId', recovery_record.user_id
            ),
            p_revoked_at
        );
    end if;

    return query select
        recovery_record.status,
        recovery_record.id,
        recovery_record.user_id,
        target_email,
        target_display_name,
        recovery_record.status,
        recovery_record.created_at,
        recovery_record.expires_at,
        recovery_record.used_at,
        recovery_record.revoked_at,
        recovery_record.version;
end;
$$;

create function inspect_account_recovery_token(
    p_token_hash text,
    p_inspected_at timestamptz
)
returns table (
    recovery_status text,
    recovery_id uuid,
    user_id uuid,
    email text,
    expires_at timestamptz
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    recovery_record account_recovery_tokens%rowtype;
    target_email text;
begin
    select recovery.*
    into recovery_record
    from account_recovery_tokens recovery
    where recovery.token_hash = p_token_hash
    for update;

    if not found then
        return query select 'invalid'::text, null::uuid, null::uuid, null::text, null::timestamptz;
        return;
    end if;

    if recovery_record.status = 'pending' and recovery_record.expires_at <= p_inspected_at then
        update account_recovery_tokens recovery
        set status = 'expired',
            updated_at = p_inspected_at,
            version = recovery.version + 1
        where recovery.id = recovery_record.id
        returning recovery.* into recovery_record;
    end if;

    select user_record.email
    into target_email
    from users user_record
    where user_record.id = recovery_record.user_id;

    return query select
        recovery_record.status,
        recovery_record.id,
        recovery_record.user_id,
        target_email,
        recovery_record.expires_at;
end;
$$;

create function reset_password_with_recovery_token(
    p_token_hash text,
    p_password_hash text,
    p_new_security_stamp uuid,
    p_reset_at timestamptz,
    p_trace_id text
)
returns table (
    result_status text,
    user_id uuid,
    email text,
    revoked_session_count integer
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    recovery_record account_recovery_tokens%rowtype;
    target_email text;
    target_status text;
    revoked_count integer;
begin
    select recovery.*
    into recovery_record
    from account_recovery_tokens recovery
    where recovery.token_hash = p_token_hash
    for update;

    if not found then
        return query select 'invalid'::text, null::uuid, null::text, 0;
        return;
    end if;

    if recovery_record.status = 'pending' and recovery_record.expires_at <= p_reset_at then
        update account_recovery_tokens recovery
        set status = 'expired',
            updated_at = p_reset_at,
            version = recovery.version + 1
        where recovery.id = recovery_record.id;

        return query select 'expired'::text, null::uuid, null::text, 0;
        return;
    end if;

    if recovery_record.status <> 'pending' then
        return query select recovery_record.status, null::uuid, null::text, 0;
        return;
    end if;

    select user_record.email, user_record.status
    into target_email, target_status
    from users user_record
    join user_credentials credential on credential.user_id = user_record.id
    where user_record.id = recovery_record.user_id
    for update of credential;

    if not found or target_status <> 'active' then
        return query select 'unavailable'::text, null::uuid, null::text, 0;
        return;
    end if;

    update user_credentials credential
    set password_hash = p_password_hash,
        failed_access_count = 0,
        lockout_end = null,
        security_stamp = p_new_security_stamp,
        password_changed_at = p_reset_at,
        updated_at = p_reset_at,
        version = credential.version + 1
    where credential.user_id = recovery_record.user_id;

    update user_sessions session_record
    set revoked_at = p_reset_at,
        revoked_reason = 'password_recovered',
        version = session_record.version + 1
    where session_record.user_id = recovery_record.user_id
      and session_record.revoked_at is null;

    get diagnostics revoked_count = row_count;

    update account_recovery_tokens recovery
    set status = case when recovery.id = recovery_record.id then 'used' else 'revoked' end,
        used_at = case when recovery.id = recovery_record.id then p_reset_at else null end,
        revoked_at = case when recovery.id = recovery_record.id then null else p_reset_at end,
        revoked_reason = case when recovery.id = recovery_record.id then null else 'password_recovered' end,
        updated_at = p_reset_at,
        version = recovery.version + 1
    where recovery.user_id = recovery_record.user_id
      and recovery.status = 'pending';

    perform append_audit_event(
        gen_random_uuid(),
        'recovery_token',
        recovery_record.id::text,
        'identity.password.recovered',
        'user',
        recovery_record.user_id::text,
        null,
        p_trace_id,
        p_trace_id,
        'Recovered account password and revoked sessions',
        jsonb_build_object(
            'recoveryId', recovery_record.id,
            'userId', recovery_record.user_id,
            'revokedSessionCount', revoked_count
        ),
        p_reset_at
    );

    return query select 'succeeded'::text, recovery_record.user_id, target_email, revoked_count;
end;
$$;

revoke all on table user_sessions from public;
revoke all on table account_recovery_tokens from public;
revoke all on table user_sessions from {{APP_ROLE}};
revoke all on table account_recovery_tokens from {{APP_ROLE}};
revoke select, insert, update, delete on table user_credentials from {{APP_ROLE}};

revoke all on function record_login_failure(uuid, timestamptz, integer, timestamptz) from public;
revoke all on function record_login_success(uuid, text, timestamptz) from public;
revoke all on function get_user_password_identity(uuid) from public;
revoke all on function create_user_session(uuid, uuid, text, timestamptz, timestamptz, text) from public;
revoke all on function validate_user_session(uuid, text, uuid, timestamptz, interval) from public;
revoke all on function list_user_sessions(uuid, text, integer, timestamptz, interval) from public;
revoke all on function revoke_user_session(uuid, uuid, timestamptz, text, text) from public;
revoke all on function revoke_other_user_sessions(uuid, text, timestamptz, text) from public;
revoke all on function change_user_password(uuid, uuid, text, uuid, timestamptz, text) from public;
revoke all on function create_account_recovery_token(uuid, text, text, uuid, timestamptz, timestamptz, text) from public;
revoke all on function list_account_recovery_tokens(uuid, integer, timestamptz) from public;
revoke all on function revoke_account_recovery_token(uuid, uuid, timestamptz, text) from public;
revoke all on function inspect_account_recovery_token(text, timestamptz) from public;
revoke all on function reset_password_with_recovery_token(text, text, uuid, timestamptz, text) from public;

grant execute on function record_login_failure(uuid, timestamptz, integer, timestamptz) to {{APP_ROLE}};
grant execute on function record_login_success(uuid, text, timestamptz) to {{APP_ROLE}};
grant execute on function get_user_password_identity(uuid) to {{APP_ROLE}};
grant execute on function create_user_session(uuid, uuid, text, timestamptz, timestamptz, text) to {{APP_ROLE}};
grant execute on function validate_user_session(uuid, text, uuid, timestamptz, interval) to {{APP_ROLE}};
grant execute on function list_user_sessions(uuid, text, integer, timestamptz, interval) to {{APP_ROLE}};
grant execute on function revoke_user_session(uuid, uuid, timestamptz, text, text) to {{APP_ROLE}};
grant execute on function revoke_other_user_sessions(uuid, text, timestamptz, text) to {{APP_ROLE}};
grant execute on function change_user_password(uuid, uuid, text, uuid, timestamptz, text) to {{APP_ROLE}};
grant execute on function create_account_recovery_token(uuid, text, text, uuid, timestamptz, timestamptz, text) to {{APP_ROLE}};
grant execute on function list_account_recovery_tokens(uuid, integer, timestamptz) to {{APP_ROLE}};
grant execute on function revoke_account_recovery_token(uuid, uuid, timestamptz, text) to {{APP_ROLE}};
grant execute on function inspect_account_recovery_token(text, timestamptz) to {{APP_ROLE}};
grant execute on function reset_password_with_recovery_token(text, text, uuid, timestamptz, text) to {{APP_ROLE}};
