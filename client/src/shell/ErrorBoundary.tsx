import { Component, type ErrorInfo, type ReactNode } from 'react'
import { useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { StatusCard } from './pages/StatusCard'

interface Props {
  children: ReactNode
  /** Changing this clears the error (the route key, so navigating away from a broken page recovers). */
  resetKey?: string
}

interface State {
  failed: boolean
  key?: string
}

class Boundary extends Component<Props & { title: string; body: string; retry: string }, State> {
  state: State = { failed: false, key: this.props.resetKey }

  static getDerivedStateFromError(): Partial<State> {
    return { failed: true }
  }

  // Navigating to another page clears the error, so one broken screen never traps the user.
  static getDerivedStateFromProps(props: Props, state: State): Partial<State> | null {
    return props.resetKey !== state.key ? { failed: false, key: props.resetKey } : null
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Screen crashed', error, info.componentStack)
  }

  render() {
    if (!this.state.failed) return this.props.children
    return (
      <StatusCard
        glyph="alert"
        title={this.props.title}
        actions={<Button variant="primary" onClick={() => this.setState({ failed: false })}>{this.props.retry}</Button>}
      >
        {this.props.body}
      </StatusCard>
    )
  }
}

/** Keeps a crash inside one page: the rail and top bar stay usable, and leaving the page clears the error. */
export function ErrorBoundary({ children }: { children: ReactNode }) {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  return (
    <Boundary
      resetKey={pathname}
      title={t('Shell_SomethingWrong')}
      body={t('Shell_SomethingWrongBody')}
      retry={t('common.retry', 'Retry')}
    >
      {children}
    </Boundary>
  )
}
