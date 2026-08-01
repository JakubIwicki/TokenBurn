import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "TokenBurn",
  description: "TokenBurn web frontend",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
