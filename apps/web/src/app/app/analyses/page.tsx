import type { Metadata } from "next";
import { AnalysisList } from "@/components/analysis-list";

export const metadata: Metadata = { title: "Analyses" };

export default function CustomerAnalysesPage() {
  return <AnalysisList />;
}
