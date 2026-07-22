import { CustomerShell } from "@/components/customer-shell";

export default function CustomerAppLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return <CustomerShell>{children}</CustomerShell>;
}
