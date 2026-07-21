import type { Metadata } from "next";
import { AnalysisWorkspace } from "@/components/analysis-workspace";

export const metadata: Metadata = { title: "Owner analyses | BidMatrix" };

export default function OwnerAnalysesPage() {
  return <AnalysisWorkspace heading="Owner analysis queue" listEndpoint="/owner/v1/analyses" />;
}
