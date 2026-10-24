// libsignal (used by Baileys) prints whole session objects -- including private keys -- with
// console.info/warn ("Closing session:", "Opening session:", ...). Keep the message, drop the
// key material. Imported first in index.js so it is in place before Baileys loads.
const redact = (arg) => {
  if (Buffer.isBuffer(arg)) return '[redacted buffer]'
  if (arg && typeof arg === 'object' && arg.constructor?.name === 'SessionEntry') return '[redacted session]'
  return arg
}

for (const level of ['log', 'info', 'warn', 'error', 'debug']) {
  const original = console[level].bind(console)
  console[level] = (...args) => original(...args.map(redact))
}
