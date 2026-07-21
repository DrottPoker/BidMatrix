create table user_credentials (
    user_id uuid primary key references users(id),
    password_hash text not null,
    failed_access_count integer not null default 0 check (failed_access_count >= 0),
    lockout_end timestamptz null,
    security_stamp uuid not null,
    password_changed_at timestamptz not null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    version integer not null check (version > 0)
);

create function get_login_identity(p_normalized_email text)
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
$$;

revoke all on function get_login_identity(text) from public;

grant select, insert, update, delete on user_credentials to {{APP_ROLE}};
grant execute on function get_login_identity(text) to {{APP_ROLE}};
