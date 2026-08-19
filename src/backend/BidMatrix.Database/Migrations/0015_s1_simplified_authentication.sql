do $migration$
begin
    if exists (select 1 from better_auth.passkey) then
        raise exception 'Cannot remove passkey support while registered passkeys exist';
    end if;
end;
$migration$;

drop table better_auth.passkey;
alter table better_auth.session drop column authentication_method;

revoke all on function public.resolve_federated_identity(text, text, boolean, timestamptz) from {{APP_ROLE}};
revoke all on function public.link_federated_identity(uuid, uuid, uuid, uuid, text, text, text, text, boolean, timestamptz, interval, text) from {{APP_ROLE}};

drop function public.resolve_federated_identity(text, text, boolean, timestamptz);
drop function public.link_federated_identity(uuid, uuid, uuid, uuid, text, text, text, text, boolean, timestamptz, interval, text);

create function public.resolve_federated_identity(
    p_issuer text,
    p_subject_hash text,
    p_authenticated_at timestamptz
)
returns table (
    result_status text,
    identity_id uuid,
    user_id uuid,
    email text,
    display_name text,
    security_stamp uuid,
    memberships_json text,
    platform_roles_json text
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    identity_record user_federated_identities%rowtype;
    user_status text;
begin
    select identity.*
    into identity_record
    from user_federated_identities identity
    where identity.issuer = p_issuer
      and identity.subject_hash = p_subject_hash
    for update;

    if not found then
        return query select
            'not_linked'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::uuid,
            null::text,
            null::text;
        return;
    end if;

    if identity_record.status <> 'active' then
        return query select
            'revoked'::text,
            identity_record.id,
            null::uuid,
            null::text,
            null::text,
            null::uuid,
            null::text,
            null::text;
        return;
    end if;

    select user_record.status
    into user_status
    from users user_record
    join user_credentials credential on credential.user_id = user_record.id
    where user_record.id = identity_record.user_id;

    if not found or user_status <> 'active' then
        return query select
            'unavailable'::text,
            identity_record.id,
            null::uuid,
            null::text,
            null::text,
            null::uuid,
            null::text,
            null::text;
        return;
    end if;

    update user_federated_identities identity
    set last_authenticated_at = p_authenticated_at,
        updated_at = p_authenticated_at,
        version = identity.version + 1
    where identity.id = identity_record.id;

    update users user_record
    set last_login_at = p_authenticated_at,
        updated_at = p_authenticated_at
    where user_record.id = identity_record.user_id;

    return query
    select
        'succeeded'::text,
        identity_record.id,
        user_record.id,
        user_record.email,
        user_record.display_name,
        credential.security_stamp,
        coalesce((
            select jsonb_agg(
                jsonb_build_object(
                    'organizationId', membership.organization_id,
                    'role', membership.role
                )
                order by membership.organization_id
            )
            from organization_memberships membership
            where membership.user_id = user_record.id
        ), '[]'::jsonb)::text,
        coalesce((
            select jsonb_agg(platform_role.role order by platform_role.role)
            from user_platform_roles platform_role
            where platform_role.user_id = user_record.id
        ), '[]'::jsonb)::text
    from users user_record
    join user_credentials credential on credential.user_id = user_record.id
    where user_record.id = identity_record.user_id;
end;
$$;

create function public.link_federated_identity(
    p_identity_id uuid,
    p_user_id uuid,
    p_session_id uuid,
    p_expected_security_stamp uuid,
    p_provider_name text,
    p_issuer text,
    p_subject_hash text,
    p_email text,
    p_linked_at timestamptz,
    p_session_idle_timeout interval,
    p_trace_id text
)
returns table (
    result_status text,
    identity_id uuid
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    target_email text;
    target_status text;
    stored_security_stamp uuid;
    existing_identity user_federated_identities%rowtype;
    conflicting_identity_id uuid;
begin
    if p_provider_name is null or length(p_provider_name) not between 2 and 80 or
       p_issuer is null or length(p_issuer) not between 8 and 2048 or
       p_subject_hash is null or p_subject_hash !~ '^[0-9a-f]{64}$' or
       p_email is null or length(p_email) not between 3 and 320 or
       p_session_idle_timeout <= interval '0 seconds' then
        return query select 'invalid'::text, null::uuid;
        return;
    end if;

    select user_record.email,
           user_record.status,
           credential.security_stamp
    into target_email, target_status, stored_security_stamp
    from users user_record
    join user_credentials credential on credential.user_id = user_record.id
    where user_record.id = p_user_id
    for update of user_record, credential;

    if not found or target_status <> 'active' then
        return query select 'unavailable'::text, null::uuid;
        return;
    end if;

    if stored_security_stamp <> p_expected_security_stamp or
       not exists (
           select 1
           from user_sessions session_record
           where session_record.id = p_session_id
             and session_record.user_id = p_user_id
             and session_record.revoked_at is null
             and session_record.absolute_expires_at > p_linked_at
             and session_record.last_seen_at + p_session_idle_timeout > p_linked_at
       ) then
        return query select 'session_invalid'::text, null::uuid;
        return;
    end if;

    if upper(target_email) <> upper(p_email) then
        return query select 'email_mismatch'::text, null::uuid;
        return;
    end if;

    select identity.*
    into existing_identity
    from user_federated_identities identity
    where identity.issuer = p_issuer
      and identity.subject_hash = p_subject_hash
    for update;

    if found then
        if existing_identity.user_id <> p_user_id then
            return query select 'identity_conflict'::text, null::uuid;
            return;
        end if;

        if existing_identity.status = 'active' then
            return query select 'already_linked'::text, existing_identity.id;
            return;
        end if;

        update user_federated_identities identity
        set provider_name = p_provider_name,
            email_at_link = p_email,
            status = 'active',
            linked_at = p_linked_at,
            last_authenticated_at = null,
            revoked_at = null,
            updated_at = p_linked_at,
            version = identity.version + 1
        where identity.id = existing_identity.id;

        perform append_audit_event(
            gen_random_uuid(),
            'user',
            p_user_id::text,
            'identity.federated.relinked',
            'federated_identity',
            existing_identity.id::text,
            null,
            p_trace_id,
            p_trace_id,
            'Relinked managed identity',
            jsonb_build_object(
                'identityId', existing_identity.id,
                'userId', p_user_id,
                'providerName', p_provider_name
            ),
            p_linked_at
        );

        return query select 'linked'::text, existing_identity.id;
        return;
    end if;

    select identity.id
    into conflicting_identity_id
    from user_federated_identities identity
    where identity.user_id = p_user_id
      and identity.issuer = p_issuer
    for update;

    if found then
        return query select 'identity_conflict'::text, null::uuid;
        return;
    end if;

    insert into user_federated_identities (
        id,
        user_id,
        provider_name,
        issuer,
        subject_hash,
        email_at_link,
        status,
        linked_at,
        last_authenticated_at,
        revoked_at,
        updated_at,
        version
    )
    values (
        p_identity_id,
        p_user_id,
        p_provider_name,
        p_issuer,
        p_subject_hash,
        p_email,
        'active',
        p_linked_at,
        null,
        null,
        p_linked_at,
        1
    );

    perform append_audit_event(
        gen_random_uuid(),
        'user',
        p_user_id::text,
        'identity.federated.linked',
        'federated_identity',
        p_identity_id::text,
        null,
        p_trace_id,
        p_trace_id,
        'Linked managed identity',
        jsonb_build_object(
            'identityId', p_identity_id,
            'userId', p_user_id,
            'providerName', p_provider_name
        ),
        p_linked_at
    );

    return query select 'linked'::text, p_identity_id;
end;
$$;

revoke all on function public.resolve_federated_identity(text, text, timestamptz) from public;
revoke all on function public.link_federated_identity(uuid, uuid, uuid, uuid, text, text, text, text, timestamptz, interval, text) from public;

grant execute on function public.resolve_federated_identity(text, text, timestamptz) to {{APP_ROLE}};
grant execute on function public.link_federated_identity(uuid, uuid, uuid, uuid, text, text, text, text, timestamptz, interval, text) to {{APP_ROLE}};
