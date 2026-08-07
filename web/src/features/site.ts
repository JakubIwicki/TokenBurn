// Public site origin used for absolute URLs (sitemap, robots, metadata, JSON-LD).
export function resolvePublicBaseUrl(): string {
  return process.env.NEXT_PUBLIC_API_BASE_URL ?? "https://localhost";
}
