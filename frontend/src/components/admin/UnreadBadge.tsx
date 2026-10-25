// The red square with the white count of unread chat messages: on the sidebar's Chats item and
// on each chat in the Chats page's list.
export default function UnreadBadge({ count, label }: { count: number; label: string }) {
  return (
    <span aria-label={label} className="shrink-0 min-w-5 h-5 px-1 rounded bg-red-600 text-white text-xs font-bold leading-5 text-center">
      {count > 99 ? '99+' : count}
    </span>
  )
}

// Shared by AdminSidebar's badge query and ChatsPage, which refreshes it after reading a chat.
export const CHATS_UNREAD_KEY = ['chats-unread']
