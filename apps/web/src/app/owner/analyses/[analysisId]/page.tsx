import type { Metadata } from "next";
import { OwnerAnalysisReview } from "@/components/owner-analysis-review";

export const metadata: Metadata = { title: "Quality review" };

export default async function OwnerAnalysisReviewPage({ params }: PageProps<"/owner/analyses/[analysisId]">) {
  const { analysisId } = await params;
  return <OwnerAnalysisReview analysisId={analysisId} />;
}
