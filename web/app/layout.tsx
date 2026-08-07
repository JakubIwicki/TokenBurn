import type { Metadata } from "next";
import JsonLd from "@/ui/JsonLd";

const softwareApplication = {
  "@context": "https://schema.org",
  "@type": "SoftwareApplication",
  name: "TokenBurn",
  description:
    "TokenBurn is a telemetry pipeline that tracks LLM token usage and spend across models and providers.",
  applicationCategory: "DeveloperApplication",
  operatingSystem: "Any",
};

export const metadata: Metadata = {
  metadataBase: new URL(
    process.env.NEXT_PUBLIC_API_BASE_URL ?? "https://localhost",
  ),
  title: "TokenBurn — Public model pricing & usage directory",
  description:
    "Browse LLM model pricing and usage on TokenBurn: context windows, per-million-token input, cache read, cache write, and output prices.",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>
        <JsonLd data={softwareApplication} />
        {children}
      </body>
    </html>
  );
}
