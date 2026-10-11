import { endImpersonation, getImpersonation } from '../lib/impersonation'

export default function ImpersonationBanner() {
  const impersonation = getImpersonation()
  if (!impersonation) return null

  function exit() {
    endImpersonation()
    window.location.href = impersonation!.returnPath
  }

  return (
    <div className="bg-yellow-500 text-black text-sm font-medium px-4 py-2 flex items-center justify-center gap-3 sticky top-0 z-50">
      <span>Viewing as {impersonation.name} (impersonated)</span>
      <button onClick={exit} className="underline font-semibold">Exit</button>
    </div>
  )
}
