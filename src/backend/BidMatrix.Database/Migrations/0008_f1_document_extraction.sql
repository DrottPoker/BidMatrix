alter table analyses
    add column extraction_status text not null default 'not_started'
        check (extraction_status in ('not_started', 'processing', 'succeeded', 'partial', 'failed')),
    add column extraction_version text null,
    add column extraction_completed_at timestamptz null;

alter table analysis_files
    add column extraction_status text not null default 'pending'
        check (extraction_status in ('pending', 'extracted', 'requires_ocr', 'failed')),
    add column extraction_method text null,
    add column extraction_failure_code text null,
    add column extracted_at timestamptz null;

create table analysis_pages (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    analysis_id uuid not null,
    analysis_file_id uuid not null,
    page_number integer not null check (page_number > 0),
    text_content text not null,
    text_sha256 text not null check (text_sha256 ~ '^[0-9a-f]{64}$'),
    extraction_method text not null,
    created_at timestamptz not null,
    unique (analysis_file_id, page_number),
    unique (id, organization_id),
    foreign key (analysis_id, organization_id) references analyses(id, organization_id),
    foreign key (analysis_file_id, analysis_id, organization_id)
        references analysis_files(id, analysis_id, organization_id)
);

create index analysis_pages_organization_id_analysis_id_idx
    on analysis_pages (organization_id, analysis_id, analysis_file_id, page_number);

alter table analysis_pages enable row level security;
alter table analysis_pages force row level security;

create policy tenant_analysis_pages on analysis_pages
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());

grant select, insert, update, delete on analyses to {{APP_ROLE}};
grant select, insert, update, delete on analysis_files to {{APP_ROLE}};
grant select, insert, update, delete on analysis_pages to {{APP_ROLE}};
grant select, insert, update, delete on analysis_requirements to {{APP_ROLE}};
grant select, insert, update, delete on analysis_citations to {{APP_ROLE}};
