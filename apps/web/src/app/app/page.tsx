import type { Metadata } from "next";
import { CustomerDashboard } from "@/components/customer-dashboard";

export const metadata: Metadata = { title: "Dashboard" };

export default function CustomerAppPage() {
  return <CustomerDashboard />;
}
