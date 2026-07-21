import type { Metadata } from "next";
import { AnalysisWorkspace } from "@/components/analysis-workspace";

export const metadata: Metadata = { title: "Analyses | BidMatrix" };

export default function AnalysesPage() {
  return <AnalysisWorkspace />;
}
