import { useState } from 'react'
import { createBrowserRouter, Outlet, RouterProvider } from 'react-router-dom'
import { moduleRoutes } from './routes'

// 0D replaces this placeholder with the real shell (rail, top bar, session bootstrap).
function Placeholder() {
  return (
    <main style={{ padding: 24 }}>
      <h1>NexPOS</h1>
      <p>React shell scaffold. {moduleRoutes.length} module route(s) registered.</p>
      <Outlet />
    </main>
  )
}

const createRouter = () =>
  createBrowserRouter(
    [{ path: '/', element: <Placeholder />, children: moduleRoutes }],
    { basename: '/app' },
  )

export function App() {
  const [router] = useState(createRouter)
  return <RouterProvider router={router} />
}
