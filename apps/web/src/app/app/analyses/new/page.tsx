import type { Metadata } from "next";
import { AnalysisCreate } from "@/components/analysis-create";

export const metadata: Metadata = { title: "New analysis" };

export default function NewCustomerAnalysisPage() {
  return <AnalysisCreate />;
}
