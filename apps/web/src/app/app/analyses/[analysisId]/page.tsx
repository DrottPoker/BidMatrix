import type { Metadata } from "next";
import { AnalysisDetail } from "@/components/analysis-detail";

export const metadata: Metadata = { title: "Analysis" };

export default async function CustomerAnalysisPage({ params }: PageProps<"/app/analyses/[analysisId]">) {
  const { analysisId } = await params;
  return <AnalysisDetail analysisId={analysisId} />;
}
