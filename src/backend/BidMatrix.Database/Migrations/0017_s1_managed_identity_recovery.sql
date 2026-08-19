set local search_path = better_auth, public;

create table "rateLimit" (
    "id" text not null primary key,
    "key" text not null unique,
    "count" integer not null,
    "lastRequest" bigint not null
);

revoke all on table better_auth."rateLimit" from public;
grant select, insert, update, delete on table better_auth."rateLimit" to {{AUTH_ROLE}};

create function public.revoke_bidmatrix_sessions_after_managed_password_update()
returns trigger
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    identity_record record;
    revoked_session_count integer;
    reset_at timestamptz := statement_timestamp();
begin
    if new."providerId" <> 'credential' or
       old."password" is not distinct from new."password" then
        return new;
    end if;

    for identity_record in
        select identity.id, identity.user_id
        from user_federated_identities identity
        where identity.status = 'active'
          and identity.subject_hash = encode(
              digest(identity.issuer || chr(10) || new."userId", 'sha256'),
              'hex'
          )
        order by identity.id
        for update
    loop
        update user_credentials credential
        set failed_access_count = 0,
            lockout_end = null,
            security_stamp = gen_random_uuid(),
            password_changed_at = reset_at,
            updated_at = reset_at,
            version = credential.version + 1
        where credential.user_id = identity_record.user_id;

        if not found then
            raise exception using
                errcode = '23503',
                message = 'Managed identity is missing its BidMatrix credential boundary.';
        end if;

        update user_sessions session_record
        set revoked_at = reset_at,
            revoked_reason = 'managed_password_reset',
            version = session_record.version + 1
        where session_record.user_id = identity_record.user_id
          and session_record.revoked_at is null;

        get diagnostics revoked_session_count = row_count;

        update account_recovery_tokens recovery
        set status = 'revoked',
            revoked_at = reset_at,
            revoked_reason = 'managed_password_reset',
            updated_at = reset_at,
            version = recovery.version + 1
        where recovery.user_id = identity_record.user_id
          and recovery.status = 'pending';

        perform append_audit_event(
            gen_random_uuid(),
            'system',
            'better-auth',
            'identity.managed_password.reset',
            'user',
            identity_record.user_id::text,
            null,
            null,
            null,
            'Reset managed password and revoked application sessions',
            jsonb_build_object(
                'identityId', identity_record.id,
                'userId', identity_record.user_id,
                'revokedSessionCount', revoked_session_count
            ),
            reset_at
        );
    end loop;

    return new;
end;
$$;

revoke all on function public.revoke_bidmatrix_sessions_after_managed_password_update()
    from public;

create trigger managed_password_update_revokes_bidmatrix_sessions
after update of "password" on better_auth."account"
for each row
execute function public.revoke_bidmatrix_sessions_after_managed_password_update();
