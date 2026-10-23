const { contextBridge, ipcRenderer } = require('electron');

// Exposed to the web app as window.nexpos (see Web/wwwroot/js/sticker-label.js).
contextBridge.exposeInMainWorld('nexpos', {
  isElectron: true,
  listPrinters: () => ipcRenderer.invoke('sticker:printers'),
  printSticker: ({ html, printerName, copies, widthMm, heightMm }) =>
    ipcRenderer.invoke('sticker:print', { html, printerName, copies, widthMm, heightMm }),
});
