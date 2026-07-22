alter table analyses
    add column reviewed_by_user_id uuid null references users(id),
    add column reviewed_at timestamptz null,
    add column published_at timestamptz null,
    add column review_note text null,
    add column correction_count integer not null default 0 check (correction_count >= 0);

alter table analysis_requirements
    add column original_requirement_text text null,
    add column reviewed_by_user_id uuid null references users(id),
    add column reviewed_at timestamptz null,
    add column correction_note text null,
    add column version integer not null default 1 check (version > 0);

update analysis_requirements
set original_requirement_text = requirement_text
where original_requirement_text is null;

alter table analysis_requirements
    alter column original_requirement_text set not null;

create table analysis_findings (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    analysis_id uuid not null,
    finding_type text not null check (finding_type in (
        'key_date',
        'requested_document',
        'evaluation_criterion'
    )),
    title text not null,
    detail text not null,
    original_detail text not null,
    normalized_value text not null,
    date_value timestamptz null,
    weight_percent numeric(5,2) null check (weight_percent is null or weight_percent between 0 and 100),
    confidence numeric(5,4) not null check (confidence between 0 and 1),
    review_status text not null check (review_status in ('pending', 'accepted', 'corrected', 'rejected')),
    analysis_file_id uuid not null,
    page_number integer not null check (page_number > 0),
    section_text text null,
    quote_text text not null,
    reviewed_by_user_id uuid null references users(id),
    reviewed_at timestamptz null,
    correction_note text null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    version integer not null check (version > 0),
    unique (id, organization_id),
    unique (id, analysis_id, organization_id),
    unique (analysis_id, finding_type, normalized_value, analysis_file_id, page_number),
    foreign key (analysis_id, organization_id) references analyses(id, organization_id),
    foreign key (analysis_file_id, analysis_id, organization_id)
        references analysis_files(id, analysis_id, organization_id)
);

create index analysis_findings_organization_id_analysis_id_idx
    on analysis_findings (organization_id, analysis_id, finding_type, review_status);

alter table analysis_findings enable row level security;
alter table analysis_findings force row level security;

create policy tenant_analysis_findings on analysis_findings
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());

grant select, insert, update, delete on analyses to {{APP_ROLE}};
grant select, insert, update, delete on analysis_requirements to {{APP_ROLE}};
grant select, insert, update, delete on analysis_findings to {{APP_ROLE}};
