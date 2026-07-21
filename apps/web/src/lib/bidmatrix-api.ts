export const apiBaseUrl =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

export type AnalysisFile = {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  sha256: string;
  scanStatus: string;
  retentionUntil: string | null;
  createdAt: string;
};

export type Analysis = {
  id: string;
  title: string | null;
  status: string;
  sourceLanguage: string;
  workflowId: string | null;
  requiresHumanReview: boolean;
  failureCode: string | null;
  failureMessage: string | null;
  createdAt: string;
  updatedAt: string;
  version: number;
  files: AnalysisFile[];
};

export type AnalysisDocumentExtraction = {
  analysisFileId: string;
  originalFileName: string;
  extractionStatus: string;
  documentType: string | null;
  pageCount: number | null;
  extractionMethod: string | null;
  failureCode: string | null;
};

export type AnalysisCitation = {
  id: string;
  analysisFileId: string;
  originalFileName: string;
  pageNumber: number;
  sectionText: string | null;
  quoteText: string;
};

export type AnalysisRequirement = {
  id: string;
  requirementCode: string | null;
  requirementText: string;
  normalizedRequirement: string;
  category: string;
  mandatory: boolean;
  requestedEvidence: string | null;
  confidence: number;
  reviewStatus: string;
  citations: AnalysisCitation[];
};

export type AnalysisExtractionMetrics = {
  documentCount: number;
  pageCount: number;
  requirementCount: number;
  mandatoryRequirementCount: number;
  citedRequirementCount: number;
  filesRequiringOcr: number;
  failedFileCount: number;
};

export type AnalysisRequirements = {
  analysisId: string;
  capabilityStatus: string;
  extractionStatus: string;
  extractionVersion: string | null;
  completedAt: string | null;
  documents: AnalysisDocumentExtraction[];
  requirements: AnalysisRequirement[];
  metrics: AnalysisExtractionMetrics;
  message: string;
};

type CsrfToken = {
  token: string;
  headerName: string;
};

type ProblemDetails = {
  title?: string;
  detail?: string;
};

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message);
  }
}

export async function apiGet<T>(path: string): Promise<T> {
  return apiRequest<T>(path, { method: "GET" });
}

export async function apiMutation<T>(
  path: string,
  init: RequestInit,
): Promise<T> {
  const csrf = await apiRequest<CsrfToken>("/v1/auth/csrf", { method: "GET" });
  const headers = new Headers(init.headers);
  headers.set(csrf.headerName, csrf.token);
  return apiRequest<T>(path, { ...init, headers });
}

async function apiRequest<T>(path: string, init: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    credentials: "include",
    cache: "no-store",
  });

  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(
      problem.detail ?? problem.title ?? `Request failed with HTTP ${response.status}.`,
      response.status,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return {};
  }
}
