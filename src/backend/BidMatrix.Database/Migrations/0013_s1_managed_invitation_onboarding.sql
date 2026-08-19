alter table user_credentials
    add column password_enabled boolean not null default true;

create function enable_password_when_hash_changes()
returns trigger
language plpgsql
set search_path = public, pg_temp
as $$
begin
    if new.password_hash is distinct from old.password_hash then
        new.password_enabled := true;
    end if;

    return new;
end;
$$;

create trigger user_credentials_enable_password_on_hash_change
before update of password_hash on user_credentials
for each row
execute function enable_password_when_hash_changes();

create or replace function get_login_identity(p_normalized_email text)
returns table (
    user_id uuid,
    email text,
    display_name text,
    status text,
    password_hash text,
    failed_access_count integer,
    lockout_end timestamptz,
    security_stamp uuid,
    memberships jsonb,
    platform_roles jsonb
)
language sql
stable
security definer
set search_path = public, pg_temp
as $$
    select
        user_record.id,
        user_record.email,
        user_record.display_name,
        user_record.status,
        credential.password_hash,
        credential.failed_access_count,
        credential.lockout_end,
        credential.security_stamp,
        coalesce(
            (
                select jsonb_agg(
                    jsonb_build_object(
                        'organizationId', membership.organization_id,
                        'role', membership.role
                    )
                    order by membership.created_at
                )
                from organization_memberships membership
                where membership.user_id = user_record.id
            ),
            '[]'::jsonb
        ),
        coalesce(
            (
                select jsonb_agg(platform_role.role order by platform_role.role)
                from user_platform_roles platform_role
                where platform_role.user_id = user_record.id
            ),
            '[]'::jsonb
        )
    from users user_record
    join user_credentials credential on credential.user_id = user_record.id
    where user_record.normalized_email = p_normalized_email
      and credential.password_enabled
$$;

create function user_has_native_password(p_user_id uuid)
returns boolean
language sql
stable
security definer
set search_path = public, pg_temp
as $$
    select coalesce(
        (
            select credential.password_enabled
            from user_credentials credential
            where credential.user_id = p_user_id
        ),
        false
    )
$$;

create function accept_managed_tenant_owner_invitation(
    p_invitation_id uuid,
    p_user_id uuid,
    p_membership_id uuid,
    p_identity_id uuid,
    p_provider_name text,
    p_issuer text,
    p_subject_hash text,
    p_email text,
    p_disabled_password_hash text,
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
    federated_identity_id uuid,
    invitation_expires_at timestamptz
)
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    invitation_record tenant_invitations%rowtype;
begin
    if p_provider_name is null or length(p_provider_name) not between 2 and 80 or
       p_issuer is null or length(p_issuer) not between 8 and 2048 or
       p_subject_hash is null or p_subject_hash !~ '^[0-9a-f]{64}$' or
       p_email is null or length(p_email) not between 3 and 320 or
       p_disabled_password_hash is null or length(p_disabled_password_hash) < 20 then
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
            null::uuid,
            null::timestamptz;
        return;
    end if;

    select invitation.*
    into invitation_record
    from tenant_invitations invitation
    where invitation.id = p_invitation_id
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
            null::uuid,
            invitation_record.expires_at;
        return;
    end if;

    if invitation_record.normalized_email <> upper(p_email) then
        return query select
            'email_mismatch'::text,
            invitation_record.id,
            invitation_record.organization_id,
            invitation_record.organization_name,
            invitation_record.email,
            invitation_record.role,
            null::uuid,
            null::text,
            null::uuid,
            null::uuid,
            invitation_record.expires_at;
        return;
    end if;

    perform pg_advisory_xact_lock(
        hashtextextended('tenant-email:' || invitation_record.normalized_email, 49089055130013)
    );
    perform pg_advisory_xact_lock(
        hashtextextended('federated-identity:' || p_issuer || ':' || p_subject_hash, 49089055130013)
    );

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
            null::uuid,
            invitation_record.expires_at;
        return;
    end if;

    if exists (
        select 1
        from user_federated_identities identity
        where identity.issuer = p_issuer
          and identity.subject_hash = p_subject_hash
    ) then
        return query select
            'identity_conflict'::text,
            invitation_record.id,
            invitation_record.organization_id,
            invitation_record.organization_name,
            invitation_record.email,
            invitation_record.role,
            null::uuid,
            null::text,
            null::uuid,
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
        invitation_record.email,
        'active',
        p_accepted_at,
        p_accepted_at,
        p_accepted_at
    );

    insert into user_credentials (
        user_id,
        password_hash,
        password_enabled,
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
        p_disabled_password_hash,
        false,
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
        p_accepted_at,
        p_accepted_at,
        null,
        p_accepted_at,
        1
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
        'Accepted tenant owner invitation through managed identity',
        jsonb_build_object(
            'invitationId', invitation_record.id,
            'organizationId', invitation_record.organization_id,
            'role', invitation_record.role,
            'userId', p_user_id,
            'authenticationMethod', 'oidc'
        ),
        p_accepted_at
    );

    perform append_audit_event(
        gen_random_uuid(),
        'user',
        p_user_id::text,
        'identity.federated.onboarded',
        'federated_identity',
        p_identity_id::text,
        invitation_record.organization_id,
        p_trace_id,
        p_trace_id,
        'Created managed identity during tenant onboarding',
        jsonb_build_object(
            'identityId', p_identity_id,
            'userId', p_user_id,
            'providerName', p_provider_name,
            'invitationId', invitation_record.id
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
        invitation_record.email,
        p_security_stamp,
        p_identity_id,
        invitation_record.expires_at;
end;
$$;

revoke all on function enable_password_when_hash_changes() from public;
revoke all on function user_has_native_password(uuid) from public;
revoke all on function accept_managed_tenant_owner_invitation(uuid, uuid, uuid, uuid, text, text, text, text, text, uuid, timestamptz, text) from public;

grant execute on function user_has_native_password(uuid) to {{APP_ROLE}};
grant execute on function accept_managed_tenant_owner_invitation(uuid, uuid, uuid, uuid, text, text, text, text, text, uuid, timestamptz, text) to {{APP_ROLE}};
