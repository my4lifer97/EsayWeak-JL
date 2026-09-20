import { useEffect, useState } from 'react'
import { platformAdminApi } from '../../lib/platformAdminApi'

type Props = {
  businessId: string
  businessName: string
  businessEmail: string
  username: string | null
  initialPassword?: string | null
  onClose: () => void
}

const TEMPLATE_STORAGE_KEY = 'ownerEmailTemplate'
const DEFAULT_TEMPLATE = `Hi,

Here's your EsayWeek account information for {{businessName}}:

{{fields}}

Thanks,
EsayWeek`

function loadTemplate(): string {
  try {
    return localStorage.getItem(TEMPLATE_STORAGE_KEY) || DEFAULT_TEMPLATE
  } catch {
    return DEFAULT_TEMPLATE
  }
}

function buildFields(username: string | null, includeUsername: boolean, password: string | null, includePassword: boolean, chatbotLink: string | null, includeChatbotLink: boolean) {
  const lines: string[] = []
  if (includeUsername && username) lines.push(`Username: ${username}`)
  if (includePassword && password) lines.push(`Password: ${password}`)
  if (includeChatbotLink && chatbotLink) lines.push(`Connect your WhatsApp chatbot (open on your phone and scan): ${chatbotLink}`)
  return lines.length ? lines.join('\n') : '(nothing selected yet)'
}

function applyTemplate(template: string, businessName: string, fields: string) {
  return template.split('{{businessName}}').join(businessName).split('{{fields}}').join(fields)
}

