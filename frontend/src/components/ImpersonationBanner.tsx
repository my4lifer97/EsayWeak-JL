type Impersonation = { type: 'barber' | 'customer'; name: string; returnPath: string }

export default function ImpersonationBanner() {
  const raw = localStorage.getItem('impersonation')
  if (!raw) return null

  let impersonation: Impersonation
  try {
    impersonation = JSON.parse(raw)
  } catch {
    return null
  }

  function exit() {
    if (impersonation.type === 'barber') {
      localStorage.removeItem('token')
      localStorage.removeItem('user')
    } else {
      localStorage.removeItem('customerToken')
      localStorage.removeItem('customerUser')
    }
    localStorage.removeItem('impersonation')
    window.location.href = impersonation.returnPath
  }

  return (
    <div className="bg-yellow-500 text-black text-sm font-medium px-4 py-2 flex items-center justify-center gap-3 sticky top-0 z-50">
      <span>Viewing as {impersonation.name} (impersonated)</span>
      <button onClick={exit} className="underline font-semibold">Exit</button>
    </div>
  )
}
