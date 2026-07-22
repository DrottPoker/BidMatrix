import type { Metadata } from "next";
import { AnalysisList } from "@/components/analysis-list";

export const metadata: Metadata = { title: "Analysis review" };

export default function OwnerAnalysesPage() {
  return <AnalysisList detailBasePath="/owner/analyses" endpoint="/owner/v1/analyses" ownerMode showCreate={false} />;
}
