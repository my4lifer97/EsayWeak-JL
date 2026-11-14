import { useEffect } from 'react'
import { BRAND_NAME } from './brand'

// "<page> · <brand>" in the browser tab (QA run 11 PUB-61: every page was just the brand).
export function pageTitle(name?: string | null): string {
  return name ? `${name} · ${BRAND_NAME}` : BRAND_NAME
}

// For pages whose title comes from data (a business's name); DocumentLanguage leaves those paths alone.
export function useDocumentTitle(name?: string | null) {
  useEffect(() => { document.title = pageTitle(name) }, [name])
}
