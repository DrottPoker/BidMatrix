"use client";

import { useMemo, useState } from "react";
import { ChevronDown, Download, FileSpreadsheet, Quote, Search } from "lucide-react";
import type { AnalysisCitation, AnalysisRequirement } from "@/lib/bidmatrix-api";
import {
  buildComplianceMatrixRows,
  createComplianceMatrixFileName,
  serializeComplianceMatrixCsv,
} from "@/lib/compliance-matrix-export";
import { humanize } from "@/components/ui/status-pill";

type RequirementScope = "all" | "mandatory" | "optional";

export function ComplianceMatrix({
  analysisTitle,
  requirements,
}: {
  analysisTitle: string | null;
  requirements: AnalysisRequirement[];
}) {
  const [query, setQuery] = useState("");
  const [scope, setScope] = useState<RequirementScope>("all");
  const [category, setCategory] = useState("all");

  const categories = useMemo(
    () => [...new Set(requirements.map((requirement) => requirement.category))].sort(),
    [requirements],
  );
  const numberedRequirements = useMemo(
    () => requirements.map((requirement, index) => ({ requirement, number: index + 1 })),
    [requirements],
  );
  const visibleRequirements = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase();
    return numberedRequirements.filter(({ requirement }) => {
      const matchesQuery =
        normalizedQuery.length === 0 ||
        `${requirement.requirementCode ?? ""} ${requirement.requirementText} ${requirement.category}`
          .toLowerCase()
          .includes(normalizedQuery);
      const matchesScope =
        scope === "all" ||
        (scope === "mandatory" && requirement.mandatory) ||
        (scope === "optional" && !requirement.mandatory);
      const matchesCategory = category === "all" || requirement.category === category;
      return matchesQuery && matchesScope && matchesCategory;
    });
  }, [category, numberedRequirements, query, scope]);

  function downloadCsv() {
    const rows = buildComplianceMatrixRows(requirements);
    const blob = new Blob([serializeComplianceMatrixCsv(rows)], {
      type: "text/csv;charset=utf-8",
    });
    const objectUrl = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = objectUrl;
    link.download = createComplianceMatrixFileName(analysisTitle);
    document.body.append(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(objectUrl);
  }

  return (
    <section className="panel overflow-hidden">
      <div className="border-b border-ink/8 p-5 sm:p-6">
        <div className="flex flex-col justify-between gap-4 lg:flex-row lg:items-start">
          <div>
            <p className="eyebrow">Compliance matrix</p>
            <h2 className="mt-1.5 text-xl font-semibold">
              {requirements.length} reviewed requirements
            </h2>
            <p className="mt-2 max-w-2xl text-xs leading-5 text-muted">
              Filter the reviewed requirements here or continue working in Excel with the CSV export.
            </p>
          </div>
          <button
            className="button-secondary h-10 shrink-0"
            disabled={requirements.length === 0}
            onClick={downloadCsv}
            type="button"
          >
            <Download size={16} /> Download CSV
          </button>
        </div>

        <div className="mt-5 grid gap-3 md:grid-cols-[minmax(0,1fr)_11rem_14rem]">
          <label className="relative block">
            <Search className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted" size={16} />
            <span className="sr-only">Search compliance matrix</span>
            <input
              className="field h-10 pl-10"
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Search requirements"
              type="search"
              value={query}
            />
          </label>
          <label>
            <span className="sr-only">Filter by obligation</span>
            <select
              className="field h-10"
              onChange={(event) => setScope(event.target.value as RequirementScope)}
              value={scope}
            >
              <option value="all">All obligations</option>
              <option value="mandatory">Mandatory only</option>
              <option value="optional">Optional only</option>
            </select>
          </label>
          <label>
            <span className="sr-only">Filter by category</span>
            <select
              className="field h-10"
              onChange={(event) => setCategory(event.target.value)}
              value={category}
            >
              <option value="all">All categories</option>
              {categories.map((value) => (
                <option key={value} value={value}>{humanize(value)}</option>
              ))}
            </select>
          </label>
        </div>
        <p className="mt-3 text-xs text-muted" role="status">
          Showing {visibleRequirements.length} of {requirements.length} requirements
        </p>
      </div>

      {requirements.length === 0 ? (
        <div className="px-6 py-14 text-center">
          <FileSpreadsheet className="mx-auto text-muted" size={25} />
          <h3 className="mt-4 font-semibold">No reviewed requirements</h3>
          <p className="mt-2 text-sm text-muted">The supplied documents did not produce any publishable requirements.</p>
        </div>
      ) : visibleRequirements.length === 0 ? (
        <div className="px-6 py-14 text-center">
          <Search className="mx-auto text-muted" size={24} />
          <p className="mt-3 text-sm text-muted">No requirements match the selected filters.</p>
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-[70rem] w-full border-collapse text-left text-sm">
            <thead className="bg-surface-muted/70 text-xs text-muted">
              <tr>
                <th className="w-16 px-5 py-3 font-semibold" scope="col">No.</th>
                <th className="w-40 px-4 py-3 font-semibold" scope="col">Obligation</th>
                <th className="w-48 px-4 py-3 font-semibold" scope="col">Category</th>
                <th className="min-w-96 px-4 py-3 font-semibold" scope="col">Requirement</th>
                <th className="min-w-72 px-4 py-3 pr-5 font-semibold" scope="col">Source</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink/7">
              {visibleRequirements.map(({ requirement, number }) => (
                <tr className="align-top" key={requirement.id}>
                  <td className="px-5 py-5 font-mono text-xs text-muted">{String(number).padStart(2, "0")}</td>
                  <td className="px-4 py-5">
                    <span className={`inline-flex rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-wide ${requirement.mandatory ? "bg-red-50 text-red-800" : "bg-stone-100 text-stone-700"}`}>
                      {requirement.mandatory ? "Mandatory" : "Optional"}
                    </span>
                    {requirement.requirementCode ? (
                      <span className="mt-2 block font-mono text-xs text-muted">{requirement.requirementCode}</span>
                    ) : null}
                  </td>
                  <td className="px-4 py-5 text-xs font-semibold text-muted">{humanize(requirement.category)}</td>
                  <td className="px-4 py-5">
                    <p className="max-w-3xl leading-6">{requirement.requirementText}</p>
                    {requirement.correctionNote ? (
                      <p className="mt-3 rounded-xl bg-amber-50 px-3 py-2 text-xs text-amber-900">
                        Review note: {requirement.correctionNote}
                      </p>
                    ) : null}
                  </td>
                  <td className="px-4 py-5 pr-5">
                    <details className="group">
                      <summary className="inline-flex list-none items-center gap-2 text-xs font-semibold text-brand">
                        <Quote size={14} /> {sourceLabel(requirement.citations)}
                        <ChevronDown className="transition group-open:rotate-180" size={14} />
                      </summary>
                      <div className="mt-3 space-y-2">
                        {requirement.citations.map((citation) => (
                          <Citation citation={citation} key={citation.id} />
                        ))}
                      </div>
                    </details>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function sourceLabel(citations: AnalysisCitation[]) {
  if (citations.length === 0) return "No source citation";
  const first = citations[0];
  return citations.length === 1
    ? `${first.originalFileName}, page ${first.pageNumber}`
    : `${citations.length} source citations`;
}

function Citation({ citation }: { citation: AnalysisCitation }) {
  return (
    <blockquote className="rounded-xl border-l-2 border-brand bg-surface-muted/75 px-4 py-3 text-xs">
      <p className="font-semibold text-brand">
        {citation.originalFileName} · Page {citation.pageNumber}
        {citation.sectionText ? ` · ${citation.sectionText}` : ""}
      </p>
      <p className="mt-2 leading-5 text-muted">“{citation.quoteText}”</p>
    </blockquote>
  );
}
