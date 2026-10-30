// The typed text and chosen file of each chat while the page is open: switching chats remounts
// the conversation, which used to lose them (QA run 5). Cleared on sign-in and sign-out, or the
// next owner on a shared tab found the previous one's drafts (QA run 7).
export const chatDrafts = new Map<string, { text: string; file: File | null }>()

export function clearChatDrafts() {
  chatDrafts.clear()
}
