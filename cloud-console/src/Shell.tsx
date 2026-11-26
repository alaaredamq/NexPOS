// The frame that never reloads: background and stars, the glass rail, the console strip. Only the page inside changes, and it
// changes with a short fade; its cards then enter one after the other (see Stagger in fx.tsx).
import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, useLocation, useNavigate, useOutlet } from 'react-router-dom'
import { AnimatePresence, motion } from 'motion/react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, auth } from './api'
import { setCurrency } from './format'
import { Background, CloudMark, EASE, GateProvider, ThemeToggle, reducedMotion, useGlassLight } from './fx'
import { Intro } from './Intro'
import { Icon } from './ui'

const NAV = [
  { to: '/', label: 'Clients', icon: 'store', end: true },
  { to: '/licences', label: 'Licences', icon: 'key' },
  { to: '/finance', label: 'Finance', icon: 'chart' },
  { to: '/new', label: 'New client', icon: 'plus' },
]

const INTRO_KEY = 'coduo.console.intro'
const wantsIntro = () => {
  if (reducedMotion) return false
  try { return /[?&]intro\b/.test(location.search) || !sessionStorage.getItem(INTRO_KEY) } catch { return false }
}

export function Shell() {
  useGlassLight()
  const location = useLocation()
  const outlet = useOutlet()
  const shell = useRef<HTMLDivElement>(null)
  const [intro, setIntro] = useState(wantsIntro)
  const [go, setGo] = useState(!intro)
  const me = useQuery({ queryKey: ['me'], queryFn: api.me, staleTime: 5 * 60_000 })
  if (me.data) setCurrency(me.data.currency)

  useEffect(() => { if (intro) try { sessionStorage.setItem(INTRO_KEY, '1') } catch { /* private mode */ } }, [intro])
  // A new page starts at the top, after the old one has faded out.
  useEffect(() => { const t = setTimeout(() => window.scrollTo({ top: 0 }), 200); return () => clearTimeout(t) }, [location.pathname])
  const title = pageTitle(location.pathname)
  useEffect(() => { document.title = title + ' · Coduo Cloud' }, [title])

  return (
    <>
      <Background />
      {intro && <Intro shell={shell} onOpen={() => setGo(true)} onDone={() => setIntro(false)} />}
      <div className="shell" ref={shell}>
        <Rail username={me.data?.username ?? ''} />
        <main id="main">
          <div className="hud" aria-hidden="true">
            <span className="live"><i />Provider console</span>
            <span className="path">Coduo Cloud / <b>{title}</b></span>
            <span className="line" />
            <Clock />
          </div>
          <GateProvider value={go}>
            <AnimatePresence mode="wait" initial={false}>
              <motion.div key={location.pathname} className="page"
                initial={reducedMotion ? false : { opacity: 1 }}
                exit={reducedMotion ? undefined : { opacity: 0, y: -10, filter: 'blur(6px)', transition: { duration: 0.2, ease: EASE } }}>
                {outlet}
              </motion.div>
            </AnimatePresence>
          </GateProvider>
        </main>
      </div>
    </>
  )
}

function pageTitle(path: string) {
  if (path.startsWith('/licences')) return 'Licences'
  if (path.startsWith('/finance')) return 'Finance'
  if (path.startsWith('/new')) return 'New client'
  if (path.startsWith('/sites')) return 'Branch'
  if (path.startsWith('/clients')) return 'Client'
  return 'Clients'
}

function Rail({ username }: { username: string }) {
  const [hover, setHover] = useState<string | null>(null)
  const location = useLocation()
  const navigate = useNavigate()
  const queries = useQueryClient()
  const activeTo = NAV.find((n) => (n.end ? location.pathname === '/' || location.pathname.startsWith('/clients') || location.pathname.startsWith('/sites') : location.pathname.startsWith(n.to)))?.to
  const glowAt = hover ?? activeTo
  const signOut = async () => {
    try { await api.logout() } catch { /* signed out here either way */ }
    auth.set(null)
    queries.clear()
    navigate('/login')
  }
  return (
    <aside className="rail">
      <Link className="brand" to="/" aria-label="Coduo Cloud, clients">
        <CloudMark className="mark" />
        <span><b>Coduo</b><span>Cloud</span></span>
      </Link>
      <nav aria-label="Main" className="has-glow" onMouseLeave={() => setHover(null)}>
        {NAV.map((n) => (
          <NavLink key={n.to} to={n.to} end={n.end} onMouseEnter={() => setHover(n.to)} onFocus={() => setHover(n.to)} onBlur={() => setHover(null)}
            aria-current={activeTo === n.to ? 'page' : undefined}>
            {glowAt === n.to && <motion.span layoutId="nav-glow" className="nav-glow on" transition={{ type: 'spring', stiffness: 420, damping: 34 }} />}
            <Icon id={n.icon} /><span className="t">{n.label}</span>
          </NavLink>
        ))}
      </nav>
      <a className="ext" href="/owner" target="_blank" rel="noopener"><Icon id="link" /><span className="t">Owner website</span></a>
      <div className="who">
        <span className="avatar" aria-hidden="true">{username ? username[0] : '?'}</span>
        <span className="who-name">{username}<small>Provider</small></span>
        <ThemeToggle />
        <button type="button" aria-label="Sign out" title="Sign out" onClick={signOut}><Icon id="out" /></button>
      </div>
    </aside>
  )
}

function Clock() {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => { const t = setInterval(() => setNow(new Date()), 1000); return () => clearInterval(t) }, [])
  const two = (n: number) => String(n).padStart(2, '0')
  return <span className="clock">{now.toLocaleDateString('en-GB', { weekday: 'short', day: '2-digit', month: 'short' })}  ·  {two(now.getHours())}:{two(now.getMinutes())}:{two(now.getSeconds())}</span>
}
