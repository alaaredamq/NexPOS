import styles from './field.module.css'

/** Banner for server errors that belong to the whole form (or to no visible field). */
export function FormError({ messages }: { messages: string[] }) {
  if (messages.length === 0) return null
  return (
    <div className={styles.banner} role="alert">
      {messages.map((m) => (
        <div key={m}>{m}</div>
      ))}
    </div>
  )
}
