import type { MetadataRoute } from "next";
import { fetchModelsDirectory } from "@/features/models-api";
import { guides } from "@/content/guides";
import { resolvePublicBaseUrl } from "@/features/site";

export const dynamic = "force-dynamic";

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const base = resolvePublicBaseUrl();
  const { models } = await fetchModelsDirectory();

  return [
    { url: new URL("/", base).toString() },
    { url: new URL("/models", base).toString() },
    ...models.map((model) => ({
      url: new URL(`/models/${model.slug}`, base).toString(),
    })),
    ...guides.map((guide) => ({
      url: new URL(`/guides/${guide.slug}`, base).toString(),
    })),
  ];
}
