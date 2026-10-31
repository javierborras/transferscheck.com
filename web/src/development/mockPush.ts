// Loaded only by the explicitly opted-in development server; eliminated from production builds.
export function mockPushToken(projectId: string): string {
  if (projectId !== "mock") throw new Error("The development push adapter requires the mock project.");
  return `mock-${crypto.randomUUID()}`;
}
