// The `error` text of a failed API call, if the server sent one. The server already translates it
// (X-Lang, see TranslateErrorFilter), so pages should prefer it over a generic message of their own.
export function apiError(err: unknown): string | undefined {
  return (err as { response?: { data?: { error?: string } } })?.response?.data?.error || undefined
}
