import { redirect } from "next/navigation";

export default async function AnalysisPage({ params }: PageProps<"/analyses/[analysisId]">) {
  const { analysisId } = await params;
  redirect(`/app/analyses/${analysisId}`);
}
