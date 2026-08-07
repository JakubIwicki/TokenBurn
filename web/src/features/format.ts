const usdFormatter = new Intl.NumberFormat("en-US", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const integerFormatter = new Intl.NumberFormat("en-US");

export function formatUsd(value: number): string {
  return `$${usdFormatter.format(value)}`;
}

export function formatNumber(value: number): string {
  return integerFormatter.format(value);
}

export function formatMtokPrice(value: number, label: string): string {
  return `${formatUsd(value)} / MTok ${label}`;
}

// Renders a token count compactly, e.g. 128000 -> "128K", 1000000 -> "1M".
export function formatContextWindow(value: number | null): string {
  if (value === null) return "—";
  if (value >= 1_000_000) return `${compactNumber(value / 1_000_000)}M`;
  if (value >= 1_000) return `${compactNumber(value / 1_000)}K`;
  return String(value);
}

function compactNumber(value: number): string {
  if (Number.isInteger(value)) return String(value);
  return value.toFixed(1).replace(/\.0$/, "");
}