// Lets the admin pick exactly which pieces of an owner's account info to send -- username,
// (freshly generated) password, chatbot link, any combination -- preview the exact email text,
// and then choose to send it (via the admin's own Gmail, see IOwnerEmailSender), copy it to send
// by hand, or just read it off screen during a phone call. Reused from both the request-approval
// flow (pre-filled with a fresh username+temp password) and the business detail page (general
// "email the owner" utility, e.g. a password reset or a WhatsApp link on its own).
//
// The surrounding wording (greeting, sign-off) is admin-editable and persisted in localStorage
// (per browser, not synced -- there's only one admin account today, so this is simpler than a
// backend-stored setting) as a template with {{businessName}}/{{fields}} placeholders; the
// per-checkbox field lines are always composed automatically so the preview never silently drops
// a selected item just because the saved template forgot the placeholder.
export default function OwnerEmailComposer({ businessId, businessName, businessEmail, username, initialPassword, onClose }: Props) {
  const [includeUsername, setIncludeUsername] = useState(!!username)
  const [includePassword, setIncludePassword] = useState(!!initialPassword)
  const [includeChatbotLink, setIncludeChatbotLink] = useState(false)
  const [password, setPassword] = useState(initialPassword ?? null)
  const [chatbotLink, setChatbotLink] = useState<string | null>(null)
  const [generatingPassword, setGeneratingPassword] = useState(false)
  const [generatingLink, setGeneratingLink] = useState(false)
  const [genError, setGenError] = useState('')

  const [template, setTemplate] = useState(loadTemplate)
  const [editingTemplate, setEditingTemplate] = useState(false)
  const [templateDraft, setTemplateDraft] = useState('')

  const [subject, setSubject] = useState(`Your EsayWeek account — ${businessName}`)
  const [body, setBody] = useState(() =>
    applyTemplate(template, businessName, buildFields(username, !!username, password, !!initialPassword, null, false)))

  const [sending, setSending] = useState(false)
  const [sendError, setSendError] = useState('')
  const [sent, setSent] = useState(false)
  const [copied, setCopied] = useState(false)

  // Re-templates the body whenever a checkbox, a generated value, or the template itself changes;
  // edits made directly in the Body textarea persist until the next such change, at which point
  // they're intentionally replaced (keeps the preview trustworthy -- what you see is always
  // consistent with what's checked and with the saved template).
  useEffect(() => {
    setBody(applyTemplate(template, businessName, buildFields(username, includeUsername, password, includePassword, chatbotLink, includeChatbotLink)))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [includeUsername, includePassword, includeChatbotLink, password, chatbotLink, template])

  function startEditingTemplate() {
    setTemplateDraft(template)
    setEditingTemplate(true)
  }

  function saveTemplate() {
    setTemplate(templateDraft)
    try {
      localStorage.setItem(TEMPLATE_STORAGE_KEY, templateDraft)
    } catch { /* private-mode/blocked storage -- template still applies for this session */ }
    setEditingTemplate(false)
  }

  function resetTemplateToOriginal() {
    setTemplateDraft(DEFAULT_TEMPLATE)
  }

  async function generatePassword() {
    setGenError(''); setGeneratingPassword(true)
    try {
      const { data } = await platformAdminApi.post(`/platform-admin/businesses/${businessId}/reset-password`, { temporary: true, silent: true })
      setPassword(data.tempPassword)
      setIncludePassword(true)
    } catch {
      setGenError('Could not generate a password')
    } finally {
      setGeneratingPassword(false)
    }
  }

  async function generateChatbotLink() {
    setGenError(''); setGeneratingLink(true)
    try {
      const { data } = await platformAdminApi.post(`/platform-admin/businesses/${businessId}/whatsapp/link-token`)
      setChatbotLink(data.url)
      setIncludeChatbotLink(true)
    } catch {
      setGenError('Could not generate a chatbot link')
    } finally {
      setGeneratingLink(false)
    }
  }

  async function send() {
    setSendError(''); setSending(true)
    try {
      await platformAdminApi.post(`/platform-admin/businesses/${businessId}/email`, { subject, body })
      setSent(true)
    } catch (err: unknown) {
      const resp = (err as { response?: { data?: { error?: string } } })?.response
      setSendError(resp?.data?.error ?? 'Could not send the email')
    } finally {
      setSending(false)
    }
  }

  async function copyToClipboard() {
    await navigator.clipboard.writeText(`Subject: ${subject}\n\n${body}`)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  return (
    <div className="fixed inset-0 bg-black/60 flex items-start justify-center p-4 overflow-y-auto z-50" onClick={onClose}>
      <div className="bg-surface border border-line rounded-2xl p-6 w-full max-w-xl my-8" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-start justify-between mb-1">
          <h2 className="text-lg font-bold">Email the owner</h2>
          <button onClick={onClose} aria-label="Close" className="text-muted hover:text-ink text-2xl leading-none -mt-1">✕</button>
        </div>
        <p className="text-muted text-sm mb-4">{businessEmail}</p>

        {genError && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-3 py-2 mb-3">{genError}</div>}

        <div className="space-y-2 mb-4">
          {username && (
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={includeUsername} onChange={(e) => setIncludeUsername(e.target.checked)} />
              Username <code className="text-muted">{username}</code>
            </label>
          )}

          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={includePassword} disabled={!password} onChange={(e) => setIncludePassword(e.target.checked)} />
            {password ? <>Password <code className="text-muted">{password}</code></> : 'Password'}
            {!password && (
              <button type="button" onClick={generatePassword} disabled={generatingPassword}
                className="text-coral-dark hover:underline text-xs disabled:opacity-50">
                {generatingPassword ? 'Generating…' : 'Generate new temporary password'}
              </button>
            )}
          </label>

          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={includeChatbotLink} disabled={!chatbotLink} onChange={(e) => setIncludeChatbotLink(e.target.checked)} />
            {chatbotLink ? <>Chatbot connection link <code className="text-muted text-xs">{chatbotLink}</code></> : 'Chatbot connection link'}
            {!chatbotLink && (
              <button type="button" onClick={generateChatbotLink} disabled={generatingLink}
                className="text-coral-dark hover:underline text-xs disabled:opacity-50">
                {generatingLink ? 'Generating…' : 'Generate link'}
              </button>
            )}
          </label>
        </div>

        {editingTemplate ? (
          <div className="space-y-2 mb-4 border border-line rounded-lg p-3 bg-cream">
            <label className="block text-xs text-muted">
              Default template — use <code>{'{{businessName}}'}</code> and <code>{'{{fields}}'}</code> as placeholders; {'{{fields}}'} is always replaced with whatever's checked above.
            </label>
            <textarea value={templateDraft} onChange={(e) => setTemplateDraft(e.target.value)} rows={7}
              className="w-full bg-surface border border-line rounded-lg px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-coral resize-y" />
            <div className="flex gap-2">
              <button type="button" onClick={saveTemplate}
                className="bg-coral hover:bg-coral-dark text-white font-semibold text-xs px-3 py-1.5 rounded-lg transition-colors">
                Save as default
              </button>
              <button type="button" onClick={() => setEditingTemplate(false)}
                className="border border-line text-ink hover:bg-surface font-semibold text-xs px-3 py-1.5 rounded-lg transition-colors">
                Cancel
              </button>
              <button type="button" onClick={resetTemplateToOriginal}
                className="text-muted hover:text-ink text-xs ms-auto">
                Reset to original
              </button>
            </div>
          </div>
        ) : (
          <div className="space-y-2 mb-4">
            <div className="flex items-center justify-between">
              <label className="block text-xs text-muted">Subject</label>
              <button type="button" onClick={startEditingTemplate} className="text-coral-dark hover:underline text-xs">
                Edit default template
              </button>
            </div>
            <input value={subject} onChange={(e) => setSubject(e.target.value)}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-coral" />
            <label className="block text-xs text-muted">Body (editable — this is exactly what will be sent)</label>
            <textarea value={body} onChange={(e) => setBody(e.target.value)} rows={8}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-sm font-mono focus:outline-none focus:ring-2 focus:ring-coral resize-y" />
          </div>
        )}

        {sendError && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-3 py-2 mb-3">{sendError}</div>}
        {sent && <div className="bg-green-50 border border-green-200 text-green-700 dark:bg-green-950/40 dark:border-green-800/50 dark:text-green-400 text-sm rounded-lg px-3 py-2 mb-3">Sent ✓</div>}

        <div className="flex gap-2">
          <button onClick={send} disabled={sending || editingTemplate}
            className="flex-1 bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold text-sm py-2.5 rounded-lg transition-colors">
            {sending ? 'Sending…' : 'Send Email'}
          </button>
          <button onClick={copyToClipboard} disabled={editingTemplate}
            className="flex-1 border border-line text-ink hover:bg-cream disabled:opacity-50 font-semibold text-sm py-2.5 rounded-lg transition-colors">
            {copied ? 'Copied!' : 'Copy to Clipboard'}
          </button>
        </div>
        <p className="text-muted text-xs mt-2">
          Or just read the text above during a phone call — nothing here is sent until you click Send.
        </p>
      </div>
    </div>
  )
}
