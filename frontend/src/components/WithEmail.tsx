// Renders a translated string with its {email} placeholder filled in, the address isolated in
// <bdi> so it doesn't reorder inside rtl text.
export default function WithEmail({ text, email }: { text: string; email: string }) {
  const [before, after = ''] = text.split('{email}')
  return <>{before}<bdi>{email}</bdi>{after}</>
}
