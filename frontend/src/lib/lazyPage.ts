import { lazy, type ComponentType } from 'react'

const RELOAD_FLAG = 'chunkReloadAt'

// React.lazy for a route page, kept out of the main bundle (customers opening a WhatsApp link
// shouldn't download the owner dashboard). After a deploy the old chunk files are gone, so a tab
// opened before it fails to load the next page; reload once to pick up the new build instead of
// showing a blank screen. The timestamp stops a reload loop if the chunk is really missing.
export function lazyPage<T extends ComponentType<object>>(load: () => Promise<{ default: T }>) {
  return lazy(() =>
    load().catch((err) => {
      let last = 0
      try { last = Number(sessionStorage.getItem(RELOAD_FLAG) ?? 0) } catch { /* storage blocked */ }
      if (Date.now() - last > 30_000) {
        try { sessionStorage.setItem(RELOAD_FLAG, String(Date.now())) } catch { /* storage blocked */ }
        window.location.reload()
        return new Promise<{ default: T }>(() => {}) // the reload replaces the page
      }
      throw err
    }),
  )
}
