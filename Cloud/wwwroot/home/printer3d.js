// The receipt printer at the bottom of the landing page, in 3D (three.js).
// Built in CSS pixels with an orthographic camera, so the paper that comes out of its slot is exactly as wide as the
// HTML receipt above it and lines up with it to the pixel. Its LED blinks and its teal line glows while the page scrolls (the
// paper feeds) and its highlights follow the pointer. It never moves itself: the HTML paper cannot move with it.
// Its slot sits --slot px above the bottom of the screen.
// Without WebGL nothing here runs and the flat CSS printer in the same dock stays on screen.
import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';

const root = document.documentElement;
const canvas = document.querySelector('[data-printer-canvas]');
const strip = document.querySelector('.strip');
const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
const css = name => parseFloat(getComputedStyle(root).getPropertyValue(name)) || 0;

let renderer;
try {
  renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'low-power' });
} catch { renderer = null; }

if (renderer && canvas && strip) {
  renderer.setClearColor(0x000000, 0);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 0.92;

  const scene = new THREE.Scene();
  const pmrem = new THREE.PMREMGenerator(renderer);
  scene.environment = pmrem.fromScene(new RoomEnvironment(renderer), 0.04).texture;

  const camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 1, 4000);
  camera.position.set(0, 0, 2000);

  scene.add(new THREE.HemisphereLight(0xbfd0ff, 0x0b1633, 0.55));
  const key = new THREE.DirectionalLight(0xffffff, 1.5); key.position.set(-420, 700, 900); scene.add(key);
  const rim = new THREE.DirectionalLight(0x3ddbcb, 2.2); rim.position.set(650, 260, -500); scene.add(rim);
  const fill = new THREE.DirectionalLight(0x6d8df0, 0.9); fill.position.set(500, -120, 700); scene.add(fill);

  const TILT = 0.36;          // the top leans toward you, so the slot and the lid show
  const mat = {
    body: new THREE.MeshPhysicalMaterial({ color: 0x1a3378, roughness: 0.5, metalness: 0, clearcoat: 0.55, clearcoatRoughness: 0.32, envMapIntensity: 0.35 }),
    lid: new THREE.MeshPhysicalMaterial({ color: 0x223f92, roughness: 0.45, metalness: 0, clearcoat: 0.6, clearcoatRoughness: 0.3, envMapIntensity: 0.4 }),
    base: new THREE.MeshStandardMaterial({ color: 0x0c1a45, roughness: 0.75, envMapIntensity: 0.4 }),
    slot: new THREE.MeshStandardMaterial({ color: 0x02050d, roughness: 1 }),
    metal: new THREE.MeshStandardMaterial({ color: 0xcfd6e8, metalness: 1, roughness: 0.22, envMapIntensity: 1.2 }),
    button: new THREE.MeshPhysicalMaterial({ color: 0x14275c, roughness: 0.35, clearcoat: 1 }),
    led: new THREE.MeshBasicMaterial({ color: 0x3ddbcb, toneMapped: false }),
    glow: new THREE.MeshBasicMaterial({ color: 0x3ddbcb, toneMapped: false, transparent: true, opacity: 0.85 }),
    // the paper is a depth mask only: it hides the lid behind it but draws nothing, so the HTML receipt (ink and all) shows down to the slot
    paper: new THREE.MeshBasicMaterial({ colorWrite: false }),
  };

  // "CODUO" on the front, as a texture
  let labelTex = null;
  const makeLabel = () => {
    const c = document.createElement('canvas'); c.width = 512; c.height = 96;
    const g = c.getContext('2d');
    g.fillStyle = '#7f98e0'; g.font = '800 54px Inter, system-ui, sans-serif'; g.textBaseline = 'middle';
    if ('letterSpacing' in g) g.letterSpacing = '20px';
    g.fillText('CODUO', 8, 50);
    g.fillStyle = '#4f66a8'; g.font = '600 22px Inter, system-ui, sans-serif';
    if ('letterSpacing' in g) g.letterSpacing = '6px';
    g.fillText('THERMAL 80', 330, 52);
    const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.anisotropy = 4;
    return t;
  };

  let pivot = null, stub = null, led = null, ledGlow = null, W = 0, H = 0, slotY = 0, built = '';

  const build = () => {
    const w = canvas.clientWidth, h = canvas.clientHeight;
    const P = Math.round(strip.getBoundingClientRect().width);
    const slot = css('--slot');
    const sig = `${w}x${h}:${P}:${slot}`;
    if (sig === built) return;
    built = sig;
    renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 2));
    renderer.setSize(w, h, false);
    camera.left = -w / 2; camera.right = w / 2; camera.top = h / 2; camera.bottom = -h / 2; camera.updateProjectionMatrix();
    W = w; H = h;

    if (pivot) { scene.remove(pivot); pivot.traverse(o => o.geometry && o.geometry.dispose()); }
    if (stub) { scene.remove(stub); stub.geometry.dispose(); }

    const s = Math.sin(TILT), co = Math.cos(TILT);
    const BW = Math.max(P + 16, Math.min(P + Math.min(120, Math.max(44, P * 0.24)), w - 10));   // body width: never wider than the screen
    const f = Math.round(P * 0.075);                        // slot to front edge
    const gap = 16;                                         // slot to the lid
    const LH = Math.round(P * 0.05);                        // lid height
    const BH = Math.round(P * 0.05);                        // base height
    // deep enough to look solid, shallow enough that the lid stays inside the canvas
    const D = Math.min(P * 0.58, (h - slot - LH * co - 10) / s + f);
    // tall enough that the body runs past the bottom of the screen
    const BHt = Math.max(P * 0.3, (slot + 34 - f * s) / co - BH);
    const r = Math.min(26, BHt * 0.2);

    pivot = new THREE.Group();           // the pivot is the slot: leaning never moves where the paper comes out
    const body = new THREE.Group();
    pivot.add(body);

    const shell = new THREE.Mesh(new RoundedBoxGeometry(BW, BHt, D, 6, r), mat.body);
    shell.position.set(0, -BHt / 2, f - D / 2);
    body.add(shell);

    const base = new THREE.Mesh(new RoundedBoxGeometry(BW + 10, BH, D + 10, 4, Math.min(10, BH / 2)), mat.base);
    base.position.set(0, -BHt - BH / 2 + 4, f - D / 2);
    body.add(base);

    const lidD = D - f - gap - 6;
    const lid = new THREE.Mesh(new RoundedBoxGeometry(BW - 14, LH + 14, lidD, 5, Math.min(14, (LH + 14) / 2.2)), mat.lid);
    lid.position.set(0, (LH + 14) / 2 - 14, -gap - lidD / 2);
    body.add(lid);

    const slotM = new THREE.Mesh(new THREE.BoxGeometry(P + 22, 4, gap - 2), mat.slot);
    slotM.position.set(0, 0.6, -gap / 2);
    body.add(slotM);

    const cutter = new THREE.Mesh(new THREE.BoxGeometry(P + 26, 3, 5), mat.metal);
    cutter.position.set(0, 1.6, 4);
    body.add(cutter);

    // front face: label, the LED, the feed button, a teal line along the base
    const frontZ = f + 0.6;
    labelTex = labelTex || makeLabel();
    const label = new THREE.Mesh(new THREE.PlaneGeometry(220, 41), new THREE.MeshBasicMaterial({ map: labelTex, transparent: true, toneMapped: false }));
    label.position.set(-BW / 2 + 34 + 110, -Math.min(46, BHt * 0.3), frontZ);
    body.add(label);

    led = new THREE.Mesh(new THREE.CircleGeometry(5.5, 24), mat.led);
    led.position.set(BW / 2 - 92, -Math.min(46, BHt * 0.3), frontZ + 0.4);
    body.add(led);
    ledGlow = new THREE.Mesh(new THREE.CircleGeometry(16, 32), new THREE.MeshBasicMaterial({ color: 0x3ddbcb, transparent: true, opacity: 0.22, toneMapped: false, depthWrite: false }));
    ledGlow.position.copy(led.position); ledGlow.position.z -= 0.2;
    body.add(ledGlow);

    const btn = new THREE.Mesh(new THREE.CylinderGeometry(15, 15, 8, 40), mat.button);
    btn.rotation.x = Math.PI / 2;
    btn.position.set(BW / 2 - 48, -Math.min(46, BHt * 0.3), frontZ + 2);
    body.add(btn);

    const line = new THREE.Mesh(new THREE.BoxGeometry(BW * 0.42, 3, 2), mat.glow);
    line.position.set(0, -BHt + 10, frontZ + 0.5);
    body.add(line);

    pivot.rotation.x = TILT;
    slotY = -h / 2 + slot;
    pivot.position.set(0, slotY, 0);
    scene.add(pivot);

    // the paper between the slot and the top of the canvas, where the HTML receipt takes over
    const sh = h / 2 - slotY + 2;
    stub = new THREE.Mesh(new THREE.PlaneGeometry(P, sh), mat.paper);
    stub.position.set(0, slotY + sh / 2, 3);
    stub.renderOrder = -1;
    scene.add(stub);
  };

  // feeding: scroll speed drives the hum and the LED
  let feed = 0, lastY = scrollY;
  addEventListener('scroll', () => { feed = Math.min(1, feed + Math.abs(scrollY - lastY) / 60); lastY = scrollY; }, { passive: true });
  let ptx = 0, pty = 0, rx = 0, ry = 0;
  if (!reduce) addEventListener('pointermove', e => { ptx = e.clientX / innerWidth * 2 - 1; pty = e.clientY / innerHeight * 2 - 1; }, { passive: true });

  let visible = true;
  new IntersectionObserver(([e]) => { visible = e.isIntersecting; }).observe(canvas);

  const t0 = performance.now();
  let first = true, lastDraw = 0;
  const loop = () => {
    requestAnimationFrame(loop);
    if (document.hidden || !visible || root.classList.contains('cut')) { feed = 0; return; }
    // idle: a few frames a second are enough for the LED to breathe; feeding or a moving pointer gets every frame
    const now = performance.now();
    const busy = feed > 0.02 || Math.abs(ptx - ry) > 0.01 || Math.abs(pty - rx) > 0.01 || first;
    if (!busy && now - lastDraw < 90) return;
    lastDraw = now;
    const t = (now - t0) / 1000;
    if (pivot) {
      // The printer itself never moves: the HTML paper can't lean or shake with it, so any motion would open a gap
      // between the slot and the paper. The light moves instead: the pointer slides the highlights across the shell.
      ry += (ptx - ry) * 0.06;
      rx += (pty - rx) * 0.06;
      if (!reduce) {
        key.position.set(-420 + ry * 900, 700 - rx * 380, 900);
        rim.intensity = 2.2 + ry * 0.8;
      }
      const on = feed > 0.05 ? (Math.sin(t * 40) > 0 ? 1 : 0.25) : 0.55 + Math.sin(t * 2.2) * 0.45;
      led.material.color.setRGB(0.24 * on + 0.02, 0.86 * on + 0.04, 0.8 * on + 0.04);
      ledGlow.material.opacity = 0.08 + on * 0.25;
      mat.glow.opacity = 0.45 + Math.min(1, feed * 1.5) * 0.55;   // the teal line brightens while paper feeds
      feed *= 0.9;
    }
    renderer.render(scene, camera);
    if (first) { first = false; root.classList.add('gl-ready'); }
  };

  const ready = () => { build(); loop(); };
  let rt = 0;
  addEventListener('resize', () => { clearTimeout(rt); rt = setTimeout(build, 120); }, { passive: true });
  // the label uses Inter: wait for the font so the texture is drawn with it
  (document.fonts ? document.fonts.ready : Promise.resolve()).then(() => { if (labelTex) { labelTex.image = makeLabel().image; labelTex.needsUpdate = true; } });
  ready();   // draw now; the label is redrawn once Inter has loaded
}
