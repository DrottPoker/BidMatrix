do $$
begin
    if exists (select 1 from public.tenant_invitations) then
        raise exception 'Cannot remove invitation support while tenant invitation records exist';
    end if;
end
$$;

drop function public.accept_managed_tenant_owner_invitation(uuid, uuid, uuid, uuid, text, text, text, text, text, uuid, timestamptz, text);
drop function public.accept_tenant_owner_invitation(text, uuid, uuid, text, text, uuid, timestamptz, text);
drop function public.revoke_tenant_owner_invitation(uuid, uuid, timestamptz, text);
drop function public.inspect_tenant_owner_invitation(text, timestamptz);
drop function public.list_tenant_owner_invitations(integer);
drop function public.create_tenant_owner_invitation(uuid, uuid, text, text, text, text, text, uuid, timestamptz, timestamptz, text);
drop table public.tenant_invitations;

create function public.register_federated_account(
    p_user_id uuid,
    p_organization_id uuid,
    p_membership_id uuid,
    p_identity_id uuid,
    p_provider_name text,
    p_issuer text,
    p_subject_hash text,
    p_email text,
    p_display_name text,
    p_organization_name text,
    p_organization_slug text,
    p_disabled_password_hash text,
    p_security_stamp uuid,
    p_registered_at timestamptz,
    p_trace_id text
)
returns text
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    v_normalized_email text := upper(trim(p_email));
begin
    if p_user_id is null or
       p_organization_id is null or
       p_membership_id is null or
       p_identity_id is null or
       p_provider_name is null or length(p_provider_name) not between 2 and 80 or
       p_issuer is null or length(p_issuer) not between 8 and 2048 or
       p_subject_hash is null or p_subject_hash !~ '^[0-9a-f]{64}$' or
       p_email is null or length(trim(p_email)) not between 3 and 320 or
       p_display_name is null or length(p_display_name) not between 2 and 120 or
       p_organization_name is null or length(p_organization_name) not between 2 and 120 or
       p_organization_slug is null or
       length(p_organization_slug) not between 3 and 63 or
       p_organization_slug !~ '^[a-z0-9]+(?:-[a-z0-9]+)*$' or
       p_disabled_password_hash is null or length(p_disabled_password_hash) < 20 or
       p_security_stamp is null or
       p_registered_at is null then
        return 'invalid';
    end if;

    perform pg_advisory_xact_lock(
        hashtextextended('self-service-email:' || v_normalized_email, 49089055130016)
    );
    perform pg_advisory_xact_lock(
        hashtextextended('federated-identity:' || p_issuer || ':' || p_subject_hash, 49089055130016)
    );
    perform pg_advisory_xact_lock(
        hashtextextended('self-service-workspace:' || p_organization_slug, 49089055130016)
    );

    if exists (
        select 1
        from public.user_federated_identities identity
        where identity.issuer = p_issuer
          and identity.subject_hash = p_subject_hash
    ) then
        return 'already_registered';
    end if;

    if exists (
        select 1
        from public.users user_record
        where user_record.normalized_email = v_normalized_email
    ) then
        return 'email_registered';
    end if;

    perform set_config('app.organization_id', p_organization_id::text, true);

    begin
        insert into public.organizations (id, name, slug, status, created_at, updated_at)
        values (
            p_organization_id,
            p_organization_name,
            p_organization_slug,
            'active',
            p_registered_at,
            p_registered_at
        );
    exception
        when unique_violation then
            return 'slug_conflict';
    end;

    insert into public.users (
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
        trim(p_email),
        v_normalized_email,
        p_display_name,
        'active',
        p_registered_at,
        p_registered_at,
        p_registered_at
    );

    insert into public.user_credentials (
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
        p_registered_at,
        p_registered_at,
        p_registered_at,
        1
    );

    insert into public.organization_memberships (
        id,
        organization_id,
        user_id,
        role,
        created_at
    )
    values (
        p_membership_id,
        p_organization_id,
        p_user_id,
        'owner',
        p_registered_at
    );

    insert into public.user_federated_identities (
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
        trim(p_email),
        'active',
        p_registered_at,
        p_registered_at,
        null,
        p_registered_at,
        1
    );

    perform public.append_audit_event(
        gen_random_uuid(),
        'user',
        p_user_id::text,
        'account.self_service.registered',
        'user',
        p_user_id::text,
        p_organization_id,
        p_trace_id,
        p_trace_id,
        'Registered account and private workspace',
        jsonb_build_object(
            'userId', p_user_id,
            'organizationId', p_organization_id,
            'identityId', p_identity_id,
            'providerName', p_provider_name,
            'role', 'owner'
        ),
        p_registered_at
    );

    return 'registered';
end;
$$;

revoke all on function public.register_federated_account(uuid, uuid, uuid, uuid, text, text, text, text, text, text, text, text, uuid, timestamptz, text) from public;
grant execute on function public.register_federated_account(uuid, uuid, uuid, uuid, text, text, text, text, text, text, text, text, uuid, timestamptz, text) to {{APP_ROLE}};
