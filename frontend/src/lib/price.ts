// Whole prices without decimals, others with two: 12.5 showed as ₪13 (QA run 7).
export function formatPrice(price: number | string) {
  const n = Number(price)
  return `₪${Number.isInteger(n) ? n : n.toFixed(2)}`
}
