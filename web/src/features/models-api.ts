// Server-only module: reads process.env at request time and issues server-side
// fetches with ISR revalidation. Never import this into a client component.

export interface ModelInfo {
  slug: string;
  provider: string;
  contextWindow: number | null;
  inputPerMtok: number;
  cacheReadPerMtok: number;
  cacheWritePerMtok: number;
  outputPerMtok: number;
}

export interface ModelsDirectory {
  models: ModelInfo[];
}

export interface ModelUsageStats {
  modelSlug: string;
  service: string;
  runCount: number;
  pricedRunCount: number;
  messageCount: number;
  inputTokens: number;
  cacheReadTokens: number;
  cacheWriteTokens: number;
  outputTokens: number;
  costUsd: number;
}

export interface ModelsStats {
  stats: ModelUsageStats[];
}

const FETCH_REVALIDATE_SECONDS = 300;

// Internal base URL wins (direct compose-network address, no TLS/nginx), then
// the public base, then a localhost fallback for local dev.
function resolveApiBaseUrl(): string {
  return (
    process.env.API_INTERNAL_URL ??
    process.env.NEXT_PUBLIC_API_BASE_URL ??
    "http://localhost:8080"
  );
}

// Error-tolerant by design: a failed or non-2xx response yields null so the
// caller renders the empty state. That lets `next build` pass with the API
// unreachable — the build-time prerender renders an empty state and ISR
// revalidation fills in real data at runtime.
async function serverFetch<T>(path: string): Promise<T | null> {
  const base = resolveApiBaseUrl();
  try {
    const res = await fetch(`${base}${path}`, {
      next: { revalidate: FETCH_REVALIDATE_SECONDS },
      headers: { Accept: "application/json" },
    });
    if (!res.ok) return null;
    return (await res.json()) as T;
  } catch {
    return null;
  }
}

export async function fetchModelsDirectory(): Promise<ModelsDirectory> {
  const payload = await serverFetch<ModelsDirectory>("/api/models");
  if (payload && Array.isArray(payload.models)) return payload;
  return { models: [] };
}

export async function fetchModelsStats(): Promise<ModelsStats> {
  const payload = await serverFetch<ModelsStats>("/api/models/stats");
  if (payload && Array.isArray(payload.stats)) return payload;
  return { stats: [] };
}
