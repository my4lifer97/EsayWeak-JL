// Arabic-Indic (٠-٩), Persian (۰-۹) and fullwidth (０-９) digits as 0-9: a code typed on an Arabic
// keyboard was erased by a plain \D filter (QA run 7).
export function toAsciiDigits(text: string) {
  return text.replace(/[٠-٩۰-۹０-９]/g, (c) => {
    const code = c.charCodeAt(0)
    const zero = code >= 0xff10 ? 0xff10 : code >= 0x06f0 ? 0x06f0 : 0x0660
    return String(code - zero)
  })
}
