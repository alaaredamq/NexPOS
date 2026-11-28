import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { auth, setChangedHandler, setUnauthorizedHandler } from './api'
import { Overlays, Sprite } from './ui'
import { Shell } from './Shell'
import { Login } from './pages/Login'
import { Clients } from './pages/Clients'
import { ClientPage } from './pages/Client'
import { SitePage } from './pages/Site'
import { NewClient } from './pages/NewClient'
import { Licences } from './pages/Licences'
import { Finance } from './pages/Finance'
import { Menus } from './pages/Menus'
import './console.css'

// Fresh for 20 s: moving between pages is instant, and anything you change refreshes every page that shows it.
const queries = new QueryClient({ defaultOptions: { queries: { staleTime: 20_000, retry: 1, refetchOnWindowFocus: true } } })
setChangedHandler(() => { void queries.invalidateQueries() })
setUnauthorizedHandler(() => {
  auth.set(null)
  queries.clear()
  if (!location.pathname.startsWith('/console/login')) location.assign('/console/login')
})

const Private = ({ children }: { children: React.ReactNode }) => (auth.token ? <>{children}</> : <Navigate to="/login" replace />)

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queries}>
      <BrowserRouter basename="/console">
        <Sprite />
        <Overlays>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route element={<Private><Shell /></Private>}>
              <Route index element={<Clients />} />
              <Route path="clients/:id" element={<ClientPage />} />
              <Route path="sites/:id" element={<SitePage />} />
              <Route path="new" element={<NewClient />} />
              <Route path="menus" element={<Menus />} />
              <Route path="licences" element={<Licences />} />
              <Route path="finance" element={<Finance />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Route>
          </Routes>
        </Overlays>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
