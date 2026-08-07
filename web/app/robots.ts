import type { MetadataRoute } from "next";
import { resolvePublicBaseUrl } from "@/features/site";

export default function robots(): MetadataRoute.Robots {
  const base = resolvePublicBaseUrl();
  return {
    rules: { allow: "/" },
    sitemap: new URL("/sitemap.xml", base).toString(),
  };
}
