import type { Metadata } from "next";
import { fetchModelsDirectory } from "@/features/models-api";
import { formatContextWindow, formatMtokPrice } from "@/features/format";
import { resolvePublicBaseUrl } from "@/features/site";
import JsonLd from "@/ui/JsonLd";

export const revalidate = 300;

export async function generateMetadata(): Promise<Metadata> {
  return {
    title: "Model directory — TokenBurn",
    description:
      "Per-model pricing and usage on TokenBurn: context windows and per-million-token input, cache read, cache write, and output prices.",
  };
}

export default async function ModelsPage() {
  const { models } = await fetchModelsDirectory();
  const base = resolvePublicBaseUrl();

  const itemList = {
    "@context": "https://schema.org",
    "@type": "ItemList",
    name: "TokenBurn model directory",
    itemListElement: models.map((model, index) => ({
      "@type": "ListItem",
      position: index + 1,
      name: model.slug,
      url: new URL(`/models/${model.slug}`, base).toString(),
    })),
  };

  return (
    <main>
      <h1>Model directory</h1>
      <p>
        Per-model pricing and usage for the models TokenBurn tracks. Prices are
        quoted per million tokens (MTok); cache reads and writes reflect the
        provider&apos;s caching discounts.
      </p>
      {models.length === 0 ? (
        <p>No models available yet</p>
      ) : (
        <ul>
          {models.map((model) => (
            <li key={model.slug}>
              <article>
                <h2>
                  <a href={`/models/${model.slug}`}>{model.slug}</a>
                </h2>
                <p>Provider: {model.provider}</p>
                <p>
                  Context window: {formatContextWindow(model.contextWindow)}
                </p>
                <ul>
                  <li>Input: {formatMtokPrice(model.inputPerMtok, "input")}</li>
                  <li>
                    Cache read:{" "}
                    {formatMtokPrice(model.cacheReadPerMtok, "cache read")}
                  </li>
                  <li>
                    Cache write:{" "}
                    {formatMtokPrice(model.cacheWritePerMtok, "cache write")}
                  </li>
                  <li>
                    Output: {formatMtokPrice(model.outputPerMtok, "output")}
                  </li>
                </ul>
              </article>
            </li>
          ))}
        </ul>
      )}
      <JsonLd data={itemList} />
    </main>
  );
}
