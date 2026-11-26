import { useState } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { motion } from 'motion/react'
import { api, auth } from '../api'
import { Background, CloudMark, EASE, ThemeToggle, useGlassLight } from '../fx'

// Only for show: a sample day for a shop that lost power for a few hours in the afternoon.
const SAMPLE = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1]

export function Login() {
  useGlassLight()
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [show, setShow] = useState(false)
  const [pwFocus, setPwFocus] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  if (auth.token) return <Navigate to="/" replace />

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true); setError(null)
    try {
      const r = await api.login(username.trim(), password)
      auth.set(r.token)
      try { sessionStorage.removeItem('coduo.console.intro') } catch { /* private mode */ }
      navigate('/', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Sign-in did not work.')
    } finally { setBusy(false) }
  }

  const rise = (i: number) => ({ initial: { opacity: 0, y: 18, filter: 'blur(8px)' }, animate: { opacity: 1, y: 0, filter: 'blur(0px)' }, transition: { duration: 0.8, ease: EASE, delay: 0.1 + i * 0.12 } })
  return (
    <>
      <Background />
      <ThemeToggle float />
      <div className="auth">
        <section className="form">
          <motion.div className="box" {...rise(0)}>
            <CloudMark className="mark-big" shut={pwFocus && !show} />
            <div className="sign-word"><b>CODUO</b><span>CLOUD</span></div>
            <h2>Sign in</h2>
            <p className="muted">Use the account set up for this portal.</p>
            <form onSubmit={submit}>
              <label htmlFor="username">Username</label>
              <input id="username" value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" autoFocus required />
              <label htmlFor="password">Password</label>
              <div className="pw">
                <input id="password" type={show ? 'text' : 'password'} value={password} onChange={(e) => setPassword(e.target.value)}
                  onFocus={() => setPwFocus(true)} onBlur={() => setPwFocus(false)} autoComplete="current-password" required />
                <button type="button" onClick={() => setShow(!show)} aria-controls="password">{show ? 'Hide' : 'Show'}</button>
              </div>
              {error && <p className="error" role="alert">{error}</p>}
              <p style={{ marginTop: 18 }}><button type="submit" className="btn" style={{ width: '100%', minHeight: 44 }} disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button></p>
            </form>
          </motion.div>
        </section>
        <section className="side">
          <motion.div {...rise(1)}>
            <h1>Every branch, <em>in sight.</em></h1>
            <p>See which shops are connected, who is due to renew, and the hours a shop's PC went quiet.</p>
            <ol className="pulse" aria-hidden="true">
              {SAMPLE.map((on, i) => <li key={i} className={on ? 'on' : 'off'} style={{ ['--i' as string]: i }} />)}
            </ol>
          </motion.div>
          <motion.small className="muted" style={{ color: 'var(--rail-ink)', opacity: 0.7 }} {...rise(2)}>For the provider only.</motion.small>
        </section>
      </div>
    </>
  )
}
