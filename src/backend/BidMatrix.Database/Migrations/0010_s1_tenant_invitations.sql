create table tenant_invitations (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    organization_name text not null,
    organization_slug text not null,
    email text not null,
    normalized_email text not null,
    role text not null check (role = 'owner'),
    token_hash text unique not null check (token_hash ~ '^[0-9a-f]{64}$'),
    status text not null check (status in ('pending', 'accepted', 'revoked', 'expired')),
    invited_by_user_id uuid not null references users(id),
    accepted_by_user_id uuid null references users(id),
    expires_at timestamptz not null,
    accepted_at timestamptz null,
    revoked_at timestamptz null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    version integer not null check (version > 0),
    check (expires_at > created_at),
    check (
        (status = 'pending' and accepted_by_user_id is null and accepted_at is null and revoked_at is null)
        or (status = 'accepted' and accepted_by_user_id is not null and accepted_at is not null and revoked_at is null)
        or (status = 'revoked' and accepted_by_user_id is null and accepted_at is null and revoked_at is not null)
        or (status = 'expired' and accepted_by_user_id is null and accepted_at is null and revoked_at is null)
    )
);

create unique index tenant_invitations_pending_email_idx
    on tenant_invitations (normalized_email)
    where status = 'pending';

create index tenant_invitations_organization_id_created_at_idx
    on tenant_invitations (organization_id, created_at desc);

create index tenant_invitations_status_expires_at_idx
    on tenant_invitations (status, expires_at);

alter table tenant_invitations enable row level security;

