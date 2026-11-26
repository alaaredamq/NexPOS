// The opening: the cloud draws itself, its eyes pop open, blink, the right one winks while the cloud tilts, then the cloud becomes a
// window onto the console (a hole in the mask) and the camera flies in through it. ~4.5 s; a click, key or scroll skips ahead.
// onOpen fires when the window opens, so the cards start entering just as they come into view through it.
import { useEffect, useRef } from 'react'
import { CLOUD_PATH } from './fx'

const span = (t: number, a: number, b: number) => Math.max(0, Math.min(1, (t - a) / (b - a)))
const out = (t: number) => 1 - Math.pow(1 - t, 3)
const inOut = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2)
const back = (t: number) => 1 + 2.9 * Math.pow(t - 1, 3) + 1.9 * Math.pow(t - 1, 2)
const OY = -12 + 1.64 * 0.95 // the point between the eyes: the camera flies in there
const RINGS = [300, 1000, 2300]

export function Intro({ shell, onOpen, onDone }: { shell: React.RefObject<HTMLElement | null>; onOpen: () => void; onDone: () => void }) {
  const root = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const el = root.current!
    const $ = <T extends Element>(s: string) => el.querySelector(s) as T
    const zooms = [...el.querySelectorAll('[data-zoom]')]
    const cloud = $<SVGPathElement>('[data-cloud]'), glow = $<SVGPathElement>('[data-glow]'), hole = $<SVGPathElement>('[data-hole]')
    const tilt = $('[data-tilt]'), eyeL = $('[data-eye="l"]'), eyeR = $('[data-eye="r"]'), eyes = $('[data-eyes]')
    const rings = [...el.querySelectorAll('[data-ring]')], reticle = $('[data-reticle]'), arc = $('[data-arc]')
    const eyeT = (cx: number, sx: number, sy: number) =>
      `translate(${cx} 1.64) scale(${Math.max(0.0001, Math.min(sx, 9))} ${Math.max(0.0001, sy)}) translate(${-cx} -1.64)`
    document.documentElement.classList.add('fx-intro')
    const place = () => {
      const unit = Math.min(innerWidth, innerHeight) / 200
      if (shell.current) shell.current.style.transformOrigin = `${innerWidth / 2}px ${scrollY + innerHeight / 2 + OY * unit}px`
    }
    place()
    addEventListener('resize', place)

    let t = 0, speed = 1, last = performance.now(), raf = 0, opened = false, done = false
    const frame = (t: number) => {
      const drawn = inOut(span(t, 150, 1250))
      cloud.style.strokeDashoffset = String(100 - 100 * drawn)
      glow.style.strokeDashoffset = String(100 - 100 * drawn)
      glow.setAttribute('opacity', (0.95 * span(t, 150, 700) * (1 - span(t, 3100, 3350))).toFixed(3))
      const pop = span(t, 900, 1320), open = pop === 0 ? 0 : back(pop)
      const blink = span(t, 1650, 1840), blinkY = blink > 0 && blink < 1 ? 1 - Math.sin(blink * Math.PI) * 0.92 : 1
      const wink = out(span(t, 2250, 2340)) * (1 - out(span(t, 2500, 2660)))
      const shut = out(span(t, 3120, 3290))
      eyeL.setAttribute('transform', eyeT(-9.5, open, open * blinkY * (1 - 0.16 * wink) * (1 - 0.95 * shut)))
      eyeR.setAttribute('transform', eyeT(9.5, open, open * blinkY * (1 - 0.92 * wink) * (1 - 0.95 * shut)))
      eyes.setAttribute('opacity', (1 - span(t, 3200, 3380)).toFixed(3))
      const lean = Math.sin(Math.PI * span(t, 2180, 2760))
      tilt.setAttribute('transform', `translate(0 ${(-2.2 * lean).toFixed(2)}) rotate(${(-7 * lean).toFixed(2)} 0 -10)`)
      rings.forEach((ring, n) => {
        const p = span(t, RINGS[n], RINGS[n] + 1800)
        ring.setAttribute('r', (30 + 115 * out(p)).toFixed(2))
        ring.setAttribute('opacity', (p > 0 && p < 1 ? 0.55 * (1 - p) : 0).toFixed(3))
      })
      reticle.setAttribute('opacity', (0.85 * span(t, 450, 1200) * (1 - span(t, 2700, 3000))).toFixed(3))
      reticle.setAttribute('transform', `rotate(${(t * 0.018).toFixed(2)} 0 -10)`)
      arc.setAttribute('transform', `rotate(${(-t * 0.09).toFixed(2)} 0 -10)`)
      const win = inOut(span(t, 2750, 3150)), g = Math.round(255 * (1 - win))
      hole.setAttribute('fill', `rgb(${g},${g},${g})`)
      const fly = span(t, 3150, 4450)
      const z = fly > 0 ? 0.93 * Math.pow(60, inOut(fly)) : 1 - 0.07 * out(span(t, 2850, 3150))
      const tf = `translate(0 ${OY}) scale(${z.toFixed(4)}) translate(0 ${-OY})`
      zooms.forEach((z) => z.setAttribute('transform', tf))
      if (shell.current) shell.current.style.transform = `scale(${(0.86 + 0.14 * out(fly)).toFixed(4)})`
      el.classList.toggle('s-word', t > 1050)
      el.classList.toggle('s-out', t > 2650)
    }
    const finish = () => {
      if (done) return
      done = true
      if (shell.current) { shell.current.style.transform = ''; shell.current.style.transformOrigin = '' }
      document.documentElement.classList.remove('fx-intro')
      if (!opened) { opened = true; onOpen() }
      onDone()
    }
    const loop = (now: number) => {
      if (done) return
      t += Math.min(64, now - last) * speed
      last = now
      try { frame(t) } catch { finish(); return }
      if (!opened && t >= 2850) { opened = true; onOpen() }
      if (t >= 4480) { finish(); return }
      raf = requestAnimationFrame(loop)
    }
    const skip = () => { if (t < 2700) t = 2700; speed = 1.7 }
    el.addEventListener('pointerdown', skip)
    addEventListener('keydown', skip)
    addEventListener('wheel', skip, { passive: true })
    el.classList.add('s-scan')
    raf = requestAnimationFrame((now) => { last = now; loop(now) })
    const safety = setTimeout(finish, 12000)
    return () => {
      cancelAnimationFrame(raf); clearTimeout(safety)
      removeEventListener('resize', place); removeEventListener('keydown', skip); removeEventListener('wheel', skip)
      document.documentElement.classList.remove('fx-intro')
      if (shell.current) { shell.current.style.transform = ''; shell.current.style.transformOrigin = '' }
    }
    // Runs once per mount by design: re-renders must not restart the opening.
  }, [])

  return (
    <div className="intro" ref={root} aria-hidden="true">
      <svg className="intro-svg" viewBox="-100 -100 200 200" preserveAspectRatio="xMidYMid meet">
        <defs>
          <radialGradient id="ig-bg" gradientUnits="userSpaceOnUse" cx="0" cy="-10" r="190">
            <stop data-bg="0" offset="0" stopColor="#16295f" /><stop data-bg="1" offset=".38" stopColor="#0a1438" /><stop data-bg="2" offset="1" stopColor="#040815" />
          </radialGradient>
          <filter id="ig-blur" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="3.2" /></filter>
          <filter id="ig-shine" x="-100%" y="-100%" width="300%" height="300%"><feGaussianBlur stdDeviation="1.6" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
          <mask id="ig-mask" maskUnits="userSpaceOnUse" x="-6000" y="-6000" width="12000" height="12000">
            <rect x="-6000" y="-6000" width="12000" height="12000" fill="#fff" />
            <g data-zoom><g transform="translate(0 -12) scale(.95)"><path data-hole transform="translate(-1.12 5) scale(1.12)" d={CLOUD_PATH} fill="#fff" /></g></g>
          </mask>
        </defs>
        <g mask="url(#ig-mask)">
          <rect x="-6000" y="-6000" width="12000" height="12000" fill="url(#ig-bg)" />
          <circle data-ring cx="0" cy="-10" r="30" fill="none" stroke="#3ddbcb" strokeWidth=".35" opacity="0" />
          <circle data-ring cx="0" cy="-10" r="30" fill="none" stroke="#6d8df0" strokeWidth=".35" opacity="0" />
          <circle data-ring cx="0" cy="-10" r="30" fill="none" stroke="#3ddbcb" strokeWidth=".35" opacity="0" />
          <g data-reticle opacity="0">
            <circle cx="0" cy="-10" r="56" fill="none" stroke="#6d8df0" strokeWidth=".45" strokeDasharray=".7 2.6" />
            <circle cx="0" cy="-10" r="62" fill="none" stroke="#6d8df0" strokeWidth=".25" strokeOpacity=".5" />
            <g data-arc>
              <path d="M-43.8 -53.8A62 62 0 0 1 43.8 -53.8" fill="none" stroke="#3ddbcb" strokeWidth=".9" strokeLinecap="round" />
              <path d="M43.8 33.8A62 62 0 0 1 -43.8 33.8" fill="none" stroke="#3ddbcb" strokeWidth=".5" strokeLinecap="round" strokeOpacity=".6" />
            </g>
          </g>
        </g>
        <g data-zoom>
          <g data-tilt>
            <g transform="translate(0 -12) scale(.95)">
              <g transform="translate(-1.12 5) scale(1.12)">
                <path data-glow d={CLOUD_PATH} fill="none" stroke="#7d9bff" strokeWidth="7.143" strokeLinejoin="round" strokeLinecap="round" pathLength={100} filter="url(#ig-blur)" opacity="0" style={{ strokeDasharray: 100, strokeDashoffset: 100 }} />
                <path data-cloud d={CLOUD_PATH} fill="none" stroke="#6d8df0" strokeWidth="7.143" strokeLinejoin="round" strokeLinecap="round" pathLength={100} style={{ strokeDasharray: 100, strokeDashoffset: 100 }} />
              </g>
              <g data-eyes filter="url(#ig-shine)">
                <circle data-eye="l" cx="-9.5" cy="1.64" r="5.5" fill="#3ddbcb" transform="scale(0)" />
                <circle data-eye="r" cx="9.5" cy="1.64" r="5.5" fill="#3ddbcb" transform="scale(0)" />
              </g>
            </g>
          </g>
        </g>
      </svg>
      <div className="intro-scan" />
      <div className="intro-word"><b>CODUO</b><span>CLOUD</span></div>
      <div className="intro-hint">Click to skip</div>
    </div>
  )
}
