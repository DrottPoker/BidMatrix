import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: {
    default: "BidMatrix",
    template: "%s | BidMatrix",
  },
  description: "Quality-reviewed, source-linked RFP intelligence for focused IT and cybersecurity teams.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className="h-full antialiased">
      <body className="min-h-full">{children}</body>
    </html>
  );
}
