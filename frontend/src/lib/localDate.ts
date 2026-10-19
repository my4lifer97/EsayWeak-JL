// Today's date as yyyy-MM-dd in the browser's own time zone. toISOString() is UTC, so between
// midnight and 03:00 in Israel it still gave yesterday (QA run 3).
export function localToday() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
