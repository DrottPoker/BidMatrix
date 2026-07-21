import { AnalysisDetail } from "@/components/analysis-detail";

export default async function CustomerAnalysisPage({ params }: { params: Promise<{ analysisId: string }> }) {
  const { analysisId } = await params;
  return <main className="mx-auto max-w-4xl px-5 py-10 sm:px-8"><AnalysisDetail analysisId={analysisId} backHref="/app/analyses" /></main>;
}
