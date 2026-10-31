import { http } from '@/api/http'
import type { LoginRequest, Session, SignInOptions } from '@/api/types'

/** The three calls the shell makes before any module loads (Web/Api/Auth, Web/Api/Session). */
export const sessionApi = {
  /** Boot probe and 60 s refresh. 401 here means "not signed in", so it must not trigger the global 401 reaction. */
  get: (signal?: AbortSignal) => http.get<Session>('/session', { quiet: true, signal }),
  /** A 401 here is a wrong PIN (`invalid_credentials`), never a lost session. */
  login: (request: LoginRequest) => http.post<Session>('/auth/login', request),
  logout: () => http.post<void>('/auth/logout'),
  signInOptions: () => http.get<SignInOptions>('/auth/sign-in-options', { quiet: true }),
}
