import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { fetchModelsDirectory, fetchModelsStats } from "@/features/models-api";
import {
  formatContextWindow,
  formatMtokPrice,
  formatNumber,
  formatUsd,
} from "@/features/format";

export const revalidate = 300;
export const dynamicParams = true;

interface ModelDetailPageProps {
  params: Promise<{ slug: string }>;
}

export async function generateMetadata({
  params,
}: ModelDetailPageProps): Promise<Metadata> {
  const { slug } = await params;
  const { models } = await fetchModelsDirectory();
  const model = models.find((m) => m.slug.toLowerCase() === slug.toLowerCase());
  if (!model) notFound();
  return {
    title: `${model.slug} — TokenBurn model pricing & usage`,
    description: `${model.slug} pricing and usage on TokenBurn. Provider: ${model.provider}. Context window: ${formatContextWindow(model.contextWindow)}.`,
  };
}

export default async function ModelDetailPage({
  params,
}: ModelDetailPageProps) {
  const { slug } = await params;
  const [directory, statsPayload] = await Promise.all([
    fetchModelsDirectory(),
    fetchModelsStats(),
  ]);
  const model = directory.models.find(
    (m) => m.slug.toLowerCase() === slug.toLowerCase(),
  );
  if (!model) notFound();

  const usageRows = statsPayload.stats.filter(
    (s) => s.modelSlug === model.slug,
  );

  return (
    <main>
      <h1>{model.slug}</h1>
      <p>Provider: {model.provider}</p>
      <p>Context window: {formatContextWindow(model.contextWindow)}</p>
      <ul>
        <li>Input: {formatMtokPrice(model.inputPerMtok, "input")}</li>
        <li>
          Cache read: {formatMtokPrice(model.cacheReadPerMtok, "cache read")}
        </li>
        <li>
          Cache write: {formatMtokPrice(model.cacheWritePerMtok, "cache write")}
        </li>
        <li>Output: {formatMtokPrice(model.outputPerMtok, "output")}</li>
      </ul>

      <h2>Usage stats</h2>
      {usageRows.length === 0 ? (
        <p>No usage recorded for this model yet.</p>
      ) : (
        <table>
          <thead>
            <tr>
              <th>Model</th>
              <th>Service</th>
              <th>Runs</th>
              <th>Priced runs</th>
              <th>Messages</th>
              <th>Input tokens</th>
              <th>Cache read tokens</th>
              <th>Cache write tokens</th>
              <th>Output tokens</th>
              <th>Cost</th>
            </tr>
          </thead>
          <tbody>
            {usageRows.map((row) => (
              <tr key={`${row.modelSlug}-${row.service}`}>
                <td>{row.modelSlug}</td>
                <td>{row.service}</td>
                <td>{formatNumber(row.runCount)}</td>
                <td>{formatNumber(row.pricedRunCount)}</td>
                <td>{formatNumber(row.messageCount)}</td>
                <td>{formatNumber(row.inputTokens)}</td>
                <td>{formatNumber(row.cacheReadTokens)}</td>
                <td>{formatNumber(row.cacheWriteTokens)}</td>
                <td>{formatNumber(row.outputTokens)}</td>
                <td>{formatUsd(row.costUsd)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </main>
  );
}
