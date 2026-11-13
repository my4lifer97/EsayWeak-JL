import { useEffect } from 'react'

// "<page> · EsayWeek" in the browser tab (QA run 11 PUB-61: every page was just "EsayWeek").
export function pageTitle(name?: string | null): string {
  return name ? `${name} · EsayWeek` : 'EsayWeek'
}

// For pages whose title comes from data (a business's name); DocumentLanguage leaves those paths alone.
export function useDocumentTitle(name?: string | null) {
  useEffect(() => { document.title = pageTitle(name) }, [name])
}
