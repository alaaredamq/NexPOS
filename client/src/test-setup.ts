import '@testing-library/jest-dom/vitest'
import { configure } from '@testing-library/react'

// The first test of a page pays for loading that page's lazy route chunk, which on a busy machine takes just over
// Testing Library's default 1 s, so findBy*/waitFor failed only for whichever test happened to run first.
configure({ asyncUtilTimeout: 5000 })
