import type { components } from './schema'

// Friendly names for the generated DTOs the shell and modules share. Everything is derived from schema.d.ts,
// so `npm run api:types` is the only thing that ever changes the shapes.
type Schemas = components['schemas']

export type Session = Schemas['SessionDto']
export type SessionUser = Schemas['SessionUserDto']
export type NavSection = Schemas['NavSectionDto']
export type NavItem = Schemas['NavItemDto']
export type LicenseSummary = Schemas['LicenseSummaryDto']
export type Branding = Schemas['BrandingDto']
export type DrawerStatus = Schemas['DrawerStatusDto']
export type SignInOptions = Schemas['SignInOptionsDto']
export type SignInUser = Schemas['SignInUserDto']
export type LoginRequest = Schemas['LoginRequest']
