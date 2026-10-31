import { render, screen } from '@testing-library/react'
import { App } from './App'

describe('App', () => {
  it('renders the shell placeholder', () => {
    window.history.pushState({}, '', '/app')
    render(<App />)
    expect(screen.getByRole('heading', { name: 'NexPOS' })).toBeInTheDocument()
  })
})