create function create_tenant_owner_invitation(
    p_invitation_id uuid,
    p_organization_id uuid,
    p_organization_name text,
    p_organization_slug text,
    p_email text,
    p_normalized_email text,
    p_token_hash text,
    p_invited_by_user_id uuid,
    p_created_at timestamptz,
    p_expires_at timestamptz,
    p_trace_id text
)
returns table (
    result_status text,
    invitation_id uuid,
    organization_id uuid,
    organization_name text,
    organization_slug text,
    invited_email text,
    invitation_role text,
    invitation_status text,
    invitation_created_at timestamptz,
    invitation_expires_at timestamptz,
    invitation_version integer
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
begin
    perform pg_advisory_xact_lock(hashtextextended('tenant-slug:' || p_organization_slug, 49089055130010));
    perform pg_advisory_xact_lock(hashtextextended('tenant-email:' || p_normalized_email, 49089055130010));

    update tenant_invitations invitation
    set status = 'expired',
        updated_at = p_created_at,
        version = invitation.version + 1
    where invitation.normalized_email = p_normalized_email
      and invitation.status = 'pending'
      and invitation.expires_at <= p_created_at;

    if exists (
        select 1
        from users user_record
        where user_record.normalized_email = p_normalized_email
    ) then
        return query select
            'email_registered'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::text,
            null::text,
            null::text,
            null::timestamptz,
            null::timestamptz,
            null::integer;
        return;
    end if;

    if exists (
        select 1
        from tenant_invitations invitation
        where invitation.normalized_email = p_normalized_email
          and invitation.status = 'pending'
    ) then
        return query select
            'invitation_pending'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::text,
            null::text,
            null::text,
            null::timestamptz,
            null::timestamptz,
            null::integer;
        return;
    end if;

    perform set_config('app.organization_id', p_organization_id::text, true);

    begin
        insert into organizations (id, name, slug, status, created_at, updated_at)
        values (
            p_organization_id,
            p_organization_name,
            p_organization_slug,
            'invited',
            p_created_at,
            p_created_at
        );
    exception
        when unique_violation then
            return query select
                'slug_conflict'::text,
                null::uuid,
                null::uuid,
                null::text,
                null::text,
                null::text,
                null::text,
                null::text,
                null::timestamptz,
                null::timestamptz,
                null::integer;
            return;
    end;

    insert into tenant_invitations (
        id,
        organization_id,
        organization_name,
        organization_slug,
        email,
        normalized_email,
        role,
        token_hash,
        status,
        invited_by_user_id,
        expires_at,
        created_at,
        updated_at,
        version
    )
    values (
        p_invitation_id,
        p_organization_id,
        p_organization_name,
        p_organization_slug,
        p_email,
        p_normalized_email,
        'owner',
        p_token_hash,
        'pending',
        p_invited_by_user_id,
        p_expires_at,
        p_created_at,
        p_created_at,
        1
    );

    perform append_audit_event(
        gen_random_uuid(),
        'user',
        p_invited_by_user_id::text,
        'tenant.invitation.created',
        'tenant_invitation',
        p_invitation_id::text,
        p_organization_id,
        p_trace_id,
        p_trace_id,
        'Created initial tenant owner invitation',
        jsonb_build_object(
            'invitationId', p_invitation_id,
            'organizationId', p_organization_id,
            'role', 'owner',
            'expiresAt', p_expires_at
        ),
        p_created_at
    );

    return query select
        'created'::text,
        p_invitation_id,
        p_organization_id,
        p_organization_name,
        p_organization_slug,
        p_email,
        'owner'::text,
        'pending'::text,
        p_created_at,
        p_expires_at,
        1;
end;
$$;

create function list_tenant_owner_invitations(p_limit integer)
returns table (
    invitation_id uuid,
    organization_id uuid,
    organization_name text,
    organization_slug text,
    invited_email text,
    invitation_role text,
    invitation_status text,
    invitation_created_at timestamptz,
    invitation_expires_at timestamptz,
    invitation_accepted_at timestamptz,
    invitation_revoked_at timestamptz,
    invitation_version integer
)
language sql
stable
security definer
set search_path = public, pg_temp
as $$
    select
        invitation.id,
        invitation.organization_id,
        invitation.organization_name,
        invitation.organization_slug,
        invitation.email,
        invitation.role,
        case
            when invitation.status = 'pending' and invitation.expires_at <= now() then 'expired'
            else invitation.status
        end,
        invitation.created_at,
        invitation.expires_at,
        invitation.accepted_at,
        invitation.revoked_at,
        invitation.version
    from tenant_invitations invitation
    order by invitation.created_at desc, invitation.id desc
    limit least(greatest(p_limit, 1), 100)
$$;

create function inspect_tenant_owner_invitation(p_token_hash text, p_inspected_at timestamptz)
returns table (
    result_status text,
    invitation_id uuid,
    organization_id uuid,
    organization_name text,
    organization_slug text,
    invited_email text,
    invitation_role text,
    invitation_expires_at timestamptz
)
language sql
stable
security definer
set search_path = public, pg_temp
as $$
    select
        case
            when invitation.status = 'pending' and invitation.expires_at <= p_inspected_at then 'expired'
            else invitation.status
        end,
        invitation.id,
        invitation.organization_id,
        invitation.organization_name,
        invitation.organization_slug,
        invitation.email,
        invitation.role,
        invitation.expires_at
    from tenant_invitations invitation
    where invitation.token_hash = p_token_hash
$$;

create function revoke_tenant_owner_invitation(
    p_invitation_id uuid,
    p_revoked_by_user_id uuid,
    p_revoked_at timestamptz,
    p_trace_id text
)
returns table (
    result_status text,
    invitation_id uuid,
    organization_id uuid,
    organization_name text,
    organization_slug text,
    invited_email text,
    invitation_role text,
    invitation_status text,
    invitation_created_at timestamptz,
    invitation_expires_at timestamptz,
    invitation_accepted_at timestamptz,
    invitation_revoked_at timestamptz,
    invitation_version integer
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    invitation_record tenant_invitations%rowtype;
begin
    select invitation.*
    into invitation_record
    from tenant_invitations invitation
    where invitation.id = p_invitation_id
    for update;

    if not found then
        return query select
            'not_found'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
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

    if invitation_record.status = 'pending' and invitation_record.expires_at <= p_revoked_at then
        update tenant_invitations invitation
        set status = 'expired',
            updated_at = p_revoked_at,
            version = invitation.version + 1
        where invitation.id = p_invitation_id
        returning invitation.* into invitation_record;
    elsif invitation_record.status = 'pending' then
        update tenant_invitations invitation
        set status = 'revoked',
            revoked_at = p_revoked_at,
            updated_at = p_revoked_at,
            version = invitation.version + 1
        where invitation.id = p_invitation_id
        returning invitation.* into invitation_record;

        perform append_audit_event(
            gen_random_uuid(),
            'user',
            p_revoked_by_user_id::text,
            'tenant.invitation.revoked',
            'tenant_invitation',
            p_invitation_id::text,
            invitation_record.organization_id,
            p_trace_id,
            p_trace_id,
            'Revoked tenant owner invitation',
            jsonb_build_object(
                'invitationId', p_invitation_id,
                'organizationId', invitation_record.organization_id,
                'role', invitation_record.role
            ),
            p_revoked_at
        );
    end if;

    return query select
        invitation_record.status,
        invitation_record.id,
        invitation_record.organization_id,
        invitation_record.organization_name,
        invitation_record.organization_slug,
        invitation_record.email,
        invitation_record.role,
        invitation_record.status,
        invitation_record.created_at,
        invitation_record.expires_at,
        invitation_record.accepted_at,
        invitation_record.revoked_at,
        invitation_record.version;
end;
$$;

create function accept_tenant_owner_invitation(
    p_token_hash text,
    p_user_id uuid,
    p_membership_id uuid,
    p_display_name text,
    p_password_hash text,
    p_security_stamp uuid,
    p_accepted_at timestamptz,
    p_trace_id text
)
returns table (
    result_status text,
    invitation_id uuid,
    organization_id uuid,
    organization_name text,
    invited_email text,
    invitation_role text,
    accepted_user_id uuid,
    accepted_display_name text,
    accepted_security_stamp uuid,
    invitation_expires_at timestamptz
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    invitation_record tenant_invitations%rowtype;
begin
    select invitation.*
    into invitation_record
    from tenant_invitations invitation
    where invitation.token_hash = p_token_hash
    for update;

    if not found then
        return query select
            'invalid'::text,
            null::uuid,
            null::uuid,
            null::text,
            null::text,
            null::text,
            null::uuid,
            null::text,
            null::uuid,
            null::timestamptz;
        return;
    end if;

    if invitation_record.status <> 'pending' then
        return query select
            invitation_record.status,
            invitation_record.id,
            invitation_record.organization_id,
            invitation_record.organization_name,
            invitation_record.email,
            invitation_record.role,
            null::uuid,
            null::text,
            null::uuid,
            invitation_record.expires_at;
        return;
    end if;

    if invitation_record.expires_at <= p_accepted_at then
        update tenant_invitations invitation
        set status = 'expired',
            updated_at = p_accepted_at,
            version = invitation.version + 1
        where invitation.id = invitation_record.id;

        return query select
            'expired'::text,
            invitation_record.id,
            invitation_record.organization_id,
            invitation_record.organization_name,
            invitation_record.email,
            invitation_record.role,
            null::uuid,
            null::text,
            null::uuid,
            invitation_record.expires_at;
        return;
    end if;

    perform pg_advisory_xact_lock(hashtextextended('tenant-email:' || invitation_record.normalized_email, 49089055130010));

    if exists (
        select 1
        from users user_record
        where user_record.normalized_email = invitation_record.normalized_email
    ) then
        return query select
            'email_registered'::text,
            invitation_record.id,
            invitation_record.organization_id,
            invitation_record.organization_name,
            invitation_record.email,
            invitation_record.role,
            null::uuid,
            null::text,
            null::uuid,
            invitation_record.expires_at;
        return;
    end if;

    perform set_config('app.organization_id', invitation_record.organization_id::text, true);

    insert into users (
        id,
        email,
        normalized_email,
        display_name,
        status,
        created_at,
        updated_at,
        last_login_at
    )
    values (
        p_user_id,
        invitation_record.email,
        invitation_record.normalized_email,
        p_display_name,
        'active',
        p_accepted_at,
        p_accepted_at,
        p_accepted_at
    );

    insert into user_credentials (
        user_id,
        password_hash,
        failed_access_count,
        lockout_end,
        security_stamp,
        password_changed_at,
        created_at,
        updated_at,
        version
    )
    values (
        p_user_id,
        p_password_hash,
        0,
        null,
        p_security_stamp,
        p_accepted_at,
        p_accepted_at,
        p_accepted_at,
        1
    );

    insert into organization_memberships (
        id,
        organization_id,
        user_id,
        role,
        created_at
    )
    values (
        p_membership_id,
        invitation_record.organization_id,
        p_user_id,
        invitation_record.role,
        p_accepted_at
    );

    update organizations organization
    set status = 'active',
        updated_at = p_accepted_at
    where organization.id = invitation_record.organization_id;

    update tenant_invitations invitation
    set status = 'accepted',
        accepted_by_user_id = p_user_id,
        accepted_at = p_accepted_at,
        updated_at = p_accepted_at,
        version = invitation.version + 1
    where invitation.id = invitation_record.id;

    perform append_audit_event(
        gen_random_uuid(),
        'user',
        p_user_id::text,
        'tenant.invitation.accepted',
        'tenant_invitation',
        invitation_record.id::text,
        invitation_record.organization_id,
        p_trace_id,
        p_trace_id,
        'Accepted tenant owner invitation',
        jsonb_build_object(
            'invitationId', invitation_record.id,
            'organizationId', invitation_record.organization_id,
            'role', invitation_record.role,
            'userId', p_user_id
        ),
        p_accepted_at
    );

    return query select
        'succeeded'::text,
        invitation_record.id,
        invitation_record.organization_id,
        invitation_record.organization_name,
        invitation_record.email,
        invitation_record.role,
        p_user_id,
        p_display_name,
        p_security_stamp,
        invitation_record.expires_at;
end;
$$;

revoke all on table tenant_invitations from public;
revoke all on function create_tenant_owner_invitation(uuid, uuid, text, text, text, text, text, uuid, timestamptz, timestamptz, text) from public;
revoke all on function list_tenant_owner_invitations(integer) from public;
revoke all on function inspect_tenant_owner_invitation(text, timestamptz) from public;
revoke all on function revoke_tenant_owner_invitation(uuid, uuid, timestamptz, text) from public;
revoke all on function accept_tenant_owner_invitation(text, uuid, uuid, text, text, uuid, timestamptz, text) from public;

grant execute on function create_tenant_owner_invitation(uuid, uuid, text, text, text, text, text, uuid, timestamptz, timestamptz, text) to {{APP_ROLE}};
grant execute on function list_tenant_owner_invitations(integer) to {{APP_ROLE}};
grant execute on function inspect_tenant_owner_invitation(text, timestamptz) to {{APP_ROLE}};
grant execute on function revoke_tenant_owner_invitation(uuid, uuid, timestamptz, text) to {{APP_ROLE}};
grant execute on function accept_tenant_owner_invitation(text, uuid, uuid, text, text, uuid, timestamptz, text) to {{APP_ROLE}};
