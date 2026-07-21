import type { Metadata } from "next";
import { AnalysisDetail } from "@/components/analysis-detail";

export const metadata: Metadata = { title: "Analysis | BidMatrix" };

export default async function AnalysisPage({
  params,
}: {
  params: Promise<{ analysisId: string }>;
}) {
  const { analysisId } = await params;
  return (
    <main className="min-h-screen bg-background px-5 py-10 sm:px-8">
      <div className="mx-auto max-w-4xl"><AnalysisDetail analysisId={analysisId} /></div>
    </main>
  );
}
