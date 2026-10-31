import { afterEach, describe, expect, it, vi } from 'vitest'
import { electron } from './electron'

const job = { html: '<p>x</p>', copies: 1, widthMm: 40, heightMm: 25 }

afterEach(() => {
  delete window.nexpos
})

describe('electron bridge in a plain browser', () => {
  it('reports not-desktop and no-ops', async () => {
    expect(electron.isElectron).toBe(false)
    expect(await electron.listPrinters()).toEqual([])
    expect(await electron.printSticker(job)).toEqual({ success: false, failureReason: 'unavailable' })
  })
})

describe('electron bridge inside the desktop shell', () => {
  it('forwards to window.nexpos', async () => {
    const printers = [{ name: 'Zebra', displayName: 'Zebra ZD220', isDefault: true }]
    window.nexpos = {
      isElectron: true,
      listPrinters: vi.fn().mockResolvedValue(printers),
      printSticker: vi.fn().mockResolvedValue({ success: true }),
    }
    expect(electron.isElectron).toBe(true)
    expect(await electron.listPrinters()).toEqual(printers)
    expect(await electron.printSticker(job)).toEqual({ success: true })
    expect(window.nexpos.printSticker).toHaveBeenCalledWith(job)
  })

  it('turns IPC failures into a result instead of throwing', async () => {
    window.nexpos = {
      isElectron: true,
      listPrinters: vi.fn().mockRejectedValue(new Error('ipc')),
      printSticker: vi.fn().mockRejectedValue(new Error('timeout')),
    }
    expect(await electron.listPrinters()).toEqual([])
    expect(await electron.printSticker(job)).toEqual({ success: false, failureReason: 'timeout' })
  })
})
